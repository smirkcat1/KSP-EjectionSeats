// Development flight test (never shipped). Built and installed only by tools/devflight/run.ps1.
// Reads GameData/EjectionSeats/PluginData/flight.flag:
//   line 1: output folder; further lines: key = value (alt, speed, duration, shots, camDist, camHdg, camPitch, dump, seat)
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using EjectionSeats;

namespace zDevFlight
{
    static class Cfg
    {
        public static string Flag { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/EjectionSeats/PluginData/flight.flag"); } }
        public static string Dir;
        static Dictionary<string, string> kv;
        public static void Load()
        {
            kv = new Dictionary<string, string>();
            var lines = File.ReadAllLines(Flag);
            Dir = lines[0].Trim();
            for (int i = 1; i < lines.Length; i++)
            {
                int e = lines[i].IndexOf('=');
                if (e > 0) kv[lines[i].Substring(0, e).Trim()] = lines[i].Substring(e + 1).Trim();
            }
        }
        public static string S(string k, string d) { string v; return kv.TryGetValue(k, out v) ? v : d; }
        public static float F(string k, float d) { float f; return float.TryParse(S(k, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : d; }
        public static float[] List(string k)
        {
            var r = new List<float>();
            foreach (var p in S(k, "").Split(',')) { float f; if (float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) r.Add(f); }
            return r.ToArray();
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class Boot : MonoBehaviour
    {
        static bool started;
        IEnumerator Start()
        {
            if (started || !File.Exists(Cfg.Flag)) yield break;
            started = true;
            yield return new WaitForSecondsRealtime(2f);
            HighLogic.SaveFolder = "test";
            var game = GamePersistence.LoadGame("persistent", "test", true, false);
            HighLogic.CurrentGame = game;
            string path = Path.Combine(KSPUtil.ApplicationRootPath, "saves/test/Ships/SPH/DevOne.craft");
            var node = ConfigNode.Load(path);
            var manifest = VesselCrewManifest.FromConfigNode(node);
            manifest = game.CrewRoster.DefaultCrewForVessel(node, manifest);
            FlightDriver.StartWithNewLaunch(path, "Squad/Flags/default", "Runway", manifest);
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Run : MonoBehaviour
    {
        readonly StringBuilder log = new StringBuilder();
        void W(string s) { log.AppendLine(s); File.WriteAllText(Path.Combine(Cfg.Dir, "log.txt"), log.ToString()); }

        IEnumerator Start()
        {
            if (!File.Exists(Cfg.Flag)) yield break;
            Cfg.Load();
            Directory.CreateDirectory(Cfg.Dir);
            Time.timeScale = 1f;
            float wait = 0f;
            while ((FlightGlobals.ActiveVessel == null || FlightGlobals.ActiveVessel.packed || !FlightGlobals.ready) && wait < 90f) { wait += Time.unscaledDeltaTime; yield return null; }
            Vessel v = FlightGlobals.ActiveVessel;
            CheatOptions.NoCrashDamage = false; CheatOptions.UnbreakableJoints = true; CheatOptions.IgnoreMaxTemperature = true;
            double lat = v.latitude, lon = v.longitude;
            float alt = Cfg.F("alt", 800f), speed = Cfg.F("speed", 200f);
            Part cockpit = v.rootPart;
            for (int i = 0; i < 150; i++) { Pin(v, lat, lon, alt, speed, true); yield return new WaitForFixedUpdate(); }
            var m = cockpit.FindModuleImplementing<ModuleEjectionSeat>();
            if (m == null) { Done("no module"); yield break; }
            m.installed = true;
            string seat = Cfg.S("seat", "");
            if (seat != "") { m.seatVariant = seat; m.seatEra = SeatVariant.Get(seat).era; }
            m.RefreshUI();
            yield return new WaitForFixedUpdate();
            Pin(v, lat, lon, alt, speed, false);
            W("before eject speed=" + v.srfSpeed.ToString("F0") + " alt=" + v.altitude.ToString("F0") + " seat=" + m.seatVariant);
            m.Eject();
            StartCoroutine(Hold(cockpit));
            bool wantDump = Cfg.F("dump", 0f) > 0f;
            W("dump requested: " + wantDump + " (" + Cfg.S("dump", "-") + ")");

            float[] shots = Cfg.List("shots");
            float camDist = Cfg.F("camDist", 4f), camHdg = Cfg.F("camHdg", 1.3f), camPitch = Cfg.F("camPitch", 0.15f);
            float duration = Cfg.F("duration", 6f), t = 0f; int shot = 0;
            EjectionSequence seq = null;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (Time.timeScale < 0.99f) { Time.timeScale = 1f; Time.fixedDeltaTime = 0.02f; }   // no slow motion in tests
                if (seq == null) { var all = FindObjectsOfType<EjectionSequence>(); if (all.Length > 0) seq = all[0]; }
                if (wantDump && seq != null && seq.Phase == "sit") { wantDump = false; try { DumpRig(seq); W("rig dumped"); } catch (System.Exception ex) { W("dump failed: " + ex); } }
                var cam = FlightCamera.fetch;
                if (cam != null && camDist > 0f)
                {
                    cam.SetDistance(camDist);
                    // view relative to the kerbal: heading measured from the kerbal's facing direction
                    if (seq != null && seq.eva != null)
                    {
                        Vector3 up = FlightGlobals.getUpAxis(seq.transform.position);
                        Vector3 f = Vector3.ProjectOnPlane(seq.transform.forward, up);
                        if (f.sqrMagnitude < 0.01f) f = Vector3.ProjectOnPlane(-seq.transform.up, up);
                        Vector3 north = Vector3.ProjectOnPlane(v.mainBody.transform.up, up).normalized;
                        float hdg = Mathf.Atan2(Vector3.Dot(Vector3.Cross(north, f.normalized), up), Vector3.Dot(north, f.normalized));
                        cam.camHdg = hdg + camHdg;
                    }
                    cam.camPitch = camPitch;
                }
                if (shot < shots.Length && t >= shots[shot])
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(Cfg.Dir, "shot" + shot + "_" + shots[shot].ToString("F1", CultureInfo.InvariantCulture) + ".png"));
                    W("shot " + shot + " t=" + t.ToString("F2") + " phase=" + (seq != null ? seq.Phase : "?"));
                    shot++;
                }
                yield return null;
            }
            Done("ok");
        }

        void Done(string s)
        {
            File.WriteAllText(Path.Combine(Cfg.Dir, "done.txt"), s);
            Application.Quit();
        }

        void DumpRig(EjectionSequence seq)
        {
            var root = seq.eva.transform;
            var sb = new StringBuilder();
            sb.AppendLine("root " + root.name);
            Dump(root, root, "", sb);
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                sb.Append("SMR " + smr.name + " bones:");
                foreach (var bn in smr.bones) sb.Append(" " + (bn != null ? bn.name : "null"));
                sb.AppendLine();
            }
            var anim = root.GetComponentInChildren<Animation>();
            if (anim != null)
            {
                sb.Append("ANIM on " + anim.name + " playing:");
                foreach (AnimationState st in anim) if (anim.IsPlaying(st.name)) sb.Append(" " + st.name + "(w" + st.weight.ToString("F2") + ")");
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(Cfg.Dir, "rig.txt"), sb.ToString());
        }

        static void Dump(Transform t, Transform root, string indent, StringBuilder sb)
        {
            Vector3 p = root.InverseTransformPoint(t.position);
            sb.AppendLine(indent + t.name + "  pos=" + p.ToString("F3") + " x=" + root.InverseTransformDirection(t.right).ToString("F2") +
                          " y=" + root.InverseTransformDirection(t.up).ToString("F2") + " z=" + root.InverseTransformDirection(t.forward).ToString("F2") +
                          (t.gameObject.activeSelf ? "" : " [inactive]"));
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), root, indent + "  ", sb);
        }

        static void Pin(Vessel v, double lat, double lon, float alt, float speed, bool place)
        {
            Vector3 up = v.mainBody.GetSurfaceNVector(lat, lon);
            Vector3 fwd = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
            v.IgnoreGForces(10);
            v.Landed = false; v.Splashed = false; v.situation = Vessel.Situations.FLYING;
            if (place)
            {
                double terrain = v.mainBody.TerrainAltitude(lat, lon);
                v.SetPosition(v.mainBody.GetWorldSurfacePosition(lat, lon, terrain + alt));
                v.SetRotation(Quaternion.LookRotation(-up, fwd));
            }
            v.ChangeWorldVelocity((Vector3d)(fwd * speed) - v.srf_velocity);
            foreach (var p in v.parts) if (p.rb != null) p.rb.angularVelocity = Vector3.zero;
        }

        IEnumerator Hold(Part p)
        {
            while (p != null && p.rb != null) { p.rb.angularVelocity = Vector3.zero; yield return new WaitForFixedUpdate(); }
        }
    }
}
