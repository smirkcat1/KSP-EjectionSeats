using System;
using System.Collections.Generic;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Small procedural modelling toolkit: rounded boxes, tubes, cones, lathes and tori,
    /// with smooth normals and one sub-mesh per material slot.
    /// </summary>
    public class MeshKit
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<List<int>> slots = new List<List<int>>();
        int slot;
        public float uvScale = 0.25f;
        public float scale = 1f;   // uniform scale applied to every vertex

        public MeshKit(int slotCount)
        {
            for (int i = 0; i < slotCount; i++) slots.Add(new List<int>());
        }

        public int Slot { get { return slot; } set { slot = Mathf.Clamp(value, 0, slots.Count - 1); } }

        int V(Vector3 p, Vector3 n, Vector2 uv)
        {
            verts.Add(p * scale);
            norms.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
            uvs.Add(uv);
            return verts.Count - 1;
        }

        /// <summary>Adds a triangle, flipping winding so its front face points along <paramref name="hint"/>.</summary>
        void Tri(int a, int b, int c, Vector3 hint)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            var t = slots[slot];
            if (Vector3.Dot(n, hint) >= 0f) { t.Add(a); t.Add(b); t.Add(c); }
            else { t.Add(a); t.Add(c); t.Add(b); }
        }

        void Quad(int a, int b, int c, int d, Vector3 hint)
        {
            Tri(a, b, c, hint);
            Tri(a, c, d, hint);
        }

        // ------------------------------------------------------------------ rounded box
        static List<float> Coords(float h, float r, int k)
        {
            var l = new List<float>();
            float inner = Mathf.Max(0f, h - r);
            if (r < 1e-4f)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(2f * h / 0.12f));
                for (int i = 0; i <= n; i++) l.Add(-h + 2f * h * i / n);
                return l;
            }
            for (int j = 0; j <= k; j++)
            {
                float th = j / (float)k * Mathf.PI * 0.5f;
                l.Add(-inner - r * Mathf.Cos(th));
            }
            if (inner > 1e-4f)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(2f * inner / 0.12f));
                for (int m = 1; m < n; m++) l.Add(-inner + 2f * inner * m / n);
            }
            for (int j = k; j >= 0; j--)
            {
                float th = j / (float)k * Mathf.PI * 0.5f;
                float v = inner + r * Mathf.Cos(th);
                if (Mathf.Abs(v - l[l.Count - 1]) > 1e-5f) l.Add(v);
            }
            return l;
        }

        /// <summary>Flat quad facing rot*forward with 0..1 UVs (for decals/placards).</summary>
        public void Decal(Vector3 center, Quaternion rot, float w, float h)
        {
            // U runs toward local -X: seen from in front (looking along -forward) that's the viewer's right,
            // so text on the decal reads correctly instead of mirrored
            Vector3 r = rot * Vector3.left * (w * 0.5f), u = rot * Vector3.up * (h * 0.5f), n = rot * Vector3.forward;
            int a = V(center - r - u, n, new Vector2(0f, 0f));
            int b = V(center + r - u, n, new Vector2(1f, 0f));
            int c = V(center + r + u, n, new Vector2(1f, 1f));
            int d = V(center - r + u, n, new Vector2(0f, 1f));
            Quad(a, b, c, d, n);
        }

        public void RoundedBox(Vector3 center, Vector3 size, float radius, Quaternion rot, int k = 4)
        {
            Vector3 h = size * 0.5f;
            float r = Mathf.Clamp(radius, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) - 1e-4f);
            if (r < 0f) r = 0f;
            List<float>[] co = { Coords(h.x, r, k), Coords(h.y, r, k), Coords(h.z, r, k) };
            Vector3 hi = new Vector3(h.x - r, h.y - r, h.z - r);
            for (int a = 0; a < 3; a++)
            {
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    int u = (a + 1) % 3, w = (a + 2) % 3;
                    var cu = co[u]; var cw = co[w];
                    int nu = cu.Count, nw = cw.Count;
                    int[,] idx = new int[nu, nw];
                    Vector3 faceN = Vector3.zero; faceN[a] = sgn;
                    for (int i = 0; i < nu; i++)
                        for (int j = 0; j < nw; j++)
                        {
                            Vector3 p = Vector3.zero;
                            p[a] = sgn * h[a]; p[u] = cu[i]; p[w] = cw[j];
                            Vector3 inner = new Vector3(Mathf.Clamp(p.x, -hi.x, hi.x), Mathf.Clamp(p.y, -hi.y, hi.y), Mathf.Clamp(p.z, -hi.z, hi.z));
                            Vector3 d = p - inner;
                            Vector3 n = d.sqrMagnitude > 1e-10f ? d.normalized : faceN;
                            Vector3 pos = r > 0f ? inner + n * r : p;
                            idx[i, j] = V(center + rot * pos, rot * n, new Vector2(p[u] / uvScale, p[w] / uvScale));
                        }
                    Vector3 hint = rot * faceN;
                    for (int i = 0; i < nu - 1; i++)
                        for (int j = 0; j < nw - 1; j++)
                            Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1], hint);
                }
            }
        }

        public void RoundedBox(Vector3 center, Vector3 size, float radius)
        {
            RoundedBox(center, size, radius, Quaternion.identity);
        }

        // ------------------------------------------------------------------ tubes / cones
        static void Basis(Vector3 axis, out Vector3 p1, out Vector3 p2)
        {
            Vector3 helper = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            p1 = Vector3.Cross(axis, helper).normalized;
            p2 = Vector3.Cross(axis, p1).normalized;
        }

        public void Cylinder(Vector3 a, Vector3 b, float ra, float rb, int sides, bool capA = true, bool capB = true)
        {
            Vector3 ax = b - a;
            float len = ax.magnitude;
            if (len < 1e-5f) return;
            ax /= len;
            Vector3 p1, p2; Basis(ax, out p1, out p2);
            int[] ia = new int[sides + 1], ib = new int[sides + 1];
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2f;
                Vector3 radial = p1 * Mathf.Cos(t) + p2 * Mathf.Sin(t);
                Vector3 n = (radial * len + ax * (ra - rb)).normalized;
                ia[i] = V(a + radial * ra, n, new Vector2(i / (float)sides, 0f));
                ib[i] = V(b + radial * rb, n, new Vector2(i / (float)sides, len / uvScale));
            }
            for (int i = 0; i < sides; i++)
            {
                Vector3 mid = (verts[ia[i]] + verts[ib[i + 1]]) * 0.5f;
                Vector3 hint = mid - (a + b) * 0.5f;
                hint -= ax * Vector3.Dot(hint, ax);
                Quad(ia[i], ia[i + 1], ib[i + 1], ib[i], hint);
            }
            if (capA && ra > 1e-5f) Disc(a, -ax, p1, p2, ra, sides);
            if (capB && rb > 1e-5f) Disc(b, ax, p1, p2, rb, sides);
        }

        void Disc(Vector3 c, Vector3 n, Vector3 p1, Vector3 p2, float r, int sides)
        {
            int ci = V(c, n, new Vector2(0.5f, 0.5f));
            int[] ring = new int[sides + 1];
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2f;
                ring[i] = V(c + (p1 * Mathf.Cos(t) + p2 * Mathf.Sin(t)) * r, n, new Vector2(0.5f + 0.5f * Mathf.Cos(t), 0.5f + 0.5f * Mathf.Sin(t)));
            }
            for (int i = 0; i < sides; i++) Tri(ci, ring[i], ring[i + 1], n);
        }

        /// <summary>Surface of revolution about the local Y axis. profile = (radius, height) pairs.</summary>
        public void Lathe(Vector3 origin, Quaternion rot, Vector2[] profile, int sides, bool flipNormals = false)
        {
            int np = profile.Length;
            int[,] idx = new int[np, sides + 1];
            for (int j = 0; j < np; j++)
            {
                Vector2 prev = profile[Mathf.Max(0, j - 1)], next = profile[Mathf.Min(np - 1, j + 1)];
                Vector2 tan = next - prev;
                Vector2 pn = new Vector2(tan.y, -tan.x);
                if (flipNormals) pn = -pn;
                for (int i = 0; i <= sides; i++)
                {
                    float t = i / (float)sides * Mathf.PI * 2f;
                    Vector3 radial = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t));
                    Vector3 p = radial * profile[j].x + Vector3.up * profile[j].y;
                    Vector3 n = radial * pn.x + Vector3.up * pn.y;
                    idx[j, i] = V(origin + rot * p, rot * n, new Vector2(i / (float)sides, j / (float)(np - 1)));
                }
            }
            for (int j = 0; j < np - 1; j++)
                for (int i = 0; i < sides; i++)
                {
                    Vector3 hint = norms[idx[j, i]] + norms[idx[j + 1, i + 1]];
                    Quad(idx[j, i], idx[j, i + 1], idx[j + 1, i + 1], idx[j + 1, i], hint);
                }
        }

        public void Sphere(Vector3 c, float r, int sides = 12)
        {
            int rings = Mathf.Max(4, sides / 2);
            var prof = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * i / rings;
                prof[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            Lathe(c, Quaternion.identity, prof, sides);
        }

        /// <summary>Torus (or arc of one) lying in the local XY plane, centred on c.</summary>
        public void Torus(Vector3 c, Quaternion rot, float R, float r, float arcDeg, int seg = 16, int sides = 8)
        {
            float arc = arcDeg * Mathf.Deg2Rad;
            float a0 = -arc * 0.5f - Mathf.PI * 0.5f;
            int[,] idx = new int[seg + 1, sides + 1];
            for (int i = 0; i <= seg; i++)
            {
                float a = a0 + arc * i / seg;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                Vector3 ctr = dir * R;
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2f;
                    Vector3 n = dir * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    idx[i, j] = V(c + rot * (ctr + n * r), rot * n, new Vector2(i / (float)seg * 4f, j / (float)sides));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < sides; j++)
                    Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1], norms[idx[i, j]] + norms[idx[i + 1, j + 1]]);
        }

        // ------------------------------------------------------------------ profile-based shapes
        static float Area(IList<Vector2> o)
        {
            float a = 0f;
            for (int i = 0; i < o.Count; i++) { Vector2 p = o[i], q = o[(i + 1) % o.Count]; a += p.x * q.y - q.x * p.y; }
            return a * 0.5f;
        }

        static float Cross2(Vector2 a, Vector2 b, Vector2 c) { return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x); }

        static List<int> EarClip(Vector2[] o)
        {
            var result = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < o.Length; i++) idx.Add(i);
            if (Area(o) < 0f) idx.Reverse();
            int guard = o.Length * o.Length + 8;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = o[ia], b = o[ib], c = o[ic];
                    if (Cross2(a, b, c) <= 1e-7f) continue;
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        Vector2 p = o[j];
                        if (Cross2(a, b, p) >= 0f && Cross2(b, c, p) >= 0f && Cross2(c, a, p) >= 0f) { inside = true; break; }
                    }
                    if (inside) continue;
                    result.Add(ia); result.Add(ib); result.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            return result;
        }

        /// <summary>Flat plate from any 2D outline (local XY), extruded along local Z.</summary>
        public void Plate(Vector2[] o, float thick, Vector3 origin, Quaternion rot)
        {
            int n = o.Length;
            float h = thick * 0.5f, sign = Area(o) >= 0f ? 1f : -1f;
            Vector3 nz = rot * Vector3.forward;
            var f = new int[n]; var b = new int[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 uv = o[i] / uvScale;
                f[i] = V(origin + rot * new Vector3(o[i].x, o[i].y, h), nz, uv);
                b[i] = V(origin + rot * new Vector3(o[i].x, o[i].y, -h), -nz, uv);
            }
            var tris = EarClip(o);
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Tri(f[tris[i]], f[tris[i + 1]], f[tris[i + 2]], nz);
                Tri(b[tris[i]], b[tris[i + 1]], b[tris[i + 2]], -nz);
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 p = o[i], q = o[(i + 1) % n], d = (q - p).normalized;
                Vector3 side = rot * (new Vector3(d.y, -d.x, 0f) * sign);
                float l = (q - p).magnitude / uvScale;
                int a = V(origin + rot * new Vector3(p.x, p.y, h), side, new Vector2(0f, 0f));
                int c = V(origin + rot * new Vector3(q.x, q.y, h), side, new Vector2(l, 0f));
                int e = V(origin + rot * new Vector3(q.x, q.y, -h), side, new Vector2(l, thick / uvScale));
                int g = V(origin + rot * new Vector3(p.x, p.y, -h), side, new Vector2(0f, thick / uvScale));
                Quad(a, c, e, g, side);
            }
        }

        /// <summary>Smooth Catmull-Rom path through the points.</summary>
        public static Vector3[] Curve(Vector3[] p, int sub, bool closed = false)
        {
            var r = new List<Vector3>();
            int n = p.Length, segs = closed ? n : n - 1;
            Func<int, Vector3> at = i => closed ? p[(i % n + n) % n] : p[Mathf.Clamp(i, 0, n - 1)];
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < sub; j++)
                {
                    float t = j / (float)sub, t2 = t * t, t3 = t2 * t;
                    Vector3 a = at(i - 1), b = at(i), c = at(i + 1), d = at(i + 2);
                    r.Add(0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3));
                }
            if (!closed) r.Add(p[n - 1]);
            return r.ToArray();
        }

        /// <summary>Round tube along a path (hoops, hoses, handles).</summary>
        public void Tube(Vector3[] pts, float r, int sides, bool closed = false)
        {
            int n = pts.Length, rings = closed ? n + 1 : n;
            Vector3 first = (pts[1] - pts[0]).normalized, up = Mathf.Abs(first.y) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 frame = Vector3.Cross(first, up).normalized;
            var idx = new int[rings, sides + 1];
            float len = 0f;
            for (int i = 0; i < rings; i++)
            {
                Vector3 p = pts[i % n];
                Vector3 prev = closed ? pts[(i + n - 1) % n] : pts[Mathf.Max(0, i - 1)], next = closed ? pts[(i + 1) % n] : pts[Mathf.Min(n - 1, i + 1)];
                Vector3 t = (next - prev).normalized;
                frame = Vector3.ProjectOnPlane(frame, t).normalized;
                Vector3 other = Vector3.Cross(t, frame).normalized;
                if (i > 0) len += (p - pts[(i - 1) % n]).magnitude;
                for (int j = 0; j <= sides; j++)
                {
                    float a = j / (float)sides * Mathf.PI * 2f;
                    Vector3 nrm = frame * Mathf.Cos(a) + other * Mathf.Sin(a);
                    idx[i, j] = V(p + nrm * r, nrm, new Vector2(len / uvScale, j / (float)sides));
                }
            }
            for (int i = 0; i < rings - 1; i++)
                for (int j = 0; j < sides; j++)
                    Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1], norms[idx[i, j]] + norms[idx[i + 1, j + 1]]);
        }

        /// <summary>Flat webbing or sheet swept along a path; "up" picks which way its wide face looks.</summary>
        public void Strap(Vector3[] pts, Vector3 up, float width, float thick)
        {
            int n = pts.Length;
            var top = new Vector3[n]; var bot = new Vector3[n]; var tn = new Vector3[n]; var sd = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (pts[Mathf.Min(n - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
                Vector3 s = Vector3.Cross(t, up).normalized;
                Vector3 u = Vector3.Cross(s, t).normalized;
                if (Vector3.Dot(u, up) < 0f) u = -u;
                sd[i] = s; tn[i] = u; top[i] = pts[i] + u * thick * 0.5f; bot[i] = pts[i] - u * thick * 0.5f;
            }
            float w = width * 0.5f;
            Action<Func<int, Vector3>, Func<int, Vector3>, Func<int, Vector3>> strip = (a, b, nrm) =>
            {
                int pa = -1, pb = -1; float l = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) l += (pts[i] - pts[i - 1]).magnitude;
                    int ia = V(a(i), nrm(i), new Vector2(0f, l / uvScale)), ib = V(b(i), nrm(i), new Vector2(width / uvScale, l / uvScale));
                    if (i > 0) Quad(pa, pb, ib, ia, nrm(i));
                    pa = ia; pb = ib;
                }
            };
            strip(i => top[i] - sd[i] * w, i => top[i] + sd[i] * w, i => tn[i]);
            strip(i => bot[i] - sd[i] * w, i => bot[i] + sd[i] * w, i => -tn[i]);
            strip(i => top[i] + sd[i] * w, i => bot[i] + sd[i] * w, i => sd[i]);
            strip(i => top[i] - sd[i] * w, i => bot[i] - sd[i] * w, i => -sd[i]);
        }

        /// <summary>Raw double-sided quad strip helper for fabric surfaces.</summary>
        public int AddVertex(Vector3 p, Vector3 n, Vector2 uv) { return V(p, n, uv); }
        public void AddTri(int a, int b, int c, Vector3 hint) { Tri(a, b, c, hint); }
        public Vector3 Normal(int i) { return norms[i]; }

        public Mesh Build(string name)
        {
            var m = new Mesh();
            m.name = name;
            if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.subMeshCount = slots.Count;
            for (int i = 0; i < slots.Count; i++) m.SetTriangles(slots[i], i);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }

    /// <summary>Material and procedural texture helpers (all cached).</summary>
    public static class MatLib
    {
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Texture2D fabric, stripes, soft, white, canvas;

        public static Shader FindShader(params string[] names)
        {
            foreach (var n in names)
            {
                Shader s = Shader.Find(n);
                if (s != null) return s;
            }
            return Shader.Find("Diffuse");
        }

        static string Key(string kind, Color c) { return kind + ColorUtility.ToHtmlStringRGBA(c); }

        /// <summary>Bumped specular (normal-mapped) material; falls back to the plain one without the shader.</summary>
        public static Material Bumped(Color c, float gloss, Texture2D tex, Texture2D normal)
        {
            string key = Key("bump" + gloss.ToString("F2") + (tex != null ? tex.name : "") + (normal != null ? normal.name : ""), c);
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            Shader sh = Shader.Find("KSP/Bumped Specular");
            if (sh == null || normal == null) return Solid(c, gloss, tex);
            m = new Material(sh);
            m.name = "EjectionSeat_" + key;
            m.SetColor("_Color", c);
            if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(gloss, gloss, gloss, 1f));
            if (m.HasProperty("_Shininess")) m.SetFloat("_Shininess", Mathf.Lerp(0.6f, 0.08f, gloss));
            m.mainTexture = tex != null ? tex : White;
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", normal);
            cache[key] = m;
            return m;
        }

        /// <summary>
        /// Builds a tangent-space normal map from a height function. Encoded so it decodes correctly with both
        /// Unity's DXT5nm (xy = AG) and RG unpacking: R=1, G=y, B=y, A=x.
        /// </summary>
        static Texture2D NormalFromHeight(string name, int N, float strength, Func<int, int, float> h)
        {
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, true);
            t.name = name;
            t.wrapMode = TextureWrapMode.Repeat;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (h((x - 1 + N) % N, y) - h((x + 1) % N, y)) * strength;
                    float dy = (h(x, (y - 1 + N) % N) - h(x, (y + 1) % N)) * strength;
                    Vector3 n = new Vector3(dx, dy, 1f).normalized;
                    float nx = n.x * 0.5f + 0.5f, ny = n.y * 0.5f + 0.5f;
                    px[y * N + x] = new Color(1f, ny, ny, nx);
                }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        static Texture2D fabricN, panelN, ejectDecal;

        static float FabricHeight(int x, int y, int N)
        {
            float v = Mathf.Repeat(y / (float)N * 4f, 1f);
            float pad = Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(v * Mathf.PI)));       // soft stitched channels
            float weave = ((x + y) & 1) == 0 ? 0.006f : 0f;
            return pad + weave;
        }

        public static Texture2D FabricNormal
        {
            get
            {
                if (fabricN == null) { const int N = 128; fabricN = NormalFromHeight("es_fabric_n", N, 0.75f, (x, y) => FabricHeight(x, y, N)); }
                return fabricN;
            }
        }

        public static Texture2D PanelNormal
        {
            get
            {
                if (panelN == null)
                {
                    const int N = 256;
                    panelN = NormalFromHeight("es_panel_n", N, 3f, (x, y) =>
                    {
                        float hgt = 0f;
                        int sx = x % 128, sy = y % 128;
                        if (sx <= 1 || sy <= 1) hgt -= 1f;                                   // recessed seams
                        int rx = x % 64, ry = y % 64;
                        foreach (int cx in new[] { 6, 58 })
                            foreach (int cy in new[] { 6, 58 })
                            {
                                float d = Mathf.Sqrt((rx - cx) * (rx - cx) + (ry - cy) * (ry - cy));
                                if (d < 3f) hgt += Mathf.Cos(d / 3f * Mathf.PI * 0.5f) * 1.2f;   // domed rivets
                            }
                        return hgt;
                    });
                }
                return panelN;
            }
        }

        /// <summary>"EJECT" warning plate: hazard border, black field, yellow block letters.</summary>
        public static Texture2D EjectDecal
        {
            get
            {
                if (ejectDecal != null) return ejectDecal;
                const int W = 128, H = 48;
                ejectDecal = new Texture2D(W, H, TextureFormat.RGBA32, true);
                ejectDecal.name = "es_eject";
                ejectDecal.wrapMode = TextureWrapMode.Clamp;
                Color yel = new Color(1f, 0.8f, 0.08f), blk = new Color(0.05f, 0.05f, 0.05f);
                var px = new Color[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        bool border = x < 6 || x >= W - 6 || y < 6 || y >= H - 6;
                        px[y * W + x] = border ? ((((x + y) / 6) % 2 == 0) ? yel : blk) : blk;
                    }
                // 5x7 block font
                string[] E = { "#####", "#....", "#....", "####.", "#....", "#....", "#####" };
                string[] J = { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." };
                string[] C = { ".####", "#....", "#....", "#....", "#....", "#....", ".####" };
                string[] T = { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." };
                var word = new[] { E, J, E, C, T };
                int scale = 3, gx0 = 14, gy0 = 13;
                for (int li = 0; li < word.Length; li++)
                    for (int r = 0; r < 7; r++)
                        for (int c = 0; c < 5; c++)
                        {
                            if (word[li][r][c] != '#') continue;
                            for (int sy = 0; sy < scale; sy++)
                                for (int sx = 0; sx < scale; sx++)
                                {
                                    int x = gx0 + li * (6 * scale) + c * scale + sx;
                                    int y = gy0 + (6 - r) * scale + sy;
                                    if (x < W && y < H) px[y * W + x] = yel;
                                }
                        }
                ejectDecal.SetPixels(px);
                ejectDecal.Apply(true);
                return ejectDecal;
            }
        }

        // ------------------------------------------------------------------ hull paint (stock / ReStock looks)
        static Texture2D hullStock, hullReStock, hullStockN, hullReStockN;
        static float[,] hullNoise;

        static float HullMottle(int x, int y, int N)
        {
            if (hullNoise == null)
            {
                var rnd = new System.Random(21);
                hullNoise = new float[16, 16];
                for (int i = 0; i < 16; i++) for (int j = 0; j < 16; j++) hullNoise[i, j] = (float)rnd.NextDouble();
            }
            float fx = x / (float)N * 16f, fy = y / (float)N * 16f;
            int ix = (int)fx, iy = (int)fy;
            float tx = Mathf.SmoothStep(0f, 1f, fx - ix), ty = Mathf.SmoothStep(0f, 1f, fy - iy);
            return Mathf.Lerp(
                Mathf.Lerp(hullNoise[ix & 15, iy & 15], hullNoise[(ix + 1) & 15, iy & 15], tx),
                Mathf.Lerp(hullNoise[ix & 15, (iy + 1) & 15], hullNoise[(ix + 1) & 15, (iy + 1) & 15], tx), ty);
        }

        /// <summary>Height field: stock = broad clean plates; ReStock = finer plating with fasteners.</summary>
        static float HullHeight(int x, int y, bool restock)
        {
            int period = restock ? 128 : 256;
            float h = (x % period <= 1 || y % period <= 1) ? -1f : 0f;
            if (restock)
            {
                // staggered ribbed plating, like ReStock's capsule skins
                int row = y / 32, off = (row & 1) * 32;
                if (y % 32 < 7 && (x + off) % 64 >= 6) h = Mathf.Min(h, -0.75f);
            }
            if (restock)
            {
                int rx = x % 128, ry = y % 128;
                foreach (int cx in new[] { 9, 119 })
                    foreach (int cy in new[] { 9, 119 })
                    {
                        float d = Mathf.Sqrt((rx - cx) * (rx - cx) + (ry - cy) * (ry - cy));
                        if (d < 2.5f) h += 0.8f * Mathf.Cos(d / 2.5f * Mathf.PI * 0.5f);
                    }
            }
            return h;
        }

        public static Texture2D Hull(bool restock)
        {
            Texture2D t = restock ? hullReStock : hullStock;
            if (t != null) return t;
            const int N = 256;
            t = new Texture2D(N, N, TextureFormat.RGBA32, true);
            t.name = restock ? "es_hull_rs" : "es_hull";
            t.wrapMode = TextureWrapMode.Repeat;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float m = HullMottle(x, y, N);
                    float g = restock ? 0.80f + 0.20f * m : 0.95f + 0.05f * m;
                    if (HullHeight(x, y, restock) < -0.5f) g *= restock ? 0.84f : 0.90f;
                    px[y * N + x] = new Color(g, g, g, 1f);
                }
            t.SetPixels(px); t.Apply(true);
            if (restock) hullReStock = t; else hullStock = t;
            return t;
        }

        public static Texture2D HullNormal(bool restock)
        {
            Texture2D t = restock ? hullReStockN : hullStockN;
            if (t != null) return t;
            t = NormalFromHeight(restock ? "es_hull_rs_n" : "es_hull_n", 256, restock ? 0.9f : 0.8f, (x, y) => HullHeight(x, y, restock));
            if (restock) hullReStockN = t; else hullStockN = t;
            return t;
        }

        // ------------------------------------------------------------------ rocket flame
        static Texture2D flameCore, flameBody;

        static float Ramp(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Flame sheets: U across, V from the nozzle (0) to the tip (1). RGB and alpha carry intensity.</summary>
        static Texture2D FlameTexture(bool core)
        {
            const int W = 64, H = 256;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true);
            t.name = core ? "es_flame_core" : "es_flame_body";
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = y / (H - 1f);
                for (int x = 0; x < W; x++)
                {
                    float u = Mathf.Abs(x / (W - 1f) * 2f - 1f);
                    // Solid-motor flame: no shock diamonds, no taper to a point. A thick, opaque, near-white column
                    // that widens downstream with ragged, churning edges and breaks up into the smoke.
                    float half, i;
                    float wob = Mathf.PerlinNoise(y * 0.045f + (core ? 2f : 9f), 0.5f);              // lumpy outline
                    float churn = 0.6f * Mathf.PerlinNoise(x * 0.11f + 1f, y * 0.035f)
                                + 0.4f * Mathf.PerlinNoise(x * 0.27f + 5f, y * 0.09f);               // turbulent cells
                    if (core)
                    {
                        half = (0.34f + 0.22f * v) * (0.82f + 0.36f * wob);
                        i = (1f - Ramp(0.3f, 1f, v)) * (0.78f + 0.4f * churn);
                    }
                    else
                    {
                        half = (0.42f + 0.5f * Ramp(0f, 0.55f, v)) * (0.78f + 0.4f * wob);
                        i = Mathf.Pow(1f - Ramp(0.1f, 1f, v), 1.25f) * (0.45f + 0.75f * churn);
                    }
                    half = Mathf.Min(half, 0.98f);
                    float edge = 1f - Ramp(half * (core ? 0.45f : 0.3f), Mathf.Max(0.02f, half), u * (0.9f + 0.2f * churn));
                    float a = Mathf.Clamp01(edge * i * Ramp(0f, 0.035f, v + 0.012f));
                    px[y * W + x] = new Color(a, a, a, a);
                }
            }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        public static Texture2D FlameCore { get { if (flameCore == null) flameCore = FlameTexture(true); return flameCore; } }
        public static Texture2D FlameBody { get { if (flameBody == null) flameBody = FlameTexture(false); return flameBody; } }

        public static Material ParticleTex(bool additive, Texture2D tex)
        {
            string key = (additive ? "ptAdd_" : "ptAlpha_") + tex.name;
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(Particle(additive));
            m.mainTexture = tex;
            if (additive) m.renderQueue = 3200;   // flames draw over the smoke they sit inside
            cache[key] = m;
            return m;
        }

        public static Material Solid(Color c, float gloss, Texture2D tex = null)
        {
            string key = Key("solid" + gloss.ToString("F2") + (tex != null ? tex.name : ""), c);
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(FindShader("KSP/Specular", "KSP/Diffuse", "Standard"));
            m.name = "EjectionSeat_" + key;
            m.SetColor("_Color", c);
            if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(gloss, gloss, gloss, 1f));
            if (m.HasProperty("_Shininess")) m.SetFloat("_Shininess", Mathf.Lerp(0.6f, 0.08f, gloss));
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            m.mainTexture = tex != null ? tex : White;
            cache[key] = m;
            return m;
        }

        public static Material Emissive(Color c)
        {
            string key = Key("emis", c);
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(FindShader("KSP/Emissive/Diffuse", "KSP/Unlit", "Unlit/Color"));
            m.SetColor("_Color", c);
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", c);
            if (m.HasProperty("_Emissive")) m.SetTexture("_Emissive", White);
            m.mainTexture = White;
            cache[key] = m;
            return m;
        }

        /// <summary>Flat unlit colour (lines, ropes) - lit shaders shade LineRenderer ribbons badly.</summary>
        public static Material Unlit(Color c)
        {
            string key = Key("unlit", c);
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = new Material(FindShader("KSP/Unlit", "Unlit/Color", "KSP/Emissive/Diffuse", "Sprites/Default"));
            m.SetColor("_Color", c);
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", c);
            m.mainTexture = White;
            cache[key] = m;
            return m;
        }

        public static Material Particle(bool additive)
        {
            string key = additive ? "pAdd" : "pAlpha";
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            Shader sh = additive
                ? FindShader("KSP/Particles/Additive", "Legacy Shaders/Particles/Additive", "Particles/Additive", "Sprites/Default")
                : FindShader("KSP/Particles/Alpha Blended", "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Sprites/Default");
            m = new Material(sh);
            m.mainTexture = SoftDot;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            cache[key] = m;
            return m;
        }

        public static Texture2D White
        {
            get
            {
                if (white == null)
                {
                    white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color[16];
                    for (int i = 0; i < 16; i++) px[i] = Color.white;
                    white.SetPixels(px); white.Apply();
                    white.name = "es_white";
                }
                return white;
            }
        }

        /// <summary>Quilted cushion fabric: diamond stitching with soft shading.</summary>
        public static Texture2D Fabric
        {
            get
            {
                if (fabric == null)
                {
                    const int N = 128;
                    fabric = new Texture2D(N, N, TextureFormat.RGBA32, true);
                    fabric.name = "es_fabric";
                    fabric.wrapMode = TextureWrapMode.Repeat;
                    var rnd = new System.Random(7);
                    var px = new Color[N * N];
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            float v = Mathf.Repeat(y / (float)N * 4f, 1f);
                            float g = 0.91f + 0.09f * Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(v * Mathf.PI)));
                            g *= 0.985f + 0.015f * (float)rnd.NextDouble();
                            px[y * N + x] = new Color(g, g, g, 1f);
                        }
                    fabric.SetPixels(px); fabric.Apply(true);
                }
                return fabric;
            }
        }

        static Texture2D panel;

        /// <summary>Painted metal: soft mottling, faint panel lines and edge wear.</summary>
        public static Texture2D Panel
        {
            get
            {
                if (panel == null)
                {
                    const int N = 256;
                    panel = new Texture2D(N, N, TextureFormat.RGBA32, true);
                    panel.name = "es_panel";
                    panel.wrapMode = TextureWrapMode.Repeat;
                    var rnd = new System.Random(11);
                    var noise = new float[16, 16];
                    for (int i = 0; i < 16; i++) for (int j = 0; j < 16; j++) noise[i, j] = (float)rnd.NextDouble();
                    var px = new Color[N * N];
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            float fx = x / (float)N * 16f, fy = y / (float)N * 16f;
                            int ix = (int)fx, iy = (int)fy;
                            float tx = fx - ix, ty = fy - iy;
                            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                            float a = noise[ix & 15, iy & 15], b = noise[(ix + 1) & 15, iy & 15];
                            float c = noise[ix & 15, (iy + 1) & 15], d = noise[(ix + 1) & 15, (iy + 1) & 15];
                            float mott = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                            float g = 0.9f + 0.1f * mott;
                            if (x % 128 == 0 || y % 128 == 0) g *= 0.8f;                       // panel seams
                            else if ((x % 128 == 1 || y % 128 == 1)) g *= 1.06f;              // seam highlight
                            if ((x % 64 == 6 || x % 64 == 58) && (y % 64 == 6 || y % 64 == 58)) g *= 0.7f; // rivets
                            g *= 0.985f + 0.015f * (float)rnd.NextDouble();
                            px[y * N + x] = new Color(g, g, g, 1f);
                        }
                    panel.SetPixels(px); panel.Apply(true);
                }
                return panel;
            }
        }

        /// <summary>Canopy fabric: fine rip-stop grid.</summary>
        public static Texture2D Canvas
        {
            get
            {
                if (canvas == null)
                {
                    const int N = 64;
                    canvas = new Texture2D(N, N, TextureFormat.RGBA32, true);
                    canvas.name = "es_canvas";
                    canvas.wrapMode = TextureWrapMode.Repeat;
                    var px = new Color[N * N];
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            float g = ((x % 16) == 0 || (y % 16) == 0) ? 0.86f : 1f;
                            if (((x ^ y) & 1) == 0) g *= 0.97f;
                            px[y * N + x] = new Color(g, g, g, 1f);
                        }
                    canvas.SetPixels(px); canvas.Apply(true);
                }
                return canvas;
            }
        }

        /// <summary>Yellow/black diagonal warning stripes for ejection handles.</summary>
        public static Texture2D Stripes
        {
            get
            {
                if (stripes == null)
                {
                    const int N = 64;
                    stripes = new Texture2D(N, N, TextureFormat.RGBA32, true);
                    stripes.name = "es_stripes";
                    stripes.wrapMode = TextureWrapMode.Repeat;
                    var px = new Color[N * N];
                    Color y1 = new Color(1f, 0.82f, 0.08f), k = new Color(0.06f, 0.06f, 0.06f);
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                            px[y * N + x] = (((x + y) / 16) % 2 == 0) ? y1 : k;
                    stripes.SetPixels(px); stripes.Apply(true);
                }
                return stripes;
            }
        }

        static Texture2D puff;

        /// <summary>Billowing smoke puff: a soft round mask with a lumpy noise edge, lighter on top.</summary>
        public static Texture2D Puff
        {
            get
            {
                if (puff == null)
                {
                    const int N = 128;
                    puff = new Texture2D(N, N, TextureFormat.RGBA32, true);
                    puff.name = "es_puff";
                    puff.wrapMode = TextureWrapMode.Clamp;
                    var px = new Color[N * N];
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                            float dx = u * 2f - 1f, dy = v * 2f - 1f;
                            float n = 0f, amp = 0.5f, f = 3f;
                            for (int o = 0; o < 4; o++) { n += amp * Mathf.PerlinNoise(u * f + 11.3f * o, v * f + 7.1f * o); amp *= 0.5f; f *= 2.1f; }
                            float edge = Mathf.Sqrt(dx * dx + dy * dy) + (0.47f - n) * 0.6f;
                            float a = Mathf.Clamp01((0.92f - edge) / 0.35f);
                            a = a * a * (3f - 2f * a);
                            float shade = 0.74f + 0.26f * Mathf.Clamp01(0.5f + dy * 0.55f + (n - 0.47f) * 0.9f);
                            px[y * N + x] = new Color(shade, shade, shade, a * Mathf.Clamp01(0.7f + n * 0.6f));
                        }
                    puff.SetPixels(px); puff.Apply(true);
                }
                return puff;
            }
        }

        public static Texture2D SoftDot
        {
            get
            {
                if (soft == null)
                {
                    const int N = 64;
                    soft = new Texture2D(N, N, TextureFormat.RGBA32, true);
                    soft.name = "es_soft";
                    soft.wrapMode = TextureWrapMode.Clamp;
                    var px = new Color[N * N];
                    var rnd = new System.Random(3);
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.Clamp01(1f - d);
                            a = a * a * (3f - 2f * a);
                            a *= 0.85f + 0.15f * (float)rnd.NextDouble();
                            px[y * N + x] = new Color(1f, 1f, 1f, a);
                        }
                    soft.SetPixels(px); soft.Apply(true);
                }
                return soft;
            }
        }
    }
}
