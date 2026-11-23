using System;
using System.IO;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Global tunables. Read from GameData/EjectionSeats/Settings.cfg. The file is re-read
    /// (at most every couple of seconds) so values can be tweaked while the game is running.
    /// </summary>
    public class EjectionSettings
    {
        // --- economy ---
        public float seatMassPerCrew = 0.08f;
        public float seatCostPerCrew = 450f;

        // --- cockpit analysis / canopy ---
        public float hatchRadius = 0.48f;          // radius of the "escape column" cut through the hull
        public float hatchStartHeight = 0.5f;      // how far above the seat the column starts cutting
        public float subdivideEdge = 0.2f;         // large triangles near the column are split to this size
        public float canopyMinScore = 6f;
        public string[] strongCanopyWords = { "canopy", "bubble" };
        public string[] weakCanopyWords = { "glass", "window", "windshield", "windscreen", "hatch", "door", "lid", "visor" };
        public string[] ignoreCanopyWords = { "flag", "decal", "light", "lamp", "emissive", "beacon" };
        public float cutMinFacing = 0.35f;         // only surfaces facing along/against the escape axis are cut (keeps side walls)
        public float interiorGlow = 0.3f;          // cabin "ambient" so the exposed interior isn't pitch black
        public float canopyDrag = 0.08f;           // CdA (m^2) of the jettisoned canopy - low so it sails away
        public float canopyMass = 0.08f;
        public float canopyTrailTime = 1.4f;       // jettison-motor burn / smoke trail duration
        public float canopyEjectSpeed = 14f;
        public float canopyBackSpeed = 4f;
        public float canopySpin = 3f;
        public bool preferVehicleUp = false;       // false: eject along the IVA seat's own up axis; true: also try the vehicle's "up"
        public float vehicleUpWeight = 0.35f;

        // --- sequence timing ---
        public float prepTime = 0.3f;              // canopy gone -> first seat moves
        public float sitDownTime = 0.45f;
        public float sequenceInterval = 0.35f;     // stagger between seats
        public float railExtraLength = 0.8f;
        public float separationDelay = 0.9f;       // after burnout
        public float seatMaxRecline = 55f;         // deg from upright the airflow or chute may lean the kerbal back
        public float seatDragArea = 0.25f;         // CdA (m^2) of seat + kerbal while strapped in
        public float kerbalDragArea = 0.3f;        // CdA (m^2) of a free-falling kerbal (replaces KSP's very high EVA drag)
        public float seatMass = 0.06f;             // tonnes, used for the seat+kerbal drag deceleration
        public bool kerbalsFeelGForce = false;

        // --- kerbal placement (tunable without a recompile) ---
        public Vector3 ivaToEvaOffset = new Vector3(0f, 0.08f, 0f);      // EVA root relative to IVA seat transform (seat frame)
        public bool autoFitKerbal = true;                                 // measure the seated kerbal's skeleton and fit the seat to it
        public Vector3 kerbalFitAdjust = Vector3.zero;                    // extra nudge on top of the auto fit
        public float pelvisAboveCushion = 0.06f;
        public float packHalfDepth = 0.06f;
        public float seatScale = 0.68f;                                   // overall seat size (1 = human-ish, ~0.7 = kerbal)
        public Vector3 kerbalSeatOffset = new Vector3(0f, -0.40f, -0.05f); // seat model origin in kerbal local space
        public Vector3 kerbalSeatRotation = Vector3.zero;
        public Vector3 internalRotation = new Vector3(90f, 180f, 0f);   // KSP's InternalModel.internalToWorld

        // --- animations (field names on KerbalAnimationManager or raw clip names) ---
        public string animBrace = "idle";
        public string animSeated = "seatIdle";
        public string animFreefall = "swimIdle";
        public string animChute = "chuteIdle";
        public string animLanded = "idle";

        // --- parachute ---
        public string chutePart = "";              // force a stock parachute part for every seat (empty = per seat model)
        public float handoverAltitude = 25f;       // below this AGL the kerbal's own EVA controller takes over for landing
        public float kerbalCrashTolerance = 50f;   // impact speed an ejected kerbal survives
        public float chuteDiameter = 6f;
        public float chuteDescentRate = 7f;
        public float chuteMaxG = 12f;
        public float chuteMinDensity = 0.002f;
        public Vector3 chuteRiserPoint = new Vector3(0f, 0.05f, -0.26f);  // behind the helmet, at the shoulders
        public float chuteSemiFraction = 0.45f;    // share of the open time spent semi-deployed (reefed), like stock chutes

        // --- FX ---
        public string seatTheme = "auto";          // auto (ReStock look if ReStock is installed) | stock | restock
        public bool enhancedFlames = true;
        public bool srbSmoke = true;               // thick solid-rocket smoke trail from the seat motor
        public float srbSmokeAmount = 1f;          // particle count multiplier
        public float srbSmokeLifetime = 9f;        // seconds the trail hangs in the air
        public bool seatAsPart = true;
        public bool kerbalPoses = true;
        public bool glassCanopy = true;            // find canopy glass painted into the hull texture and take that off            // braced ejection posture, hands on the handle and the risers             // the separated seat becomes a real part (fuel, aero, crashes)
        public float flameScale = 1f;
        public bool useBDAPlumes = true;
        public string plumePrefab = "BDArmory/Models/exhaust/smallExhaust";
        public float plumeScale = 0.45f;
        public Vector3 plumeRotation = Vector3.zero;
        public string rocketSound = "Squad/Sounds/sound_rocket_mini";
        public string canopySound = "Squad/Sounds/sound_decoupler_fire";

        // --- misc ---
        public bool focusEjectedKerbal = true;
        public bool ivaHelmetRespawn = true;       // respawn helmetless IVA kerbals in an opened cockpit so they wear one
        public float restoreCollisionsAfter = 4f;
        public float debrisLifetime = 45f;
        public float seatLifetime = 60f;
        public int maxInteriorRenderers = 1500;
        public bool debug = false;

        static EjectionSettings cached;
        static float lastLoad = -100f;

        public static string FilePath
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/EjectionSeats/Settings.cfg"); }
        }

        public static EjectionSettings Get()
        {
            if (cached == null || Time.realtimeSinceStartup - lastLoad > 2f)
            {
                cached = Load();
                lastLoad = Time.realtimeSinceStartup;
            }
            return cached;
        }

        static EjectionSettings Load()
        {
            var s = new EjectionSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                ConfigNode root = ConfigNode.Load(FilePath);
                if (root == null) return s;
                ConfigNode n = root.GetNode("EJECTION_SEAT_SETTINGS") ?? root;

                F(n, "seatMassPerCrew", ref s.seatMassPerCrew);
                F(n, "seatCostPerCrew", ref s.seatCostPerCrew);
                F(n, "hatchRadius", ref s.hatchRadius);
                F(n, "hatchStartHeight", ref s.hatchStartHeight);
                F(n, "subdivideEdge", ref s.subdivideEdge);
                F(n, "canopyMinScore", ref s.canopyMinScore);
                L(n, "strongCanopyWords", ref s.strongCanopyWords);
                L(n, "weakCanopyWords", ref s.weakCanopyWords);
                L(n, "ignoreCanopyWords", ref s.ignoreCanopyWords);
                F(n, "cutMinFacing", ref s.cutMinFacing);
                F(n, "interiorGlow", ref s.interiorGlow);
                S(n, "chutePart", ref s.chutePart);
                F(n, "handoverAltitude", ref s.handoverAltitude);
                F(n, "kerbalCrashTolerance", ref s.kerbalCrashTolerance);
                F(n, "canopyEjectSpeed", ref s.canopyEjectSpeed);
                F(n, "canopyBackSpeed", ref s.canopyBackSpeed);
                F(n, "canopySpin", ref s.canopySpin);
                B(n, "preferVehicleUp", ref s.preferVehicleUp);
                F(n, "vehicleUpWeight", ref s.vehicleUpWeight);

                F(n, "prepTime", ref s.prepTime);
                F(n, "sitDownTime", ref s.sitDownTime);
                F(n, "sequenceInterval", ref s.sequenceInterval);
                F(n, "railExtraLength", ref s.railExtraLength);
                F(n, "separationDelay", ref s.separationDelay);
                F(n, "seatMaxRecline", ref s.seatMaxRecline);
                F(n, "seatDragArea", ref s.seatDragArea);
                F(n, "kerbalDragArea", ref s.kerbalDragArea);
                F(n, "seatMass", ref s.seatMass);
                B(n, "kerbalsFeelGForce", ref s.kerbalsFeelGForce);

                V(n, "ivaToEvaOffset", ref s.ivaToEvaOffset);
                B(n, "autoFitKerbal", ref s.autoFitKerbal);
                V(n, "kerbalFitAdjust", ref s.kerbalFitAdjust);
                F(n, "pelvisAboveCushion", ref s.pelvisAboveCushion);
                F(n, "packHalfDepth", ref s.packHalfDepth);
                F(n, "canopyDrag", ref s.canopyDrag);
                F(n, "canopyMass", ref s.canopyMass);
                F(n, "canopyTrailTime", ref s.canopyTrailTime);
                F(n, "seatScale", ref s.seatScale);
                F(n, "chuteSemiFraction", ref s.chuteSemiFraction);
                V(n, "kerbalSeatOffset", ref s.kerbalSeatOffset);
                V(n, "kerbalSeatRotation", ref s.kerbalSeatRotation);
                V(n, "internalRotation", ref s.internalRotation);

                S(n, "animBrace", ref s.animBrace);
                S(n, "animSeated", ref s.animSeated);
                S(n, "animFreefall", ref s.animFreefall);
                S(n, "animChute", ref s.animChute);
                S(n, "animLanded", ref s.animLanded);

                F(n, "chuteDiameter", ref s.chuteDiameter);
                F(n, "chuteDescentRate", ref s.chuteDescentRate);
                F(n, "chuteMaxG", ref s.chuteMaxG);
                F(n, "chuteMinDensity", ref s.chuteMinDensity);
                V(n, "chuteRiserPoint", ref s.chuteRiserPoint);

                S(n, "seatTheme", ref s.seatTheme);
                B(n, "enhancedFlames", ref s.enhancedFlames);
                B(n, "srbSmoke", ref s.srbSmoke);
                F(n, "srbSmokeAmount", ref s.srbSmokeAmount);
                F(n, "srbSmokeLifetime", ref s.srbSmokeLifetime);
                B(n, "seatAsPart", ref s.seatAsPart);
                B(n, "kerbalPoses", ref s.kerbalPoses);
                B(n, "glassCanopy", ref s.glassCanopy);
                F(n, "flameScale", ref s.flameScale);
                B(n, "useBDAPlumes", ref s.useBDAPlumes);
                S(n, "plumePrefab", ref s.plumePrefab);
                F(n, "plumeScale", ref s.plumeScale);
                V(n, "plumeRotation", ref s.plumeRotation);
                S(n, "rocketSound", ref s.rocketSound);
                S(n, "canopySound", ref s.canopySound);

                B(n, "focusEjectedKerbal", ref s.focusEjectedKerbal);
                B(n, "ivaHelmetRespawn", ref s.ivaHelmetRespawn);
                F(n, "restoreCollisionsAfter", ref s.restoreCollisionsAfter);
                F(n, "debrisLifetime", ref s.debrisLifetime);
                F(n, "seatLifetime", ref s.seatLifetime);
                int mir = s.maxInteriorRenderers;
                if (n.HasValue("maxInteriorRenderers") && int.TryParse(n.GetValue("maxInteriorRenderers"), out mir)) s.maxInteriorRenderers = mir;
                B(n, "debug", ref s.debug);
            }
            catch (Exception e)
            {
                Log.Warn("Failed to read settings: " + e.Message);
            }
            return s;
        }

        static void F(ConfigNode n, string k, ref float v)
        {
            float t;
            if (n.HasValue(k) && float.TryParse(n.GetValue(k), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out t)) v = t;
        }
        static void B(ConfigNode n, string k, ref bool v)
        {
            bool t;
            if (n.HasValue(k) && bool.TryParse(n.GetValue(k), out t)) v = t;
        }
        static void S(ConfigNode n, string k, ref string v)
        {
            if (n.HasValue(k)) v = n.GetValue(k).Trim();
        }
        static void V(ConfigNode n, string k, ref Vector3 v)
        {
            if (!n.HasValue(k)) return;
            try { v = ConfigNode.ParseVector3(n.GetValue(k)); } catch { }
        }
        static void L(ConfigNode n, string k, ref string[] v)
        {
            if (!n.HasValue(k)) return;
            var parts = n.GetValue(k).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim().ToLowerInvariant();
            v = parts;
        }
    }

    public static class Log
    {
        public static void Info(string m) { Debug.Log("[EjectionSeats] " + m); }
        public static void Warn(string m) { Debug.LogWarning("[EjectionSeats] " + m); }
        public static void Error(string m) { Debug.LogError("[EjectionSeats] " + m); }
        public static void Dbg(string m) { if (EjectionSettings.Get().debug) Debug.Log("[EjectionSeats:dbg] " + m); }
    }

    /// <summary>Marker so our generated objects are never mistaken for part geometry.</summary>
    public class EjectionGenerated : MonoBehaviour { }
}
