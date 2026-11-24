using System.Collections.Generic;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Per-axis scale of a part's model relative to its prefab. TweakScale and StretchScale both work by
    /// scaling the part's "model" transform (StretchScale on top of TweakScale), so this covers both
    /// without depending on either. Neither scales the IVA, so IVA-derived positions get multiplied by this.
    /// </summary>
    public static class PartScale
    {
        public static Vector3 Of(Part p)
        {
            if (p == null) return Vector3.one;
            Transform m = p.transform.Find("model");
            if (m == null) return Vector3.one;
            Vector3 baseScale = Vector3.one;
            if (p.partInfo != null && p.partInfo.partPrefab != null)
            {
                Transform pm = p.partInfo.partPrefab.transform.Find("model");
                if (pm != null) baseScale = pm.localScale;
            }
            Vector3 s = m.localScale;
            Vector3 r = new Vector3(Div(s.x, baseScale.x), Div(s.y, baseScale.y), Div(s.z, baseScale.z));
            // model axes are normally aligned with the part; if not, fall back to a uniform average
            if (Quaternion.Angle(m.localRotation, Quaternion.identity) > 1f)
            {
                float avg = (r.x + r.y + r.z) / 3f;
                r = new Vector3(avg, avg, avg);
            }
            return r;
        }

        static float Div(float a, float b) { return Mathf.Abs(b) < 1e-5f ? 1f : Mathf.Abs(a / b); }

        public static bool IsScaled(Part p)
        {
            Vector3 s = Of(p);
            return Mathf.Abs(s.x - 1f) > 0.001f || Mathf.Abs(s.y - 1f) > 0.001f || Mathf.Abs(s.z - 1f) > 0.001f;
        }
    }

    /// <summary>
    /// A live copy of an IVA kerbal placed in the world interior copy, so a kerbal who climbs back into an
    /// opened cockpit can be seen through the hole. Mirrors the IVA rig's bone transforms every frame, so
    /// the kerbal's idle animations, expressions and helmet all show up.
    /// </summary>
    public class KerbalMirror : MonoBehaviour
    {
        public Kerbal source;
        public int RendererCount { get { return rends.Count; } }

        /// <summary>
        /// False once KSP has replaced the kerbal's model (Kerbal.Start runs SetupKerbalModel a frame after
        /// the kerbal is seated) - then this mirror points at destroyed objects and must be rebuilt.
        /// </summary>
        public bool Valid
        {
            get
            {
                if (source == null || rends.Count == 0) return false;
                foreach (var kv in rends) if (kv.Key == null) return false;
                return Mirrorable(source) == rends.Count;
            }
        }

        /// <summary>Number of renderers on the kerbal that a mirror copies (skinned meshes + meshes with a mesh).</summary>
        static int Mirrorable(Kerbal k)
        {
            int n = 0;
            foreach (var r in k.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer) n++;
                else if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) n++; }
            }
            return n;
        }
        InternalModel im;
        Quaternion conv;
        readonly List<Transform> src = new List<Transform>();
        readonly List<Transform> dst = new List<Transform>();
        readonly List<KeyValuePair<Renderer, Renderer>> rends = new List<KeyValuePair<Renderer, Renderer>>();

        public static KerbalMirror Create(Kerbal k, Transform interiorRoot, InternalModel im, EjectionSettings s)
        {
            var go = new GameObject("EjectionSeat_KerbalMirror_" + k.name);
            go.layer = 0;
            go.transform.SetParent(interiorRoot, false);
            var m = go.AddComponent<KerbalMirror>();
            m.source = k;
            m.im = im;
            m.conv = Quaternion.Euler(s.internalRotation);
            var map = new Dictionary<Transform, Transform>();
            m.Copy(k.transform, go.transform, map, true);

            foreach (var r in k.GetComponentsInChildren<Renderer>(true))
            {
                Transform t;
                if (!map.TryGetValue(r.transform, out t)) continue;
                var smr = r as SkinnedMeshRenderer;
                Renderer nr = null;
                if (smr != null)
                {
                    var n = t.gameObject.AddComponent<SkinnedMeshRenderer>();
                    n.sharedMesh = smr.sharedMesh;
                    var bones = new Transform[smr.bones.Length];
                    int missing = 0;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Transform b;
                        bones[i] = smr.bones[i] != null && map.TryGetValue(smr.bones[i], out b) ? b : null;
                        if (bones[i] == null) { bones[i] = t; missing++; }   // never leave a null bone
                    }
                    if (missing > 0) Log.Dbg("mirror " + k.name + ": " + r.name + " had " + missing + " bones outside the kerbal rig");
                    n.bones = bones;
                    Transform rb;
                    if (smr.rootBone != null && map.TryGetValue(smr.rootBone, out rb)) n.rootBone = rb;
                    n.updateWhenOffscreen = true;
                    n.quality = smr.quality;
                    nr = n;
                }
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    t.gameObject.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    nr = t.gameObject.AddComponent<MeshRenderer>();
                }
                if (nr == null) continue;
                var mats = r.sharedMaterials;
                var outMats = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++) outMats[i] = InteriorReveal.ToWorldMaterial(mats[i], s);
                nr.sharedMaterials = outMats;
                m.rends.Add(new KeyValuePair<Renderer, Renderer>(r, nr));
            }
            m.LateUpdate();
            m.created = Time.time;
            return m;
        }

        float created;
        bool reported;

        void Update()
        {
            // one-off visibility report (debug) so problems show up in KSP.log
            if (reported || Time.time - created < 1f) return;
            if (!EjectionSettings.Get().debug) { reported = true; return; }
            reported = true;
            int vis = 0;
            Bounds b = new Bounds(transform.position, Vector3.zero);
            foreach (var kv in rends)
                if (kv.Value != null && kv.Value.enabled && kv.Value.gameObject.activeInHierarchy) { vis++; b.Encapsulate(kv.Value.bounds); }
            Log.Info("mirror " + name + ": " + vis + "/" + rends.Count + " renderers visible, bounds " + b.center.ToString("F2") + " size " + b.size.ToString("F2") +
                     ", root " + transform.position.ToString("F2"));
        }

        void Copy(Transform from, Transform to, Dictionary<Transform, Transform> map, bool isRoot)
        {
            map[from] = to;
            src.Add(from);
            dst.Add(to);
            // copy every transform (only empty GameObjects are created, so cameras/lights aren't duplicated);
            // skipping a branch would leave skinned-mesh bones missing and the mesh wouldn't render
            for (int i = 0; i < from.childCount; i++)
            {
                Transform c = from.GetChild(i);
                var go = new GameObject(c.name);
                go.layer = 0;
                go.transform.SetParent(to, false);
                Copy(c, go.transform, map, false);
            }
        }

        const float DrawDistance = 150f;   // further than this the cabin interior can't be made out
        bool farHidden;

        static Camera ViewCamera()
        {
            if (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null) return FlightCamera.fetch.mainCamera;
            return Camera.main;
        }

        void LateUpdate()
        {
            if (source == null || im == null) { Destroy(gameObject); return; }
            // far away: no bone copying and nothing drawn (skinned meshes here skin even when off screen)
            Camera cam = ViewCamera();
            bool far = cam != null && (cam.transform.position - transform.position).sqrMagnitude > DrawDistance * DrawDistance;
            if (far != farHidden)
            {
                farHidden = far;
                foreach (var kv in rends) if (kv.Value != null) kv.Value.enabled = !far && kv.Key != null;
            }
            if (far) return;
            // root: IVA pose -> interior-root local (same mapping as the interior copy)
            Transform s0 = src[0];
            Quaternion inv = Quaternion.Inverse(im.transform.rotation);
            transform.localPosition = conv * (inv * (s0.position - im.transform.position));
            transform.localRotation = conv * (inv * s0.rotation);
            Vector3 rs = im.transform.lossyScale, ls = s0.lossyScale;
            transform.localScale = new Vector3(ls.x / Safe(rs.x), ls.y / Safe(rs.y), ls.z / Safe(rs.z));
            for (int i = 1; i < src.Count; i++)
            {
                Transform a = src[i], b = dst[i];
                if (b == null) continue;
                if (a == null)
                {
                    // source part removed (e.g. KSP destroyed the helmet) - hide ours too
                    if (b.gameObject.activeSelf) b.gameObject.SetActive(false);
                    continue;
                }
                b.localPosition = a.localPosition;
                b.localRotation = a.localRotation;
                b.localScale = a.localScale;
                if (b.gameObject.activeSelf != a.gameObject.activeSelf) b.gameObject.SetActive(a.gameObject.activeSelf);
            }
            // note: don't copy renderer.enabled - KSP disables IVA renderers whenever you're not in IVA view,
            // which would make the mirrored kerbal vanish from the outside
            foreach (var kv in rends)
                if (kv.Key == null && kv.Value != null) kv.Value.enabled = false;
        }

        static float Safe(float f) { return Mathf.Abs(f) < 1e-4f ? 1f : f; }
    }
}
