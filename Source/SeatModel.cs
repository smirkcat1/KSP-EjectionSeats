using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>One colour scheme for a seat. Each era has a stock-KSP and a ReStock version.</summary>
    public class SeatPalette
    {
        public Color frame, panel, cushion, metal, accent, dark, webbing, warning;
        public float frameGloss, metalGloss;
    }

    /// <summary>A specific seat, grouped by era and modelled on real hardware.</summary>
    public class SeatVariant
    {
        public string id, era, title;
        public SeatPalette stock, restock;
        public Color chuteA, chuteB;
        public Color flameCore, flameMid, flameOuter;
        public float flameLength = 1f, flameWidth = 1f;
        public int gores = 16;
        public string chutePart = "parachuteSingle";   // stock part whose canopy model is used

        public static readonly string[] EraIds = { "modern", "coldwar", "ww2" };
        public static readonly string[] EraTitles = { "Modern", "Cold War", "World War II" };

        static Color C(float r, float g, float b) { return new Color(r, g, b, 1f); }

        static SeatPalette P(Color frame, Color panel, Color cushion, Color metal, Color accent, Color web, Color warn, float fg, float mg)
        {
            return new SeatPalette { frame = frame, panel = panel, cushion = cushion, metal = metal, accent = accent, dark = C(0.05f, 0.05f, 0.052f),
                webbing = web, warning = warn, frameGloss = fg, metalGloss = mg };
        }

        /// <summary>ReStock version: darker, more saturated-down paint and shinier bare metal.</summary>
        static SeatPalette Re(SeatPalette p)
        {
            return new SeatPalette { frame = p.frame * 0.58f, panel = p.panel * 0.76f, cushion = p.cushion * 0.72f, metal = p.metal * 0.8f, accent = p.accent * 0.82f,
                dark = C(0.028f, 0.028f, 0.03f), webbing = p.webbing * 0.72f, warning = p.warning * 0.86f, frameGloss = p.frameGloss + 0.07f, metalGloss = p.metalGloss + 0.22f };
        }

        static SeatVariant Make(string id, string era, string title, string chute, int gores, SeatPalette stock, SeatPalette restock = null)
        {
            var v = new SeatVariant { id = id, era = era, title = title, chutePart = chute, gores = gores, stock = stock, restock = restock ?? Re(stock) };
            if (era == "modern") { v.chuteA = C(0.92f, 0.46f, 0.08f); v.chuteB = C(0.94f, 0.94f, 0.90f); v.flameCore = C(1f, 0.96f, 0.86f); v.flameMid = C(1f, 0.70f, 0.28f); v.flameOuter = C(1f, 0.34f, 0.07f); v.flameLength = 1.05f; v.flameWidth = 0.92f; }
            else if (era == "coldwar") { v.chuteA = C(0.95f, 0.45f, 0.08f); v.chuteB = C(0.95f, 0.94f, 0.90f); v.flameCore = C(1f, 0.90f, 0.66f); v.flameMid = C(1f, 0.56f, 0.15f); v.flameOuter = C(1f, 0.25f, 0.04f); v.flameLength = 1.2f; v.flameWidth = 1.05f; }
            else { v.chuteA = C(0.86f, 0.83f, 0.70f); v.chuteB = C(0.76f, 0.72f, 0.58f); v.flameCore = C(1f, 0.82f, 0.50f); v.flameMid = C(1f, 0.46f, 0.11f); v.flameOuter = C(0.90f, 0.20f, 0.04f); v.flameLength = 0.55f; v.flameWidth = 1.15f; }
            return v;
        }

        public static readonly List<SeatVariant> All = new List<SeatVariant>
        {
            Make("modern", "modern", "ACES II", "parachuteRadial", 20,
                P(C(0.60f, 0.62f, 0.62f), C(0.40f, 0.42f, 0.43f), C(0.30f, 0.32f, 0.31f), C(0.27f, 0.28f, 0.30f), C(0.95f, 0.50f, 0.10f), C(0.20f, 0.25f, 0.21f), C(0.95f, 0.72f, 0.12f), 0.28f, 0.45f),
                P(C(0.31f, 0.32f, 0.34f), C(0.70f, 0.68f, 0.61f), C(0.15f, 0.165f, 0.17f), C(0.22f, 0.225f, 0.24f), C(0.78f, 0.36f, 0.075f), C(0.12f, 0.14f, 0.13f), C(0.82f, 0.60f, 0.10f), 0.36f, 0.70f)),
            Make("us16e", "modern", "Martin-Baker US16E", "parachuteRadial", 20,
                P(C(0.25f, 0.255f, 0.27f), C(0.33f, 0.34f, 0.36f), C(0.17f, 0.175f, 0.19f), C(0.30f, 0.31f, 0.33f), C(0.28f, 0.52f, 0.62f), C(0.08f, 0.085f, 0.09f), C(0.95f, 0.72f, 0.10f), 0.24f, 0.42f)),
            Make("k36", "modern", "Zvezda K-36D", "parachuteLarge", 18,
                P(C(0.31f, 0.34f, 0.33f), C(0.36f, 0.40f, 0.36f), C(0.34f, 0.43f, 0.31f), C(0.25f, 0.26f, 0.27f), C(0.92f, 0.40f, 0.08f), C(0.58f, 0.58f, 0.52f), C(0.78f, 0.12f, 0.10f), 0.26f, 0.42f)),

            Make("coldwar", "coldwar", "Martin-Baker Mk 4/5", "parachuteSingle", 16,
                P(C(0.25f, 0.265f, 0.275f), C(0.34f, 0.36f, 0.32f), C(0.66f, 0.58f, 0.42f), C(0.30f, 0.31f, 0.32f), C(0.90f, 0.62f, 0.12f), C(0.47f, 0.42f, 0.27f), C(0.95f, 0.72f, 0.10f), 0.34f, 0.42f),
                P(C(0.17f, 0.18f, 0.19f), C(0.26f, 0.28f, 0.25f), C(0.47f, 0.42f, 0.31f), C(0.24f, 0.25f, 0.26f), C(0.72f, 0.46f, 0.09f), C(0.31f, 0.28f, 0.18f), C(0.80f, 0.58f, 0.09f), 0.38f, 0.66f)),
            Make("mk7", "coldwar", "Martin-Baker Mk 7", "parachuteSingle", 16,
                P(C(0.15f, 0.155f, 0.16f), C(0.30f, 0.34f, 0.25f), C(0.38f, 0.45f, 0.28f), C(0.30f, 0.31f, 0.32f), C(0.25f, 0.42f, 0.23f), C(0.74f, 0.64f, 0.26f), C(0.95f, 0.72f, 0.10f), 0.28f, 0.42f)),
            Make("km1", "coldwar", "KM-1", "parachuteLarge", 18,
                P(C(0.60f, 0.70f, 0.68f), C(0.44f, 0.54f, 0.53f), C(0.60f, 0.58f, 0.50f), C(0.46f, 0.47f, 0.47f), C(0.78f, 0.20f, 0.13f), C(0.76f, 0.71f, 0.58f), C(0.90f, 0.70f, 0.20f), 0.30f, 0.55f)),

            Make("ww2", "ww2", "Heinkel He 162", "parachuteSingle", 16,
                P(C(0.66f, 0.68f, 0.67f), C(0.50f, 0.52f, 0.50f), C(0.36f, 0.39f, 0.25f), C(0.72f, 0.73f, 0.72f), C(0.72f, 0.58f, 0.32f), C(0.72f, 0.66f, 0.50f), C(0.72f, 0.20f, 0.12f), 0.30f, 0.60f),
                P(C(0.40f, 0.42f, 0.41f), C(0.30f, 0.32f, 0.30f), C(0.24f, 0.27f, 0.17f), C(0.48f, 0.49f, 0.49f), C(0.48f, 0.36f, 0.17f), C(0.52f, 0.47f, 0.34f), C(0.55f, 0.15f, 0.09f), 0.34f, 0.74f)),
            Make("he219", "ww2", "Heinkel He 219", "parachuteSingle", 16,
                P(C(0.34f, 0.37f, 0.33f), C(0.26f, 0.28f, 0.25f), C(0.27f, 0.17f, 0.11f), C(0.48f, 0.49f, 0.48f), C(0.62f, 0.50f, 0.28f), C(0.56f, 0.51f, 0.38f), C(0.72f, 0.20f, 0.12f), 0.20f, 0.52f)),
        };

        public static List<SeatVariant> ForEra(string era)
        {
            var r = new List<SeatVariant>();
            foreach (var v in All) if (v.era == era) r.Add(v);
            return r;
        }

        public static SeatVariant Get(string id)
        {
            // earlier versions of the mod had five cosmetic seats
            if (id == "aces" || id == "spectre") id = "modern";
            else if (id == "mk10" || id == "zvezda" || id == "kosmo") id = "coldwar";
            foreach (var v in All) if (v.id == id) return v;
            return All[0];
        }
    }

    public static class SeatTheme
    {
        static int detected = -1;

        public static bool UseReStock(EjectionSettings s)
        {
            if (s.seatTheme == "restock") return true;
            if (s.seatTheme == "stock") return false;
            if (detected < 0)
            {
                detected = Directory.Exists(Path.Combine(KSPUtil.ApplicationRootPath, "GameData/ReStock")) ? 1 : 0;
                foreach (var a in AssemblyLoader.loadedAssemblies)
                    if (string.Equals(a.name, "Restock", StringComparison.OrdinalIgnoreCase)) detected = 1;
            }
            return detected == 1;
        }
    }

    public class SeatModelResult
    {
        public GameObject root;
        public List<Transform> nozzles = new List<Transform>();
    }

    /// <summary>
    /// Builds the seat. Local frame: origin = top of the seat cushion (centre of the pan),
    /// +Y = up the seat back, +Z = the direction the occupant faces. Geometry is authored at
    /// "human" size and uniformly scaled (default ~0.68) to fit a kerbal.
    /// </summary>
    public static class SeatModel
    {
        const int FRAME = 0, CUSHION = 1, HANDLE = 2, METAL = 3, TRIM = 4, DARK = 5, DECAL = 6, PANEL = 7, WARN = 8, WEB = 9, PAD = 10;
        static readonly Vector3 pivot = new Vector3(0f, -0.04f, -0.235f);
        static readonly Quaternion Side = Quaternion.LookRotation(Vector3.left, Vector3.up);   // plate XY = seat ZY

        public static SeatModelResult Build(SeatVariant v, float scale)
        {
            return Build(v, scale, SeatTheme.UseReStock(EjectionSettings.Get()));
        }

        public static SeatModelResult Build(SeatVariant v, float scale, bool restock)
        {
            var kit = new MeshKit(11);
            kit.scale = scale;
            kit.uvScale = 0.4f;
            var nozzles = new List<KeyValuePair<Vector3, Quaternion>>();
            switch (v.id)
            {
                case "us16e": US16E(kit, restock, nozzles); break;
                case "k36": K36(kit, restock, nozzles); break;
                case "coldwar": ColdWar(kit, restock, nozzles, false); break;
                case "mk7": ColdWar(kit, restock, nozzles, true); break;
                case "km1": KM1(kit, restock, nozzles); break;
                case "ww2": WorldWar2(kit, restock, nozzles); break;
                case "he219": He219(kit, restock, nozzles); break;
                default: Modern(kit, restock, nozzles); break;
            }

            SeatPalette p = restock ? v.restock : v.stock;
            Mesh mesh = kit.Build("EjectionSeat_" + v.id + (restock ? "_restock" : ""));
            var go = new GameObject("EjectionSeat_" + v.id);
            go.AddComponent<EjectionGenerated>();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            Texture2D hull = MatLib.Hull(restock), hullN = MatLib.HullNormal(restock);
            mr.sharedMaterials = new[]
            {
                MatLib.Bumped(p.frame, p.frameGloss, hull, hullN),
                MatLib.Bumped(p.cushion, 0.08f, MatLib.Fabric, MatLib.FabricNormal),
                MatLib.Solid(Color.white, 0.3f, MatLib.Stripes),
                MatLib.Bumped(p.metal, p.metalGloss, MatLib.Hull(false), MatLib.HullNormal(false)),
                MatLib.Solid(p.accent, restock ? 0.42f : 0.28f),
                MatLib.Solid(p.dark, restock ? 0.28f : 0.14f),
                MatLib.Solid(Color.white, 0.35f, MatLib.EjectDecal),
                MatLib.Bumped(p.panel, p.frameGloss, hull, hullN),
                MatLib.Solid(p.warning, 0.4f),
                MatLib.Solid(p.webbing, 0.06f),
                MatLib.Bumped(p.accent, 0.05f, MatLib.Fabric, MatLib.FabricNormal),
            };

            var res = new SeatModelResult { root = go };
            for (int i = 0; i < nozzles.Count; i++)
            {
                var n = new GameObject("ejectionNozzle" + i).transform;
                n.SetParent(go.transform, false);
                n.localPosition = nozzles[i].Key * scale;
                n.localRotation = nozzles[i].Value;   // +Z = exhaust direction
                res.nozzles.Add(n);
            }
            return res;
        }

        // ------------------------------------------------------------------ helpers
        static Vector2 V2(float z, float y) { return new Vector2(z, y); }
        static Vector3 Back(Quaternion rb, float x, float h, float d) { return pivot + rb * new Vector3(x, h, d); }
        static Vector2 BackP(Quaternion rb, float h, float d) { Vector3 p = pivot + rb * new Vector3(0f, h, d); return V2(p.z, p.y); }

        static void SidePlate(MeshKit kit, int slot, float x, float thick, params Vector2[] outline)
        {
            kit.Slot = slot;
            kit.Plate(outline, thick, new Vector3(x, 0f, 0f), Side);
            kit.Plate(outline, thick, new Vector3(-x, 0f, 0f), Side);
        }

        static void Rivets(MeshKit kit, Vector3 a, Vector3 b, int n, float r)
        {
            kit.Slot = METAL;
            for (int i = 0; i <= n; i++) kit.Sphere(Vector3.Lerp(a, b, i / (float)n), r, 6);
        }

        static void Web(MeshKit kit, Vector3 up, float width, params Vector3[] p)
        {
            kit.Slot = WEB;
            kit.Strap(p.Length > 2 ? MeshKit.Curve(p, 5) : p, up, width, 0.007f);
        }

        static void Buckle(MeshKit kit, Vector3 c, Quaternion r)
        {
            kit.Slot = METAL;
            kit.RoundedBox(c, new Vector3(0.05f, 0.032f, 0.012f), 0.004f, r);
        }

        static Vector3[] Hoop(Vector3 c, Vector3 a, Vector3 b, float ra, float rb, int n)
        {
            var p = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n * Mathf.PI * 2f;
                p[i] = c + a * (Mathf.Cos(t) * ra) + b * (Mathf.Sin(t) * rb);
            }
            return p;
        }

        static void Nozzle(MeshKit kit, List<KeyValuePair<Vector3, Quaternion>> list, Vector3 throat, Vector3 exhaust, float size)
        {
            exhaust.Normalize();
            Quaternion q = Quaternion.FromToRotation(Vector3.down, exhaust);
            var bell = new[] {
                new Vector2(0.034f, 0.02f) * size, new Vector2(0.026f, 0f) * size, new Vector2(0.029f, -0.022f) * size,
                new Vector2(0.040f, -0.05f) * size, new Vector2(0.053f, -0.08f) * size, new Vector2(0.060f, -0.095f) * size };
            kit.Slot = DARK; kit.Lathe(throat, q, bell, 24, true);
            kit.Slot = METAL; kit.Lathe(throat, q, bell, 24);
            Vector3 lip = throat + q * new Vector3(0f, -0.095f * size, 0f);
            list.Add(new KeyValuePair<Vector3, Quaternion>(lip + exhaust * 0.004f, Quaternion.LookRotation(exhaust, Vector3.forward)));
        }

        // ================================================================== modern (ACES II layout)
        static void Modern(MeshKit kit, bool rs, List<KeyValuePair<Vector3, Quaternion>> nozzles)
        {
            Quaternion rb = Quaternion.AngleAxis(-13f, Vector3.right);

            // full-height side plates: leg-guide ear at the front, stepped shoulder at the top
            SidePlate(kit, FRAME, 0.215f, 0.026f,
                V2(-0.30f, -0.215f), V2(0.13f, -0.215f), V2(0.20f, -0.285f), V2(0.305f, -0.285f), V2(0.325f, -0.20f),
                V2(0.265f, 0.05f), V2(0.195f, 0.05f), V2(0.15f, -0.035f), V2(-0.10f, -0.035f), V2(-0.155f, 0.005f),
                BackP(rb, 0.56f, 0.008f), BackP(rb, 0.66f, -0.035f), BackP(rb, 0.66f, -0.125f), BackP(rb, -0.08f, -0.125f));
            SidePlate(kit, DARK, 0.215f, 0.031f, V2(0.215f, -0.245f), V2(0.28f, -0.245f), V2(0.262f, -0.055f), V2(0.235f, -0.055f));
            SidePlate(kit, PANEL, 0.215f, 0.031f, V2(-0.20f, -0.185f), V2(0.095f, -0.185f), V2(0.095f, -0.075f), V2(-0.20f, -0.075f));
            SidePlate(kit, DARK, 0.215f, 0.0335f, V2(-0.165f, -0.165f), V2(-0.07f, -0.165f), V2(-0.095f, -0.095f), V2(-0.165f, -0.095f));

            kit.Slot = FRAME;      // pan and hidden motor housing between the plates
            kit.RoundedBox(new Vector3(0f, -0.078f, 0.025f), new Vector3(0.405f, 0.03f, 0.39f), 0.008f);
            kit.Slot = METAL;
            kit.RoundedBox(new Vector3(0f, -0.15f, -0.055f), new Vector3(0.36f, 0.115f, 0.31f), 0.014f);
            kit.Slot = PANEL;
            kit.RoundedBox(new Vector3(0f, -0.145f, 0.19f), new Vector3(0.36f, 0.095f, 0.018f), 0.006f);
            kit.Slot = CUSHION;
            kit.RoundedBox(new Vector3(0f, -0.032f, 0.022f), new Vector3(0.385f, 0.062f, 0.385f), 0.024f);

            kit.Slot = FRAME;      // boxy central spine and narrowed head section
            kit.RoundedBox(Back(rb, 0f, 0.42f, -0.08f), new Vector3(0.40f, 1.0f, 0.09f), 0.012f, rb);
            kit.RoundedBox(Back(rb, 0f, 0.93f, -0.07f), new Vector3(0.27f, 0.36f, 0.105f), 0.018f, rb);
            kit.Slot = PANEL;
            for (int s = -1; s <= 1; s += 2)
                kit.RoundedBox(Back(rb, s * 0.058f, 0.88f, -0.012f), new Vector3(0.094f, 0.25f, 0.018f), 0.006f, rb);
            kit.RoundedBox(Back(rb, 0f, 1.095f, -0.07f), new Vector3(0.23f, 0.05f, 0.12f), 0.022f, rb);
            for (int s = -1; s <= 1; s += 2)
                kit.RoundedBox(Back(rb, s * 0.175f, 0.72f, -0.035f), new Vector3(0.085f, 0.125f, 0.105f), 0.018f, rb);

            kit.Slot = CUSHION;    // stacked slab pads
            kit.RoundedBox(Back(rb, 0f, 0.235f, 0.022f), new Vector3(0.36f, 0.42f, 0.05f), 0.018f, rb);
            kit.RoundedBox(Back(rb, 0f, 0.565f, 0.03f), new Vector3(0.39f, 0.215f, 0.066f), 0.026f, rb);

            kit.Slot = METAL;      // V-shaped pitot horns over the top
            for (int s = -1; s <= 1; s += 2)
                kit.Tube(MeshKit.Curve(new[] { Back(rb, s * 0.09f, 1.11f, -0.05f), Back(rb, s * 0.17f, 1.17f, -0.03f), Back(rb, s * 0.285f, 1.11f, 0.02f) }, 6), 0.0085f, 8);

            Vector3 shoulderA = Back(rb, -0.052f, 0.705f, 0.066f), shoulderB = Back(rb, 0.052f, 0.705f, 0.066f);
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -0.052f : 0.052f;
                Web(kit, rb * Vector3.forward, 0.04f, Back(rb, x, 0.705f, 0.066f), Back(rb, x * 1.1f, 0.58f, 0.069f), Back(rb, x * 1.2f, 0.43f, 0.05f));
                Buckle(kit, Back(rb, x * 1.15f, 0.505f, 0.075f), rb);
                Web(kit, Vector3.up, 0.042f, new Vector3(x * 3.7f, 0.002f, -0.10f), new Vector3(x * 2.4f, 0.012f, 0.0f), new Vector3(x * 0.8f, 0.012f, 0.07f));
            }

            kit.Slot = WARN;       // centre ejection loop between the knees
            Vector3 lc = new Vector3(0f, 0.07f, 0.205f);
            kit.Tube(MeshKit.Curve(Hoop(lc, Vector3.right, new Vector3(0f, 0.96f, 0.28f).normalized, 0.04f, 0.062f, 10), 3, true), 0.0105f, 8, true);
            kit.Cylinder(lc + new Vector3(0f, -0.075f, -0.02f), lc + new Vector3(0f, 0.04f, 0.012f), 0.007f, 0.007f, 8);
            kit.Slot = HANDLE;     // striped arming lever
            kit.RoundedBox(new Vector3(-0.18f, 0.012f, 0.145f), new Vector3(0.065f, 0.03f, 0.022f), 0.006f);
            kit.Slot = DARK;       // sequencer cable loop on the side plate
            kit.Tube(MeshKit.Curve(new[] { Back(rb, 0.236f, 0.52f, -0.075f), Back(rb, 0.262f, 0.42f, -0.10f), Back(rb, 0.25f, 0.24f, -0.03f), new Vector3(0.242f, -0.08f, -0.18f) }, 7), 0.0065f, 7);
            kit.Slot = TRIM;
            kit.RoundedBox(Back(rb, 0f, 0.77f, -0.012f), new Vector3(0.19f, 0.014f, 0.014f), 0.003f, rb);
            kit.Slot = DECAL;
            kit.Decal(new Vector3(0f, -0.145f, 0.2f), Quaternion.identity, 0.13f, 0.046f);

            if (rs)
                for (int s = -1; s <= 1; s += 2)
                    Rivets(kit, new Vector3(s * 0.23f, -0.195f, -0.25f), new Vector3(s * 0.23f, -0.195f, 0.1f), 7, 0.0048f);
            Nozzle(kit, nozzles, new Vector3(0f, -0.205f, -0.075f), new Vector3(0f, -1f, -0.2f), 0.72f);
        }

        // ================================================================== Cold War (Martin-Baker Mk 4/5 layout)
        static void ColdWar(MeshKit kit, bool rs, List<KeyValuePair<Vector3, Quaternion>> nozzles, bool mk7)
        {
            Quaternion rb = Quaternion.AngleAxis(-15f, Vector3.right);

            // wedge-shaped riveted bucket, deep at the rear and open at the knees
            SidePlate(kit, FRAME, 0.205f, 0.02f,
                V2(-0.285f, -0.265f), V2(0.10f, -0.245f), V2(0.255f, -0.145f), V2(0.275f, -0.035f), V2(0.20f, 0.05f), V2(0.05f, 0.012f),
                V2(-0.125f, 0.03f), BackP(rb, 0.46f, 0.035f), BackP(rb, 0.72f, -0.02f), BackP(rb, 0.72f, -0.115f), BackP(rb, -0.14f, -0.115f));
            kit.Slot = FRAME;
            kit.Plate(new[] { V2(-0.20f, -0.235f), V2(0.20f, -0.235f), V2(0.20f, -0.215f), V2(-0.20f, -0.215f) }, 0.42f, Vector3.zero, Side);
            kit.RoundedBox(new Vector3(0f, -0.135f, 0.245f), new Vector3(0.39f, 0.17f, 0.016f), 0.005f, Quaternion.AngleAxis(-20f, Vector3.right));
            kit.RoundedBox(Back(rb, 0f, 0.31f, -0.09f), new Vector3(0.41f, 0.82f, 0.03f), 0.009f, rb);

            int rc = rs ? 12 : 7;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 0.2165f;
                Rivets(kit, new Vector3(x, -0.235f, -0.25f), new Vector3(x, -0.222f, 0.09f), rc, 0.0055f);
                Rivets(kit, new Vector3(x, -0.008f, -0.10f), new Vector3(x, -0.014f, 0.17f), rc - 2, 0.0055f);
                Rivets(kit, Back(rb, x, -0.08f, -0.095f), Back(rb, x, 0.66f, -0.095f), rc, 0.0055f);
                Rivets(kit, new Vector3(x, -0.20f, -0.05f), new Vector3(x, -0.03f, -0.05f), 4, 0.005f);
            }

            kit.Slot = METAL;      // rear main beam, guide rails and telescopic gun
            kit.RoundedBox(Back(rb, 0f, 0.53f, -0.145f), new Vector3(0.10f, 1.34f, 0.055f), 0.008f, rb);
            for (int s = -1; s <= 1; s += 2)
            {
                kit.RoundedBox(Back(rb, s * 0.145f, 0.48f, -0.14f), new Vector3(0.025f, 1.22f, 0.04f), 0.006f, rb);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 c = Back(rb, s * 0.145f, 0.05f + i * 0.47f, -0.14f);
                    kit.Cylinder(c - rb * Vector3.right * 0.03f, c + rb * Vector3.right * 0.03f, 0.018f, 0.018f, 12);
                }
            }
            kit.Cylinder(Back(rb, 0f, -0.14f, -0.188f), Back(rb, 0f, 0.62f, -0.188f), 0.04f, 0.04f, 18);
            kit.Cylinder(Back(rb, 0f, 0.62f, -0.188f), Back(rb, 0f, 1.16f, -0.188f), 0.029f, 0.029f, 18);
            Vector3 gun0 = Back(rb, 0.105f, 0.92f, -0.165f), gun1 = Back(rb, 0.105f, 1.25f, -0.17f);
            kit.Cylinder(gun0, gun1, 0.024f, 0.024f, 16);
            kit.Slot = DARK;       // drogue canister and head box
            kit.RoundedBox(Back(rb, 0f, 1.12f, -0.085f), new Vector3(0.30f, 0.19f, 0.13f), 0.03f, rb);
            kit.Cylinder(gun1, gun1 + rb * new Vector3(0f, 0.035f, 0f), 0.031f, 0.026f, 16);

            kit.Slot = CUSHION;    // bulky canvas back pad and parachute pack
            kit.RoundedBox(Back(rb, 0f, 0.20f, 0.018f), new Vector3(0.34f, 0.36f, 0.055f), 0.024f, rb);
            if (mk7)
            {
                // Mk 7: slimmer back with two tall green pack bottles
                kit.RoundedBox(Back(rb, 0f, 0.56f, 0.012f), new Vector3(0.26f, 0.38f, 0.055f), 0.024f, rb);
                for (int s = -1; s <= 1; s += 2)
                    kit.RoundedBox(Back(rb, s * 0.135f, 0.68f, -0.005f), new Vector3(0.125f, 0.44f, 0.125f), 0.058f, rb);
                kit.Slot = METAL;
                kit.Tube(MeshKit.Curve(new[] { Back(rb, 0.23f, 0.92f, -0.07f), Back(rb, 0.27f, 0.72f, -0.10f), Back(rb, 0.235f, 0.52f, -0.095f), Back(rb, 0.225f, 0.34f, -0.09f) }, 6), 0.012f, 10);
                kit.Slot = CUSHION;
            }
            else
            {
                kit.RoundedBox(Back(rb, 0f, 0.58f, 0.0f), new Vector3(0.36f, 0.42f, 0.13f), 0.058f, rb);
                for (int s = -1; s <= 1; s += 2)
                    kit.RoundedBox(Back(rb, s * 0.17f, 0.56f, 0.02f), new Vector3(0.075f, 0.36f, 0.11f), 0.035f, rb);
            }
            kit.RoundedBox(Back(rb, 0f, 0.925f, 0.002f), new Vector3(0.235f, mk7 ? 0.11f : 0.15f, 0.105f), mk7 ? 0.04f : 0.046f, rb);

            kit.Slot = PAD;        // split thigh cushions on the survival pack
            for (int s = -1; s <= 1; s += 2)
                kit.RoundedBox(new Vector3(s * 0.098f, -0.032f, 0.05f), new Vector3(0.178f, 0.074f, 0.40f), 0.036f);
            kit.Slot = PANEL;
            kit.RoundedBox(new Vector3(0f, -0.12f, 0.025f), new Vector3(0.385f, 0.105f, 0.44f), 0.015f);

            for (int s = -1; s <= 1; s += 2)
            {
                Web(kit, rb * Vector3.forward, 0.052f, Back(rb, s * 0.075f, 0.79f, 0.07f), Back(rb, s * 0.095f, 0.62f, 0.078f), Back(rb, s * 0.105f, 0.38f, 0.06f), Back(rb, s * 0.055f, 0.17f, 0.06f));
                Web(kit, Vector3.up, 0.05f, new Vector3(s * 0.197f, 0.012f, -0.13f), new Vector3(s * 0.12f, 0.016f, -0.045f), new Vector3(s * 0.035f, 0.014f, 0.035f));
                Buckle(kit, Back(rb, s * 0.1f, 0.48f, 0.082f), rb);
            }
            kit.Slot = METAL;
            kit.Cylinder(new Vector3(0f, 0.012f, 0.025f), new Vector3(0f, 0.03f, 0.025f), 0.034f, 0.034f, 18);

            kit.Slot = HANDLE;     // striped face-blind bar across the top and pan firing loop
            Vector3 barL = Back(rb, -0.17f, 1.09f, 0.12f), barR = Back(rb, 0.17f, 1.09f, 0.12f);
            kit.Tube(new[] { barL, Vector3.Lerp(barL, barR, 0.5f), barR }, 0.0165f, 10);
            for (int s = -1; s <= 1; s += 2)
                kit.Tube(MeshKit.Curve(new[] { Back(rb, s * 0.17f, 1.09f, 0.12f), Back(rb, s * 0.16f, 1.13f, 0.05f), Back(rb, s * 0.12f, 1.13f, -0.03f) }, 5), 0.0115f, 8);
            if (mk7)
                for (int s = -1; s <= 1; s += 2)   // Mk 7 hanging face-blind loops
                    kit.Tube(MeshKit.Curve(Hoop(Back(rb, s * 0.085f, 1.03f, 0.155f), rb * Vector3.right, rb * new Vector3(0f, 0.92f, 0.38f).normalized, 0.058f, 0.075f, 10), 3, true), 0.0095f, 8, true);
            Vector3 d = new Vector3(0f, 0.018f, 0.245f);
            kit.Tube(MeshKit.Curve(Hoop(d, Vector3.right, new Vector3(0f, 0.8f, 0.6f).normalized, 0.058f, 0.05f, 10), 3, true), 0.0115f, 8, true);

            kit.Slot = WEB;        // leg restraint lines
            for (int s = -1; s <= 1; s += 2)
                kit.Tube(MeshKit.Curve(new[] { new Vector3(s * 0.15f, -0.165f, 0.235f), new Vector3(s * 0.18f, -0.25f, 0.27f), new Vector3(s * 0.14f, -0.30f, 0.19f) }, 5), 0.006f, 6);
            if (mk7)
                SidePlate(kit, FRAME, 0.178f, 0.018f, V2(0.20f, -0.20f), V2(0.34f, -0.255f), V2(0.405f, -0.17f), V2(0.335f, -0.075f), V2(0.25f, -0.06f));
            kit.Slot = DECAL;
            kit.Decal(new Vector3(0f, -0.145f, 0.2548f), Quaternion.AngleAxis(-20f, Vector3.right), 0.12f, 0.042f);

            for (int s = -1; s <= 1; s += 2)
                Nozzle(kit, nozzles, new Vector3(s * 0.11f, -0.235f, -0.13f), new Vector3(s * 0.08f, -1f, -0.22f), 0.68f);
        }

        // ================================================================== World War II (Heinkel He 162 layout)
        static void WorldWar2(MeshKit kit, bool rs, List<KeyValuePair<Vector3, Quaternion>> nozzles)
        {
            Quaternion rb = Quaternion.AngleAxis(-20f, Vector3.right);

            // twin bare guide columns, running from below the seat to above the head
            kit.Slot = METAL;
            for (int s = -1; s <= 1; s += 2)
            {
                kit.RoundedBox(Back(rb, s * 0.145f, 0.37f, -0.085f), new Vector3(0.05f, 1.28f, 0.062f), 0.006f, rb);
                kit.Slot = FRAME;
                kit.RoundedBox(Back(rb, s * 0.145f, 0.37f, -0.052f), new Vector3(0.028f, 1.22f, 0.006f), 0.002f, rb);
                kit.Slot = METAL;
            }
            kit.Slot = FRAME;      // head plate and cross-braces between the columns
            kit.RoundedBox(Back(rb, 0f, 0.82f, -0.075f), new Vector3(0.30f, 0.43f, 0.018f), 0.006f, rb);
            for (int i = 0; i < 2; i++)
                kit.RoundedBox(Back(rb, 0f, 0.10f + i * 0.36f, -0.085f), new Vector3(0.28f, 0.035f, 0.035f), 0.006f, rb);

            var arch = new List<Vector2> { new Vector2(-0.112f, -0.105f), new Vector2(0.112f, -0.105f), new Vector2(0.125f, -0.08f), new Vector2(0.125f, 0.035f) };
            for (int i = 1; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI;
                arch.Add(new Vector2(Mathf.Cos(a) * 0.125f, 0.035f + Mathf.Sin(a) * 0.105f));
            }
            arch.Add(new Vector2(-0.125f, 0.035f)); arch.Add(new Vector2(-0.125f, -0.08f));
            kit.Slot = DARK;       // arched black leather head pad
            kit.Plate(arch.ToArray(), 0.05f, Back(rb, 0f, 0.84f, -0.045f), rb);

            // curved metal seat scoop
            var scoop = new Vector3[9];
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f, a = Mathf.Lerp(-0.2f, 1.42f, t);
                scoop[i] = new Vector3(0f, -0.055f - 0.145f * Mathf.Sin(Mathf.Clamp01(1f - t) * Mathf.PI * 0.5f) * 0.45f + 0.20f * (1f - Mathf.Cos(a)) * t, 0.205f - 0.43f * t);
            }
            kit.Slot = PANEL;
            kit.Strap(MeshKit.Curve(scoop, 3), Vector3.up, 0.40f, 0.012f);
            SidePlate(kit, PANEL, 0.2f, 0.014f, V2(-0.235f, -0.13f), V2(-0.06f, -0.17f), V2(0.13f, -0.12f), V2(0.215f, -0.055f), V2(0.15f, -0.015f), V2(-0.08f, -0.02f), V2(-0.215f, 0.06f));

            kit.Slot = CUSHION;    // padded canvas seat and back sling
            kit.RoundedBox(new Vector3(0f, -0.03f, 0.018f), new Vector3(0.375f, 0.07f, 0.385f), 0.032f);
            kit.RoundedBox(Back(rb, 0f, 0.29f, 0.022f), new Vector3(0.325f, 0.52f, 0.05f), 0.024f, rb);
            kit.Slot = METAL;
            for (int s = -1; s <= 1; s += 2)
            {
                kit.Sphere(Back(rb, s * 0.075f, 0.51f, 0.05f), 0.009f, 8);
                kit.Sphere(Back(rb, s * 0.075f, 0.09f, 0.05f), 0.009f, 8);
            }

            for (int s = -1; s <= 1; s += 2)
            {
                // crossed shoulder webbing from the head-plate bar down to the lap
                Web(kit, rb * Vector3.forward, 0.05f, Back(rb, s * 0.12f, 0.62f, -0.02f), Back(rb, s * 0.07f, 0.53f, 0.058f), Back(rb, -s * 0.09f, 0.28f, 0.056f), Back(rb, -s * 0.15f, 0.07f, 0.055f));
                Web(kit, Vector3.up, 0.05f, new Vector3(s * 0.20f, 0.012f, -0.10f), new Vector3(s * 0.13f, 0.012f, 0.0f), new Vector3(s * 0.035f, 0.012f, 0.06f));

                // inverted-U side hoops and long foot stirrups
                kit.Slot = DARK;
                float x = s * 0.235f;
                kit.Tube(MeshKit.Curve(new[] { new Vector3(x, -0.11f, 0.03f), new Vector3(x, 0.09f, 0.02f), new Vector3(x, 0.175f, 0.08f), new Vector3(x, 0.16f, 0.20f), new Vector3(x, -0.03f, 0.26f), new Vector3(x, -0.12f, 0.27f) }, 6), 0.0135f, 10);
                float a = s * 0.075f, b = s * 0.19f;
                var stirrup = new[] { new Vector3(a, -0.15f, 0.22f), new Vector3(a, -0.265f, 0.47f), new Vector3(b, -0.265f, 0.47f), new Vector3(b, -0.15f, 0.22f) };
                kit.Tube(new[] { stirrup[0], stirrup[1] }, 0.0115f, 8);
                kit.Tube(new[] { stirrup[1], stirrup[2] }, 0.0115f, 8);
                kit.Tube(new[] { stirrup[2], stirrup[3] }, 0.0115f, 8);
                kit.Sphere(stirrup[1], 0.0115f, 8); kit.Sphere(stirrup[2], 0.0115f, 8);
                kit.Tube(new[] { new Vector3(a, -0.225f, 0.385f), new Vector3(b, -0.225f, 0.385f) }, 0.009f, 8);
                kit.Tube(new[] { new Vector3(a, -0.185f, 0.30f), new Vector3(b, -0.185f, 0.30f) }, 0.009f, 8);

                kit.Slot = METAL;   // brace from the scoop to the column
                kit.Tube(new[] { new Vector3(s * 0.13f, -0.10f, 0.10f), Back(rb, s * 0.145f, -0.17f, -0.07f) }, 0.012f, 10);
            }

            kit.Slot = METAL;      // cartridge catapult tube between the columns
            Vector3 t0 = Back(rb, 0f, -0.27f, -0.105f), t1 = Back(rb, 0f, 0.74f, -0.105f);
            kit.Cylinder(t0, t1, 0.036f, 0.03f, 18);
            kit.Slot = TRIM;
            for (int i = 0; i < 3; i++)
            {
                Vector3 c = Back(rb, 0f, -0.14f + i * 0.31f, -0.105f);
                kit.Cylinder(c, c + rb * new Vector3(0f, 0.016f, 0f), 0.043f, 0.043f, 18);
            }
            kit.Slot = WARN;       // firing lever
            Vector3 l0 = new Vector3(0.232f, 0.03f, -0.03f), l1 = l0 + new Vector3(0.012f, 0.11f, 0.055f);
            kit.Cylinder(l0, l1, 0.0075f, 0.0065f, 8);
            kit.Sphere(l1, 0.017f, 12);

            int rc = rs ? 8 : 4;
            for (int s = -1; s <= 1; s += 2)
                Rivets(kit, new Vector3(s * 0.2085f, -0.13f, -0.16f), new Vector3(s * 0.2085f, -0.09f, 0.14f), rc, 0.005f);

            Nozzle(kit, nozzles, Back(rb, 0f, -0.27f, -0.105f), rb * new Vector3(0f, -1f, -0.05f), 0.7f);
        }

        // ================================================================== US16E: black head box with two round head pads
        static void US16E(MeshKit kit, bool rs, List<KeyValuePair<Vector3, Quaternion>> nozzles)
        {
            Quaternion rb = Quaternion.AngleAxis(-16f, Vector3.right);
            SidePlate(kit, FRAME, 0.205f, 0.024f,
                V2(-0.30f, -0.235f), V2(0.11f, -0.235f), V2(0.27f, -0.19f), V2(0.325f, -0.075f), V2(0.285f, -0.015f), V2(0.13f, -0.015f), V2(-0.08f, -0.03f),
                BackP(rb, 0.33f, 0.012f), BackP(rb, 0.55f, -0.04f), BackP(rb, 0.55f, -0.115f), BackP(rb, -0.12f, -0.115f));
            SidePlate(kit, PANEL, 0.205f, 0.029f, V2(-0.21f, -0.20f), V2(0.08f, -0.20f), V2(0.13f, -0.08f), V2(-0.21f, -0.08f));
            kit.Slot = FRAME;
            kit.RoundedBox(new Vector3(0f, -0.10f, 0.02f), new Vector3(0.39f, 0.12f, 0.43f), 0.012f);
            kit.RoundedBox(Back(rb, 0f, 0.42f, -0.085f), new Vector3(0.34f, 1.0f, 0.075f), 0.012f, rb);
            kit.Slot = CUSHION;
            for (int s = -1; s <= 1; s += 2)
                kit.RoundedBox(new Vector3(s * 0.098f, -0.022f, 0.055f), new Vector3(0.18f, 0.066f, 0.45f), 0.032f);
            kit.RoundedBox(Back(rb, 0f, 0.30f, 0.018f), new Vector3(0.33f, 0.58f, 0.052f), 0.024f, rb);

            kit.Slot = FRAME;      // large square head box
            kit.RoundedBox(Back(rb, 0f, 0.99f, -0.05f), new Vector3(0.43f, 0.30f, 0.17f), 0.02f, rb);
            kit.Slot = PANEL;
            kit.RoundedBox(Back(rb, 0f, 1.15f, -0.055f), new Vector3(0.39f, 0.025f, 0.135f), 0.006f, rb);
            kit.Slot = CUSHION;    // the two round head pads
            for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 c0 = Back(rb, s * 0.105f, 0.82f, -0.025f), c1 = Back(rb, s * 0.105f, 0.82f, 0.03f), c2 = Back(rb, s * 0.105f, 0.82f, 0.05f);
                    kit.Cylinder(c0, c1, 0.085f, 0.085f, 24, true, false);
                    kit.Cylinder(c1, c2, 0.085f, 0.066f, 24, false, true);
                }
            kit.Slot = WARN;
            kit.Plate(new[] { new Vector2(-0.018f, 0.018f), new Vector2(0.018f, 0.018f), new Vector2(0f, -0.018f) }, 0.004f, Back(rb, 0f, 1.03f, 0.038f), rb);
            kit.Slot = METAL;
            Vector3 g0 = Back(rb, 0.20f, 0.56f, -0.13f), g1 = Back(rb, 0.20f, 0.96f, -0.135f);
            kit.Cylinder(g0, g1, 0.03f, 0.03f, 18);

            for (int s = -1; s <= 1; s += 2)
            {
                Web(kit, rb * Vector3.forward, 0.05f, Back(rb, s * 0.09f, 0.70f, 0.05f), Back(rb, s * 0.10f, 0.50f, 0.052f), Back(rb, s * 0.055f, 0.13f, 0.052f));
                Web(kit, Vector3.up, 0.048f, new Vector3(s * 0.19f, 0.018f, -0.12f), new Vector3(s * 0.11f, 0.018f, -0.04f), new Vector3(s * 0.025f, 0.018f, 0.02f));
                kit.Slot = TRIM;   // row of blue gas bottles along the base
                for (int i = 0; i < 3; i++)
                {
                    Vector3 a = new Vector3(s * (0.055f + i * 0.055f), -0.225f, 0.17f);
                    kit.Cylinder(a, a + new Vector3(0f, -0.014f, 0.08f), 0.017f, 0.017f, 10);
                }
            }
            kit.Slot = METAL; kit.Cylinder(new Vector3(0f, 0.012f, 0.0f), new Vector3(0f, 0.026f, 0.0f), 0.036f, 0.036f, 18);
            kit.Slot = WARN;       // cord firing loop
            Vector3 lc = new Vector3(0f, 0.07f, 0.17f);
            kit.Tube(MeshKit.Curve(Hoop(lc, Vector3.right, new Vector3(0f, 0.96f, 0.28f).normalized, 0.035f, 0.075f, 10), 3, true), 0.0075f, 8, true);
            Nozzle(kit, nozzles, new Vector3(0f, -0.235f, -0.09f), new Vector3(0f, -1f, -0.2f), 0.72f);
        }

        // ================================================================== K-36D: tall green pad, shoulder rocket tubes
        static void K36(MeshKit kit, bool rs, List<KeyValuePair<Vector3, Quaternion>> nozzles)
        {
            Quaternion rb = Quaternion.AngleAxis(-15f, Vector3.right);
            SidePlate(kit, FRAME, 0.205f, 0.024f,
                V2(-0.29f, -0.25f), V2(0.13f, -0.25f), V2(0.27f, -0.19f), V2(0.30f, -0.03f), V2(0.23f, 0.075f), V2(0.13f, 0.025f), V2(-0.14f, 0.025f),
                BackP(rb, 0.62f, 0.01f), BackP(rb, 0.78f, -0.03f), BackP(rb, 0.78f, -0.12f), BackP(rb, -0.11f, -0.12f));
            kit.Slot = FRAME;
            kit.RoundedBox(new Vector3(0f, -0.13f, 0.015f), new Vector3(0.39f, 0.20f, 0.42f), 0.014f);
            kit.RoundedBox(Back(rb, 0f, 0.48f, -0.085f), new Vector3(0.38f, 1.12f, 0.075f), 0.018f, rb);
            kit.Slot = CUSHION;
            for (int s = -1; s <= 1; s += 2)
                kit.RoundedBox(new Vector3(s * 0.098f, -0.022f, 0.035f), new Vector3(0.178f, 0.07f, 0.40f), 0.035f);

            var pad = new List<Vector2> { new Vector2(-0.165f, -0.36f), new Vector2(0.165f, -0.36f), new Vector2(0.175f, 0.20f) };
            for (int i = 0; i <= 12; i++)
            {
                float a = i / 12f * Mathf.PI;
                pad.Add(new Vector2(Mathf.Cos(a) * 0.145f, 0.36f + Mathf.Sin(a) * 0.12f));
            }
            pad.Add(new Vector2(-0.175f, 0.20f));
            kit.Slot = CUSHION;    // one tall rounded back and head pad
            kit.Plate(pad.ToArray(), 0.055f, Back(rb, 0f, 0.42f, 0.018f), rb);
            kit.Slot = WARN;       // red warning placards
            kit.RoundedBox(Back(rb, 0f, 0.56f, 0.052f), new Vector3(0.12f, 0.09f, 0.006f), 0.002f, rb);
            kit.Slot = DARK;       // stabiliser tubes at shoulder height
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 c = Back(rb, s * 0.205f, 0.72f, -0.075f);
                kit.Cylinder(c, c + rb * new Vector3(0f, 0f, 0.15f), 0.048f, 0.052f, 20);
                kit.Slot = METAL;
                kit.Tube(MeshKit.Curve(new[] { Back(rb, s * 0.12f, 0.76f, 0.055f), Back(rb, s * 0.07f, 0.56f, 0.066f), Back(rb, s * 0.075f, 0.30f, 0.055f) }, 5), 0.005f, 6);
                kit.Slot = DARK;
                Web(kit, Vector3.up, 0.044f, new Vector3(s * 0.195f, 0.017f, -0.11f), new Vector3(s * 0.10f, 0.017f, -0.02f), new Vector3(s * 0.025f, 0.017f, 0.04f));
                kit.Slot = DARK;
            }
            kit.Slot = TRIM;       // orange pull straps at the seat front
            kit.RoundedBox(new Vector3(0f, 0.022f, 0.13f), new Vector3(0.05f, 0.012f, 0.12f), 0.003f, Quaternion.AngleAxis(-10f, Vector3.right));
            kit.Slot = WARN;
            Vector3 lc = new Vector3(0f, 0.065f, 0.195f);
            kit.Tube(MeshKit.Curve(Hoop(lc, Vector3.right, new Vector3(0f, 0.9f, 0.42f).normalized, 0.05f, 0.052f, 10), 3, true), 0.0095f, 8, true);
            Nozzle(kit, nozzles, new Vector3(-0.09f, -0.25f, -0.10f), new Vector3(-0.05f, -1f, -0.2f), 0.66f);
            Nozzle(kit, nozzles, new Vector3(0.09f, -0.25f, -0.10f), new Vector3(0.05f, -1f, -0.2f), 0.66f);
        }

        // ================================================================== KM-1: pale shell back, split pan, foot pedals
        static void KM1(MeshKit kit, bool rs, List<KeyValuePair<Vector3, Quaternion>> nozzles)
        {
            Quaternion rb = Quaternion.AngleAxis(-14f, Vector3.right);
            var shell = new[] {
                new Vector2(-0.16f, -0.02f), new Vector2(0.16f, -0.02f), new Vector2(0.20f, 0.05f), new Vector2(0.235f, 0.42f), new Vector2(0.215f, 0.60f),
                new Vector2(0.155f, 0.66f), new Vector2(-0.155f, 0.66f), new Vector2(-0.215f, 0.60f), new Vector2(-0.235f, 0.42f), new Vector2(-0.20f, 0.05f) };
            kit.Slot = FRAME;      // one-piece pressed back shell with raised centre panel
            kit.Plate(shell, 0.024f, Back(rb, 0f, 0.20f, 0.0f), rb);
            for (int s = -1; s <= 1; s += 2)
                kit.Strap(new[] { Back(rb, s * 0.205f, 0.20f, 0.018f), Back(rb, s * 0.232f, 0.60f, 0.018f), Back(rb, s * 0.212f, 0.80f, 0.018f) }, rb * Vector3.right * s, 0.045f, 0.012f);
            kit.Slot = PANEL;
            kit.RoundedBox(Back(rb, 0f, 0.55f, 0.016f), new Vector3(0.21f, 0.32f, 0.012f), 0.008f, rb);
            kit.Slot = CUSHION;
            kit.RoundedBox(Back(rb, 0f, 0.91f, 0.018f), new Vector3(0.30f, 0.17f, 0.075f), 0.035f, rb);   // canvas head pad
            kit.RoundedBox(Back(rb, 0f, 0.28f, 0.022f), new Vector3(0.28f, 0.30f, 0.05f), 0.022f, rb);

            kit.Slot = METAL;      // exposed gun, cross tube and mechanism between back and pan
            kit.Tube(new[] { Back(rb, -0.235f, 0.14f, -0.015f), Back(rb, 0.235f, 0.14f, -0.015f) }, 0.015f, 12);
            kit.Cylinder(Back(rb, 0f, -0.20f, -0.075f), Back(rb, 0f, 1.03f, -0.075f), 0.034f, 0.034f, 18);
            for (int s = -1; s <= 1; s += 2)
                kit.RoundedBox(Back(rb, s * 0.125f, 0.42f, -0.06f), new Vector3(0.028f, 1.15f, 0.04f), 0.006f, rb);

            SidePlate(kit, FRAME, 0.205f, 0.02f, V2(-0.17f, -0.20f), V2(0.13f, -0.20f), V2(0.29f, -0.115f), V2(0.335f, 0.01f), V2(0.27f, 0.03f), V2(0.18f, -0.01f), V2(-0.13f, 0.02f));
            kit.Slot = FRAME;
            kit.RoundedBox(new Vector3(0f, -0.105f, 0.03f), new Vector3(0.39f, 0.05f, 0.39f), 0.008f);
            kit.Slot = CUSHION;
            kit.RoundedBox(new Vector3(0f, -0.04f, 0.03f), new Vector3(0.36f, 0.075f, 0.37f), 0.024f);
            Web(kit, Vector3.up, 0.042f, new Vector3(-0.12f, 0.004f, -0.09f), new Vector3(-0.03f, 0.008f, 0.04f), new Vector3(0f, 0.008f, 0.13f));
            Web(kit, Vector3.up, 0.042f, new Vector3(0.12f, 0.004f, -0.09f), new Vector3(0.03f, 0.008f, 0.04f), new Vector3(0f, 0.008f, 0.13f));
            kit.Slot = TRIM; kit.RoundedBox(new Vector3(0f, 0.012f, 0.085f), new Vector3(0.042f, 0.012f, 0.032f), 0.003f);

            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 0.125f;   // forward foot pedals with side cheeks
                kit.Slot = METAL;
                kit.Tube(new[] { new Vector3(x, -0.14f, 0.20f), new Vector3(x, -0.285f, 0.40f) }, 0.014f, 10);
                kit.Slot = PANEL;
                kit.RoundedBox(new Vector3(x, -0.305f, 0.45f), new Vector3(0.115f, 0.03f, 0.15f), 0.008f, Quaternion.AngleAxis(-18f, Vector3.right));
                kit.Slot = FRAME;
                kit.Plate(new[] { V2(0.385f, -0.31f), V2(0.525f, -0.355f), V2(0.525f, -0.285f), V2(0.43f, -0.23f) }, 0.012f, new Vector3(x + s * 0.06f, 0f, 0f), Side);
                kit.Slot = WEB;
                kit.RoundedBox(new Vector3(x, -0.285f, 0.47f), new Vector3(0.09f, 0.012f, 0.03f), 0.003f, Quaternion.AngleAxis(-18f, Vector3.right));
            }
            kit.Slot = WARN;
            kit.Tube(MeshKit.Curve(Hoop(new Vector3(0f, 0.04f, 0.225f), Vector3.right, new Vector3(0f, 0.8f, 0.6f).normalized, 0.052f, 0.04f, 10), 3, true), 0.009f, 8, true);
            Nozzle(kit, nozzles, Back(rb, 0f, -0.2f, -0.075f), rb * new Vector3(0f, -1f, -0.06f), 0.74f);
        }

        // ================================================================== He 219: leather pads on a riveted side frame
        static void He219(MeshKit kit, bool rs, List<KeyValuePair<Vector3, Quaternion>> nozzles)
        {
            Quaternion rb = Quaternion.AngleAxis(-13f, Vector3.right);
            SidePlate(kit, FRAME, 0.21f, 0.022f,
                V2(-0.265f, -0.20f), V2(0.13f, -0.20f), V2(0.235f, -0.11f), V2(0.24f, 0.015f), V2(0.135f, 0.045f), V2(-0.115f, 0.075f),
                BackP(rb, 0.72f, 0.06f), BackP(rb, 1.0f, 0.02f), BackP(rb, 1.04f, -0.05f), BackP(rb, 1.0f, -0.10f), BackP(rb, -0.08f, -0.10f));
            kit.Slot = FRAME;
            kit.RoundedBox(new Vector3(0f, -0.145f, 0.0f), new Vector3(0.41f, 0.11f, 0.40f), 0.012f);
            kit.RoundedBox(Back(rb, 0f, 0.46f, -0.085f), new Vector3(0.41f, 1.08f, 0.024f), 0.008f, rb);
            int rc = rs ? 16 : 9;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 0.2215f;
                Rivets(kit, Back(rb, x, -0.03f, -0.088f), Back(rb, x, 0.97f, -0.088f), rc, 0.0055f);
                Rivets(kit, Back(rb, x, 0.1f, 0.04f), Back(rb, x, 0.96f, 0.012f), rc - 2, 0.0055f);
                Rivets(kit, new Vector3(x, -0.183f, -0.21f), new Vector3(x, -0.183f, 0.11f), rs ? 7 : 4, 0.0055f);
            }
            kit.Slot = CUSHION;    // heavy buttoned leather
            kit.RoundedBox(new Vector3(0f, -0.04f, 0.015f), new Vector3(0.385f, 0.11f, 0.40f), 0.05f);
            kit.RoundedBox(Back(rb, 0f, 0.31f, 0.012f), new Vector3(0.36f, 0.56f, 0.075f), 0.035f, rb);
            kit.RoundedBox(Back(rb, 0f, 0.87f, 0.0f), new Vector3(0.30f, 0.26f, 0.095f), 0.046f, rb);

            kit.Slot = METAL;      // exposed compressed-air gun down the right side and rear guide
            Vector3 a0 = Back(rb, 0.262f, 0.05f, -0.055f), a1 = Back(rb, 0.262f, 0.84f, -0.055f);
            kit.Cylinder(a0, a1, 0.033f, 0.033f, 18);
            kit.Slot = TRIM;
            for (int i = 0; i < 2; i++)
            {
                Vector3 c = Vector3.Lerp(a0, a1, 0.25f + i * 0.5f);
                kit.Cylinder(c, c + rb * new Vector3(0f, 0.022f, 0f), 0.04f, 0.04f, 18);
            }
            kit.Slot = METAL;
            kit.RoundedBox(Back(rb, 0f, 0.5f, -0.122f), new Vector3(0.09f, 1.18f, 0.05f), 0.008f, rb);
            for (int s = -1; s <= 1; s += 2)
                Web(kit, rb * Vector3.forward, 0.05f, Back(rb, s * 0.10f, 0.72f, 0.052f), Back(rb, s * 0.11f, 0.45f, 0.056f), Back(rb, s * 0.12f, 0.10f, 0.054f));
            Web(kit, Vector3.up, 0.052f, new Vector3(-0.205f, 0.02f, -0.09f), new Vector3(-0.10f, 0.022f, -0.02f), new Vector3(0f, 0.022f, 0.01f), new Vector3(0.10f, 0.022f, -0.02f), new Vector3(0.205f, 0.02f, -0.09f));
            kit.Slot = METAL; kit.RoundedBox(new Vector3(0f, 0.026f, 0.012f), new Vector3(0.06f, 0.014f, 0.042f), 0.004f);
            kit.Slot = WARN;
            Vector3 l0 = new Vector3(-0.225f, 0.02f, 0.03f), l1 = l0 + new Vector3(-0.014f, 0.105f, 0.055f);
            kit.Cylinder(l0, l1, 0.0075f, 0.0065f, 8); kit.Sphere(l1, 0.017f, 12);
            Nozzle(kit, nozzles, Back(rb, 0.262f, 0.03f, -0.055f), rb * new Vector3(0.06f, -1f, -0.08f), 0.62f);
        }
    }
}
