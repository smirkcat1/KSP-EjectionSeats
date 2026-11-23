using System.Collections.Generic;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Pose layer for the ejecting kerbal, applied on top of KSP's own animation every frame: two-bone IK puts the
    /// hands on the seat's firing handle or the parachute risers, plus head and spine adjustments for the braced
    /// ejection posture. Works on any kerbal that has the stock rig; does nothing if a bone is missing.
    /// Positions are in the kerbal's own frame (x right, y up, z forward) unless noted.
    /// </summary>
    [DefaultExecutionOrder(-50)]   // before mirrors that copy the bones (RealWindows copy, cabin mirror)
    public class KerbalPoser : MonoBehaviour
    {
        public enum Mode { None, Brace, Freefall, Chute }

        public Mode mode = Mode.None;
        public string grip = "pan";        // pan | sides | overhead | lever_r | lever_l
        public ChuteRig chute;

        class Limb { public Transform upper, mid, end, tip, thumb; public float side; }
        Animation anim;
        string chuteClip;
        float nextChuteClip;
        Limb armL, armR, legL, legR;
        Transform spine, neck, head;
        Vector3 headFwdLocal;
        bool ok;

        // smoothed weights
        float wBrace, wFree, wChute, modeAge;
        Mode lastMode = Mode.None;
        float seed;

        public static KerbalPoser Attach(KerbalEVA eva, string grip)
        {
            if (eva == null) return null;
            var p = eva.gameObject.GetComponent<KerbalPoser>() ?? eva.gameObject.AddComponent<KerbalPoser>();
            p.grip = string.IsNullOrEmpty(grip) ? "pan" : grip;
            return p;
        }

        /// <summary>Which firing handle each seat has, in the way a kerbal can reach it.</summary>
        public static string GripFor(string variantId)
        {
            switch (variantId)
            {
                case "modern":
                case "km1": return "sides";         // handles on both sides of the seat pan
                case "ww2": return "lever_r";       // Heinkel firing lever
                case "he219": return "lever_l";
                default: return "pan";              // loop between the knees
            }
        }

        void Awake()
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            armL = MakeLimb(bones, "bn_l_arm01 1", "bn_l_elbow_a01", "bn_l_wrist01");
            armR = MakeLimb(bones, "bn_r_arm01 1", "bn_r_elbow_a01", "bn_r_wrist01");
            Transform tipL, tipR;
            if (armL != null && bones.TryGetValue("bn_l_mid_a01", out tipL)) armL.tip = tipL;
            if (armR != null && bones.TryGetValue("bn_r_mid_a01", out tipR)) armR.tip = tipR;
            Transform thL, thR;
            if (armL != null) { armL.side = 1f; if (bones.TryGetValue("bn_l_thumb_a01", out thL)) armL.thumb = thL; }
            if (armR != null) { armR.side = -1f; if (bones.TryGetValue("bn_r_thumb_a01", out thR)) armR.thumb = thR; }
            legL = MakeLimb(bones, "bn_l_hip01", "bn_l_knee_a01", "bn_l_foot01");
            legR = MakeLimb(bones, "bn_r_hip01", "bn_r_knee_a01", "bn_r_foot01");
            bones.TryGetValue("bn_spD01", out spine);
            bones.TryGetValue("bn_neck01", out neck);
            bones.TryGetValue("bn_headPivot_a01", out head);
            ok = armL != null && armR != null && legL != null && legR != null && spine != null && neck != null && head != null;
            if (!ok) { Log.Dbg("kerbal poser: rig not recognised, poses disabled"); return; }
            headFwdLocal = head.InverseTransformDirection(transform.forward);
            anim = GetComponentInChildren<Animation>();
            seed = Random.value * 100f;
        }

        static Limb MakeLimb(Dictionary<string, Transform> b, string u, string m, string e)
        {
            Transform tu, tm, te;
            if (!b.TryGetValue(u, out tu) || !b.TryGetValue(m, out tm) || !b.TryGetValue(e, out te)) return null;
            return new Limb { upper = tu, mid = tm, end = te };
        }

        Vector3 K(float x, float y, float z) { return transform.TransformPoint(new Vector3(x, y, z)); }
        Vector3 KD(float x, float y, float z) { return transform.TransformDirection(new Vector3(x, y, z)); }

        void LateUpdate()
        {
            if (!ok) return;
            float dt = Time.deltaTime;
            if (mode != lastMode) { lastMode = mode; modeAge = 0f; }
            modeAge += dt;
            wBrace = Mathf.MoveTowards(wBrace, mode == Mode.Brace ? 1f : 0f, dt * 4f);
            wFree = Mathf.MoveTowards(wFree, mode == Mode.Freefall ? 1f : 0f, dt * 5f);
            if (mode != Mode.Chute) chuteClip = null;
            wChute = Mathf.MoveTowards(wChute, mode == Mode.Chute && chute != null ? 1f : 0f, dt * 1.6f);
            if (wBrace + wFree + wChute < 0.001f) return;

            if (wBrace > 0f) Brace(wBrace);
            if (wFree > 0f) Freefall(wFree);
            if (wChute > 0f) Chute(wChute);
        }

        // ------------------------------------------------------------------ poses
        void Brace(float w)
        {
            // straight back against the seat, chin slightly down (head pressed to the headrest)
            Turn(spine, transform.right, -6f * w);
            Turn(neck, transform.right, 5f * w);

            // Targets are wrist positions; each hand is then aimed so it lies on the leg or hangs beside the seat
            // instead of pushing into the suit. Kerbal arms are short (about 0.17 m to the wrist).
            Vector3 lT, rT, lPole, rPole, lHand, rHand;
            const float p = 1f;   // one pose: hands on the handle, pulling it
            Vector3 down = KD(0f, -1f, 0.15f), onKnee = KD(0f, -0.55f, 1f);
            switch (grip)
            {
                case "sides":      // a handle each side of the seat pan, outside the thighs, pulled up
                    lT = K(-0.128f, -0.06f + 0.03f * p, 0f); rT = K(0.128f, -0.06f + 0.03f * p, 0f);
                    lPole = K(-0.35f, 0f, -0.25f); rPole = K(0.35f, 0f, -0.25f);
                    lHand = down; rHand = down;
                    break;
                case "lever_r":    // right hand on the lever beside the seat, pulled back; left hand resting on the thigh
                    rT = K(0.128f, -0.06f + 0.015f * p, -0.04f * p); rPole = K(0.35f, 0f, -0.25f); rHand = down;
                    lT = K(-0.05f, 0.0f, -0.03f); lPole = K(-0.35f, -0.05f, -0.1f); lHand = onKnee;
                    break;
                case "lever_l":
                    lT = K(-0.128f, -0.06f + 0.015f * p, -0.04f * p); lPole = K(-0.35f, 0f, -0.25f); lHand = down;
                    rT = K(0.05f, 0.0f, -0.03f); rPole = K(0.35f, -0.05f, -0.1f); rHand = onKnee;
                    break;
                default:           // loop at the knees: hands together on top of the knees, pulled up towards the chest
                    lT = K(-0.04f, -0.005f + 0.025f * p, -0.005f - 0.015f * p); rT = K(0.04f, -0.005f + 0.025f * p, -0.005f - 0.015f * p);
                    lPole = K(-0.35f, -0.05f, -0.1f); rPole = K(0.35f, -0.05f, -0.1f);
                    lHand = onKnee; rHand = onKnee;
                    break;
            }
            Solve(armL, lT, lPole, w);
            Solve(armR, rT, rPole, w);
            AimHand(armL, lHand, w);
            AimHand(armR, rHand, w);
        }

        void AimHand(Limb l, Vector3 dir, float w)
        {
            if (l == null || l.tip == null || w <= 0f) return;
            Vector3 cur = l.tip.position - l.end.position;
            if (cur.sqrMagnitude < 1e-8f) return;
            Quaternion d = Quaternion.FromToRotation(cur, dir);
            l.end.rotation = Quaternion.Slerp(Quaternion.identity, d, w) * l.end.rotation;
            // twist round the fingers so the thumb points forward and inwards on both hands alike (KSP's seated
            // animation rolls the two hands differently)
            if (l.thumb == null) return;
            Vector3 f = (l.tip.position - l.end.position).normalized;
            Vector3 th = Vector3.ProjectOnPlane(l.thumb.position - l.end.position, f);
            Vector3 want = Vector3.ProjectOnPlane(KD(0.6f * l.side, 0f, 0.8f), f);
            if (th.sqrMagnitude < 1e-10f || want.sqrMagnitude < 1e-10f) return;
            float twist = Vector3.SignedAngle(th, want, f);
            l.end.rotation = Quaternion.AngleAxis(twist * w, f) * l.end.rotation;
        }
        void Freefall(float w)
        {
            // KSP's own animation does the body; just keep the head up, looking ahead
            Turn(neck, transform.right, -8f * w);
        }

        void Chute(float w)
        {
            // KSP's own parachute animations: braking while the canopy opens, then hanging, with the odd
            // look round (the turn clips)
            if (anim != null && Time.time >= nextChuteClip)
            {
                string want;
                float hold;
                if (chute.OpenFraction < 0.9f) { want = "chute_decelerate"; hold = 0.5f; }
                else if (chuteClip == "chute_idle" && Random.value < 0.45f) { want = Random.value < 0.5f ? "chute_leftTurn" : "chute_rightTurn"; hold = 1.6f; }
                else { want = "chute_idle"; hold = Random.Range(3.5f, 7f); }
                if (anim[want] != null && want != chuteClip)
                {
                    anim[want].wrapMode = WrapMode.Loop;
                    anim.CrossFade(want, 0.5f, PlayMode.StopAll);
                    chuteClip = want;
                }
                nextChuteClip = Time.time + hold;
            }
            // glance up at the canopy while it opens
            if (chute.OpenFraction < 0.98f) Look(chute.CanopyCenter - head.position, 35f, w * 0.7f);
        }

        // ------------------------------------------------------------------ helpers
        void Turn(Transform bone, Vector3 worldAxis, float degrees)
        {
            if (Mathf.Abs(degrees) < 0.01f) return;
            bone.rotation = Quaternion.AngleAxis(degrees, worldAxis) * bone.rotation;
        }

        void Look(Vector3 dir, float maxAngle, float w)
        {
            if (dir.sqrMagnitude < 1e-6f) return;
            Vector3 fwd = head.TransformDirection(headFwdLocal);
            Vector3 want = Vector3.RotateTowards(transform.forward, dir.normalized, maxAngle * Mathf.Deg2Rad, 0f);
            Quaternion d = Quaternion.FromToRotation(fwd, want);
            head.rotation = Quaternion.Slerp(Quaternion.identity, d, w) * head.rotation;
        }

        /// <summary>Two-bone IK: puts the limb's end on the target, bending towards the pole point.</summary>
        static void Solve(Limb l, Vector3 target, Vector3 pole, float w)
        {
            if (l == null || w <= 0f) return;
            Quaternion u0 = l.upper.rotation, m0 = l.mid.rotation;
            Vector3 a = l.upper.position, b = l.mid.position, c = l.end.position;
            float la = (b - a).magnitude, lb = (c - b).magnitude;
            if (la < 1e-4f || lb < 1e-4f) return;
            Vector3 toT = target - a;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 1e-3f, (la + lb) * 0.999f);
            Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (c - a).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole - a, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(b - a, dir);
            if (bend.sqrMagnitude < 1e-8f) return;
            bend.Normalize();
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            Vector3 elbow = a + dir * (la * cosA) + bend * (la * Mathf.Sqrt(1f - cosA * cosA));
            Vector3 endPos = a + dir * d;

            l.upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * u0;
            Vector3 b2 = l.mid.position, c2 = l.end.position;
            l.mid.rotation = Quaternion.FromToRotation(c2 - b2, endPos - b2) * l.mid.rotation;
            if (w < 0.999f)
            {
                Quaternion u1 = l.upper.rotation, m1 = l.mid.rotation;
                l.upper.rotation = Quaternion.Slerp(u0, u1, w);
                // mid relative to its parent chain: re-solve the blend in local space
                Quaternion mLocal1 = Quaternion.Inverse(u1) * m1, mLocal0 = Quaternion.Inverse(u0) * m0;
                l.mid.rotation = l.upper.rotation * Quaternion.Slerp(mLocal0, mLocal1, w);
            }
        }
    }
}
