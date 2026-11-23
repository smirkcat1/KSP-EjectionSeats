using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Look of one seat's rocket plume. Defaults come from the seat's era; EJECTION_PLUME nodes in
    /// GameData/EjectionSeats/Plumes/*.cfg (made with tools/plume_editor) override them per seat.
    /// The files are re-read every couple of seconds, so a new plume shows on the next ejection.
    /// </summary>
    public class PlumeConfig
    {
        public string name = "";
        // nozzle fit: offset in metres (x mirrors on the left nozzle of a pair), angles in degrees
        public Vector3 nozzleOffset = Vector3.zero;
        public float nozzlePitch, nozzleSplay;

        public float flameLength = 1f, flameWidth = 1f, coreLength = 0.55f, coreWidth = 0.55f;
        public Color coreColor, flameColor, edgeColor;
        public float bodyOpacity = 0.7f, outerOpacity = 0.4f, flicker = 1f;
        public float glowSize = 1.7f, glowBrightness = 0.55f, lightRange = 7f, lightIntensity = 1f;
        public float fireRate = 260f, fireSize = 1f, fireSpeed = 1f, fireLife = 1f, fireSpread = 9f;
        public float sparkRate = 160f, sparkSpeed = 1f, sparkLife = 1f, sparkSpread = 18f;
        public float smokeAmount = 1f, smokeLife = 9f, smokeSize = 1f, smokeStart = 0.45f, smokeOpacity = 1f;
        public Color smokeColor = new Color(0.96f, 0.95f, 0.93f);

        public static PlumeConfig Defaults(SeatVariant v, EjectionSettings s)
        {
            var p = new PlumeConfig
            {
                flameLength = v.flameLength,
                flameWidth = v.flameWidth,
                coreColor = Color.Lerp(v.flameCore, Color.white, 0.45f),
                flameColor = Color.Lerp(v.flameMid, v.flameCore, 0.55f),
                edgeColor = Color.Lerp(v.flameOuter, v.flameMid, 0.5f),
                sparkRate = Mathf.Round(160f * v.flameLength),
                smokeLife = s.srbSmokeLifetime,
            };
            return p;
        }

        // ---------------------------------------------------------------- loading
        static Dictionary<string, ConfigNode> nodes = new Dictionary<string, ConfigNode>();
        static float lastLoad = -100f;

        public static string Folder { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/EjectionSeats/Plumes"); } }

        public static PlumeConfig For(SeatVariant v, EjectionSettings s)
        {
            var p = Defaults(v, s);
            Reload();
            ConfigNode n;
            if (v != null && (nodes.TryGetValue(v.id, out n) || nodes.TryGetValue("all", out n)))
            {
                try { p.Apply(n); Log.Dbg("plume '" + p.name + "' applied to " + v.id); }
                catch (Exception e) { Log.Warn("plume config for " + v.id + " is broken: " + e.Message); }
            }
            return p;
        }

        static void Reload()
        {
            if (Time.realtimeSinceStartup - lastLoad < 2f) return;
            lastLoad = Time.realtimeSinceStartup;
            var found = new Dictionary<string, ConfigNode>();
            try
            {
                if (Directory.Exists(Folder))
                {
                    var files = Directory.GetFiles(Folder, "*.cfg");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);   // later files win
                    foreach (var f in files)
                    {
                        ConfigNode root = ConfigNode.Load(f);
                        if (root == null) continue;
                        foreach (ConfigNode n in root.GetNodes("EJECTION_PLUME"))
                        {
                            string seat = (n.GetValue("seat") ?? "all").Trim();
                            found[seat] = n;
                        }
                    }
                }
            }
            catch (Exception e) { Log.Warn("Failed to read plume configs: " + e.Message); }
            nodes = found;
        }

        void Apply(ConfigNode n)
        {
            if (n.HasValue("name")) name = n.GetValue("name").Trim();
            V(n, "nozzleOffset", ref nozzleOffset);
            F(n, "nozzlePitch", ref nozzlePitch); F(n, "nozzleSplay", ref nozzleSplay);
            F(n, "flameLength", ref flameLength); F(n, "flameWidth", ref flameWidth);
            F(n, "coreLength", ref coreLength); F(n, "coreWidth", ref coreWidth);
            C(n, "coreColor", ref coreColor); C(n, "flameColor", ref flameColor); C(n, "edgeColor", ref edgeColor);
            F(n, "bodyOpacity", ref bodyOpacity); F(n, "outerOpacity", ref outerOpacity); F(n, "flicker", ref flicker);
            F(n, "glowSize", ref glowSize); F(n, "glowBrightness", ref glowBrightness);
            F(n, "lightRange", ref lightRange); F(n, "lightIntensity", ref lightIntensity);
            F(n, "fireRate", ref fireRate); F(n, "fireSize", ref fireSize); F(n, "fireSpeed", ref fireSpeed);
            F(n, "fireLife", ref fireLife); F(n, "fireSpread", ref fireSpread);
            F(n, "sparkRate", ref sparkRate); F(n, "sparkSpeed", ref sparkSpeed); F(n, "sparkLife", ref sparkLife); F(n, "sparkSpread", ref sparkSpread);
            F(n, "smokeAmount", ref smokeAmount); F(n, "smokeLife", ref smokeLife); F(n, "smokeSize", ref smokeSize);
            F(n, "smokeStart", ref smokeStart); F(n, "smokeOpacity", ref smokeOpacity); C(n, "smokeColor", ref smokeColor);
        }

        static void F(ConfigNode n, string k, ref float v)
        {
            float t;
            if (n.HasValue(k) && float.TryParse(Strip(n.GetValue(k)), NumberStyles.Float, CultureInfo.InvariantCulture, out t) && !float.IsNaN(t) && !float.IsInfinity(t)) v = t;
        }
        static float[] Nums(string s)
        {
            var parts = Strip(s).Split(',');
            var r = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out r[i])) return null;
            return r;
        }
        static void V(ConfigNode n, string k, ref Vector3 v)
        {
            if (!n.HasValue(k)) return;
            var f = Nums(n.GetValue(k));
            if (f != null && f.Length >= 3) v = new Vector3(f[0], f[1], f[2]);
        }
        static void C(ConfigNode n, string k, ref Color c)
        {
            if (!n.HasValue(k)) return;
            var f = Nums(n.GetValue(k));
            if (f != null && f.Length >= 3) c = new Color(Mathf.Clamp01(f[0]), Mathf.Clamp01(f[1]), Mathf.Clamp01(f[2]));
        }
        static string Strip(string s) { int i = s.IndexOf("//"); return (i >= 0 ? s.Substring(0, i) : s).Trim(); }

        // ---------------------------------------------------------------- nozzle fit (same maths as the editor)
        /// <summary>Moves and re-aims one nozzle transform (seat-root space, +Z = exhaust).</summary>
        public void FitNozzle(Transform n)
        {
            if (n == null) return;
            float side = n.localPosition.x < -0.005f ? -1f : 1f;
            n.localPosition += new Vector3(nozzleOffset.x * side, nozzleOffset.y, nozzleOffset.z);
            if (Mathf.Abs(nozzlePitch) < 1e-4f && Mathf.Abs(nozzleSplay) < 1e-4f) return;
            Vector3 dir = n.localRotation * Vector3.forward;
            if (Mathf.Abs(nozzlePitch) > 1e-4f) dir = Vector3.RotateTowards(dir, Vector3.forward, nozzlePitch * Mathf.Deg2Rad, 0f);
            if (Mathf.Abs(nozzleSplay) > 1e-4f) dir = Vector3.RotateTowards(dir, Vector3.right * side, nozzleSplay * Mathf.Deg2Rad, 0f);
            n.localRotation = Quaternion.LookRotation(dir, Vector3.forward);
        }
    }
}
