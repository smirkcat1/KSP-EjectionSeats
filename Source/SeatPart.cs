using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// The spent seat as a real KSP part (EjectionSeat, hidden from the editor). It carries what is left of its
    /// solid rocket propellant, has KSP aero and crash handling, and breaks up when it hits the ground too fast.
    /// The model is the same procedural seat the ejection used, rebuilt from the saved seat type.
    /// </summary>
    public class ModuleEjectionSeatPart : PartModule
    {
        [KSPField(isPersistant = true)]
        public string seatVariant = "modern";

        /// <summary>Every spent seat part in the flight scene (kerbals ignore them for a moment after separating).</summary>
        public static readonly HashSet<ModuleEjectionSeatPart> Live = new HashSet<ModuleEjectionSeatPart>();

        GameObject visual;

        void OnDestroy() { Live.Remove(this); }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight) Live.Add(this);
            if (!HighLogic.LoadedSceneIsFlight || visual != null) return;
            var S = EjectionSettings.Get();
            var built = SeatModel.Build(SeatVariant.Get(seatVariant), S.seatScale);
            visual = built.root;
            Transform model = part.transform.Find("model");
            visual.transform.SetParent(model != null ? model : part.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Destroy(c);

            // the model file's box collider, sized to the seat as configured
            var box = part.GetComponentInChildren<BoxCollider>();
            if (box != null)
            {
                box.center = new Vector3(0f, 0.3f, -0.08f) * S.seatScale;
                box.size = new Vector3(0.5f, 1.0f, 0.45f) * S.seatScale;
            }
            if (SeatPartHandoff.IsPending(part.flightID)) visual.SetActive(false);
        }

        public void SetVisible(bool on)
        {
            if (visual != null && visual.activeSelf != on) visual.SetActive(on);
        }
    }

    /// <summary>
    /// Keeps a spent seat's colliders switched off for a moment after separation, so it can never knock the kerbal
    /// it has just released (ignoring collider pairs is not enough: Unity forgets the pairs whenever KSP switches a
    /// collider on, which it does when the seat part comes off rails right next to the kerbal). They come back once
    /// the time is up and no kerbal is within reach, and always near the ground so the seat can't sink into it.
    /// </summary>
    public class SeatNoCollide : MonoBehaviour
    {
        const float KerbalClearance = 1.5f;   // metres between seat and any kerbal before collisions return
        const float MaxExtra = 3f;            // never more than this past the planned time
        const float GroundClearance = 4f;     // metres: closer to the ground than this, collisions are always on
        public float until;
        readonly List<Collider> off = new List<Collider>();

        public static SeatNoCollide Apply(GameObject go, float until)
        {
            if (go == null) return null;
            var n = go.GetComponent<SeatNoCollide>() ?? go.AddComponent<SeatNoCollide>();
            n.until = Mathf.Max(n.until, until);
            n.Hold();
            return n;
        }

        readonly List<Collider> all = new List<Collider>();
        float nextScan;

        void Hold()
        {
            // the seat's colliders are looked up once a second, not on every call (twice a frame)
            if (all.Count == 0 || Time.unscaledTime >= nextScan)
            {
                GetComponentsInChildren(true, all);
                nextScan = Time.unscaledTime + 1f;
            }
            foreach (var c in all)
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                c.enabled = false;
                if (!off.Contains(c)) off.Add(c);
            }
        }

        void Release()
        {
            foreach (var c in off) if (c != null) c.enabled = true;
            off.Clear();
            Destroy(this);
        }

        bool NearGround()
        {
            Vector3 p = transform.position;
            Vector3 up = FlightGlobals.getUpAxis(p);
            return Physics.Raycast(p + up * 0.5f, -up, GroundClearance + 0.5f, 1 << 15, QueryTriggerInteraction.Ignore);
        }

        bool KerbalNear()
        {
            Vector3 p = transform.position;
            foreach (var v in FlightGlobals.VesselsLoaded)
                if (v != null && v.isEVA && (v.CoMD - (Vector3d)p).sqrMagnitude < KerbalClearance * KerbalClearance) return true;
            return false;
        }

        void FixedUpdate()
        {
            float now = Time.time;
            if (NearGround() || now > until + MaxExtra || (now >= until && !KerbalNear())) { Release(); return; }
            Hold();   // KSP may have switched one back on (coming off rails, packing)
        }

        void LateUpdate() { if (off.Count > 0 || Time.time < until) Hold(); }

        void OnDestroy() { foreach (var c in off) if (c != null) c.enabled = true; }
    }

    /// <summary>
    /// At seat/man separation the seat first flies on as a lightweight physics object. Meanwhile a one-part
    /// vessel is created; as soon as KSP has it loaded and off rails it is put exactly where the stand-in is,
    /// with the same velocity and spin, and the stand-in is removed.
    /// </summary>
    public class SeatPartHandoff : MonoBehaviour
    {
        public const string PartName = "EjectionSeat";
        static readonly HashSet<uint> pending = new HashSet<uint>();
        static bool warned;

        Rigidbody standIn;
        Vessel vessel;
        uint flightId;
        List<Collider> ignore;
        float age, separatedAt;
        public const float NoCollideTime = 2f;   // seconds after separation the spent seat collides with nothing

        public static bool IsPending(uint id) { return pending.Contains(id); }

        public static void Begin(GameObject seat, Rigidbody standIn, KerbalEVA eva, string variantId, float fuelLeft, List<Collider> ignore)
        {
            var S = EjectionSettings.Get();
            if (!S.seatAsPart || seat == null || standIn == null || eva == null || eva.vessel == null || eva.part == null) return;
            if (PartLoader.getPartInfoByName(PartName) == null)
            {
                if (!warned) Log.Warn("part '" + PartName + "' is not loaded - the seat stays a simple physics object");
                warned = true;
                return;
            }
            try
            {
                uint id = ShipConstruction.GetUniqueFlightID(HighLogic.CurrentGame.flightState);
                ConfigNode pn = ProtoVessel.CreatePartNode(PartName, id, new ProtoCrewMember[0]);
                foreach (ConfigNode rn in pn.GetNodes("RESOURCE"))
                {
                    if (rn.GetValue("name") != "SolidFuel") continue;
                    double max;
                    if (!double.TryParse(rn.GetValue("maxAmount"), NumberStyles.Float, CultureInfo.InvariantCulture, out max)) continue;
                    rn.SetValue("amount", (max * Mathf.Clamp01(fuelLeft)).ToString("R", CultureInfo.InvariantCulture), true);
                }
                bool setVariant = false;
                foreach (ConfigNode mn in pn.GetNodes("MODULE"))
                    if (mn.GetValue("name") == "ModuleEjectionSeatPart") { mn.SetValue("seatVariant", variantId, true); setVariant = true; }
                if (!setVariant)
                {
                    var mn = pn.AddNode("MODULE");
                    mn.AddValue("name", "ModuleEjectionSeatPart");
                    mn.AddValue("seatVariant", variantId);
                }

                // start from the kerbal's own orbit, offset by where the seat is relative to the kerbal
                Vessel k = eva.vessel;
                double ut = Planetarium.GetUniversalTime();
                Vector3d kPos = k.orbit.getRelativePositionAtUT(ut), kVel = k.orbit.getOrbitalVelocityAtUT(ut);
                Vector3d dPos = ((Vector3d)(standIn.position - (Vector3)k.CoMD)).xzy;
                Vector3d dVel = ((Vector3d)(standIn.velocity - eva.part.rb.velocity)).xzy;
                var orbit = new Orbit();
                orbit.UpdateFromStateVectors(kPos + dPos, kVel + dVel, k.mainBody, ut);

                string name = (eva.part.protoModuleCrew.Count > 0 ? eva.part.protoModuleCrew[0].name : k.vesselName) + "'s Ejection Seat";
                ConfigNode vn = ProtoVessel.CreateVesselNode(name, VesselType.Debris, orbit, 0, new[] { pn },
                    ProtoVessel.CreateDiscoveryNode(DiscoveryLevels.Owned, UntrackedObjectClass.A, double.PositiveInfinity, double.PositiveInfinity));
                vn.SetValue("sit", k.situation == Vessel.Situations.SUB_ORBITAL || k.situation == Vessel.Situations.ORBITING ? k.situation.ToString() : "FLYING", true);
                vn.SetValue("landed", "False", true);
                vn.SetValue("splashed", "False", true);

                pending.Add(id);
                ProtoVessel pv = HighLogic.CurrentGame.AddVessel(vn);
                if (pv == null || pv.vesselRef == null) { pending.Remove(id); Log.Warn("seat vessel was not created"); return; }
                var h = seat.AddComponent<SeatPartHandoff>();
                h.standIn = standIn;
                h.vessel = pv.vesselRef;
                h.flightId = id;
                h.ignore = ignore ?? new List<Collider>();
                h.separatedAt = Time.time;
                Log.Dbg("seat part spawned: " + name + " fuel " + fuelLeft.ToString("F2"));
            }
            catch (Exception e)
            {
                Log.Warn("seat part spawn failed, keeping the simple seat: " + e.Message);
            }
        }

        void FixedUpdate()
        {
            age += Time.fixedDeltaTime;
            if (vessel == null || standIn == null)
            {
                Finish(false);
                return;
            }
            Part p = vessel.rootPart;
            if (vessel.loaded && p != null)
            {
                // never let the waiting part knock the kerbal or the craft about: no colliders until it's clear
                SeatNoCollide.Apply(p.gameObject, separatedAt + NoCollideTime);
                var mod = p.FindModuleImplementing<ModuleEjectionSeatPart>();
                if (mod != null) mod.SetVisible(false);
                if (vessel.packed) { try { vessel.GoOffRails(); } catch { } }
            }
            if (!vessel.loaded || vessel.packed || p == null || p.rb == null)
            {
                if (age > 6f)
                {
                    Log.Warn("seat part did not come off rails - removing it and keeping the simple seat");
                    try { vessel.Die(); } catch { }
                    Finish(false);
                }
                return;
            }

            vessel.SetPosition(standIn.position);
            vessel.SetRotation(standIn.rotation);
            p.rb.velocity = standIn.velocity;
            p.rb.angularVelocity = standIn.angularVelocity;
            vessel.IgnoreGForces(3);
            Finish(true);
        }

        void Finish(bool tookOver)
        {
            pending.Remove(flightId);
            if (tookOver)
            {
                var mod = vessel.rootPart.FindModuleImplementing<ModuleEjectionSeatPart>();
                if (mod != null) mod.SetVisible(true);
                Log.Dbg("seat part took over after " + age.ToString("F2") + " s");
                Destroy(gameObject);      // the stand-in seat
            }
            else Destroy(this);
        }

        void OnDestroy() { pending.Remove(flightId); }
    }
}
