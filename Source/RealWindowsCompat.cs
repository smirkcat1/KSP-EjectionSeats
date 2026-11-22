using System;
using System.Collections.Generic;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>Keeps an object on a part without parenting it; goes away with the part.</summary>
    public class FollowPart : MonoBehaviour
    {
        public Transform target;

        void LateUpdate()
        {
            if (target == null) { Destroy(gameObject); return; }
            transform.position = target.position;
            transform.rotation = target.rotation;
        }
    }

    /// <summary>
    /// RealWindows paints a separate render of the IVA over each window, so anything that is only in the flight
    /// scene (the seat and the EVA kerbal riding it) disappears behind the glass. RealWindows' interior camera
    /// draws layer 16 live every frame, so a copy of both is kept there at the matching interior position.
    /// No reference to the RealWindows assembly is needed, and nothing is created without it.
    /// </summary>
    public class SeatInteriorMirror : MonoBehaviour
    {
        const int KerbalsLayer = 16;
        const float MaxDistance = 60f;

        static bool? installed;
        public static bool Installed
        {
            get
            {
                if (!installed.HasValue)
                {
                    installed = false;
                    foreach (var a in AssemblyLoader.loadedAssemblies)
                        if (string.Equals(a.name, "RealWindows", StringComparison.OrdinalIgnoreCase)) { installed = true; break; }
                }
                return installed.Value;
            }
        }

        Part cockpit;
        Transform kerbal, seat, kerbalCopy, seatCopy;
        EjectionSettings S;
        readonly List<Transform> src = new List<Transform>();
        readonly List<Transform> dst = new List<Transform>();
        readonly List<KeyValuePair<Renderer, Renderer>> rends = new List<KeyValuePair<Renderer, Renderer>>();
        readonly List<Renderer> seatRends = new List<Renderer>();
        bool lastShown = true;

        public static SeatInteriorMirror Create(Part cockpit, KerbalEVA eva, GameObject seat, SeatVariant variant, EjectionSettings s)
        {
            if (!Installed || cockpit == null || eva == null || seat == null) return null;
            // never parented to the InternalModel: RealWindows would list the copy as cabin scenery and
            // switch it off in the pass that draws layer 16
            var go = new GameObject("EjectionSeat_InteriorMirror");
            go.layer = KerbalsLayer;
            var m = go.AddComponent<SeatInteriorMirror>();
            m.cockpit = cockpit;
            m.kerbal = eva.transform;
            m.seat = seat.transform;
            m.S = s;

            var map = new Dictionary<Transform, Transform>();
            var kc = new GameObject("kerbal");
            kc.layer = KerbalsLayer;
            kc.transform.SetParent(go.transform, false);
            m.kerbalCopy = kc.transform;
            m.Copy(eva.transform, kc.transform, map);
            foreach (var r in eva.GetComponentsInChildren<Renderer>(true))
            {
                Transform t;
                if (r == null || !map.TryGetValue(r.transform, out t)) continue;
                Renderer nr = null;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null)
                {
                    var n = t.gameObject.AddComponent<SkinnedMeshRenderer>();
                    n.sharedMesh = smr.sharedMesh;
                    var bones = new Transform[smr.bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Transform b;
                        bones[i] = smr.bones[i] != null && map.TryGetValue(smr.bones[i], out b) ? b : t;
                    }
                    n.bones = bones;
                    Transform root;
                    if (smr.rootBone != null && map.TryGetValue(smr.rootBone, out root)) n.rootBone = root;
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
                nr.sharedMaterials = r.sharedMaterials;
                nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                nr.receiveShadows = false;
                m.rends.Add(new KeyValuePair<Renderer, Renderer>(r, nr));
            }

            var sm = SeatModel.Build(variant, s.seatScale).root;
            sm.transform.SetParent(go.transform, false);
            foreach (var c in sm.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var t in sm.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = KerbalsLayer;
            foreach (var r in sm.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                m.seatRends.Add(r);
            }
            m.seatCopy = sm.transform;
            m.LateUpdate();
            Log.Dbg("RealWindows seat mirror: " + m.rends.Count + " kerbal renderers, " + m.seatRends.Count + " seat renderers");
            return m;
        }

        void Copy(Transform from, Transform to, Dictionary<Transform, Transform> map)
        {
            map[from] = to;
            src.Add(from);
            dst.Add(to);
            for (int i = 0; i < from.childCount; i++)
            {
                Transform c = from.GetChild(i);
                if (c == seat) continue;              // has its own clean copy, without the rocket effects
                var go = new GameObject(c.name);
                go.layer = KerbalsLayer;
                go.transform.SetParent(to, false);
                Copy(c, go.transform, map);
            }
        }

        void LateUpdate()
        {
            if (cockpit == null || kerbal == null || (kerbal.position - cockpit.transform.position).sqrMagnitude > MaxDistance * MaxDistance)
            {
                Retire();
                return;
            }
            InternalModel im = cockpit.internalModel;
            Show(im != null);
            if (im == null) return;

            Matrix4x4 worldToInternal = InteriorReveal.InternalToWorldMatrix(cockpit, im, S).inverse;
            Quaternion rot = im.transform.rotation * Quaternion.Inverse(cockpit.transform.rotation * Quaternion.Euler(S.internalRotation));
            Pose(kerbalCopy, kerbal, worldToInternal, rot);
            for (int i = 1; i < src.Count; i++)
            {
                Transform a = src[i], b = dst[i];
                if (b == null) continue;
                if (a == null) { if (b.gameObject.activeSelf) b.gameObject.SetActive(false); continue; }
                b.localPosition = a.localPosition;
                b.localRotation = a.localRotation;
                b.localScale = a.localScale;
                if (b.gameObject.activeSelf != a.gameObject.activeSelf) b.gameObject.SetActive(a.gameObject.activeSelf);
            }
            foreach (var kv in rends)
                if (kv.Value != null) kv.Value.enabled = kv.Key != null && kv.Key.enabled;

            bool hasSeat = seat != null;
            if (seatCopy.gameObject.activeSelf != hasSeat) seatCopy.gameObject.SetActive(hasSeat);
            if (hasSeat) Pose(seatCopy, seat, worldToInternal, rot);
        }

        /// <summary>
        /// Switched off, never destroyed mid-flight: RealWindows may still hold these renderers for drawing.
        /// The inactive copy goes away with the scene.
        /// </summary>
        public void Retire()
        {
            enabled = false;
            gameObject.SetActive(false);
        }

        static void Pose(Transform copy, Transform source, Matrix4x4 worldToInternal, Quaternion rot)
        {
            copy.position = worldToInternal.MultiplyPoint3x4(source.position);
            copy.rotation = rot * source.rotation;
        }

        void Show(bool on)
        {
            if (on == lastShown) return;
            lastShown = on;
            kerbalCopy.gameObject.SetActive(on);
            seatCopy.gameObject.SetActive(on);
        }
    }
}
