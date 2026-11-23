using System;
using System.Collections.Generic;
using UnityEngine;

namespace EjectionSeats
{
    public class SeatPose
    {
        public Vector3 pos;          // where the EVA kerbal root starts (world)
        public Vector3 up, fwd, right;
        public Vector3 ejectDir;     // after angle offsets + path search
        public int seatIndex = -1;
        public bool fromIVA;
        public float exitDistance;   // distance along ejectDir until clear of the cockpit
        public List<string> obstructions = new List<string>();
        public Quaternion Rotation { get { return Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, ejectDir).normalized, ejectDir); } }

        // The craft keeps moving (fast!) while the sequence waits, so poses are pinned to the cockpit
        // right after analysis and turned back into world space only when used.
        Vector3 lPos, lUp, lFwd, lRight, lDir;
        bool captured;

        public void Capture(Transform pt)
        {
            lPos = pt.InverseTransformPoint(pos);
            lUp = pt.InverseTransformDirection(up);
            lFwd = pt.InverseTransformDirection(fwd);
            lRight = pt.InverseTransformDirection(right);
            lDir = pt.InverseTransformDirection(ejectDir);
            captured = true;
        }

        public void Refresh(Transform pt)
        {
            if (!captured || pt == null) return;
            pos = pt.TransformPoint(lPos);
            up = pt.TransformDirection(lUp);
            fwd = pt.TransformDirection(lFwd);
            right = pt.TransformDirection(lRight);
            ejectDir = pt.TransformDirection(lDir);
        }
    }

    public class EjectionPlan
    {
        public List<SeatPose> seats = new List<SeatPose>();
        public List<MeshFilter> canopy = new List<MeshFilter>();
        public string canopyDescription = "none";
        public bool isAircraft;
        public Vector3 vehicleUp, vehicleFwd;
        public List<HullCutter.Column> columns = new List<HullCutter.Column>();

        public string Summary()
        {
            var obs = new HashSet<string>();
            foreach (var s in seats) foreach (var o in s.obstructions) obs.Add(o);
            string path = obs.Count == 0 ? "clear" : "blocked by " + string.Join(", ", new List<string>(obs).ToArray());
            return seats.Count + " seat(s), canopy: " + canopyDescription + ", path " + path;
        }
    }

    /// <summary>
    /// Works out, for any cockpit from any mod, where the seats are, which way is "out",
    /// which meshes are the canopy and which bit of hull has to go.
    /// </summary>
    public static class CockpitAnalyzer
    {
        public static EjectionPlan Analyze(Part part, float pitch, float yaw, EjectionSettings s)
        {
            var plan = new EjectionPlan();
            Bounds lb = LocalBounds(part);
            plan.isAircraft = IsAircraft(part, lb);
            Transform pt = part.transform;
            plan.vehicleUp = plan.isAircraft ? -pt.forward : pt.up;
            plan.vehicleFwd = plan.isAircraft ? pt.up : pt.forward;

            plan.seats = GetSeats(part, plan, lb, s);
            HashSet<Part> ship = ShipParts(part);

            foreach (var seat in plan.seats)
            {
                // The seat's own "up" (the IVA seat's head axis) is the ejection axis: aircraft seats fire up
                // through the canopy, capsule crews fire out the way they face (often "forward" for the craft).
                // Optionally (preferVehicleUp) the vehicle's "up" and a blend are tried too and scored.
                var cands = new List<Vector3>();
                cands.Add(seat.up);
                if (s.preferVehicleUp)
                {
                    cands.Add(Vector3.Slerp(seat.up, plan.vehicleUp, s.vehicleUpWeight).normalized);
                    cands.Add(plan.vehicleUp);
                }
                float best = float.MaxValue;
                foreach (var c0 in cands)
                {
                    Vector3 c = ApplyAngles(c0, seat.fwd, seat.right, pitch, yaw);
                    var obs = new List<string>();
                    float exit = CastPath(part, ship, seat.pos, c, obs);
                    float score = obs.Count * 10f + exit * 0.3f;
                    if (s.preferVehicleUp)
                    {
                        score += Vector3.Angle(c, plan.vehicleUp) * 0.02f;
                        if (Vector3.Dot(c, plan.vehicleUp) < -0.2f) score += 50f;   // never punch out downward
                    }
                    if (score < best)
                    {
                        best = score;
                        seat.ejectDir = c;
                        seat.exitDistance = exit;
                        seat.obstructions = obs;
                    }
                }
                plan.columns.Add(new HullCutter.Column
                {
                    origin = seat.pos,
                    dir = seat.ejectDir,
                    radius = s.hatchRadius,
                    start = s.hatchStartHeight,
                    minFacing = s.cutMinFacing
                });
            }

            FindCanopy(part, plan, lb, s);
            return plan;
        }

        public static Vector3 ApplyAngles(Vector3 up, Vector3 fwd, Vector3 right, float pitchDeg, float yawDeg)
        {
            // +pitch tilts the trajectory aft (away from the instrument panel), +yaw tilts it to the right
            float p = pitchDeg * Mathf.Deg2Rad, y = yawDeg * Mathf.Deg2Rad;
            Vector3 f = Vector3.ProjectOnPlane(fwd, up).normalized;
            Vector3 r = Vector3.ProjectOnPlane(right, up).normalized;
            Vector3 d = up * Mathf.Cos(p) - f * Mathf.Sin(p);
            d = d * Mathf.Cos(y) + r * Mathf.Sin(y);
            return d.normalized;
        }

        // ------------------------------------------------------------------ geometry helpers
        static IEnumerable<MeshFilter> ModelFilters(Part part)
        {
            foreach (var mf in part.FindModelComponents<MeshFilter>())
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (mf.GetComponentInParent<EjectionGenerated>() != null) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || !mf.gameObject.activeInHierarchy) continue;
                yield return mf;
            }
        }

        public static Bounds LocalBounds(Part part)
        {
            Transform pt = part.transform;
            bool any = false;
            Bounds b = new Bounds();
            foreach (var mf in ModelFilters(part))
            {
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 l = pt.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(l, Vector3.zero); any = true; } else b.Encapsulate(l);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, Vector3.one);
            return b;
        }

        static bool IsAircraft(Part part, Bounds lb)
        {
            string n = (part.name + " " + (part.partInfo != null ? part.partInfo.title : "")).ToLowerInvariant();
            int score = 0;
            if (HighLogic.LoadedSceneIsEditor && EditorDriver.editorFacility == EditorFacility.SPH) score += 2;
            if (HighLogic.LoadedSceneIsFlight && part.vessel != null && part.vessel.vesselType == VesselType.Plane) score += 2;
            if (n.Contains("cockpit")) score += 1;
            if (n.Contains("pod") || n.Contains("capsule")) score -= 2;
            float lateral = Mathf.Max(lb.size.x, lb.size.z);
            if (lb.size.y > lateral * 1.3f) score += 2;          // long along the stack axis => fuselage
            if (lb.size.y < lateral * 0.9f) score -= 1;
            return score >= 2;
        }

        static HashSet<Part> ShipParts(Part part)
        {
            var set = new HashSet<Part>();
            if (HighLogic.LoadedSceneIsFlight && part.vessel != null) foreach (var p in part.vessel.parts) set.Add(p);
            else if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.fetch.ship != null) foreach (var p in EditorLogic.fetch.ship.parts) set.Add(p);
            set.Add(part);
            return set;
        }

        /// <summary>
        /// Casts five rays (centre + seat-width ring) inward from far along the path. Returns the distance
        /// at which the seat is clear of its own cockpit, and lists any other part in the way.
        /// </summary>
        static float CastPath(Part part, HashSet<Part> ship, Vector3 start, Vector3 dir, List<string> obstructions)
        {
            const float L = 25f;
            float exit = 0.4f;
            Vector3 a, b;
            Vector3 helper = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            a = Vector3.Cross(dir, helper).normalized * 0.22f;
            b = Vector3.Cross(dir, a).normalized * 0.22f;
            Vector3[] offs = { Vector3.zero, a, -a, b, -b };
            int mask = ~((1 << 2) | (1 << 16) | (1 << 17) | (1 << 20));   // ignore raycast, kerbals, EVA, internal space
            foreach (var o in offs)
            {
                Vector3 far = start + o + dir * L;
                RaycastHit[] hits;
                try { hits = Physics.RaycastAll(far, -dir, L - 0.05f, mask, QueryTriggerInteraction.Ignore); }
                catch { continue; }
                foreach (var h in hits)
                {
                    if (h.collider == null) continue;
                    Part p = h.collider.GetComponentInParent<Part>();
                    if (p == null || !ship.Contains(p)) continue;
                    float along = L - h.distance;
                    if (p == part) { exit = Mathf.Max(exit, along); continue; }
                    if (along < 0.3f) continue;   // touching neighbours at the seat itself
                    string t = p.partInfo != null ? p.partInfo.title : p.name;
                    if (!obstructions.Contains(t)) obstructions.Add(t);
                }
            }
            return exit;
        }

        // ------------------------------------------------------------------ seats
        static List<SeatPose> GetSeats(Part part, EjectionPlan plan, Bounds lb, EjectionSettings s)
        {
            var list = new List<SeatPose>();
            Transform pt = part.transform;
            Quaternion conv = Quaternion.Euler(s.internalRotation);

            // 1) live IVA (flight)
            InternalModel im = part.internalModel;
            if (im != null && im.seats != null && im.seats.Count > 0)
            {
                Quaternion inv = Quaternion.Inverse(im.transform.rotation);
                for (int i = 0; i < im.seats.Count; i++)
                {
                    var seat = im.seats[i];
                    Transform st = seat != null ? seat.seatTransform : null;
                    if (st == null) continue;
                    Vector3 lp = inv * (st.position - im.transform.position);
                    Quaternion lr = inv * st.rotation;
                    var sp = MakeSeat(pt, conv, lp, lr, i, s);
                    Log.Dbg("IVA seat " + i + " '" + st.name + "': ivaLocal=" + lp.ToString("F3") +
                            " imPos=" + im.transform.position.ToString("F2") + " imLossy=" + im.transform.lossyScale.ToString("F3") +
                            " seatWorld=" + st.position.ToString("F2") + " -> partLocal=" + pt.InverseTransformPoint(sp.pos).ToString("F3") +
                            " partScale=" + PartScale.Of(part).ToString("F3"));
                    list.Add(sp);
                }
            }

            // 2) IVA prefab (editor, or flight with no spawned IVA)
            if (list.Count == 0 && part.partInfo != null && part.partInfo.internalConfig != null && part.partInfo.internalConfig.HasValue("name"))
            {
                try
                {
                    InternalModel prefab = PartLoader.GetInternalPart(part.partInfo.internalConfig.GetValue("name"));
                    if (prefab != null)
                    {
                        Transform root = prefab.transform;
                        Quaternion inv = Quaternion.Inverse(root.rotation);
                        var names = new List<string>();
                        if (prefab.seats != null) foreach (var se in prefab.seats) if (se != null) names.Add(se.seatTransformName);
                        if (names.Count == 0)
                            foreach (ConfigNode mn in part.partInfo.internalConfig.GetNodes("MODULE"))
                                if (mn.GetValue("name") == "InternalSeat" && mn.HasValue("seatTransformName")) names.Add(mn.GetValue("seatTransformName"));
                        for (int i = 0; i < names.Count; i++)
                        {
                            Transform st = FindDeep(root, names[i]);
                            if (st == null) continue;
                            Vector3 lp = inv * (st.position - root.position);
                            Quaternion lr = inv * st.rotation;
                            list.Add(MakeSeat(pt, conv, lp, lr, i, s));
                        }
                    }
                }
                catch (Exception e) { Log.Dbg("IVA prefab lookup failed: " + e.Message); }
            }

            // sanity: discard IVA seats that land outside the part or point down into the floor
            Bounds grown = lb; grown.Expand(1.0f);
            int before = list.Count;
            list.RemoveAll(x => !grown.Contains(pt.InverseTransformPoint(x.pos)));
            Log.Dbg("part local bounds " + lb.center.ToString("F2") + " size " + lb.size.ToString("F2") + "; IVA seats kept " + list.Count + "/" + before);
            foreach (var x in list)
            {
                // only override the IVA seat axis when vehicle-up mode is on (or the axis is degenerate)
                bool bad = float.IsNaN(x.up.x) || x.up.sqrMagnitude < 0.5f;
                if (bad || (s.preferVehicleUp && Vector3.Dot(x.up, plan.vehicleUp) < -0.3f))
                {
                    x.up = plan.vehicleUp;
                    x.fwd = Vector3.ProjectOnPlane(plan.vehicleFwd, x.up).normalized;
                    x.right = Vector3.Cross(x.up, x.fwd);
                }
            }

            // 3) geometric fallback: seats side by side, low in the part
            if (list.Count == 0)
            {
                int n = Mathf.Max(1, part.CrewCapacity);
                Vector3 up = plan.vehicleUp, fwd = plan.vehicleFwd;
                Vector3 right = Vector3.Cross(up, fwd).normalized;
                Vector3 c = pt.TransformPoint(lb.center);
                float h = Mathf.Abs(Vector3.Dot(pt.TransformVector(lb.size), up));
                c -= up * h * 0.2f;
                for (int i = 0; i < n; i++)
                {
                    float off = (i - (n - 1) * 0.5f) * 0.65f;
                    list.Add(new SeatPose { pos = c + right * off, up = up, fwd = fwd, right = right, seatIndex = i });
                }
            }
            return list;
        }

        static SeatPose MakeSeat(Transform pt, Quaternion conv, Vector3 ivaLocalPos, Quaternion ivaLocalRot, int idx, EjectionSettings s)
        {
            Quaternion wr = pt.rotation * conv * ivaLocalRot;
            Vector3 up = wr * Vector3.up, fwd = wr * Vector3.forward;
            // TweakScale / StretchScale scale the part model but not the IVA: stretch seat positions to match
            Vector3 sc = PartScale.Of(pt.GetComponent<Part>());
            Vector3 wp = pt.position + pt.rotation * Vector3.Scale(sc, conv * ivaLocalPos) + wr * s.ivaToEvaOffset;
            return new SeatPose { pos = wp, up = up, fwd = fwd, right = Vector3.Cross(up, fwd).normalized, seatIndex = idx, fromIVA = true };
        }

        public static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        // ------------------------------------------------------------------ canopy
        static void FindCanopy(Part part, EjectionPlan plan, Bounds partLocal, EjectionSettings s)
        {
            float partVol = Mathf.Max(1e-3f, partLocal.size.x * partLocal.size.y * partLocal.size.z);
            var names = new List<string>();
            foreach (var mf in ModelFilters(part))
            {
                var r = mf.GetComponent<MeshRenderer>();
                string nm = mf.name;
                Transform p = mf.transform.parent;
                for (int k = 0; k < 2 && p != null; k++, p = p.parent) nm += " " + p.name;
                bool transparent = false;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    nm += " " + m.name;
                    string sh = m.shader != null ? m.shader.name.ToLowerInvariant() : "";
                    if (sh.Contains("alpha") || sh.Contains("transparent") || sh.Contains("translucent") || sh.Contains("glass") || m.renderQueue >= 2900)
                        transparent = true;
                }
                nm = nm.ToLowerInvariant();
                bool ignored = false;
                foreach (var w in s.ignoreCanopyWords) if (nm.Contains(w)) { ignored = true; break; }
                if (ignored) { Log.Dbg("canopy candidate '" + mf.name + "' ignored (name)"); continue; }

                float score = 0f;
                bool strong = false;
                foreach (var w in s.strongCanopyWords) if (nm.Contains(w)) { score += 5f; strong = true; break; }
                foreach (var w in s.weakCanopyWords) if (nm.Contains(w)) { score += 3f; break; }
                if (transparent) score += 3f;

                Bounds wb = r.bounds;
                bool onPath = false, above = false;
                foreach (var seat in plan.seats)
                {
                    Bounds e = wb; e.Expand(0.3f);
                    if (e.IntersectRay(new Ray(seat.pos, seat.ejectDir)) || e.Contains(seat.pos + seat.ejectDir * 0.8f)) onPath = true;
                    if (Vector3.Dot(wb.center - seat.pos, seat.ejectDir) > 0.15f) above = true;
                }
                if (onPath) score += 3f;
                if (above) score += 2f;

                Vector3 ls = part.transform.InverseTransformVector(wb.size);
                float vol = Mathf.Abs(ls.x * ls.y * ls.z);
                if (vol > partVol * 0.55f && !strong) score = -100f;   // that's the whole fuselage, not a canopy
                if (!onPath) score = Mathf.Min(score, s.canopyMinScore - 1f);

                Log.Dbg("canopy candidate '" + mf.name + "' score " + score.ToString("F1") + (transparent ? " (transparent)" : ""));
                if (score >= s.canopyMinScore)
                {
                    plan.canopy.Add(mf);
                    names.Add(mf.name);
                }
            }
            plan.canopyDescription = plan.canopy.Count > 0 ? string.Join(", ", names.ToArray()) : "hull breach (no separate canopy mesh)";
        }
    }

    /// <summary>
    /// Cuts the "escape column" out of a mesh: every triangle whose centre lies inside a cylinder
    /// above the seat is removed (big triangles are subdivided first so the hole is round-ish).
    /// Removed triangles can be returned as a separate debris mesh.
    /// </summary>
    public static class HullCutter
    {
        public struct Column
        {
            public Vector3 origin, dir;
            public float radius, start;
            public float minFacing;   // |dot(surface normal, dir)| needed to cut; keeps side walls
        }

        static bool InColumnFacing(List<Column> cols, Vector3 w, Vector3 nrm)
        {
            foreach (var c in cols)
            {
                Vector3 d = w - c.origin;
                float s = Vector3.Dot(d, c.dir);
                if (s < c.start || s > c.start + 30f) continue;
                if ((d - c.dir * s).sqrMagnitude >= c.radius * c.radius) continue;
                if (nrm.sqrMagnitude < 1e-8f || Mathf.Abs(Vector3.Dot(nrm, c.dir)) >= c.minFacing) return true;
            }
            return false;
        }

        struct Vx
        {
            public Vector3 p, n; public Vector4 t; public Vector2 uv, uv2; public Color c;
            public static Vx Lerp(Vx a, Vx b, float k)
            {
                return new Vx
                {
                    p = Vector3.Lerp(a.p, b.p, k),
                    n = Vector3.Lerp(a.n, b.n, k).normalized,
                    t = Vector4.Lerp(a.t, b.t, k),
                    uv = Vector2.Lerp(a.uv, b.uv, k),
                    uv2 = Vector2.Lerp(a.uv2, b.uv2, k),
                    c = Color.Lerp(a.c, b.c, k)
                };
            }
        }

        static bool InColumn(List<Column> cols, Vector3 w)
        {
            foreach (var c in cols)
            {
                Vector3 d = w - c.origin;
                float s = Vector3.Dot(d, c.dir);
                if (s < c.start || s > c.start + 30f) continue;
                if ((d - c.dir * s).sqrMagnitude < c.radius * c.radius) return true;
            }
            return false;
        }

        static bool NearColumn(List<Column> cols, Vector3 w, float slack)
        {
            foreach (var c in cols)
            {
                Vector3 d = w - c.origin;
                float s = Vector3.Dot(d, c.dir);
                if (s < c.start - slack) continue;
                float rr = c.radius + slack;
                if ((d - c.dir * s).sqrMagnitude < rr * rr) return true;
            }
            return false;
        }

        public static Bounds TransformBounds(Matrix4x4 m, Bounds b)
        {
            Bounds r = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                r.Encapsulate(m.MultiplyPoint3x4(c));
            }
            return r;
        }

        public static bool BoundsTouch(Bounds b, List<Column> cols)
        {
            foreach (var c in cols)
            {
                Bounds e = b; e.Expand(c.radius * 2f);
                Vector3 s0 = c.origin + c.dir * c.start;
                if (e.Contains(s0) || e.IntersectRay(new Ray(s0, c.dir))) return true;
            }
            return false;
        }

        /// <summary>
        /// Cuts <paramref name="mf"/> in place. If <paramref name="debrisRoot"/> is given, the removed
        /// triangles become a child mesh of it (same materials). Returns number of triangles removed.
        /// </summary>
        public static int Cut(MeshFilter mf, List<Column> cols, float maxEdge, Transform debrisRoot, Matrix4x4? spaceToWorld = null)
        {
            Mesh src = mf.sharedMesh;
            var rend = mf.GetComponent<Renderer>();
            if (src == null || rend == null) return 0;
            if (!src.isReadable) { Log.Dbg("mesh '" + src.name + "' not readable, cannot cut"); return 0; }
            Bounds wb = rend.bounds;
            if (spaceToWorld.HasValue) wb = TransformBounds(spaceToWorld.Value, wb);
            if (!BoundsTouch(wb, cols)) return 0;

            // spaceToWorld maps a foreign space (KSP's internal/IVA space) into the flight world
            Matrix4x4 l2w = spaceToWorld.HasValue ? spaceToWorld.Value * mf.transform.localToWorldMatrix : mf.transform.localToWorldMatrix;
            Vector3[] pv = src.vertices;
            Vector3[] nv = src.normals;
            Vector4[] tv = src.tangents;
            Vector2[] uv = src.uv;
            Vector2[] uv2 = src.uv2;
            Color[] cv = src.colors;
            bool hasN = nv != null && nv.Length == pv.Length, hasT = tv != null && tv.Length == pv.Length;
            bool hasUV = uv != null && uv.Length == pv.Length, hasUV2 = uv2 != null && uv2.Length == pv.Length;
            bool hasC = cv != null && cv.Length == pv.Length;

            Func<int, Vx> get = i => new Vx
            {
                p = pv[i],
                n = hasN ? nv[i] : Vector3.up,
                t = hasT ? tv[i] : new Vector4(1, 0, 0, 1),
                uv = hasUV ? uv[i] : Vector2.zero,
                uv2 = hasUV2 ? uv2[i] : Vector2.zero,
                c = hasC ? cv[i] : Color.white
            };

            var outV = new List<Vx>(pv.Length + 1024);
            for (int i = 0; i < pv.Length; i++) outV.Add(get(i));
            var debV = new List<Vx>();
            int subCount = src.subMeshCount;
            var keep = new List<int>[subCount];
            var deb = new List<int>[subCount];
            int removed = 0;
            float maxEdgeSq = maxEdge * maxEdge;
            var stack = new Stack<KeyValuePair<Vx[], int>>();

            for (int sm = 0; sm < subCount; sm++)
            {
                keep[sm] = new List<int>();
                deb[sm] = new List<int>();
                if (src.GetTopology(sm) != MeshTopology.Triangles) { keep[sm].AddRange(src.GetIndices(sm)); continue; }
                int[] tris = src.GetTriangles(sm);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    Vector3 w0 = l2w.MultiplyPoint3x4(pv[tris[t]]), w1 = l2w.MultiplyPoint3x4(pv[tris[t + 1]]), w2 = l2w.MultiplyPoint3x4(pv[tris[t + 2]]);
                    Vector3 wc = (w0 + w1 + w2) / 3f;
                    float tr = Mathf.Sqrt(Mathf.Max((w0 - wc).sqrMagnitude, Mathf.Max((w1 - wc).sqrMagnitude, (w2 - wc).sqrMagnitude)));
                    if (!NearColumn(cols, wc, tr + 0.05f))
                    {
                        keep[sm].Add(tris[t]); keep[sm].Add(tris[t + 1]); keep[sm].Add(tris[t + 2]);
                        continue;
                    }
                    // subdivide by longest-edge bisection until small, then classify each piece
                    stack.Clear();
                    stack.Push(new KeyValuePair<Vx[], int>(new[] { get(tris[t]), get(tris[t + 1]), get(tris[t + 2]) }, 0));
                    while (stack.Count > 0)
                    {
                        var item = stack.Pop();
                        Vx[] v = item.Key;
                        Vector3 a = l2w.MultiplyPoint3x4(v[0].p), b = l2w.MultiplyPoint3x4(v[1].p), c = l2w.MultiplyPoint3x4(v[2].p);
                        float e0 = (b - a).sqrMagnitude, e1 = (c - b).sqrMagnitude, e2 = (a - c).sqrMagnitude;
                        float em = Mathf.Max(e0, Mathf.Max(e1, e2));
                        if (em > maxEdgeSq && item.Value < 12)
                        {
                            int d = item.Value + 1;
                            if (em == e0) { Vx m = Vx.Lerp(v[0], v[1], 0.5f); stack.Push(new KeyValuePair<Vx[], int>(new[] { v[0], m, v[2] }, d)); stack.Push(new KeyValuePair<Vx[], int>(new[] { m, v[1], v[2] }, d)); }
                            else if (em == e1) { Vx m = Vx.Lerp(v[1], v[2], 0.5f); stack.Push(new KeyValuePair<Vx[], int>(new[] { v[0], v[1], m }, d)); stack.Push(new KeyValuePair<Vx[], int>(new[] { v[0], m, v[2] }, d)); }
                            else { Vx m = Vx.Lerp(v[2], v[0], 0.5f); stack.Push(new KeyValuePair<Vx[], int>(new[] { v[0], v[1], m }, d)); stack.Push(new KeyValuePair<Vx[], int>(new[] { m, v[1], v[2] }, d)); }
                            continue;
                        }
                        Vector3 nW = Vector3.Cross(b - a, c - a);
                        if (hasN) nW = l2w.MultiplyVector(v[0].n + v[1].n + v[2].n);
                        nW = nW.sqrMagnitude > 1e-12f ? nW.normalized : Vector3.zero;
                        if (InColumnFacing(cols, (a + b + c) / 3f, nW))
                        {
                            removed++;
                            if (debrisRoot != null)
                            {
                                int bi = debV.Count;
                                debV.Add(v[0]); debV.Add(v[1]); debV.Add(v[2]);
                                deb[sm].Add(bi); deb[sm].Add(bi + 1); deb[sm].Add(bi + 2);
                            }
                        }
                        else
                        {
                            int bi = outV.Count;
                            outV.Add(v[0]); outV.Add(v[1]); outV.Add(v[2]);
                            keep[sm].Add(bi); keep[sm].Add(bi + 1); keep[sm].Add(bi + 2);
                        }
                    }
                    if (outV.Count > 600000) { Log.Warn("cut of '" + src.name + "' aborted (too dense)"); return 0; }
                }
            }
            if (removed == 0) return 0;

            // kept mesh (stays on the cockpit)
            Mesh km = BuildMesh(src.name + "_cut", outV, keep, hasN, hasT, hasUV, hasUV2, hasC, null, Matrix4x4.identity);
            mf.sharedMesh = km;

            // debris mesh in debris-root space
            if (debrisRoot != null && debV.Count > 0)
            {
                Matrix4x4 toDeb = debrisRoot.worldToLocalMatrix * l2w;
                Mesh dm = BuildMesh(src.name + "_panel", debV, deb, hasN, hasT, hasUV, hasUV2, hasC, toDeb, toDeb);
                var go = new GameObject(mf.name + "_panel");
                go.transform.SetParent(debrisRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = dm;
                var dr = go.AddComponent<MeshRenderer>();
                dr.sharedMaterials = rend.sharedMaterials;
                CopyLook(rend, dr);
            }
            return removed;
        }

        /// <summary>
        /// Debris keeps the cockpit's look (KSP's per-part property block: tint, temperature) but with the
        /// lightmap switched off - a jettisoned canopy has no cabin lights behind it.
        /// </summary>
        public static void CopyLook(Renderer from, Renderer to)
        {
            if (from == null || to == null) return;
            var mpb = new MaterialPropertyBlock();
            from.GetPropertyBlock(mpb);
            mpb.SetColor("_EmissiveColor", Color.black);
            to.SetPropertyBlock(mpb);
        }

        static Mesh BuildMesh(string name, List<Vx> v, List<int>[] subs, bool hasN, bool hasT, bool hasUV, bool hasUV2, bool hasC, Matrix4x4? xf, Matrix4x4 xfDir)
        {
            var m = new Mesh();
            m.name = name;
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var p = new List<Vector3>(v.Count);
            var n = new List<Vector3>(v.Count);
            var t = new List<Vector4>(v.Count);
            var u = new List<Vector2>(v.Count);
            var u2 = new List<Vector2>(v.Count);
            var c = new List<Color>(v.Count);
            foreach (var x in v)
            {
                if (xf.HasValue)
                {
                    p.Add(xf.Value.MultiplyPoint3x4(x.p));
                    n.Add(xfDir.MultiplyVector(x.n).normalized);
                    Vector3 td = xfDir.MultiplyVector(new Vector3(x.t.x, x.t.y, x.t.z)).normalized;
                    t.Add(new Vector4(td.x, td.y, td.z, x.t.w));
                }
                else { p.Add(x.p); n.Add(x.n); t.Add(x.t); }
                u.Add(x.uv); u2.Add(x.uv2); c.Add(x.c);
            }
            m.SetVertices(p);
            if (hasN) m.SetNormals(n);
            if (hasT) m.SetTangents(t);
            if (hasUV) m.SetUVs(0, u);
            if (hasUV2) m.SetUVs(1, u2);
            if (hasC) m.SetColors(c);
            m.subMeshCount = subs.Length;
            for (int i = 0; i < subs.Length; i++) m.SetTriangles(subs[i], i);
            if (!hasN) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>
    /// Copies the cockpit's IVA into the outside world so you can see the interior through the hole
    /// the canopy/hull panel left behind.
    /// </summary>
    public static class InteriorReveal
    {
        public static GameObject Build(Part part, List<HullCutter.Column> cols, EjectionSettings s)
        {
            InternalModel im = part.internalModel;
            if (im == null)
            {
                try { part.CreateInternalModel(); im = part.internalModel; } catch { }
                if (im == null) return null;
            }
            Transform pt = part.transform;
            Quaternion conv = Quaternion.Euler(s.internalRotation);
            Quaternion inv = Quaternion.Inverse(im.transform.rotation);
            Vector3 rootScale = im.transform.lossyScale;

            // root = the IVA's frame inside the part (scaled with TweakScale/StretchScale); children use IVA-local poses
            var root = new GameObject("EjectionSeat_Interior");
            root.AddComponent<EjectionGenerated>();
            // follows the part without being its child: mods that scan a part's renderers (RealWindows treats
            // them as outer hull and analyses each for windows) must not pick up this copy of the cabin
            root.transform.position = pt.position;
            root.transform.rotation = pt.rotation;
            root.transform.localScale = PartScale.Of(part);
            root.AddComponent<FollowPart>().target = pt;
            root.layer = 0;

            int count = 0;
            var filters = new List<MeshFilter>();
            foreach (var mr in im.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (count >= s.maxInteriorRenderers) break;
                // KSP hides the IVA of a cockpit you're not inside (renderers disabled / root deactivated),
                // so judge visibility by the prop's own active flags below the IVA root, not by what's drawn now
                if (mr == null || !ActiveBelow(mr.transform, im.transform)) continue;
                if (mr.GetComponentInParent<Kerbal>() != null) continue;
                if (IsDepthMask(mr)) continue;   // IVA window masks punch black holes in the world view
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Transform t = mr.transform;
                Vector3 lp = inv * (t.position - im.transform.position);
                Quaternion lr = inv * t.rotation;
                var go = new GameObject(mr.name);
                go.layer = 0;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = conv * lp;
                go.transform.localRotation = conv * lr;
                Vector3 ls = t.lossyScale;
                go.transform.localScale = new Vector3(ls.x / Safe(rootScale.x), ls.y / Safe(rootScale.y), ls.z / Safe(rootScale.z));
                var nf = go.AddComponent<MeshFilter>();
                nf.sharedMesh = mf.sharedMesh;
                var nr = go.AddComponent<MeshRenderer>();
                var srcMats = mr.sharedMaterials;
                var dstMats = new Material[srcMats.Length];
                for (int i = 0; i < srcMats.Length; i++) dstMats[i] = ToWorldMaterial(srcMats[i], s);
                nr.sharedMaterials = dstMats;
                nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                nr.receiveShadows = true;
                filters.Add(nf);
                count++;
            }
            // clear the escape column inside the cabin too (overhead panels, ceilings)
            if (cols != null)
                foreach (var f in filters) HullCutter.Cut(f, cols, s.subdivideEdge, null);
            Log.Dbg("interior reveal: " + count + " renderers");
            return root;
        }

        /// <summary>Matrix mapping KSP internal (IVA) space into the flight world for this part's IVA.</summary>
        public static Matrix4x4 InternalToWorldMatrix(Part part, InternalModel im, EjectionSettings s)
        {
            Transform pt = part.transform;
            Matrix4x4 partM = Matrix4x4.TRS(pt.position, pt.rotation, Vector3.one) * Matrix4x4.Scale(PartScale.Of(part));
            Matrix4x4 convM = Matrix4x4.Rotate(Quaternion.Euler(s.internalRotation));
            Matrix4x4 imInv = Matrix4x4.TRS(im.transform.position, im.transform.rotation, Vector3.one).inverse;
            return partM * convM * imInv;
        }

        /// <summary>
        /// Cuts the escape column out of the real IVA too, so from inside the cockpit you look up through
        /// the hole at the sky (the flight camera fills in wherever the IVA has no geometry).
        /// </summary>
        public static int CutIVA(Part part, List<HullCutter.Column> cols, EjectionSettings s)
        {
            InternalModel im = part.internalModel;
            if (im == null || cols == null) return 0;
            Matrix4x4 m = InternalToWorldMatrix(part, im, s);
            int removed = 0;
            foreach (var mf in im.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (mf.GetComponentInParent<Kerbal>() != null) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null) continue;
                try { removed += HullCutter.Cut(mf, cols, s.subdivideEdge, null, m); }
                catch (Exception e) { Log.Dbg("IVA cut failed on " + mf.name + ": " + e.Message); }
            }
            Log.Dbg("IVA cut: " + removed + " triangles removed");
            return removed;
        }

        static float Safe(float f) { return Mathf.Abs(f) < 1e-4f ? 1f : f; }

        /// <summary>True if t and every parent up to (not including) root are activeSelf.</summary>
        public static bool ActiveBelow(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        /// <summary>Depth-mask / occluder materials (write depth, draw nothing) - shown from outside they render as black holes.</summary>
        public static bool IsDepthMask(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                string s = m.shader.name.ToLowerInvariant();
                string n = m.name.ToLowerInvariant();
                if (s.Contains("depth") || s.Contains("mask") || s.Contains("occlu") || n.Contains("depthmask") || n.Contains("depth mask")) return true;
            }
            string go = r.name.ToLowerInvariant();
            return go.Contains("depthmask") || go.Contains("depth_mask") || go.Contains("occluder");
        }

        static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();

        /// <summary>
        /// IVA materials use KSP/InternalSpace/* shaders that are only lit by the IVA's own lights and render
        /// black in the normal flight camera. Swap them for the equivalent world shader plus a faint cabin glow.
        /// </summary>
        public static Material ToWorldMaterial(Material m, EjectionSettings s)
        {
            if (m == null || m.shader == null) return m;
            Material r;
            if (converted.TryGetValue(m, out r) && r != null) return r;
            string sn = m.shader.name;
            if (!sn.StartsWith("KSP/InternalSpace") && !sn.Contains("Internal")) { converted[m] = m; return m; }
            bool bump = sn.Contains("Bump");
            bool alpha = sn.Contains("Alpha") || sn.Contains("Transparent") || sn.Contains("Translucent");
            Shader sh = alpha
                ? MatLib.FindShader("KSP/Alpha/Translucent Specular", "KSP/Alpha/Translucent", "KSP/Specular")
                : bump
                    ? MatLib.FindShader("KSP/Emissive/Bumped Specular", "KSP/Bumped Specular", "KSP/Bumped", "KSP/Specular")
                    : MatLib.FindShader("KSP/Emissive/Specular", "KSP/Emissive/Diffuse", "KSP/Specular", "KSP/Diffuse");
            r = new Material(sh);
            r.name = m.name + "_world";
            foreach (var p in new[] { "_MainTex", "_BumpMap" })
                if (m.HasProperty(p) && r.HasProperty(p)) { r.SetTexture(p, m.GetTexture(p)); r.SetTextureScale(p, m.GetTextureScale(p)); r.SetTextureOffset(p, m.GetTextureOffset(p)); }
            foreach (var p in new[] { "_Color", "_SpecColor" })
                if (m.HasProperty(p) && r.HasProperty(p)) r.SetColor(p, m.GetColor(p));
            if (m.HasProperty("_Shininess") && r.HasProperty("_Shininess")) r.SetFloat("_Shininess", m.GetFloat("_Shininess"));
            if (r.HasProperty("_Emissive"))
            {
                Texture em = m.HasProperty("_Emissive") ? m.GetTexture("_Emissive") : null;
                r.SetTexture("_Emissive", em != null ? em : (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null));
            }
            if (r.HasProperty("_EmissiveColor")) r.SetColor("_EmissiveColor", new Color(s.interiorGlow, s.interiorGlow, s.interiorGlow, 1f));
            converted[m] = r;
            Log.Dbg("interior material '" + m.name + "' " + sn + " -> " + sh.name);
            return r;
        }
    }
}
