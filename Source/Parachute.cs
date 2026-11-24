using System;
using System.Collections.Generic;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Parachute = a canopy taken from a real stock parachute part (so ReStock / texture packs restyle it),
    /// hung on custom suspension lines and risers sized for a kerbal.
    /// Local frame: origin at the riser confluence (kerbal's shoulders), +Y toward the canopy.
    ///   root
    ///    |- canopy  (at the hem centre; ChuteRig animates its scale)
    ///    |- lines   (suspension lines + risers; ChuteRig scales X/Z with the canopy)
    /// Falls back to a procedural round canopy if no stock parachute model can be read.
    /// </summary>
    public static class ParachuteModel
    {
        class StockCanopy
        {
            public Mesh mesh;          // hem centre at origin, hem radius 1, dome toward +Y
            public Material[] mats;
        }

        static readonly Dictionary<string, StockCanopy> cache = new Dictionary<string, StockCanopy>();

        public static GameObject Build(SeatVariant v, EjectionSettings S) { return Build(v, S, null); }

        /// <param name="sourceVessel">If it carries a parachute part of the chosen type, that live part's canopy
        /// (with any runtime texture switching - TexturesUnlimited, B9PartSwitch, part variants...) is used.</param>
        public static GameObject Build(SeatVariant v, EjectionSettings S, Vessel sourceVessel)
        {
            float R = S.chuteDiameter * 0.5f;
            float riserLen = 0.75f;
            float lineLen = S.chuteDiameter * 1.05f;
            float hemY = riserLen + Mathf.Sqrt(Mathf.Max(0.1f, lineLen * lineLen - R * R));

            // root origin = riser confluence (top of the risers); canopy hem sits hemY - riserLen above it
            var root = new GameObject("EjectionSeat_Parachute");
            root.AddComponent<EjectionGenerated>();
            var canopy = new GameObject("canopy");
            canopy.transform.SetParent(root.transform, false);
            canopy.transform.localPosition = new Vector3(0f, hemY - riserLen, 0f);

            // canopy: first stock part that works, else procedural
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(S.chutePart)) candidates.Add(S.chutePart);
            if (!string.IsNullOrEmpty(v.chutePart)) candidates.Add(v.chutePart);
            candidates.AddRange(new[] { "parachuteSingle", "parachuteRadial", "parachuteLarge" });
            StockCanopy sc = null;
            foreach (var name in candidates)
            {
                // 1) a live copy of this parachute on the craft (runtime texture/variant changes)
                Part live = FindLiveParachute(sourceVessel, name);
                if (live != null)
                {
                    sc = GetCanopy("live:" + live.persistentId, live.transform, live.FindModuleImplementing<ModuleParachute>());
                    if (sc != null) { Log.Dbg("chute canopy from live part '" + name + "' on " + sourceVessel.vesselName); break; }
                }
                // 2) the part as loaded (ReStock, texture packs, TextureReplacer all apply here)
                sc = GetStock(name);
                if (sc != null) { Log.Dbg("chute canopy from part '" + name + "'"); break; }
            }
            var dome = new GameObject("dome");
            dome.transform.SetParent(canopy.transform, false);
            if (sc != null)
            {
                dome.transform.localScale = Vector3.one * R;
                dome.AddComponent<MeshFilter>().sharedMesh = sc.mesh;
                dome.AddComponent<MeshRenderer>().sharedMaterials = sc.mats;
            }
            else
            {
                dome.AddComponent<MeshFilter>().sharedMesh = ProceduralCanopy(v, R);
                dome.AddComponent<MeshRenderer>().sharedMaterials = new[]
                {
                    MatLib.Solid(v.chuteA, 0.12f, MatLib.Canvas),
                    MatLib.Solid(v.chuteB, 0.12f, MatLib.Canvas),
                };
            }

            // connector links where the suspension lines gather (riser tops); lines/risers are drawn live by ChuteRig
            var kit = new MeshKit(1);
            kit.Sphere(new Vector3(-ChuteRig.LinkSpread, 0f, 0f), 0.022f, 8);
            kit.Sphere(new Vector3(ChuteRig.LinkSpread, 0f, 0f), 0.022f, 8);
            var links = new GameObject("links");
            links.transform.SetParent(root.transform, false);
            links.AddComponent<MeshFilter>().sharedMesh = kit.Build("EjectionSeat_ChuteLinks");
            links.AddComponent<MeshRenderer>().sharedMaterial = MatLib.Solid(new Color(0.35f, 0.35f, 0.37f), 0.6f);

            var rig = root.AddComponent<ChuteRig>();
            rig.riserLen = riserLen;
            rig.hemRadius = R * 0.97f;
            rig.lineCount = Mathf.Max(8, v.gores);
            return root;
        }

        /// <summary>
        /// Instantiates a stock parachute part model, poses it fully deployed, bakes the canopy into a
        /// static mesh (dropping the stock cords below the hem) and normalises it to hem radius 1.
        /// </summary>
        /// <summary>Removes scripts, cloth, physics and colliders from a model copy (dependents first).</summary>
        public static void StripForBake(GameObject go)
        {
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(j);
            foreach (var c in go.GetComponentsInChildren<Cloth>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) { try { UnityEngine.Object.DestroyImmediate(mb); } catch { } }
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(rb);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col);
        }

        static Part FindLiveParachute(Vessel v, string partName)
        {
            if (v == null || v.parts == null) return null;
            foreach (var p in v.parts)
                if (p != null && p.partInfo != null && p.partInfo.name == partName && p.FindModuleImplementing<ModuleParachute>() != null)
                    return p;
            return null;
        }

        /// <summary>Bakes the stock canopies a seat may use ahead of time (the first bake costs a frame mid-ejection).</summary>
        public static void Prewarm(EjectionSettings s)
        {
            var names = new HashSet<string> { "parachuteSingle", "parachuteRadial", "parachuteLarge" };
            if (!string.IsNullOrEmpty(s.chutePart)) names.Add(s.chutePart);
            foreach (var v in SeatVariant.All) if (!string.IsNullOrEmpty(v.chutePart)) names.Add(v.chutePart);
            foreach (var n in names) GetStock(n);
        }

        static StockCanopy GetStock(string partName)
        {
            StockCanopy sc;
            if (cache.TryGetValue(partName, out sc)) return sc;
            AvailablePart ap = PartLoader.getPartInfoByName(partName);
            if (ap == null || ap.partPrefab == null) { cache[partName] = null; return null; }
            sc = GetCanopy(partName, ap.partPrefab.transform, ap.partPrefab.FindModuleImplementing<ModuleParachute>());
            cache[partName] = sc;
            return sc;
        }

        const float CollapsedRatio = 2f;        // canopy height / hem radius above which it counts as not deployed
        const float MinDeployedRadius = 0.4f;   // metres: anything smaller is a packed canopy, not an open one

        /// <summary>
        /// Samples the part's deploy animations at their end on the baking copy. Returns how many clips were sampled.
        /// KSP puts a model's animations under the part's "model" transform, so they normally come along with the copy;
        /// if a part (or a mod) keeps them elsewhere on the part, the clips are replayed on a stand-in for the part root
        /// that holds the copy, so the clips' paths ("model/...") still resolve. Clips not named by the module are
        /// found by name (deploy / full / inflate / open).
        /// </summary>
        static int PoseDeployed(GameObject copy, Transform partRoot, Transform model, ModuleParachute mp)
        {
            int n = 0;
            foreach (var anim in copy.GetComponentsInChildren<Animation>(true))
            {
                n += SampleEnd(anim, mp);
                anim.enabled = false;
            }
            if (n > 0) return n;

            // animations that live on the part but outside its model (not on any attached child part)
            foreach (var src in partRoot.GetComponentsInChildren<Animation>(true))
            {
                if (src == null || src.transform.IsChildOf(model) && model != partRoot) continue;   // those came with the copy
                if (src.GetComponentInParent<Part>() != null && src.GetComponentInParent<Part>().transform != partRoot) continue;
                string rel = PathFrom(partRoot, src.transform);
                if (rel == null) continue;
                var stand = new GameObject("EjectionSeat_BakeRoot");
                string modelName = copy.name;
                copy.name = model.name;
                copy.transform.SetParent(stand.transform, false);
                try
                {
                    Transform host = rel.Length == 0 ? stand.transform : stand.transform.Find(rel);
                    if (host == null) continue;
                    var a = host.gameObject.AddComponent<Animation>();
                    foreach (AnimationState st in src)
                        if (st != null && st.clip != null && a.GetClip(st.name) == null) a.AddClip(st.clip, st.name);
                    n += SampleEnd(a, mp);
                    UnityEngine.Object.DestroyImmediate(a);
                }
                catch (Exception e) { Log.Dbg("replaying parachute animation failed: " + e.Message); }
                finally
                {
                    copy.transform.SetParent(null, true);
                    copy.name = modelName;
                    UnityEngine.Object.DestroyImmediate(stand);
                }
                if (n > 0) break;
            }
            return n;
        }

        /// <summary>Samples the deploy clips (semi, then full, so full wins) at their last frame.</summary>
        static int SampleEnd(Animation anim, ModuleParachute mp)
        {
            var names = new List<string>();
            foreach (var c in new[] { mp.semiDeployedAnimation, mp.fullyDeployedAnimation })
                if (!string.IsNullOrEmpty(c) && anim[c] != null) names.Add(c);
            if (names.Count == 0)
            {
                // module names don't match this model's clips: take the most deployed-sounding one
                string best = null; int bestScore = 0;
                foreach (AnimationState st in anim)
                {
                    if (st == null) continue;
                    string l = st.name.ToLowerInvariant();
                    int score = l.Contains("full") ? 3 : l.Contains("inflate") ? 2 : (l.Contains("deploy") || l.Contains("open")) ? 1 : 0;
                    if (l.Contains("semi") || l.Contains("cut") || l.Contains("repack")) score = Math.Min(score, 1) - 1;
                    if (score > bestScore) { bestScore = score; best = st.name; }
                }
                if (best != null) names.Add(best);
            }
            foreach (var name in names)
            {
                AnimationState st = anim[name];
                st.enabled = true; st.weight = 1f; st.normalizedTime = 1f;
                anim.Sample();
                st.enabled = false;
            }
            return names.Count;
        }

        /// <summary>Transform path from root to t ("" for root itself), or null if t is not under root.</summary>
        static string PathFrom(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            if (t != root) return null;
            return string.Join("/", parts.ToArray());
        }

        /// <summary>Bakes the fully-deployed canopy of any parachute part (prefab or live instance).</summary>
        static StockCanopy GetCanopy(string key, Transform partRoot, ModuleParachute mp)
        {
            StockCanopy sc;
            bool live = key.StartsWith("live:");          // live parts may be re-textured at any time: never cache
            if (!live && cache.TryGetValue(key, out sc)) return sc;
            sc = null;
            GameObject copy = null;
            string partName = key;
            try
            {
                if (mp == null || partRoot == null) { cache[key] = null; return null; }
                Transform model = partRoot.Find("model") ?? partRoot;

                // copying the GameObject keeps the renderers' *current* materials, so whatever texture a
                // texture-switching mod has applied to this part comes along
                // Instantiate under an inactive parent so no copied script (cloth-canopy mods, texture
                // switchers...) ever runs Awake/OnEnable; strip everything but meshes + animation, then activate.
                var holder = new GameObject("EjectionSeat_Bake");
                holder.SetActive(false);
                copy = UnityEngine.Object.Instantiate(model.gameObject, holder.transform);
                StripForBake(copy);
                copy.transform.SetParent(null, false);
                UnityEngine.Object.DestroyImmediate(holder);
                copy.SetActive(true);
                copy.transform.position = Vector3.zero;
                copy.transform.rotation = Quaternion.identity;
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);

                // pose the canopy fully deployed before baking (issue #3: if nothing is sampled, the stowed
                // canopy gets baked and no amount of scaling turns it into an open one)
                int sampled = PoseDeployed(copy, partRoot, model, mp);
                if (sampled == 0)
                    Log.Warn("parachute '" + partName + "': no deploy animation could be sampled, so its canopy may be stowed - " +
                             "checking its shape before using it");
                else Log.Dbg("parachute '" + partName + "': posed with " + sampled + " deploy clip(s)");

                Transform can = CockpitAnalyzer.FindDeep(copy.transform, string.IsNullOrEmpty(mp.canopyName) ? "canopy" : mp.canopyName);
                if (can == null) { cache[partName] = null; return null; }
                Matrix4x4 toRoot = copy.transform.worldToLocalMatrix;

                var V = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>();
                var subs = new List<List<int>>(); var mats = new List<Material>();
                foreach (Renderer r in can.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh m; Matrix4x4 mtx;
                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null)
                    {
                        m = new Mesh();
                        smr.BakeMesh(m);
                        mtx = toRoot * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    }
                    else
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                        m = mf.sharedMesh;
                        mtx = toRoot * mf.transform.localToWorldMatrix;
                    }
                    Vector3[] mv = m.vertices; Vector3[] mn = m.normals; Vector2[] mu = m.uv;
                    int baseIdx = V.Count;
                    for (int i = 0; i < mv.Length; i++)
                    {
                        V.Add(mtx.MultiplyPoint3x4(mv[i]));
                        N.Add(mn != null && mn.Length == mv.Length ? mtx.MultiplyVector(mn[i]).normalized : Vector3.up);
                        UV.Add(mu != null && mu.Length == mv.Length ? mu[i] : Vector2.zero);
                    }
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        if (m.GetTopology(s) != MeshTopology.Triangles) continue;
                        var tri = m.GetTriangles(s);
                        var l = new List<int>(tri.Length);
                        foreach (int ix in tri) l.Add(ix + baseIdx);
                        subs.Add(l);
                        var sm = r.sharedMaterials;
                        mats.Add(sm.Length > 0 ? sm[Mathf.Min(s, sm.Length - 1)] : MatLib.Solid(Color.white, 0.1f));
                    }
                }
                if (V.Count == 0) { cache[partName] = null; return null; }

                // locate the dome: axis = part up (Y). Hem = lowest ring among the widest vertices.
                Vector3 c = Vector3.zero;
                foreach (var p in V) c += p;
                c /= V.Count;
                float maxR = 0f;
                foreach (var p in V) maxR = Mathf.Max(maxR, new Vector2(p.x - c.x, p.z - c.z).magnitude);
                float hem = float.MaxValue, top = float.MinValue;
                foreach (var p in V)
                {
                    float rr = new Vector2(p.x - c.x, p.z - c.z).magnitude;
                    if (rr > maxR * 0.75f) hem = Mathf.Min(hem, p.y);
                    top = Mathf.Max(top, p.y);
                }
                if (maxR < 1e-3f || top <= hem) { cache[partName] = null; return null; }
                // A deployed canopy is metres across and about as tall as it is wide (stock: 3.3-6.2 m, height/radius
                // 0.7-1.0). Stowed it is a few-centimetre bundle (stock: 1-3 cm), and baking that and scaling it up is
                // what gave the collapsed canopy of issue #3. Never hang that over a kerbal: use the procedural canopy.
                if (maxR < MinDeployedRadius || (top - hem) > CollapsedRatio * maxR)
                {
                    Log.Warn("parachute '" + partName + "': baked canopy is not deployed (" + (maxR * 2f).ToString("F2") + " m wide, " +
                             (top - hem).ToString("F2") + " m tall) - using the built-in canopy instead");
                    cache[partName] = null;
                    return null;
                }
                float cut = hem - 0.04f * (top - hem);

                // build: drop stock cords (anything under the hem), add back faces, normalise
                var outV = new List<Vector3>(); var outN = new List<Vector3>(); var outUV = new List<Vector2>();
                var outSubs = new List<List<int>>();
                var outMats = new List<Material>();
                for (int s = 0; s < subs.Count; s++)
                {
                    var front = new List<int>(); var back = new List<int>();
                    var tri = subs[s];
                    for (int i = 0; i + 2 < tri.Count; i += 3)
                    {
                        Vector3 a = V[tri[i]], b = V[tri[i + 1]], d = V[tri[i + 2]];
                        Vector3 cen = (a + b + d) / 3f;
                        if (cen.y < cut) continue;
                        float rad = new Vector2(cen.x - c.x, cen.z - c.z).magnitude;
                        if (rad < maxR * 0.15f && cen.y < hem + 0.3f * (top - hem)) continue;   // stray cord bundles in the middle
                        for (int k = 0; k < 3; k++)
                        {
                            int src = tri[i + k];
                            Vector3 p = (V[src] - new Vector3(c.x, hem, c.z)) / maxR;
                            outV.Add(p); outN.Add(N[src]); outUV.Add(UV[src]);
                            front.Add(outV.Count - 1);
                            outV.Add(p); outN.Add(-N[src]); outUV.Add(UV[src]);
                        }
                        int bi = outV.Count - 6;
                        back.Add(bi + 1); back.Add(bi + 5); back.Add(bi + 3);
                    }
                    if (front.Count == 0) continue;
                    front.AddRange(back);
                    outSubs.Add(front);
                    outMats.Add(mats[s]);
                }
                if (outV.Count == 0) { cache[partName] = null; return null; }

                var mesh = new Mesh();
                mesh.name = "EjectionSeat_StockCanopy_" + partName;
                if (outV.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(outV);
                mesh.SetNormals(outN);
                mesh.SetUVs(0, outUV);
                mesh.subMeshCount = outSubs.Count;
                for (int s = 0; s < outSubs.Count; s++) mesh.SetTriangles(outSubs[s], s);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                var finalMats = outMats.ToArray();
                if (key.StartsWith("live:"))
                {
                    // own copies, without any part-highlight rim glow the live part may have right now
                    for (int i = 0; i < finalMats.Length; i++)
                    {
                        if (finalMats[i] == null) continue;
                        var mc = new Material(finalMats[i]);
                        if (mc.HasProperty("_RimColor")) mc.SetColor("_RimColor", Color.clear);
                        if (mc.HasProperty("_RimFalloff")) mc.SetFloat("_RimFalloff", 0f);
                        finalMats[i] = mc;
                    }
                }
                sc = new StockCanopy { mesh = mesh, mats = finalMats };
            }
            catch (Exception e)
            {
                Log.Warn("Could not use stock parachute '" + partName + "': " + e.Message);
                sc = null;
            }
            finally
            {
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            }
            if (live) cache.Remove(key); else cache[partName] = sc;
            return sc;
        }

        /// <summary>Fallback: deep, gored, scalloped round canopy; hem centre at origin.</summary>
        static Mesh ProceduralCanopy(SeatVariant v, float R)
        {
            var kit = new MeshKit(2);
            kit.uvScale = 1.5f;
            float crown = R * 0.95f;
            int gores = Mathf.Max(8, v.gores), sub = 5, rings = 14;
            float thetaVent = 0.08f;
            int cols = gores * sub;
            var pos = new Vector3[rings + 1, cols + 1];
            var outer = new int[rings + 1, cols + 1];
            var inner = new int[rings + 1, cols + 1];
            for (int j = 0; j <= rings; j++)
            {
                float t = j / (float)rings;
                float th = Mathf.Lerp(thetaVent, Mathf.PI * 0.5f, t);
                for (int i = 0; i <= cols; i++)
                {
                    float az = i / (float)cols * Mathf.PI * 2f;
                    float u = (i % sub) / (float)sub;
                    float r = R * Mathf.Sin(th) * (1f + 0.07f * Mathf.Sin(Mathf.PI * u) * Mathf.Sin(th));
                    float y = crown * Mathf.Cos(th) + 0.05f * R * Mathf.Sin(Mathf.PI * u) * t * t;
                    pos[j, i] = new Vector3(Mathf.Cos(az) * r, y, Mathf.Sin(az) * r);
                }
            }
            Vector3 centre = new Vector3(0f, -crown * 0.2f, 0f);
            for (int j = 0; j <= rings; j++)
                for (int i = 0; i <= cols; i++)
                {
                    Vector3 n = (pos[j, i] - centre).normalized;
                    Vector2 uv = new Vector2(i / (float)sub, j / 3f);
                    outer[j, i] = kit.AddVertex(pos[j, i], n, uv);
                    inner[j, i] = kit.AddVertex(pos[j, i], -n, uv);
                }
            for (int g = 0; g < gores; g++)
            {
                kit.Slot = g % 2;
                for (int s = 0; s < sub; s++)
                {
                    int i = g * sub + s;
                    for (int j = 0; j < rings; j++)
                    {
                        Vector3 h = kit.Normal(outer[j, i]);
                        kit.AddTri(outer[j, i], outer[j, i + 1], outer[j + 1, i + 1], h);
                        kit.AddTri(outer[j, i], outer[j + 1, i + 1], outer[j + 1, i], h);
                        kit.AddTri(inner[j, i], inner[j, i + 1], inner[j + 1, i + 1], -h);
                        kit.AddTri(inner[j, i], inner[j + 1, i + 1], inner[j + 1, i], -h);
                    }
                }
            }
            return kit.Build("EjectionSeat_Chute_" + v.id);
        }
    }

    /// <summary>
    /// Drives the deploy animation (streamer -> inflation with overshoot -> breathing),
    /// keeps the canopy pointed into the relative wind, and collapses it on landing.
    /// </summary>
    public class ChuteRig : MonoBehaviour
    {
        public const float LinkSpread = 0.2f;   // half distance between the two riser-top links
        public float riserLen = 0.75f;
        public float hemRadius = 3f;
        public int lineCount = 16;
        public float attachSpread = 0.11f;      // half distance between the two riser roots on the pack

        Transform canopy, kerbal, attach;
        Vector3 attachLocal;
        LineRenderer[] lines, risers;
        float t;
        bool collapsing;
        float collapseT;
        public Vector3 targetUp = Vector3.up;
        public float openTime = 5f;        // pack -> fully open
        public float semiFraction = 0.45f; // share of openTime spent reefed / semi-deployed
        Quaternion smoothedRot;
        bool init;

        public float OpenFraction { get; private set; }

        /// <summary>
        /// kerbalRoot/attachPoint: the risers' roots stay fixed on the kerbal's pack; the canopy and riser
        /// tops pivot freely around them.
        /// </summary>
        public void Setup(Transform canopyTransform, Transform kerbalRoot, Transform attachPoint, Vector3 fallbackLocal)
        {
            canopy = canopyTransform;
            kerbal = kerbalRoot;
            attach = attachPoint;
            attachLocal = fallbackLocal;
            if (attach != null && kerbal != null && (attach.position - kerbal.position).sqrMagnitude > 1f) attach = null;
            transform.SetParent(null, true);

            Material lineMat = MatLib.Unlit(new Color(0.62f, 0.61f, 0.58f));
            Material webMat = MatLib.Unlit(new Color(0.36f, 0.34f, 0.28f));
            lines = new LineRenderer[lineCount];
            for (int i = 0; i < lineCount; i++) lines[i] = NewLine("line" + i, 0.012f, lineMat);
            risers = new[] { NewLine("riserL", 0.035f, webMat), NewLine("riserR", 0.035f, webMat) };
            Apply(0.02f, 0.05f);
            Follow();
            UpdateLines();
        }

        LineRenderer NewLine(string name, float width, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = lr.endWidth = width;
            lr.sharedMaterial = m;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 0;
            return lr;
        }

        Vector3 AttachCenter
        {
            get
            {
                if (attach != null) return attach.position + kerbal.up * 0.04f;
                if (kerbal != null) return kerbal.TransformPoint(attachLocal);
                return transform.position - transform.up * riserLen;
            }
        }

        /// <summary>A point on the left (0) or right (1) riser, "along" metres up from its root on the pack.</summary>
        public Vector3 RiserPoint(int side, float along)
        {
            Vector3 c = AttachCenter;
            Vector3 right = kerbal != null ? kerbal.right : transform.right;
            Vector3 b = side == 0 ? c - right * attachSpread : c + right * attachSpread;
            Vector3 t = transform.TransformPoint(new Vector3(side == 0 ? -LinkSpread : LinkSpread, 0f, 0f));
            Vector3 d = t - b;
            return d.sqrMagnitude > 1e-6f ? b + d.normalized * along : b;
        }

        public Vector3 CanopyCenter { get { return canopy != null ? canopy.position : transform.position; } }

        /// <summary>Riser tops = attach point + riser length along the canopy axis.</summary>
        void Follow()
        {
            if (kerbal == null || collapsing) return;
            transform.position = AttachCenter + transform.up * riserLen;
        }

        const float LinesDrawDistance = 250f;   // beyond this the lines are under a pixel wide
        bool linesHidden;

        void UpdateLines()
        {
            if (lines == null) return;
            Camera cam = FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null ? FlightCamera.fetch.mainCamera : Camera.main;
            bool far = cam != null && (cam.transform.position - transform.position).sqrMagnitude > LinesDrawDistance * LinesDrawDistance;
            if (far != linesHidden)
            {
                linesHidden = far;
                foreach (var l in lines) if (l != null) l.enabled = !far;
                foreach (var l in risers) if (l != null) l.enabled = !far;
            }
            if (far) return;
            Vector3 c = AttachCenter;
            Vector3 right = kerbal != null ? kerbal.right : transform.right;
            Vector3 bL = c - right * attachSpread, bR = c + right * attachSpread;
            Vector3 tL = transform.TransformPoint(new Vector3(-LinkSpread, 0f, 0f));
            Vector3 tR = transform.TransformPoint(new Vector3(LinkSpread, 0f, 0f));
            risers[0].SetPosition(0, bL); risers[0].SetPosition(1, tL);
            risers[1].SetPosition(0, bR); risers[1].SetPosition(1, tR);
            int n = lines.Length;
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) / n * Mathf.PI * 2f;
                float x = Mathf.Cos(a);
                Vector3 hem = canopy.TransformPoint(new Vector3(x * hemRadius, 0f, Mathf.Sin(a) * hemRadius));
                lines[i].SetPosition(0, hem);
                lines[i].SetPosition(1, x < 0f ? tL : tR);
            }
        }

        void Apply(float sx, float sy)
        {
            if (canopy != null) canopy.localScale = new Vector3(sx, sy, sx);
        }

        public void Collapse()
        {
            if (collapsing) return;
            collapsing = true;
            collapseT = 0f;
            Destroy(gameObject, 0.9f);
        }

        bool cut;

        /// <summary>Released from the kerbal: the canopy stays up in the air, spills and streams away.</summary>
        public void Cut()
        {
            if (collapsing) return;
            cut = true;
            collapsing = true;
            collapseT = 0f;
            kerbal = null;
            attach = null;
            Destroy(gameObject, 6f);
        }

        void LateUpdate()
        {
            if (canopy == null) return;
            float dt = Time.deltaTime;
            Quaternion want = Quaternion.FromToRotation(Vector3.up, targetUp.normalized);
            if (!init) { smoothedRot = want; init = true; }

            if (collapsing && cut)
            {
                // released canopy: spills its air and trails out into a long streamer, drifting on the wind
                collapseT += dt;
                float k = Mathf.Clamp01(collapseT / 2.5f);
                float e = 1f - (1f - k) * (1f - k);
                canopy.localScale = new Vector3(Mathf.Lerp(1f, 0.18f, e), Mathf.Lerp(1f, 1.7f, e), Mathf.Lerp(1f, 0.18f, e));
                smoothedRot = Quaternion.Slerp(smoothedRot, want, dt * 1.5f);
                transform.rotation = smoothedRot * Quaternion.Euler(Mathf.Sin(collapseT * 5f) * 10f, collapseT * 40f, 0f);
                transform.position += Vector3.Normalize(-targetUp) * dt * 1.5f;
                OpenFraction = 0f;
                UpdateLines();
                return;
            }
            if (collapsing)
            {
                // quick deflate & sink, then the object is gone
                collapseT += dt;
                float k = Mathf.Clamp01(collapseT / 0.75f);
                float e = k * k;
                canopy.localScale = new Vector3(Mathf.Lerp(1f, 1.2f, e), Mathf.Lerp(1f, 0.02f, e), Mathf.Lerp(1f, 1.2f, e));
                transform.position += Vector3.Normalize(-targetUp) * dt * 2f;
                OpenFraction = 0f;
                UpdateLines();
                return;
            }

            t += dt;
            // stages (like stock chutes): pull-out -> streamer -> semi-deployed (reefed) -> full inflation
            float T = Mathf.Max(0.3f, openTime);
            float tPull = T * 0.08f, tStream = T * 0.17f, tSemiEnd = tPull + tStream + T * Mathf.Clamp(semiFraction, 0.05f, 0.8f);
            float sx, sy;
            if (t < tPull)
            {
                float k = t / tPull;
                sx = Mathf.Lerp(0.02f, 0.05f, k); sy = Mathf.Lerp(0.05f, 0.6f, k);
                OpenFraction = 0.01f;
            }
            else if (t < tPull + tStream)
            {
                float k = (t - tPull) / tStream;
                sx = Mathf.Lerp(0.05f, 0.12f, k); sy = Mathf.Lerp(0.6f, 1.6f, k);
                OpenFraction = Mathf.Lerp(0.01f, 0.05f, k);
            }
            else if (t < tSemiEnd)
            {
                // reefed: narrow, long canopy that flutters
                float k = (t - tPull - tStream) / (tSemiEnd - tPull - tStream);
                float e = 1f - (1f - k) * (1f - k);
                float flutter = Mathf.Sin(t * 17f) * 0.015f;
                sx = Mathf.Lerp(0.12f, 0.32f, Mathf.Min(1f, e * 1.6f)) + flutter;
                sy = Mathf.Lerp(1.6f, 1.25f, e);
                OpenFraction = 0.12f;
            }
            else if (t < T)
            {
                // disreef: full inflation with overshoot
                float k = (t - tSemiEnd) / Mathf.Max(0.05f, T - tSemiEnd);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                float over = Mathf.Sin(k * Mathf.PI) * 0.08f;
                sx = Mathf.Lerp(0.32f, 1f, e) + over;
                sy = Mathf.Lerp(1.25f, 1f, e) - over * 0.5f;
                OpenFraction = Mathf.Lerp(0.12f, 1f, e);
            }
            else
            {
                float b = Mathf.Sin((t - T) * 2.3f) * 0.025f * Mathf.Exp(-(t - T) * 0.15f);
                sx = 1f + b;
                sy = 1f - b * 0.6f;
                OpenFraction = 1f;
            }
            Apply(sx, sy);

            float sway = Mathf.Sin(t * 1.1f) * 3f;
            smoothedRot = Quaternion.Slerp(smoothedRot, want, dt * 2.5f);
            transform.rotation = smoothedRot * Quaternion.Euler(sway, t * 6f, sway * 0.5f);
            Follow();
            UpdateLines();
        }
    }
}
