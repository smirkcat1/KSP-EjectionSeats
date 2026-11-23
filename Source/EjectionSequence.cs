using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Lives on the ejected kerbal. Runs: settle into seat -> catapult rail -> rocket burn -> coast ->
    /// seat/man separation -> parachute (1-2 s later) -> landing, then hands control back to the player.
    /// </summary>
    public class EjectionSequence : MonoBehaviour
    {
        // configuration (set by the director)
        public KerbalEVA eva;
        public Part cockpit;
        public SeatVariant variant;
        public EjectionSettings S;
        public float accel;         // m/s^2
        public float burnTime;
        public float railLength;
        public float chuteDelay;
        public float chuteOpenTime = 2f;
        public float startDelay;    // time strapped in before the seat fires (canopy jettison, seat order)
        public bool hasIvaPelvis;   // where the kerbal's pelvis was in the IVA seat (cockpit space)
        public Vector3 ivaPelvisLocal;
        Vector3 fitPelvis;          // pelvis in the EVA kerbal's own frame, seated pose
        bool fitOk;
        public string chuteType = "auto";   // auto | seat | eva
        public Vector3 localStart, localDir, localFwd;   // in cockpit space
        public List<Collider> vesselColliders = new List<Collider>();

        /// <summary>Current step of the sequence (sit, rail, burn, coast, freefall, chute, landed).</summary>
        public string Phase { get; private set; }
        GameObject seat;
        SeatRocketFX fx;
        Animation anim;
        ChuteRig chute;
        KerbalPoser poser;
        SeatInteriorMirror interiorMirror;
        Rigidbody rb;
        bool done;
        Quaternion seatLocalRot;
        Vector3 seatOffset;
        RigidbodyInterpolation origInterp = RigidbodyInterpolation.None;
        float origCrashTol = -1f;
        double origGTol = -1;
        bool burning;
        bool shielded;
        float fuelLeft = 1f;        // share of the seat motor's propellant not yet burnt

        /// <summary>Aero drag of the seat + kerbal combination (applied while KSP aero is off for the kerbal).</summary>
        void SeatDrag() { Drag(S.seatDragArea, S.seatMass); }
        void KerbalDrag() { if (shielded) Drag(S.kerbalDragArea, 0f); }

        /// <summary>Dynamic pressure on the kerbal in kPa.</summary>
        float DynamicPressure()
        {
            if (eva == null || eva.vessel == null) return 0f;
            float sp = (float)eva.vessel.srfSpeed;
            return (float)(0.0005 * eva.vessel.atmDensity * sp * sp);
        }

        /// <summary>
        /// The attitude the airflow (or chute) should settle the kerbal into: head towards "hint", but never
        /// reclined further than seatMaxRecline from upright, so high-speed flow can't leave them head-first.
        /// </summary>
        Vector3 StableUp(Vector3 hint)
        {
            Vector3 up = eva.vessel.upAxis;
            if (Vector3.Dot(hint, up) < -0.98f) hint = Vector3.ProjectOnPlane(-transform.forward, up).normalized;
            return Vector3.RotateTowards(up, hint, S.seatMaxRecline * Mathf.Deg2Rad, 0f).normalized;
        }

        Vector3 WindUp()
        {
            Vector3 v = eva.vessel.srf_velocity;
            float sp = v.magnitude;
            return sp > 2f ? -v / sp : (Vector3)eva.vessel.upAxis;
        }

        /// <summary>
        /// Critically damped turn towards "want". The push grows with the angle, so being exactly opposite
        /// the target is not a resting point; there it pitches back, the way a seat's drogue pulls it.
        /// </summary>
        void SteerUp(Vector3 want, float k)
        {
            Vector3 up = transform.up;
            float ang = Vector3.Angle(up, want) * Mathf.Deg2Rad;
            Vector3 axis = Vector3.Cross(up, want);
            if (axis.sqrMagnitude < 1e-4f) axis = ang > 1f ? -transform.right : Vector3.zero;
            else axis.Normalize();
            rb.AddTorque(axis * (ang * k) - rb.angularVelocity * (2f * Mathf.Sqrt(k)), ForceMode.Acceleration);
        }

        void Drag(float cdA, float extraMass)
        {
            if (rb == null || eva == null || eva.vessel == null) return;
            Vessel v = eva.vessel;
            double rho = v.atmDensity;
            if (rho <= 0) return;
            Vector3 vSrf = v.srf_velocity;
            float sp = vSrf.magnitude;
            if (sp < 0.1f) return;
            float totalMass = rb.mass + extraMass;                      // tonnes
            float fKN = (float)(0.5 * rho * sp * sp * cdA / 1000.0);
            float accel = fKN / totalMass;                              // m/s^2 on the combination
            rb.AddForce(-vSrf / sp * accel, ForceMode.Acceleration);
        }

        // ---- collisions with the ejecting craft ----
        // Kept off until the kerbal is genuinely clear of every part of the craft (not just after a fixed
        // time): at steep seat angles or high speed the seat can still be alongside the fuselage/tail after
        // several seconds, and switching collisions back on inside it was crashing kerbals into the plane.
        bool collisionsRestored;
        float clearFor;
        float runStart = -1f;

        bool OverlapsVessel()
        {
            Vector3 p = transform.position;
            foreach (var c in vesselColliders)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                Bounds b = c.bounds;
                b.Expand(4f);                 // 2 m margin on every side
                if (b.Contains(p)) return true;
            }
            return false;
        }

        void CheckClearance()
        {
            if (collisionsRestored || eva == null || vesselColliders.Count == 0) return;
            if (runStart < 0f || Time.time - runStart < S.restoreCollisionsAfter) return;
            if (OverlapsVessel()) { clearFor = 0f; return; }
            clearFor += Time.fixedDeltaTime;
            if (clearFor < 1f) return;
            IgnoreVessel(false);
            collisionsRestored = true;
            Log.Dbg("kerbal clear of the craft - collisions restored after " + (Time.time - runStart).ToString("F1") + " s");
        }

        // ---- kerbal's own EVA parachute ----
        static int canopiesInstalled = -1;

        /// <summary>True if a mod that reworks stock parachute canopies (C.A.N.O.P.I.E.S.) is loaded.</summary>
        public static bool CanopiesInstalled
        {
            get
            {
                if (canopiesInstalled < 0)
                {
                    canopiesInstalled = 0;
                    foreach (var la in AssemblyLoader.loadedAssemblies)
                    {
                        string n = ((la.name ?? "") + " " + (la.assembly != null ? la.assembly.GetName().Name : "")).ToLowerInvariant();
                        if (n.Contains("canopies")) { canopiesInstalled = 1; Log.Info("C.A.N.O.P.I.E.S. detected (" + la.name + ")"); break; }
                    }
                }
                return canopiesInstalled == 1;
            }
        }

        ModuleEvaChute EvaChute { get { return eva != null && eva.part != null ? eva.part.FindModuleImplementing<ModuleEvaChute>() : null; } }

        bool UseEvaChute()
        {
            bool want = chuteType == "eva" || (chuteType == "auto" && CanopiesInstalled);
            if (!want) return false;
            if (eva == null || EvaChute == null || !eva.HasParachute)
            {
                Log.Dbg("EVA chute requested but kerbal has none - using the seat chute");
                return false;
            }
            try { return EvaChute.CanCrewMemberUseParachute(); } catch { return true; }
        }

        float savedMinPressure, savedDeployAlt;
        int savedSafe;
        bool evaChuteTweaked;

        bool DeployEvaChute()
        {
            try
            {
                var c = EvaChute;
                Unshield();                 // stock chute drag works through KSP aero
                eva.enabled = true;         // KerbalEVA runs its own semi/fully-deployed chute states
                ForceRecover();
                savedMinPressure = c.minAirPressureToOpen;
                savedDeployAlt = c.deployAltitude;
                savedSafe = c.automateSafeDeploy;
                evaChuteTweaked = true;
                c.minAirPressureToOpen = 0.0001f;   // open now, whatever the altitude
                c.deployAltitude = 100000f;
                c.automateSafeDeploy = 0;
                c.Deploy();
                Log.Info("deployed " + eva.vessel.vesselName + "'s EVA parachute (" + c.deploymentState + ")" +
                         (CanopiesInstalled ? " - C.A.N.O.P.I.E.S. cloth" : ""));
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("EVA chute deploy failed, using the seat chute: " + e.Message);
                return false;
            }
        }

        void RestoreEvaChute()
        {
            if (!evaChuteTweaked || eva == null || EvaChute == null) return;
            evaChuteTweaked = false;
            EvaChute.minAirPressureToOpen = savedMinPressure;
            EvaChute.deployAltitude = savedDeployAlt;
            EvaChute.automateSafeDeploy = savedSafe;
        }

        float origStumble = -1f;

        void NoRagdoll(bool on)
        {
            if (eva == null) return;
            try
            {
                if (on)
                {
                    if (origStumble < 0f) origStumble = eva.stumbleThreshold;
                    eva.stumbleThreshold = 1e6f;
                }
                else if (origStumble >= 0f)
                {
                    eva.stumbleThreshold = origStumble;
                    origStumble = -1f;
                }
            }
            catch (Exception e) { Log.Dbg("stumble threshold: " + e.Message); }
        }

        /// <summary>If the kerbal is somehow ragdolling, bring it back to a normal state.</summary>
        void ForceRecover()
        {
            if (eva == null || eva.fsm == null) return;
            try
            {
                if (eva.isRagdoll || eva.fsm.CurrentState == eva.st_ragdoll)
                {
                    eva.fsm.RunEvent(eva.On_recover_start);
                    Log.Dbg("forced ragdoll recovery");
                }
            }
            catch (Exception e) { Log.Dbg("recover failed: " + e.Message); }
        }

        void Unshield()
        {
            if (shielded && eva != null && eva.part != null) eva.part.ShieldedFromAirstream = false;
            shielded = false;
        }
        float restoreTolAt = -1f;

        public static EjectionSequence Attach(KerbalEVA eva)
        {
            return eva.gameObject.AddComponent<EjectionSequence>();
        }

        static readonly List<EjectionSequence> running = new List<EjectionSequence>();

        void Start()
        {
            running.Add(this);
            StartCoroutine(Run());
        }

        /// <summary>Crew leaving side by side must not knock each other around inside the cockpit.</summary>
        void IgnoreOtherCrew()
        {
            var mine = eva.GetComponentsInChildren<Collider>(true);
            foreach (var other in running)
            {
                if (other == null || other == this || other.eva == null) continue;
                foreach (var theirs in other.eva.GetComponentsInChildren<Collider>(true))
                    foreach (var c in mine)
                        if (c != null && theirs != null) Physics.IgnoreCollision(c, theirs, true);
            }
        }

        void OnDestroy()
        {
            running.Remove(this);
            ShowGear();
            if (poser != null) Destroy(poser);
            // never leave a kerbal without its controller
            if (eva != null && !eva.enabled) eva.enabled = true;
            Unshield();
            NoRagdoll(false);
            RestoreEvaChute();
            if (interiorMirror != null) interiorMirror.Retire();
            if (eva != null && eva.part != null && origCrashTol > 0f) eva.part.crashTolerance = origCrashTol;
            if (eva != null && eva.part != null && origGTol > 0) eva.part.gTolerance = origGTol;
            if (seat != null && seat.transform.parent == transform) Destroy(seat);
        }

        // ------------------------------------------------------------------ helpers
        Quaternion CockpitRot { get { return cockpit.transform.rotation * Quaternion.LookRotation(localFwd, localDir); } }
        Vector3 CockpitPoint(Vector3 local) { return cockpit.transform.TransformPoint(local); }
        // Only "gone" if the cockpit object/rigidbody no longer exists. (KSP can flag a vessel packed for a
        // frame while switching focus; treating that as "dead" used to drop the kerbal off the rail.)
        bool CockpitAlive
        {
            get
            {
                bool alive = cockpit != null && cockpit.rb != null && cockpit.gameObject.activeInHierarchy;
                if (alive && (cockpit.packed || cockpit.vessel == null) && !warnedPacked)
                {
                    warnedPacked = true;
                    Log.Info("cockpit reported packed/vesselless during ejection - still tracking it");
                }
                return alive;
            }
        }
        bool warnedPacked;

        // ---- diagnostics: per-frame trace for the first seconds (debug = true) ----
        float traceT;
        int traceN;
        void Trace(string phase)
        {
            if (!S.debug || traceT > 4f) return;
            traceT += Time.fixedDeltaTime;
            if ((traceN++ % 5) != 0) return;
            string c = "cockpit=gone";
            if (cockpit != null && cockpit.rb != null && rb != null)
            {
                Vector3 target = cockpit.transform.TransformPoint(localStart);
                c = "dSeat=" + (rb.position - target).magnitude.ToString("F2") +
                    " relV=" + (rb.velocity - cockpit.rb.GetPointVelocity(rb.position)).magnitude.ToString("F1") +
                    " cockpitV=" + cockpit.rb.velocity.magnitude.ToString("F1") +
                    " cPacked=" + cockpit.packed;
            }
            Log.Info("trace t=" + traceT.ToString("F2") + " " + phase + " " + c +
                     " kPacked=" + (eva != null && eva.vessel != null ? eva.vessel.packed.ToString() : "?") +
                     " active=" + (FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.vesselName : "-") +
                     " frameV=" + Krakensbane.GetFrameVelocity().magnitude.ToString("F1") +
                     " ts=" + Time.timeScale.ToString("F2") + " fdt=" + Time.fixedDeltaTime.ToString("F4"));
        }

        void Place(Vector3 pos, Quaternion rot)
        {
            transform.position = pos;
            transform.rotation = rot;
            if (rb != null) { rb.position = pos; rb.rotation = rot; }
        }

        void Play(string key, float fade)
        {
            if (anim == null || string.IsNullOrEmpty(key)) return;
            string clip = ResolveClip(key);
            if (clip == null || anim[clip] == null) { Log.Dbg("animation '" + key + "' not found"); return; }
            anim[clip].wrapMode = WrapMode.Loop;
            anim.CrossFade(clip, fade, PlayMode.StopAll);
        }

        /// <summary>Accepts a KerbalAnimationManager field name (e.g. "seatIdle") or a raw clip name.</summary>
        string ResolveClip(string key)
        {
            try
            {
                if (eva.Animations != null)
                {
                    FieldInfo f = typeof(KerbalAnimationManager).GetField(key, BindingFlags.Public | BindingFlags.Instance);
                    if (f != null)
                    {
                        var st = f.GetValue(eva.Animations) as KerbalAnimationState;
                        if (st != null)
                        {
                            string n = st.GetAnimationString();
                            if (!string.IsNullOrEmpty(n) && anim[n] != null) return n;
                            if (!string.IsNullOrEmpty(st.animationName) && anim[st.animationName] != null) return st.animationName;
                        }
                    }
                }
            }
            catch { }
            if (anim[key] != null) return key;
            // fuzzy: first clip containing the key
            string lk = key.ToLowerInvariant();
            foreach (AnimationState a in anim) if (a.name.ToLowerInvariant().Contains(lk)) return a.name;
            return null;
        }

        /// <summary>
        /// Steers the kerbal onto a target pose through its velocity (plus a small position correction)
        /// instead of teleporting, so rigidbody interpolation keeps it glued to the craft on screen.
        /// </summary>
        // Position/rotation corrections are capped: dividing by the physics step blows up in slow motion
        // (BetterTimeWarp shrinks fixedDeltaTime), which used to fling the kerbal at record speed.
        const float MaxCorrectionSpeed = 4f;       // m/s on top of the rail velocity
        const float MaxCorrectionSpin = 6f;        // rad/s on top of the cockpit's spin
        const float TeleportDistance = 0.5f;

        void Track(Vector3 pos, Quaternion rot, Vector3 baseVel, Vector3 baseAngVel, float dt)
        {
            Vector3 err = pos - rb.position;
            if (err.sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                Place(pos, rot);
                err = Vector3.zero;
            }
            Vector3 corr = Vector3.ClampMagnitude(err * (0.5f / Mathf.Max(dt, 0.02f)), MaxCorrectionSpeed);
            rb.velocity = baseVel + corr;
            TrackRotation(rot, baseAngVel, dt, 0.5f);
            HoldG();
        }

        void TrackRotation(Quaternion rot, Vector3 baseAngVel, float dt, float gain)
        {
            Quaternion dq = rot * Quaternion.Inverse(rb.rotation);
            float ang; Vector3 axis;
            dq.ToAngleAxis(out ang, out axis);
            if (ang > 180f) ang -= 360f;
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) { rb.angularVelocity = baseAngVel; return; }
            if (Mathf.Abs(ang) > 45f) { rb.rotation = rot; transform.rotation = rot; rb.angularVelocity = baseAngVel; return; }
            Vector3 w = axis.normalized * (ang * Mathf.Deg2Rad * gain / Mathf.Max(dt, 0.02f));
            rb.angularVelocity = baseAngVel + Vector3.ClampMagnitude(w, MaxCorrectionSpin);
        }

        /// <summary>
        /// Poses the kerbal in the seated animation for one sample and measures where its pelvis and
        /// backpack are, then returns the seat offset (kerbal local) that puts the cushion under the
        /// pelvis and the back cushion against the pack.
        /// </summary>
        bool TryFitSeat(out Vector3 offset)
        {
            offset = Vector3.zero;
            if (anim == null) return false;
            string clip = ResolveClip(S.animSeated);
            if (clip == null) return false;
            AnimationState st = anim[clip];
            bool wasEnabled = st.enabled; float wasWeight = st.weight; float wasTime = st.time;
            var others = new List<KeyValuePair<AnimationState, float>>();
            foreach (AnimationState a in anim) if (a != st && a.enabled) { others.Add(new KeyValuePair<AnimationState, float>(a, a.weight)); a.weight = 0f; }
            try
            {
                st.enabled = true; st.weight = 1f; st.normalizedTime = 0.25f;
                anim.Sample();

                Transform pelvis = null, hipL = null, hipR = null, pack = null;
                foreach (Transform t in eva.GetComponentsInChildren<Transform>(true))
                {
                    if (t.GetComponentInParent<EjectionGenerated>() != null) continue;
                    string n = t.name.ToLowerInvariant();
                    if (pelvis == null && n.Contains("pelvis")) pelvis = t;
                    else if (hipL == null && n.Contains("hip") && (n.Contains("_l") || n.Contains("l_") || n.Contains("left"))) hipL = t;
                    else if (hipR == null && n.Contains("hip") && (n.Contains("_r") || n.Contains("r_") || n.Contains("right"))) hipR = t;
                    if (pack == null && (n.Contains("jetpack") || n.Contains("backpack"))) pack = t;
                }
                if (eva.JetpackTransform != null) pack = eva.JetpackTransform;
                Vector3 pl;
                if (pelvis != null) pl = transform.InverseTransformPoint(pelvis.position);
                else if (hipL != null && hipR != null) pl = transform.InverseTransformPoint((hipL.position + hipR.position) * 0.5f);
                else return false;

                float backZ = pack != null ? transform.InverseTransformPoint(pack.position).z - S.packHalfDepth : pl.z - 0.15f;
                float suitBack;
                if (TrySuitBack(pl.y, out suitBack)) backZ = suitBack;   // the pack is hidden in the seat
                // back-cushion front face in seat units (see SeatModel): z ~ -0.19 at hip height
                float cushionFront = -0.19f * S.seatScale;
                offset = new Vector3(0f, pl.y - S.pelvisAboveCushion, backZ - cushionFront);
                Log.Dbg("seat fit: pelvis " + pl.ToString("F3") + (pelvis != null ? " (" + pelvis.name + ")" : " (hips)") +
                        " pack " + (pack != null ? pack.name + " z=" + transform.InverseTransformPoint(pack.position).z.ToString("F3") : "none") +
                        " -> offset " + offset.ToString("F3"));
                // sanity: never wildly off
                if (offset.magnitude > 1.2f) return false;
                fitPelvis = pl; fitOk = true;
                return true;
            }
            catch (Exception e) { Log.Dbg("seat fit failed: " + e.Message); return false; }
            finally
            {
                st.enabled = wasEnabled; st.weight = wasWeight; st.time = wasTime;
                foreach (var kv in others) if (kv.Key != null) kv.Key.weight = kv.Value;
            }
        }

        /// <summary>Rearmost point of the suit (not the pack) at lower-back height, in the kerbal's frame.</summary>
        bool TrySuitBack(float pelvisY, out float back)
        {
            back = 0f;
            bool any = false;
            var tmp = new Mesh();
            try
            {
                foreach (var smr in eva.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr == null || smr.sharedMesh == null || !smr.gameObject.activeInHierarchy || IsGear(smr.transform)) continue;
                    if (!smr.name.ToLowerInvariant().Contains("body")) continue;
                    smr.BakeMesh(tmp);
                    Matrix4x4 m = transform.worldToLocalMatrix * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    foreach (var v in tmp.vertices)
                    {
                        Vector3 k = m.MultiplyPoint3x4(v);
                        if (k.y < pelvisY + 0.02f || k.y > pelvisY + 0.16f || Mathf.Abs(k.x) > 0.06f) continue;
                        if (!any || k.z < back) back = k.z;
                        any = true;
                    }
                }
            }
            catch (Exception e) { Log.Dbg("suit back: " + e.Message); any = false; }
            finally { Destroy(tmp); }
            if (any) Log.Dbg("seat fit: suit back z=" + back.ToString("F3"));
            return any;
        }

        void IgnoreVessel(bool ignore)
        {
            if (eva == null) return;
            var mine = eva.GetComponentsInChildren<Collider>(true);
            foreach (var a in mine)
            {
                if (a == null) continue;
                foreach (var b in vesselColliders)
                    if (b != null && a != b) Physics.IgnoreCollision(a, b, ignore);
            }
        }

        void HoldG()
        {
            // teleports during setup always ignore g; the burn itself only if the player opted out
            if (eva == null || eva.vessel == null) return;
            if (!S.kerbalsFeelGForce || !burning) eva.vessel.IgnoreGForces(2);
        }

        // ------------------------------------------------------------------ sequence
        IEnumerator Run()
        {
            Vector3 startPos = CockpitAlive ? CockpitPoint(localStart) : transform.position;
            Quaternion startRot = CockpitAlive ? CockpitRot : transform.rotation;
            Place(startPos, startRot);
            IgnoreVessel(true);
            runStart = Time.time;

            // A fresh EVA vessel can spend a few frames "on rails", where KSP positions it from its orbit
            // (i.e. wherever the craft was a moment ago). Keep the kerbal hidden and force it off rails
            // until we actually control it, so it never pops up behind the plane.
            var hidden = new List<Renderer>();
            foreach (var r in eva.GetComponentsInChildren<Renderer>(true))
                if (r != null && r.enabled) { r.enabled = false; hidden.Add(r); }

            float wait = 0f;
            while ((eva == null || !eva.Ready || eva.part.rb == null || eva.vessel.packed) && wait < 5f)
            {
                wait += Time.fixedDeltaTime;
                if (eva != null && eva.vessel != null && eva.vessel.packed && eva.vessel.loaded)
                {
                    try { eva.vessel.GoOffRails(); } catch { }
                }
                if (eva != null && CockpitAlive) { Place(CockpitPoint(localStart), CockpitRot); if (eva.part.rb != null) eva.part.rb.velocity = cockpit.rb.GetPointVelocity(transform.position); }
                if (eva != null && eva.vessel != null) eva.vessel.IgnoreGForces(3);
                if (S.debug) Log.Info("trace wait: ready=" + (eva != null && eva.Ready) + " rb=" + (eva != null && eva.part.rb != null) + " packed=" + (eva != null && eva.vessel.packed) + " cockpitAlive=" + CockpitAlive);
                yield return new WaitForFixedUpdate();
            }
            if (eva == null) yield break;
            rb = eva.part.rb;
            IgnoreVessel(true);
            IgnoreOtherCrew();

            // render with the same interpolation as the cockpit, otherwise the two are drawn up to one
            // physics step apart (several metres at aircraft speeds)
            origInterp = rb.interpolation;
            if (CockpitAlive) rb.interpolation = cockpit.rb.interpolation;
            Log.Dbg("interpolation kerbal=" + origInterp + " cockpit=" + (CockpitAlive ? cockpit.rb.interpolation.ToString() : "-"));
            if (CockpitAlive)
            {
                Place(CockpitPoint(localStart), CockpitRot);
                rb.velocity = cockpit.rb.GetPointVelocity(rb.position);
                rb.angularVelocity = cockpit.rb.angularVelocity;
            }
            foreach (var r in hidden) if (r != null) r.enabled = true;
            origCrashTol = eva.part.crashTolerance;
            eva.part.crashTolerance = Mathf.Max(origCrashTol, S.kerbalCrashTolerance);
            // the seat itself takes the catapult/rocket load; don't let the part's g limit destroy the kerbal
            origGTol = eva.part.gTolerance;
            if (!S.kerbalsFeelGForce) eva.part.gTolerance = Math.Max(origGTol, 1e6);
            eva.vessel.IgnoreGForces(5);

            // KSP treats a bare EVA kerbal as very draggy (~15 g at aircraft speeds), which threw the
            // seat straight behind the plane. While strapped in, switch KSP aero off for the kerbal and
            // fly seat + kerbal with our own drag (SeatDrag) until separation.
            eva.part.ShieldedFromAirstream = true;
            shielded = true;

            // No ragdolling during the ejection: KerbalEVA.OnCollisionEnter (which Unity calls even while the
            // component is disabled) fires On_stumble -> ragdoll whenever an impact exceeds stumbleThreshold.
            NoRagdoll(true);

            // helmet on: no canopy, rocket blast and windblast
            try { eva.ToggleHelmetAndNeckRing(true, true, true, false); }
            catch (Exception e) { Log.Dbg("helmet toggle failed: " + e.Message); }

            // take over from the EVA state machine while we drive the kerbal
            eva.enabled = false;
            anim = eva.GetComponentInChildren<Animation>();
            if (S.kerbalPoses) poser = KerbalPoser.Attach(eva);
            if (S.debug && anim != null)
            {
                var names = new List<string>();
                foreach (AnimationState a in anim) names.Add(a.name);
                Log.Info("kerbal clips: " + string.Join(", ", names.ToArray()));
            }

            // the seat rides on the kerbal from the first frame, so both always render with the
            // kerbal's own rigidbody interpolation (no lagging behind a fast-moving craft)
            var model = SeatModel.Build(variant, S.seatScale);
            seat = model.root;
            seatLocalRot = Quaternion.Euler(S.kerbalSeatRotation);
            seat.transform.SetParent(transform, false);
            seat.transform.localRotation = seatLocalRot;
            seatOffset = S.kerbalSeatOffset;
            Vector3 fit;
            if (S.autoFitKerbal && TryFitSeat(out fit)) seatOffset = fit + S.kerbalFitAdjust;
            // The EVA kerbal's origin sits at a different height in the body than the IVA kerbal's, so start from
            // where the IVA kerbal's pelvis actually was: kerbal and seat then line up with the cockpit's own seat.
            if (hasIvaPelvis && fitOk)
            {
                Vector3 newStart = ivaPelvisLocal - Quaternion.LookRotation(localFwd, localDir) * fitPelvis;
                Vector3 shift = newStart - localStart;
                if (shift.magnitude < 0.5f) { Log.Dbg("aligned to the IVA kerbal: start moved " + shift.ToString("F3")); localStart = newStart; }
                else Log.Dbg("IVA pelvis " + shift.magnitude.ToString("F2") + " m from the seat start - not aligned");
            }
            seat.transform.localPosition = seatOffset;
            fx = SeatRocketFX.Create(seat, model.nozzles, S, variant);
            HideGear();
            try { interiorMirror = SeatInteriorMirror.Create(cockpit, eva, seat, variant, S); }
            catch (Exception e) { Log.Dbg("interior mirror failed: " + e.Message); }

            // ---- 1. sit down / brace ----

            Phase = "sit";
            // kerbal starts a little above and ahead of the seat cushion and settles in; the seat itself
            // stays fixed in the cockpit (its local offset counter-moves the kerbal's lift)
            // seated from the first frame (they were sitting in the cockpit); just a small settle into the cushion
            Play(S.animSeated, 0f);
            Vector3 liftLocal = new Vector3(0f, 0.035f, 0.02f);
            float t = 0f;
            while (t < Mathf.Max(S.sitDownTime, startDelay))
            {
                // the cockpit was destroyed while waiting (e.g. hitting the ground): fire straight away
                if (!CockpitAlive) { Log.Info("cockpit lost before the seat fired - firing now"); break; }
                float dt = Time.fixedDeltaTime;
                t += dt;
                float k = Mathf.Clamp01(t / S.sitDownTime);
                float e = 1f - (1f - k) * (1f - k);
                if (CockpitAlive)
                {
                    Quaternion r = CockpitRot;
                    Vector3 basePos = CockpitPoint(localStart);
                    Vector3 p = basePos + r * (liftLocal * (1f - e));
                    Track(p, r, cockpit.rb.GetPointVelocity(p), cockpit.rb.angularVelocity, dt);
                }
                seat.transform.localPosition = seatOffset - liftLocal * (1f - e);
                HoldG();
                Trace("sit");
                yield return new WaitForFixedUpdate();
            }
            seat.transform.localPosition = seatOffset;

            // ---- 2. catapult rail ----

            Phase = "rail";
            if (poser != null) poser.mode = KerbalPoser.Mode.Brace;   // the ejecting pose, from the moment the seat fires
            Vector3 seatBase = seat.transform.position;
            Burst.Smoke(seatBase, -transform.up, 25, 0.35f, 4f, 1.2f, new Color(0.85f, 0.85f, 0.85f, 0.8f));
            Burst.Flash(seatBase, 5f, 0.25f);
            float s = 0f, v = 0f, burn = 0f;
            bool plume = false;
            burning = true;
            while (s < railLength && CockpitAlive)
            {
                float dt = Time.fixedDeltaTime;
                v += accel * dt; s += v * dt; burn += dt;
                Quaternion r = CockpitRot;
                Vector3 dirW = cockpit.transform.TransformDirection(localDir);
                Vector3 p = CockpitPoint(localStart) + dirW * s;
                Track(p, r, cockpit.rb.GetPointVelocity(p) + dirW * v, cockpit.rb.angularVelocity, dt);
                if (!plume && s > railLength * 0.55f) { fx.SetThrust(true); plume = true; }
                HoldG();
                Trace("rail s=" + s.ToString("F2") + "/" + railLength.ToString("F2"));
                yield return new WaitForFixedUpdate();
            }
            if (!plume) { fx.SetThrust(true); plume = true; }

            // ---- 3. free rocket burn ----

            Phase = "burn";
            // taken from the cockpit, not the kerbal: another crew member spawning can knock the kerbal's own
            // attitude for a step, and holding that would fire the rocket in the wrong direction
            Quaternion hold = CockpitAlive ? CockpitRot : rb.rotation;
            while (burn < burnTime)
            {
                float dt = Time.fixedDeltaTime;
                burn += dt;
                fuelLeft = Mathf.Clamp01(1f - burn / Mathf.Max(0.01f, burnTime));
                rb.AddForce(transform.up * accel, ForceMode.Acceleration);
                // seat stabiliser: hold attitude while the rocket fires through the CG
                TrackRotation(hold, Vector3.zero, dt, 0.3f);
                SeatDrag();
                HoldG();
                Trace("burn");
                yield return new WaitForFixedUpdate();
            }
            fx.SetThrust(false);
            burning = false;

            // ---- 4. coast: the drogue pitches the seat back gently ----

            Phase = "coast";
            t = 0f;
            float restoreAt = S.restoreCollisionsAfter;
            float sinceStart = burn;
            while (t < S.separationDelay)
            {
                float dt = Time.fixedDeltaTime;
                t += dt; sinceStart += dt;
                float q = DynamicPressure();
                if (q > 0.02f) SteerUp(StableUp(WindUp()), Mathf.Lerp(1.5f, 9f, q / 4f));
                else rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, transform.right * -0.6f, 2f * dt);
                SeatDrag();
                Trace("coast");
                yield return new WaitForFixedUpdate();
            }

            // ---- 5. seat / man separation ----

            Phase = "freefall";
            if (poser != null) poser.mode = KerbalPoser.Mode.Freefall;
            Separate();
            Play(S.animFreefall, 0.3f);

            t = 0f;
            while (t < chuteDelay)
            {
                float dt = Time.fixedDeltaTime;
                t += dt; sinceStart += dt;
                CheckClearance();
                float q = DynamicPressure();
                if (q > 0.02f) SteerUp(StableUp(WindUp()), Mathf.Lerp(1f, 6f, q / 4f));
                else rb.angularVelocity *= Mathf.Clamp01(1f - 0.8f * dt);
                KerbalDrag();
                yield return new WaitForFixedUpdate();
            }

            // ---- 6. parachute ----

            Phase = "chute";
            Vessel vsl = eva.vessel;
            if (vsl == null || vsl.LandedOrSplashed || vsl.atmDensity < S.chuteMinDensity)
            {
                if (vsl != null && !vsl.LandedOrSplashed) ScreenMessages.PostScreenMessage("No atmosphere - " + vsl.vesselName + " is on their own (jetpack available).", 5f);
                Finish();
                yield break;
            }
            // Kerbal's own EVA chute (stock ModuleEvaChute): mods that rework stock parachutes - e.g. the cloth
            // canopies of C.A.N.O.P.I.E.S. - then apply to it automatically.
            if (UseEvaChute() && DeployEvaChute())
            {
                if (poser != null) poser.mode = KerbalPoser.Mode.None;
                while (eva != null && eva.vessel != null && !eva.vessel.LandedOrSplashed && !eva.part.GroundContact && !eva.part.WaterContact)
                {
                    sinceStart += Time.fixedDeltaTime;
                    CheckClearance();
                    yield return new WaitForFixedUpdate();
                }
                RestoreEvaChute();
                Finish();
                yield break;
            }
            DeployChute();
            if (poser != null) { poser.chute = chute; poser.mode = KerbalPoser.Mode.Chute; }
            float mass = rb.mass;
            float cdA = (float)(2.0 * mass * 1000.0 * 9.81 / (1.225 * S.chuteDescentRate * S.chuteDescentRate));
            float settled = 0f;
            bool handedOver = false;
            float sinceHandover = 0f;
            while (true)
            {
                float dt = Time.fixedDeltaTime;
                sinceStart += dt;
                CheckClearance();
                if (eva == null || vsl == null) yield break;
                float agl = AGL(vsl);

                // touchdown: the chute goes away the instant the kerbal touches ground or water
                if (eva.part.GroundContact || eva.part.WaterContact || vsl.LandedOrSplashed || (agl >= 0f && agl < 0.3f)) break;
                if (handedOver)
                {
                    sinceHandover += dt;
                    bool ragdoll = eva.isRagdoll || (eva.fsm != null && eva.fsm.CurrentState == eva.st_ragdoll);
                    if (ragdoll && (agl < 4f || vsl.verticalSpeed > -1f)) break;
                    if (sinceHandover > 1f && Mathf.Abs((float)vsl.verticalSpeed) < 0.3f && vsl.srfSpeed < 1.5) break;
                    if (sinceHandover > 20f) break;
                }

                // near the ground, give the kerbal back to KSP's EVA controller so its own
                // landing / impact handling applies (the chute keeps pulling via the force below)
                if (!handedOver && agl >= 0f && agl < S.handoverAltitude)
                {
                    handedOver = true;
                    if (poser != null) poser.mode = KerbalPoser.Mode.None;
                    eva.enabled = true;
                    ForceRecover();
                    Unshield();   // KSP aero + landing handling from here
                }
                KerbalDrag();

                Vector3 vSrf = vsl.srf_velocity;
                float sp = vSrf.magnitude;
                Vector3 up = sp > 1f ? -vSrf / sp : (Vector3)vsl.upAxis;
                if (chute != null)
                {
                    chute.targetUp = up;
                    float f = (float)(0.5 * vsl.atmDensity * sp * sp * cdA * chute.OpenFraction / 1000.0);
                    f = Mathf.Min(f, mass * 9.81f * S.chuteMaxG);
                    if (sp > 0.05f) rb.AddForce(-vSrf / sp * f, ForceMode.Force);
                }
                // hang under the canopy (torque, never teleporting the rotation into the terrain)
                if (!handedOver)
                {
                    Vector3 hangUp = chute != null ? chute.transform.up : up;
                    SteerUp(StableUp(hangUp), 6f);
                }

                if (vsl.atmDensity < S.chuteMinDensity * 0.5f) settled += dt; else settled = 0f;
                if (settled > 3f) break;
                yield return new WaitForFixedUpdate();
            }

            // ---- 7. touchdown: chute removed immediately ----

            Phase = "landed";
            if (chute != null) Destroy(chute.gameObject);
            chute = null;
            if (!handedOver) Play(S.animLanded, 0.4f);
            Finish();
        }

        // ---- EVA gear (jetpack, parachute pack, backpacks) is hidden while strapped in ----
        readonly List<Renderer> hiddenGear = new List<Renderer>();
        static readonly HashSet<string> GearRoots = new HashSet<string> { "jetpack01", "model", "EVABackpack", "EVAStorage", "EVAStorageSlim", "EVABackpack_standalone" };

        bool IsGear(Transform t)
        {
            for (; t != null && t != eva.transform; t = t.parent)
            {
                if (GearRoots.Contains(t.name)) return true;
                string n = t.name.ToLowerInvariant();
                if (n.Contains("jetpack") || n.Contains("backpack") || n.Contains("parachute") || n.Contains("chute")) return true;
            }
            return false;
        }

        void HideGear()
        {
            if (eva == null) return;
            foreach (var r in eva.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r.GetComponentInParent<EjectionGenerated>() != null || !IsGear(r.transform)) continue;
                r.enabled = false;
                hiddenGear.Add(r);
            }
            Log.Dbg("gear hidden: " + hiddenGear.Count + " renderers");
        }

        void ShowGear()
        {
            foreach (var r in hiddenGear) if (r != null) r.enabled = true;
            hiddenGear.Clear();
        }

        void Separate()
        {
            ShowGear();
            if (seat == null) return;
            Vector3 vel = rb.velocity;
            Vector3 up = transform.up, fwd = transform.forward, right = transform.right;
            seat.transform.SetParent(null, true);

            var col = seat.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.3f, -0.08f) * S.seatScale;
            col.size = new Vector3(0.5f, 1.0f, 0.45f) * S.seatScale;
            foreach (var kc in eva.GetComponentsInChildren<Collider>(true)) if (kc != null) Physics.IgnoreCollision(col, kc, true);
            foreach (var vc in vesselColliders) if (vc != null) Physics.IgnoreCollision(col, vc, true);

            Rigidbody srb = EjectionDebris.MakePhysical(seat, eva.part, 0.06f, S.seatLifetime, 0.25f);
            srb.velocity = vel - fwd * 2.5f - up * 1.2f;
            srb.angularVelocity = -right * 3.5f + UnityEngine.Random.insideUnitSphere * 0.8f;
            // the stand-in becomes a real EjectionSeat part (fuel, KSP aero, breaks up on hard impact)
            var ignore = new List<Collider>(vesselColliders);
            ignore.AddRange(eva.GetComponentsInChildren<Collider>(true));
            SeatPartHandoff.Begin(seat, srb, eva, variant != null ? variant.id : "modern", fuelLeft, ignore);

            // kerbal kicks free of the seat (stays on our lighter kerbal drag until handover near the ground)
            rb.interpolation = origInterp;
            rb.velocity = vel + fwd * 1.4f + up * 0.6f;
            rb.angularVelocity += -right * 0.3f + UnityEngine.Random.insideUnitSphere * 0.25f;

            Burst.Smoke(seat.transform.position, -fwd, 10, 0.2f, 1.5f, 0.8f, new Color(0.8f, 0.8f, 0.8f, 0.6f));
            Burst.Sound(S.canopySound, transform.position, 0.35f);
            seat = null;
        }

        void DeployChute()
        {
            var go = ParachuteModel.Build(variant, S, cockpit != null ? cockpit.vessel : null);
            go.transform.rotation = Quaternion.LookRotation(transform.forward, transform.up);
            go.transform.position = transform.TransformPoint(S.chuteRiserPoint);
            chute = go.GetComponent<ChuteRig>();
            chute.openTime = chuteOpenTime;
            chute.semiFraction = S.chuteSemiFraction;
            chute.targetUp = transform.up;
            // risers anchor on the kerbal's EVA chute pack (fallback: jetpack, then chuteRiserPoint)
            Transform pack = eva.ChuteContainerTransform != null ? eva.ChuteContainerTransform
                           : (eva.JetpackTransform != null ? eva.JetpackTransform : null);
            chute.Setup(go.transform.Find("canopy"), transform, pack, S.chuteRiserPoint);
            chute.targetUp = transform.up;
            Burst.Smoke(transform.TransformPoint(S.chuteRiserPoint), transform.up, 8, 0.15f, 1f, 0.6f, new Color(0.9f, 0.9f, 0.88f, 0.5f));
            Play(S.animChute, 0.6f);
        }

        static float AGL(Vessel v)
        {
            float a = -1f;
            if (v.heightFromTerrain >= 0f) a = v.heightFromTerrain;
            float r = (float)v.radarAltitude;
            if (r >= 0f && (a < 0f || r < a)) a = r;
            return a;
        }

        void Finish()
        {
            if (done) return;
            done = true;
            if (!collisionsRestored && !OverlapsVessel()) { IgnoreVessel(false); collisionsRestored = true; }
            if (eva != null) eva.enabled = true;
            Unshield();
            ForceRecover();
            restoreTolAt = Time.time + 4f;   // keep the raised crash tolerance through the landing bounce
        }

        void Update()
        {
            if (!done) return;
            if (Time.time < restoreTolAt) { ForceRecover(); return; }   // settle the landing without ragdolling
            // landed next to (or on) the wreck: keep collisions off until clear, for at most 30 s
            if (!collisionsRestored && Time.time < restoreTolAt + 30f) { CheckClearance(); if (!collisionsRestored) return; }
            if (eva != null && origCrashTol > 0f) eva.part.crashTolerance = origCrashTol;
            if (eva != null && origGTol > 0) eva.part.gTolerance = origGTol;
            NoRagdoll(false);
            Destroy(this);
        }
    }

    /// <summary>
    /// Scene-level coroutine host so the sequence survives the cockpit being destroyed mid-ejection.
    /// </summary>
    public class EjectionDirector : MonoBehaviour
    {
        public static void Run(ModuleEjectionSeat module)
        {
            var go = new GameObject("EjectionSeat_Director");
            var d = go.AddComponent<EjectionDirector>();
            d.StartCoroutine(d.Sequence(module));
        }

        IEnumerator Sequence(ModuleEjectionSeat m)
        {
            EjectionSettings S = EjectionSettings.Get();
            Part part = m.part;
            Vessel vessel = part.vessel;
            bool wasActive = vessel == FlightGlobals.ActiveVessel;
            SeatVariant variant = SeatVariant.Get(m.seatVariant);
            EjectionPlan plan = CockpitAnalyzer.Analyze(part, m.ejectPitch, m.ejectYaw, S);
            foreach (var sp in plan.seats) sp.Capture(part.transform);   // pin to the cockpit before any waiting
            Log.Info("Ejecting from " + part.partInfo.title + ": " + plan.Summary());

            var vesselColliders = new List<Collider>();
            foreach (var p in vessel.parts)
                foreach (var c in p.GetComponentsInChildren<Collider>(true))
                    if (c != null && !c.isTrigger) vesselColliders.Add(c);

            // ---- canopy / hatch ----
            GameObject debris = null;
            if (m.blowCanopy)
            {
                debris = BlowCanopy(part, plan, S, vesselColliders);
                m.canopyGone = true;
            }
            try { m.ApplyCabinDamage(plan); }
            catch (Exception e) { Log.Warn("Cabin damage failed: " + e.Message); }
            // ---- seats ----
            // Every kerbal leaves the crew list straight away and waits strapped into their seat (so a cockpit
            // destroyed in the next moments can't take them with it); the seats then fire one after another.
            var crew = new List<ProtoCrewMember>(part.protoModuleCrew);
            var ejected = new List<KerbalEVA>();
            int seatCursor = 0;
            for (int i = 0; i < crew.Count; i++)
            {
                if (part == null || part.vessel == null) break;
                ProtoCrewMember pcm = crew[i];
                if (!part.protoModuleCrew.Contains(pcm)) continue;
                SeatPose pose = PoseFor(plan, pcm, ref seatCursor);
                // re-evaluate pose relative to current cockpit attitude
                pose.Refresh(part.transform);   // back to *current* world space (plane has moved since analysis)
                Vector3 localStart = part.transform.InverseTransformPoint(pose.pos);
                Log.Dbg("seat start in cockpit space " + localStart.ToString("F3") + " (" + pose.pos.ToString("F1") + " world, cockpit at " + part.transform.position.ToString("F1") + ")");
                Vector3 localDir = part.transform.InverseTransformDirection(pose.ejectDir);
                Vector3 localFwd = part.transform.InverseTransformDirection(Vector3.ProjectOnPlane(pose.fwd, pose.ejectDir).normalized);

                // the EVA kerbal inherits these when it spawns
                pcm.hasHelmetOn = true;
                pcm.hasNeckRingOn = true;
                Vector3 ivaPelvis;
                bool hasIvaPelvis = TryIvaPelvis(part, pcm, S, out ivaPelvis);   // before the IVA kerbal goes away
                KerbalEVA eva = Spawn(part, pcm, pose);
                if (eva == null)
                {
                    ScreenMessages.PostScreenMessage("Ejection failed for " + pcm.name + "!", 4f, ScreenMessageStyle.UPPER_CENTER);
                    continue;
                }
                foreach (var other in ejected)
                    if (other != null) IgnorePair(eva, other);
                if (debris != null) IgnoreObject(eva, debris);

                var seq = EjectionSequence.Attach(eva);
                seq.eva = eva;
                seq.cockpit = part;
                seq.variant = variant;
                seq.S = S;
                seq.accel = m.ejectionG * 9.81f;
                seq.burnTime = m.burnTime;
                seq.railLength = Mathf.Max(1.0f, pose.exitDistance + S.railExtraLength);
                seq.chuteDelay = Mathf.Clamp(m.chuteDelay + UnityEngine.Random.Range(-0.15f, 0.15f), 1f, 2f);
                seq.chuteOpenTime = m.chuteOpenTime;
                seq.chuteType = m.chuteType;
                seq.localStart = localStart;
                seq.localDir = localDir;
                seq.localFwd = localFwd;
                seq.vesselColliders = vesselColliders;
                seq.hasIvaPelvis = hasIvaPelvis;
                seq.ivaPelvisLocal = ivaPelvis;
                seq.startDelay = S.sitDownTime + S.prepTime + ejected.Count * S.sequenceInterval;
                ejected.Add(eva);
                yield return null;
            }
            if (m != null) { m.expended = ejected.Count > 0 || m.expended; m.ejectionInProgress = false; m.RefreshUI(); }

            // ---- camera focus ----
            yield return new WaitForSeconds(0.2f);
            if (wasActive && ejected.Count > 0)
            {
                if (S.focusEjectedKerbal)
                {
                    if (ejected[0] != null && FlightGlobals.ActiveVessel != ejected[0].vessel) FlightGlobals.ForceSetActiveVessel(ejected[0].vessel);
                }
                else if (vessel != null && FlightGlobals.ActiveVessel != vessel)
                {
                    FlightGlobals.ForceSetActiveVessel(vessel);
                }
            }
            Destroy(gameObject, 1f);
        }

        /// <summary>The IVA kerbal's pelvis, in cockpit space.</summary>
        static bool TryIvaPelvis(Part part, ProtoCrewMember pcm, EjectionSettings S, out Vector3 local)
        {
            local = Vector3.zero;
            try
            {
                Kerbal k = pcm.KerbalRef;
                InternalModel im = part.internalModel;
                if (k == null || im == null) return false;
                Transform pelvis = null;
                foreach (var t in k.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "bn_spA01") { pelvis = t; break; }
                    if (pelvis == null && t.name.ToLowerInvariant().Contains("pelvis")) pelvis = t;
                }
                if (pelvis == null) return false;
                Vector3 world = InteriorReveal.InternalToWorldMatrix(part, im, S).MultiplyPoint3x4(pelvis.position);
                local = part.transform.InverseTransformPoint(world);
                return true;
            }
            catch (Exception e) { Log.Dbg("IVA pelvis: " + e.Message); return false; }
        }

        static SeatPose PoseFor(EjectionPlan plan, ProtoCrewMember pcm, ref int cursor)
        {
            foreach (var s in plan.seats)
                if (s.fromIVA && s.seatIndex == pcm.seatIdx) return s;
            if (plan.seats.Count == 0) return new SeatPose();
            var r = plan.seats[Mathf.Min(cursor, plan.seats.Count - 1)];
            cursor++;
            return r;
        }

        /// <summary>
        /// Spawns the EVA kerbal through a temporary "airlock" placed outside the hull on the ejection
        /// path (so the stock hatch-obstruction test passes), then the sequence pulls them into the seat.
        /// </summary>
        static KerbalEVA Spawn(Part part, ProtoCrewMember pcm, SeatPose pose)
        {
            if (FlightEVA.fetch == null) return null;
            Transform originalAirlock = part.airlock;
            var tmp = new GameObject("EjectionSeat_Airlock");
            KerbalEVA eva = null;
            try
            {
                float[] dists = { pose.exitDistance + 0.6f, pose.exitDistance + 4f, 30f };
                foreach (float d in dists)
                {
                    tmp.transform.position = pose.pos + pose.ejectDir * d;
                    // onGoForEVA places the kerbal at airlock - airlock.forward*standoff; face outward
                    tmp.transform.rotation = Quaternion.LookRotation(-pose.ejectDir, pose.fwd);
                    tmp.transform.SetParent(part.transform, true);
                    if (part.airlock == null) part.airlock = tmp.transform;
                    eva = FlightEVA.fetch.spawnEVA(pcm, part, tmp.transform, false);
                    if (eva != null) break;
                    tmp.transform.SetParent(null, true);
                }
            }
            catch (Exception e)
            {
                Log.Error("spawnEVA threw: " + e);
            }
            finally
            {
                part.airlock = originalAirlock;
                UnityEngine.Object.Destroy(tmp, 2f);
            }
            if (eva != null)
            {
                eva.transform.position = pose.pos;
                eva.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(pose.fwd, pose.ejectDir).normalized, pose.ejectDir);
            }
            return eva;
        }

        static void IgnorePair(KerbalEVA a, KerbalEVA b)
        {
            foreach (var ca in a.GetComponentsInChildren<Collider>(true))
                foreach (var cb in b.GetComponentsInChildren<Collider>(true))
                    if (ca != null && cb != null) Physics.IgnoreCollision(ca, cb, true);
        }

        static void IgnoreObject(KerbalEVA a, GameObject o)
        {
            foreach (var ca in a.GetComponentsInChildren<Collider>(true))
                foreach (var cb in o.GetComponentsInChildren<Collider>(true))
                    if (ca != null && cb != null) Physics.IgnoreCollision(ca, cb, true);
        }

        /// <summary>Detaches canopy meshes and cuts the escape hole; everything removed flies off as one piece.</summary>
        public static GameObject BlowCanopy(Part part, EjectionPlan plan, EjectionSettings S, List<Collider> vesselColliders)
        {
            Vector3 centre = Vector3.zero; Vector3 dir = Vector3.zero;
            foreach (var s in plan.seats) { centre += s.pos + s.ejectDir * Mathf.Max(0.8f, s.exitDistance); dir += s.ejectDir; }
            if (plan.seats.Count > 0) { centre /= plan.seats.Count; dir.Normalize(); } else { centre = part.transform.position; dir = plan.vehicleUp; }

            var root = new GameObject("EjectionSeat_Canopy");
            root.AddComponent<EjectionGenerated>();
            root.transform.position = centre;
            root.transform.rotation = part.transform.rotation;
            CutHull(part, plan, S, root.transform);

            if (root.transform.childCount == 0)
            {
                Destroy(root);
                Burst.Smoke(centre, dir, 30, 0.6f, 6f, 2f, new Color(0.75f, 0.75f, 0.75f, 0.8f));
                return null;
            }

            // collider + physics
            Bounds b = new Bounds(root.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            var col = root.AddComponent<BoxCollider>();
            col.center = root.transform.InverseTransformPoint(b.center);
            col.size = root.transform.InverseTransformVector(b.size);
            col.size = new Vector3(Mathf.Max(0.05f, Mathf.Abs(col.size.x)), Mathf.Max(0.05f, Mathf.Abs(col.size.y)), Mathf.Max(0.05f, Mathf.Abs(col.size.z)));
            foreach (var vc in vesselColliders) if (vc != null) Physics.IgnoreCollision(col, vc, true);

            Vector3 baseVel = part.rb != null ? part.rb.GetPointVelocity(centre) : Vector3.zero;
            Rigidbody rb = EjectionDebris.MakePhysical(root, part, S.canopyMass, S.debrisLifetime, S.canopyDrag);
            rb.velocity = baseVel + dir * S.canopyEjectSpeed - plan.vehicleFwd * S.canopyBackSpeed;
            Vector3 right = Vector3.Cross(dir, plan.vehicleFwd).normalized;
            // pitch up and back off the hinge line, with a lazy roll/yaw so it tumbles rather than spins
            rb.angularVelocity = right * S.canopySpin + plan.vehicleFwd * UnityEngine.Random.Range(-1.2f, 1.2f) + dir * UnityEngine.Random.Range(-0.8f, 0.8f);

            // det-cord charge around the frame, jettison motors, glass and sparks
            Burst.Smoke(centre, dir, 40, 0.5f, 5f, 1.8f, new Color(0.8f, 0.8f, 0.78f, 0.85f));
            Burst.Flash(centre, 8f, 0.25f);
            Burst.Sound(S.canopySound, centre, 1f);
            CanopyJettisonFX.Attach(root, b, dir, plan.vehicleFwd, S.canopyTrailTime);
            return root;
        }

        /// <summary>Hides canopy renderers / cuts the hull. With a null root, it only cuts (used to restore state on reload).</summary>
        public static void CutHull(Part part, EjectionPlan plan, EjectionSettings S, Transform debrisRoot)
        {
            var canopySet = new HashSet<MeshFilter>(plan.canopy);
            foreach (var mf in plan.canopy)
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null) continue;
                if (debrisRoot != null)
                {
                    var go = new GameObject(mf.name + "_canopy");
                    go.transform.position = mf.transform.position;
                    go.transform.rotation = mf.transform.rotation;
                    go.transform.localScale = mf.transform.lossyScale;
                    go.transform.SetParent(debrisRoot, true);
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var dr = go.AddComponent<MeshRenderer>();
                    dr.sharedMaterials = r.sharedMaterials;
                    HullCutter.CopyLook(r, dr);
                }
                r.enabled = false;
            }
            // canopy glass painted into the hull (found like RealWindows does): taken off along the panes' outline
            GlassFinder.Result glass = null;
            if (S.glassCanopy && plan.canopy.Count == 0)
            {
                try { glass = GlassFinder.Find(part, plan, S); }
                catch (Exception e) { Log.Warn("glass search failed: " + e.Message); }
                if (glass != null) Log.Info("canopy glass: " + (glass.found ? "found, " : "not found, ") + glass.how);
            }
            bool needHole = glass == null || !glass.found || !glass.Covers(plan.columns);
            Vector3 inside = part.transform.position;
            foreach (var mf in part.FindModelComponents<MeshFilter>())
            {
                if (mf == null || canopySet.Contains(mf) || mf.GetComponentInParent<EjectionGenerated>() != null) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || !mf.gameObject.activeInHierarchy) continue;
                if (InteriorReveal.IsDepthMask(r)) { r.enabled = false; continue; }
                if (glass != null && glass.found)
                {
                    // with RealWindows the cockpit's windows are see-through, so the jettisoned canopy's are too
                    Material pane = SeatInteriorMirror.Installed && debrisRoot != null ? MatLib.CanopyGlass : null;
                    try { HullCutter.CutTriangles(mf, inside, (a, b, c, n) => glass.Contains((a + b + c) / 3f, n), debrisRoot, pane); }
                    catch (Exception e) { Log.Warn("glass cut failed on " + mf.name + ": " + e.Message); }
                }
                // round hole where the glass doesn't already open the way out (or there was no glass)
                if (!needHole) continue;
                try { HullCutter.Cut(mf, plan.columns, S.subdivideEdge, debrisRoot); }
                catch (Exception e) { Log.Warn("cut failed on " + mf.name + ": " + e.Message); }
            }
        }
    }
}
