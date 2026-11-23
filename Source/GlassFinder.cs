using System;
using System.Collections.Generic;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Finds canopy glass that is painted into a cockpit's hull texture, the way RealWindows finds windows: the
    /// hull's night-lighting map (windows glow) or, if it has none, its gloss map (glass is shiny and slightly
    /// blue). Glass triangles facing the way the crew leave, around their path out, are grouped into panes; the
    /// canopy is everything inside the outline of those panes (glass plus the frame between the panes).
    /// Textures are read back from the GPU, so KSP's non-readable textures work.
    /// </summary>
    /// <summary>Fast "is anything within r of this point" over a fixed set of world points.</summary>
    public class PointGrid
    {
        readonly Dictionary<Vector3Int, List<Vector3>> cells = new Dictionary<Vector3Int, List<Vector3>>();
        readonly float size;
        public int Count { get; private set; }

        public PointGrid(IEnumerable<Vector3> points, float cellSize)
        {
            size = cellSize;
            foreach (var p in points)
            {
                var k = Key(p); List<Vector3> l;
                if (!cells.TryGetValue(k, out l)) cells[k] = l = new List<Vector3>();
                l.Add(p); Count++;
            }
        }

        Vector3Int Key(Vector3 p) { return new Vector3Int(Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.y / size), Mathf.FloorToInt(p.z / size)); }

        public bool Near(Vector3 p, float r)
        {
            int span = Mathf.CeilToInt(r / size);
            var k = Key(p); float rr = r * r;
            for (int x = -span; x <= span; x++)
                for (int y = -span; y <= span; y++)
                    for (int z = -span; z <= span; z++)
                    {
                        List<Vector3> l;
                        if (!cells.TryGetValue(new Vector3Int(k.x + x, k.y + y, k.z + z), out l)) continue;
                        foreach (var q in l) if ((q - p).sqrMagnitude < rr) return true;
                    }
            return false;
        }
    }

    public static class GlassFinder
    {
        public class Result
        {
            public bool found;
            public Vector3 origin, axis, u, v;     // projection plane across the escape direction
            public List<Vector2> outline = new List<Vector2>();
            public int glassTriangles, panes;
            public string how = "";

            Vector2 Project(Vector3 w) { Vector3 d = w - origin; return new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)); }

            /// <summary>A hull triangle that belongs to the canopy: faces outwards along the escape direction and
            /// lies inside the panes' outline.</summary>
            public bool Contains(Vector3 centroid, Vector3 normal)
            {
                if (!found || Vector3.Dot(normal, axis) < 0.1f) return false;
                return Inside(outline, Project(centroid));
            }

            /// <summary>0 = keep, 1 = canopy, 2 = on the edge of the opening (split it and decide piece by piece).</summary>
            public int Classify(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                if (!found || Vector3.Dot(normal, axis) < 0.1f) return 0;
                Vector2 pa = Project(a), pb = Project(b), pc = Project(c);
                int inside = (Inside(outline, pa) ? 1 : 0) + (Inside(outline, pb) ? 1 : 0) + (Inside(outline, pc) ? 1 : 0);
                if (inside == 3) return 1;
                if (inside > 0 || Inside(outline, Project((a + b + c) / 3f))) return 2;
                if (DistanceTo(outline, pa) < 0.03f || DistanceTo(outline, pb) < 0.03f || DistanceTo(outline, pc) < 0.03f) return 2;
                return 0;
            }

            /// <summary>For a small piece of an edge triangle: part of the canopy if it lies inside the outline and is
            /// painted as glass or as the dark window border.</summary>
            public bool PieceIsCanopy(Vector3 centre, Vector3 normal, Material m, Vector2 uv)
            {
                if (!found || Vector3.Dot(normal, axis) < 0.1f) return false;
                Vector2 p = Project(centre);
                if (Inside(outline, p)) return true;                       // the opening follows the window outline
                // the dark border painted right round the glass goes with it (judged on the texture, so no other
                // dark paint nearby is touched)
                return DistanceTo(outline, p) < 0.03f && Luma(m, uv) < 0.35f && NearGlassInTexture(m, uv, 5);
            }

            /// <summary>True if the opening covers every seat's way out (so no round hole is needed as well).</summary>
            public bool Covers(List<HullCutter.Column> cols)
            {
                if (!found) return false;
                foreach (var c in cols)
                {
                    Vector3 a = Vector3.Cross(c.dir, Vector3.up); if (a.sqrMagnitude < 1e-4f) a = Vector3.Cross(c.dir, Vector3.right); a.Normalize();
                    Vector3 b = Vector3.Cross(c.dir, a);
                    for (int i = 0; i < 12; i++)
                    {
                        float ang = i / 12f * Mathf.PI * 2f;
                        Vector3 p = c.origin + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * c.radius * 0.8f;
                        if (!Inside(outline, Project(p))) return false;
                    }
                }
                return true;
            }
        }

        class Sampler { public Color32[] px; public int w, h; public float litFrac, glossFrac; }
        static readonly Dictionary<int, Sampler> cache = new Dictionary<int, Sampler>();

        /// <summary>Copies a texture through a render target so it can be read, scaled down to at most 512 px.</summary>
        static Sampler Read(Texture t)
        {
            if (t == null) return null;
            Sampler s;
            if (cache.TryGetValue(t.GetInstanceID(), out s)) return s;
            s = null;
            RenderTexture prev = RenderTexture.active, rt = null;
            Texture2D tmp = null;
            try
            {
                int w = Mathf.Clamp(t.width, 8, 512), h = Mathf.Clamp(t.height, 8, 512);
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tmp.Apply(false);
                s = new Sampler { px = tmp.GetPixels32(), w = w, h = h };
                int lit = 0, gloss = 0;
                foreach (var c in s.px)
                {
                    if ((c.r + c.g + c.b) / 3 > 128) lit++;
                    if (c.a > 102 && c.b - c.r > 8) gloss++;
                }
                s.litFrac = lit / (float)s.px.Length;
                s.glossFrac = gloss / (float)s.px.Length;
            }
            catch (Exception e) { Log.Dbg("glass: could not read texture " + t.name + ": " + e.Message); }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (tmp != null) UnityEngine.Object.Destroy(tmp);
            }
            cache[t.GetInstanceID()] = s;
            return s;
        }

        static Color32 At(Sampler s, Vector2 uv)
        {
            float x = uv.x - Mathf.Floor(uv.x), y = uv.y - Mathf.Floor(uv.y);
            int ix = Mathf.Clamp((int)(x * s.w), 0, s.w - 1), iy = Mathf.Clamp((int)(y * s.h), 0, s.h - 1);
            return s.px[iy * s.w + ix];
        }

        /// <summary>How a material marks glass: its lightmap if it has a useful one, otherwise its gloss.</summary>
        class GlassMap
        {
            public Sampler lit, main;
            public bool useLit;
            public bool IsGlass(Vector2 uv)
            {
                if (useLit) { var c = At(lit, uv); return (c.r + c.g + c.b) / 3 > 128; }
                var m = At(main, uv); return m.a > 102 && m.b - m.r > 8;
            }
        }

        static GlassMap MapFor(Material m)
        {
            if (m == null) return null;
            var g = new GlassMap();
            if (m.HasProperty("_Emissive")) g.lit = Read(m.GetTexture("_Emissive"));
            if (g.lit != null && g.lit.litFrac > 0.0005f && g.lit.litFrac < 0.7f) { g.useLit = true; return g; }
            if (m.HasProperty("_MainTex")) g.main = Read(m.GetTexture("_MainTex"));
            if (g.main != null && g.main.glossFrac > 0.002f && g.main.glossFrac < 0.7f) return g;
            return null;
        }

        /// <summary>True if this triangle (its material and corner UVs) is painted as glass.</summary>
        public static bool IsGlass(Material m, Vector2 u0, Vector2 u1, Vector2 u2)
        {
            GlassMap gm = MapFor(m);
            if (gm == null) return false;
            int votes = (gm.IsGlass((u0 + u1 + u2) / 3f) ? 2 : 0) + (gm.IsGlass(u0) ? 1 : 0) + (gm.IsGlass(u1) ? 1 : 0) + (gm.IsGlass(u2) ? 1 : 0);
            return votes >= 3;
        }

        /// <summary>True if there is glass paint within <paramref name="texels"/> texels of this point.</summary>
        public static bool NearGlassInTexture(Material m, Vector2 uv, int texels)
        {
            GlassMap gm = MapFor(m);
            if (gm == null) return false;
            Sampler s = gm.useLit ? gm.lit : gm.main;
            if (s == null) return false;
            float dx = texels / (float)s.w, dy = texels / (float)s.h;
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                for (int k = 1; k <= 2; k++)
                    if (gm.IsGlass(uv + new Vector2(Mathf.Cos(a) * dx * k * 0.5f, Mathf.Sin(a) * dy * k * 0.5f))) return true;
            }
            return false;
        }

        public static bool IsGlassAt(Material m, Vector2 uv)
        {
            GlassMap gm = MapFor(m);
            return gm != null && gm.IsGlass(uv);
        }

        /// <summary>Brightness of the hull paint (main texture) at a point, 0-1.</summary>
        public static float Luma(Material m, Vector2 uv)
        {
            if (m == null || !m.HasProperty("_MainTex")) return 1f;
            Sampler s = Read(m.GetTexture("_MainTex"));
            if (s == null) return 1f;
            Color32 c = At(s, uv);
            return (0.3f * c.r + 0.59f * c.g + 0.11f * c.b) / 255f;
        }

        class Tri { public Vector3 a, b, c, centre, normal; public int mesh; }

        public static Result Find(Part part, EjectionPlan plan, EjectionSettings S)
        {
            var res = new Result();
            Vector3 axis = Vector3.zero;
            foreach (var s in plan.seats) axis += s.ejectDir;
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : plan.vehicleUp;
            res.axis = axis;
            res.origin = part.transform.position;
            res.u = Vector3.Cross(axis, plan.vehicleFwd.sqrMagnitude > 0.5f ? plan.vehicleFwd : Vector3.forward);
            if (res.u.sqrMagnitude < 1e-4f) res.u = Vector3.Cross(axis, Vector3.right);
            res.u.Normalize();
            res.v = Vector3.Cross(axis, res.u);
            if (plan.columns == null || plan.columns.Count == 0) { res.how = "no seat path"; return res; }

            // 1. glass triangles on the hull meshes, facing out along the escape direction
            var glass = new List<Tri>();
            var meshes = new List<MeshFilter>();
            string maps = "";
            foreach (var mf in part.FindModelComponents<MeshFilter>())
            {
                if (mf == null || mf.sharedMesh == null || mf.GetComponentInParent<EjectionGenerated>() != null) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || !mf.gameObject.activeInHierarchy || !mf.sharedMesh.isReadable) continue;
                string nm = mf.name.ToLowerInvariant();
                if (nm.Contains("flag") || nm.Contains("decal")) continue;   // flags aren't glass
                Mesh mesh = mf.sharedMesh;
                Vector2[] uv = mesh.uv;
                if (uv == null || uv.Length != mesh.vertexCount) continue;
                Vector3[] pv = mesh.vertices;
                Matrix4x4 l2w = mf.transform.localToWorldMatrix;
                var mats = r.sharedMaterials;
                int meshIndex = meshes.Count;
                bool used = false;
                for (int sm = 0; sm < mesh.subMeshCount && sm < mats.Length; sm++)
                {
                    if (mesh.GetTopology(sm) != MeshTopology.Triangles) continue;
                    GlassMap gm = MapFor(mats[sm]);
                    if (gm == null) continue;
                    maps += (maps.Length > 0 ? ", " : "") + mf.name + (gm.useLit ? " (lightmap)" : " (gloss)");
                    int[] t = mesh.GetTriangles(sm);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        // majority vote of the centre and the three corners
                        Vector2 u0 = uv[t[i]], u1 = uv[t[i + 1]], u2 = uv[t[i + 2]];
                        int votes = (gm.IsGlass((u0 + u1 + u2) / 3f) ? 2 : 0) + (gm.IsGlass(u0) ? 1 : 0) + (gm.IsGlass(u1) ? 1 : 0) + (gm.IsGlass(u2) ? 1 : 0);
                        if (votes < 3) continue;
                        Vector3 a = l2w.MultiplyPoint3x4(pv[t[i]]), b = l2w.MultiplyPoint3x4(pv[t[i + 1]]), c = l2w.MultiplyPoint3x4(pv[t[i + 2]]);
                        Vector3 n = Vector3.Cross(b - a, c - a);
                        if (n.sqrMagnitude < 1e-12f) continue;
                        n.Normalize();
                        Vector3 centre = (a + b + c) / 3f;
                        if (Vector3.Dot(n, centre - part.transform.position) < 0f) n = -n;   // outward
                        glass.Add(new Tri { a = a, b = b, c = c, centre = centre, normal = n, mesh = meshIndex });
                        used = true;
                    }
                }
                if (used) meshes.Add(mf);
            }
            res.glassTriangles = glass.Count;
            if (glass.Count == 0) { res.how = maps.Length > 0 ? "no glass marked in " + maps : "no lightmap or gloss map"; return res; }

            // 2. group into panes (triangles sharing corners)
            var key = new Dictionary<Vector3Int, List<int>>();
            Func<Vector3, Vector3Int> q = p => new Vector3Int(Mathf.RoundToInt(p.x * 500f), Mathf.RoundToInt(p.y * 500f), Mathf.RoundToInt(p.z * 500f));
            for (int i = 0; i < glass.Count; i++)
                foreach (var p in new[] { glass[i].a, glass[i].b, glass[i].c })
                {
                    List<int> l; var k = q(p);
                    if (!key.TryGetValue(k, out l)) key[k] = l = new List<int>();
                    l.Add(i);
                }
            int[] group = new int[glass.Count];
            for (int i = 0; i < group.Length; i++) group[i] = -1;
            int groups = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < glass.Count; i++)
            {
                if (group[i] >= 0) continue;
                group[i] = groups; stack.Push(i);
                while (stack.Count > 0)
                {
                    var g = glass[stack.Pop()];
                    foreach (var p in new[] { g.a, g.b, g.c })
                        foreach (int j in key[q(p)])
                            if (group[j] < 0) { group[j] = groups; stack.Push(j); }
                }
                groups++;
            }

            // 3. keep the panes the crew leave through: facing out along the escape direction, near their path
            var areaN = new Vector3[groups];
            var near = new bool[groups];
            var area = new float[groups];
            var mid = new Vector3[groups];
            for (int i = 0; i < glass.Count; i++)
            {
                var g = glass[i];
                float ar = Vector3.Cross(g.b - g.a, g.c - g.a).magnitude * 0.5f;
                areaN[group[i]] += g.normal * ar;
                area[group[i]] += ar;
                mid[group[i]] += g.centre * ar;
            }
            // a pane counts only if its middle is over a seat's way out (not the windscreen in front of it)
            for (int gi = 0; gi < groups; gi++)
            {
                if (area[gi] <= 0f) continue;
                Vector3 m = mid[gi] / area[gi];
                foreach (var c in plan.columns)
                {
                    Vector3 d = m - c.origin;
                    float along = Vector3.Dot(d, c.dir);
                    if (along < c.start - 0.3f) continue;
                    if ((d - c.dir * along).magnitude < c.radius * 1.4f) { near[gi] = true; break; }
                }
            }
            var pts = new List<Vector2>();
            int kept = 0;
            for (int gi = 0; gi < groups; gi++)
            {
                if (!near[gi] || area[gi] < 0.002f) continue;
                if (Vector3.Dot(areaN[gi].normalized, axis) < 0.2f) continue;
                kept++;
                for (int i = 0; i < glass.Count; i++)
                    if (group[i] == gi)
                    {
                        Vector3 d0 = glass[i].a - res.origin, d1 = glass[i].b - res.origin, d2 = glass[i].c - res.origin;
                        pts.Add(new Vector2(Vector3.Dot(d0, res.u), Vector3.Dot(d0, res.v)));
                        pts.Add(new Vector2(Vector3.Dot(d1, res.u), Vector3.Dot(d1, res.v)));
                        pts.Add(new Vector2(Vector3.Dot(d2, res.u), Vector3.Dot(d2, res.v)));
                    }
            }
            res.panes = kept;
            if (kept == 0 || pts.Count < 3) { res.how = glass.Count + " glass triangles, none facing the way out"; return res; }

            // 4. canopy outline: convex hull of the panes, grown a little to take the frame edge with it
            res.outline = Grow(Hull(pts), 0.025f);
            res.found = res.outline.Count >= 3;
            res.how = kept + " pane(s), " + glass.Count + " glass triangles from " + maps;
            return res;
        }

        // ---------------------------------------------------------------- 2D helpers
        static float Cross(Vector2 o, Vector2 a, Vector2 b) { return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x); }

        static List<Vector2> Hull(List<Vector2> p)
        {
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 pt = pass == 0 ? p[i] : p[p.Count - 1 - i];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], pt) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(pt);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;   // counter-clockwise
        }

        static List<Vector2> Grow(List<Vector2> h, float by)
        {
            if (h.Count < 3) return h;
            Vector2 c = Vector2.zero; foreach (var p in h) c += p; c /= h.Count;
            var r = new List<Vector2>(h.Count);
            foreach (var p in h) { Vector2 d = p - c; r.Add(p + (d.sqrMagnitude > 1e-8f ? d.normalized * by : Vector2.zero)); }
            return r;
        }

        static float DistanceTo(List<Vector2> h, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < h.Count; i++)
            {
                Vector2 a = h[i], b = h[(i + 1) % h.Count], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }

        static bool Inside(List<Vector2> h, Vector2 p)
        {
            if (h.Count < 3) return false;
            for (int i = 0; i < h.Count; i++)
                if (Cross(h[i], h[(i + 1) % h.Count], p) < 0f) return false;
            return true;
        }
    }
}
