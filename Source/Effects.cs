using System;
using System.Collections.Generic;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Seat rocket plume. Uses BDArmory's missile exhaust prefabs when BDArmory is installed
    /// (same look as BDA missiles), otherwise a built-in flame + smoke trail particle system.
    /// </summary>
    public class SeatRocketFX : MonoBehaviour
    {
        readonly List<KSPParticleEmitter> kspEmitters = new List<KSPParticleEmitter>();
        readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        readonly List<Light> lights = new List<Light>();
        readonly List<SrbSmoke> smokes = new List<SrbSmoke>();
        float smokePower;
        AudioSource audioSrc;
        bool thrusting;

        // layered flame: camera-facing sheets along the exhaust axis, plus a round glow at the nozzle
        class FlameSheet
        {
            public Transform t; public Mesh mesh; public Color32[] colors = new Color32[4];
            public Color tint; public float length, width, flickerRate, seed;
            public Transform nozzle; public bool glow;
        }
        readonly List<FlameSheet> sheets = new List<FlameSheet>();
        float power, thrustAge;
        public static Camera ViewCameraOverride;   // used by the development render tool

        public static SeatRocketFX Create(GameObject seatRoot, List<Transform> nozzles, EjectionSettings s, SeatVariant variant = null)
        {
            var fx = seatRoot.AddComponent<SeatRocketFX>();
            if (variant == null) variant = SeatVariant.All[0];
            foreach (var n in nozzles)
            {
                bool ok = false;
                if (s.useBDAPlumes && !string.IsNullOrEmpty(s.plumePrefab) && GameDatabase.Instance.ExistsModel(s.plumePrefab))
                    ok = fx.AttachBDA(n, s);
                if (!ok) fx.AttachBuiltIn(n, s);
                if (s.enhancedFlames) fx.AttachFlame(n, s, variant);
                if (s.srbSmoke) fx.smokes.Add(SrbSmoke.Create(n, s, 0.45f * variant.flameLength * s.flameScale));

                var lgo = new GameObject("rocketLight");
                lgo.transform.SetParent(n, false);
                lgo.transform.localPosition = new Vector3(0f, 0f, 0.65f);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = variant.flameMid;
                l.range = 4f;
                l.intensity = 0f;
                fx.lights.Add(l);
            }

            AudioClip clip = null;
            if (!string.IsNullOrEmpty(s.rocketSound) && GameDatabase.Instance.ExistsAudioClip(s.rocketSound))
                clip = GameDatabase.Instance.GetAudioClip(s.rocketSound);
            if (clip != null)
            {
                fx.audioSrc = seatRoot.AddComponent<AudioSource>();
                fx.audioSrc.clip = clip;
                fx.audioSrc.loop = true;
                fx.audioSrc.playOnAwake = false;
                fx.audioSrc.spatialBlend = 1f;
                fx.audioSrc.dopplerLevel = 0f;
                fx.audioSrc.minDistance = 4f;
                fx.audioSrc.maxDistance = 600f;
                fx.audioSrc.rolloffMode = AudioRolloffMode.Logarithmic;
                fx.audioSrc.volume = GameSettings.SHIP_VOLUME;
            }
            return fx;
        }

        bool AttachBDA(Transform nozzle, EjectionSettings s)
        {
            try
            {
                GameObject g = GameDatabase.Instance.GetModel(s.plumePrefab);
                if (g == null) return false;
                g.SetActive(true);
                g.AddComponent<EjectionGenerated>();
                var ems = g.GetComponentsInChildren<KSPParticleEmitter>(true);
                var kept = new List<KSPParticleEmitter>();
                foreach (var e in ems)
                {
                    var er = e.GetComponent<Renderer>();
                    Material em = e.material != null ? e.material : (er != null ? er.sharedMaterial : null);
                    string tex = em != null && em.mainTexture != null ? em.mainTexture.name.ToLowerInvariant() : "";
                    Log.Dbg("BDA plume emitter '" + e.name + "' texture '" + tex + "'");
                    bool isSmoke = tex.Contains("smoke");
                    if ((s.enhancedFlames && !isSmoke) || (s.srbSmoke && isSmoke))
                    {
                        // the layered flame replaces BDArmory's flame particles, the SRB trail its thin smoke
                        e.emit = false;
                        if (er != null) er.enabled = false;
                        continue;
                    }
                    kept.Add(e);
                }
                if (kept.Count == 0) { Destroy(g); return false; }

                // Work out which way the prefab blows its exhaust and align that with the nozzle's +Z.
                Vector3 dirRoot = Vector3.zero;
                foreach (var pe in kept)
                {
                    Vector3 lv = pe.localVelocity;
                    if (lv.sqrMagnitude < 1e-6f) continue;
                    dirRoot += g.transform.InverseTransformDirection(pe.transform.TransformDirection(lv)).normalized;
                }
                g.transform.SetParent(nozzle, false);
                g.transform.localPosition = Vector3.zero;
                Quaternion align = dirRoot.sqrMagnitude > 1e-4f ? Quaternion.FromToRotation(dirRoot.normalized, Vector3.forward) : Quaternion.identity;
                g.transform.localRotation = Quaternion.Euler(s.plumeRotation) * align;
                g.transform.localScale = Vector3.one * s.plumeScale;

                foreach (var pe in kept)
                {
                    pe.minSize *= s.plumeScale; pe.maxSize *= s.plumeScale;
                    pe.localVelocity *= s.plumeScale; pe.worldVelocity *= s.plumeScale;
                    pe.emit = false;
                    if (s.enhancedFlames && pe.colorAnimation != null)
                    {
                        // neutral exhaust smoke, kept thin so the flame stays readable
                        for (int i = 0; i < pe.colorAnimation.Length; i++)
                        {
                            float grey = 0.82f - 0.12f * i / Mathf.Max(1, pe.colorAnimation.Length - 1);
                            pe.colorAnimation[i] = new Color(grey, grey, grey * 0.98f, pe.colorAnimation[i].a * 0.55f);
                        }
                    }
                    EffectBehaviour.AddParticleEmitter(pe);
                    kspEmitters.Add(pe);
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("BDArmory plume failed, using built-in plume: " + e.Message);
                return false;
            }
        }

        void AttachBuiltIn(Transform nozzle, EjectionSettings s)
        {
            // hot core: short, bright, additive, moves with the seat
            var core = NewSystem(nozzle, "flame", true, false);
            var m = core.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(14f, 20f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.28f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.75f, 1f), new Color(1f, 0.6f, 0.2f, 1f));
            m.maxParticles = 400;
            var em = core.emission; em.rateOverTime = 320f;
            var sh = core.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 4f; sh.radius = 0.02f;
            var sz = core.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            var col = core.colorOverLifetime; col.enabled = true;
            col.color = Grad(new Color(1f, 0.9f, 0.6f), 1f, new Color(1f, 0.35f, 0.05f), 0f);
            if (s.enhancedFlames) { systems.Remove(core); Destroy(core.gameObject); }   // the layered flame replaces it

            if (s.srbSmoke) return;   // SrbSmoke draws the trail

            // smoke trail: world space, grows and lingers (BDA-style)
            var smoke = NewSystem(nozzle, "smoke", false, true);
            m = smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 8f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.77f, 0.74f, 0.34f), new Color(0.62f, 0.61f, 0.58f, 0.24f));
            m.maxParticles = 500;
            em = smoke.emission; em.rateOverTime = s.enhancedFlames ? 55f : 140f;
            sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 7f; sh.radius = 0.03f;
            sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 9f));
            col = smoke.colorOverLifetime; col.enabled = true;
            var smokeFade = new Gradient();
            smokeFade.SetKeys(new[] { new GradientColorKey(new Color(0.95f, 0.88f, 0.76f), 0f), new GradientColorKey(new Color(0.78f, 0.78f, 0.78f), 0.35f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.16f), new GradientAlphaKey(0.45f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = smokeFade;
            var vel = smoke.limitVelocityOverLifetime; vel.enabled = true; vel.limit = 1.5f; vel.dampen = 0.15f;
        }

        void AttachFlame(Transform nozzle, EjectionSettings s, SeatVariant v)
        {
            float L = 1.35f * v.flameLength * s.flameScale, W = 0.31f * v.flameWidth * s.flameScale;
            AddSheet(nozzle, MatLib.FlameBody, v.flameOuter * 0.32f, L * 1.2f, W * 1.45f, 19f, false);
            AddSheet(nozzle, MatLib.FlameBody, v.flameOuter * 0.62f, L, W, 27f, false);
            AddSheet(nozzle, MatLib.FlameBody, v.flameMid * 0.62f, L * 0.68f, W * 0.62f, 34f, false);
            AddSheet(nozzle, MatLib.FlameCore, v.flameCore, L * 0.52f, W * 0.42f, 43f, false);
            AddSheet(nozzle, MatLib.SoftDot, v.flameMid * 0.42f, W * 1.25f, W * 1.25f, 31f, true);

            // hot embers thrown out of the plume
            var em = NewSystem(nozzle, "embers", true, true);
            var m = em.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(5f * L, 12f * L);
            m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.032f);
            m.startColor = new ParticleSystem.MinMaxGradient(v.flameMid, v.flameOuter);
            m.maxParticles = 120;
            var rate = em.emission; rate.rateOverTime = 70f * v.flameLength;
            var sh = em.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 15f; sh.radius = 0.025f;
            var col = em.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, 1f, v.flameOuter, 0f);
            var r = em.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.018f; r.lengthScale = 1.2f;
        }

        void AddSheet(Transform nozzle, Texture2D tex, Color tint, float length, float width, float flicker, bool glow)
        {
            var go = new GameObject(glow ? "flameGlow" : "flameSheet");
            go.transform.SetParent(nozzle, false);
            var mesh = new Mesh();
            mesh.name = "EjectionSeat_FlameSheet";
            float z0 = glow ? -0.5f : 0f, z1 = glow ? 0.5f : 1f;
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, z0), new Vector3(0.5f, 0f, z0), new Vector3(-0.5f, 0f, z1), new Vector3(0.5f, 0f, z1) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MatLib.ParticleTex(true, tex);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            sheets.Add(new FlameSheet { t = go.transform, mesh = mesh, tint = tint, length = length, width = width,
                flickerRate = flicker, seed = UnityEngine.Random.value * 100f, nozzle = nozzle, glow = glow });
        }

        void LateUpdate() { UpdateFlame(); }

        /// <summary>Points each sheet at the camera around the exhaust axis and animates its size and brightness.</summary>
        public void UpdateFlame()
        {
            if (sheets.Count == 0) return;
            Camera cam = ViewCameraOverride;
            if (cam == null && FlightCamera.fetch != null) cam = FlightCamera.fetch.mainCamera;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            float time = Time.time;
            foreach (var f in sheets)
            {
                if (f.t == null || f.nozzle == null) continue;
                bool visible = power > 0.01f;
                if (f.t.gameObject.activeSelf != visible) f.t.gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 axis = f.nozzle.forward, toCam = cam.transform.position - f.nozzle.position;
                float n1 = Mathf.PerlinNoise(f.seed, time * f.flickerRate), n2 = Mathf.PerlinNoise(f.seed + 7f, time * f.flickerRate * 0.63f);
                float alongView = Mathf.Abs(Vector3.Dot(axis, toCam.normalized));
                float brightness;
                if (f.glow)
                {
                    f.t.rotation = Quaternion.LookRotation(cam.transform.up, -cam.transform.forward);
                    float size = f.width * (0.75f + 0.5f * n1) * (0.6f + 0.6f * alongView) * Mathf.Sqrt(power);
                    f.t.localScale = new Vector3(size, 1f, size);
                    brightness = (0.35f + 0.65f * alongView) * (0.75f + 0.25f * n2);
                }
                else
                {
                    Vector3 up = Vector3.ProjectOnPlane(toCam, axis);
                    if (up.sqrMagnitude < 1e-5f) up = f.nozzle.up;
                    f.t.rotation = Quaternion.LookRotation(axis, up);
                    f.t.localScale = new Vector3(f.width * (0.86f + 0.28f * n1) * Mathf.Sqrt(power), 1f,
                                                 f.length * (0.9f + 0.18f * n2) * power);
                    brightness = 0.78f + 0.3f * n2;
                }
                Color c = f.tint * Mathf.Clamp01(brightness * Mathf.Min(1f, power));
                c.a = 1f;
                Color32 c32 = c;
                for (int i = 0; i < 4; i++) f.colors[i] = c32;
                f.mesh.colors32 = f.colors;
            }
        }

        ParticleSystem NewSystem(Transform parent, string name, bool additive, bool world)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.duration = 1f;
            m.loop = true;
            m.playOnAwake = false;
            m.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MatLib.Particle(additive);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            systems.Add(ps);
            return ps;
        }

        static Gradient Grad(Color a, float aa, Color b, float ba)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                      new[] { new GradientAlphaKey(aa, 0f), new GradientAlphaKey(aa * 0.8f, 0.3f), new GradientAlphaKey(ba, 1f) });
            return g;
        }

        public void SetThrust(bool on)
        {
            if (on == thrusting) return;
            thrusting = on;
            if (on)
            {
                thrustAge = 0f;
                foreach (var sm in smokes) if (sm != null) sm.Puff(30);   // ignition cloud
            }
            foreach (var pe in kspEmitters) if (pe != null) pe.emit = on;
            foreach (var ps in systems)
            {
                if (ps == null) continue;
                if (on) ps.Play(); else ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            if (audioSrc != null)
            {
                if (on) audioSrc.Play(); else audioSrc.Stop();
            }
        }

        void Update()
        {
            // ignition overshoot on start, quick tail-off at burnout
            thrustAge += Time.deltaTime;
            float target = thrusting ? 1f + 0.45f * Mathf.Exp(-thrustAge * 11f) : 0f;
            power = Mathf.MoveTowards(power, target, Time.deltaTime * (thrusting ? 16f : 6f));
            float flicker = power * (0.75f + UnityEngine.Random.value * 0.3f);
            foreach (var l in lights) if (l != null) l.intensity = Mathf.MoveTowards(l.intensity, flicker, Time.deltaTime * 40f);
            // smoke carries on thinning for a moment after burnout, as a solid motor tails off
            smokePower = thrusting ? 1f : Mathf.MoveTowards(smokePower, 0f, Time.deltaTime / 1.6f);
            foreach (var sm in smokes) if (sm != null) sm.power = smokePower;
        }

        void OnDestroy()
        {
            foreach (var pe in kspEmitters) if (pe != null) EffectBehaviour.RemoveParticleEmitter(pe);
            foreach (var f in sheets) if (f.mesh != null) Destroy(f.mesh);
        }
    }

    /// <summary>
    /// Thick solid-rocket smoke trail. It lives on its own object rather than under the seat, so the trail stays
    /// in the sky after the seat has gone, and it is registered with KSP's floating origin so it stays where it
    /// was laid down at high speed instead of being dragged along with the camera's frame.
    /// </summary>
    public class SrbSmoke : MonoBehaviour
    {
        public float power;
        Transform nozzle;
        Rigidbody body;
        ParticleSystem ps;
        float perSecond, perMetre, offset, life;
        bool registered, stopping;

        public static SrbSmoke Create(Transform nozzle, EjectionSettings s, float offset)
        {
            var go = new GameObject("EjectionSeat_SrbSmoke");
            go.AddComponent<EjectionGenerated>();
            var sm = go.AddComponent<SrbSmoke>();
            sm.nozzle = nozzle;
            sm.offset = offset;
            sm.body = nozzle.GetComponentInParent<Rigidbody>();
            float amount = Mathf.Max(0f, s.srbSmokeAmount);
            sm.perSecond = 70f * amount;
            sm.perMetre = 1.4f * amount;
            sm.life = Mathf.Max(1f, s.srbSmokeLifetime);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.duration = 1f;
            m.loop = true;
            m.playOnAwake = false;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(sm.life * 0.7f, sm.life * 1.15f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.55f, 0.9f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.96f, 0.95f, 0.93f, 0.9f), new Color(0.8f, 0.8f, 0.78f, 0.7f));
            m.maxParticles = 2500;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.05f;

            // billows out quickly behind the motor, then keeps spreading slowly
            var grow = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.05f, 3.6f), new Keyframe(0.35f, 7.5f), new Keyframe(1f, 11f));
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, grow);

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.8f, 0.52f), 0f), new GradientColorKey(new Color(0.96f, 0.95f, 0.93f), 0.04f), new GradientColorKey(new Color(0.84f, 0.84f, 0.83f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.02f), new GradientAlphaKey(0.8f, 0.25f), new GradientAlphaKey(0.42f, 0.6f), new GradientAlphaKey(0.12f, 0.85f), new GradientAlphaKey(0f, 1f) });
            var col = ps.colorOverLifetime; col.enabled = true; col.color = fade;

            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.6f; lim.dampen = 0.3f;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.5f; noise.frequency = 0.3f; noise.scrollSpeed = 0.15f;
            noise.damping = true; noise.octaveCount = 2; noise.quality = ParticleSystemNoiseQuality.Medium;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MatLib.ParticleTex(false, MatLib.Puff);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.maxParticleSize = 4f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            sm.ps = ps;
            sm.Follow();
            ps.Play();
            try { sm.registered = FloatingOrigin.RegisterParticleSystem(ps); } catch { }
            return sm;
        }

        public void Puff(int count)
        {
            if (ps == null || nozzle == null) return;
            Follow();
            ps.Emit(count);
        }

        void Follow()
        {
            transform.position = nozzle.position + nozzle.forward * offset;
            transform.rotation = nozzle.rotation;
        }

        void Update()
        {
            if (ps == null) return;
            var em = ps.emission;
            if (nozzle == null)
            {
                if (!stopping)
                {
                    stopping = true;
                    em.rateOverTimeMultiplier = 0f;
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    Destroy(gameObject, life * 1.2f + 1f);
                }
                return;
            }
            Follow();
            // more smoke per second the faster the motor moves through the air, so the trail stays solid
            float speed = body != null ? (body.velocity + Krakensbane.GetFrameVelocityV3f()).magnitude : 0f;
            em.rateOverTimeMultiplier = (perSecond + perMetre * speed) * power;
        }

        void OnDestroy()
        {
            if (registered && ps != null) { try { FloatingOrigin.UnregisterParticleSystem(ps); } catch { } }
        }
    }

    /// <summary>One-shot particle bursts (canopy charge, catapult gas, seat separation).</summary>
    public static class Burst
    {
        public static void Smoke(Vector3 pos, Vector3 dir, int count, float size, float speed, float life, Color color)
        {
            var go = new GameObject("EjectionSeat_Burst");
            go.AddComponent<EjectionGenerated>();
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.duration = 0.5f; m.loop = false; m.playOnAwake = false;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            m.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = color;
            m.maxParticles = count + 10;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55f; sh.radius = 0.2f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 3.5f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.5f; lim.dampen = 0.2f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = MatLib.Particle(false);
            ps.Emit(count);
            UnityEngine.Object.Destroy(go, life + 0.5f);
        }

        static ParticleSystem OneShot(Vector3 pos, Vector3 dir, bool additive, int count)
        {
            var go = new GameObject("EjectionSeat_Burst");
            go.AddComponent<EjectionGenerated>();
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.duration = 0.5f; m.loop = false; m.playOnAwake = false;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = count + 10;
            var em = ps.emission; em.rateOverTime = 0f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = MatLib.Particle(additive);
            // local gravity, so fragments and sparks arc and fall
            try
            {
                Vector3 gv = FlightGlobals.getGeeForceAtPosition(pos);
                var f = ps.forceOverLifetime; f.enabled = true; f.space = ParticleSystemSimulationSpace.World;
                f.x = gv.x; f.y = gv.y; f.z = gv.z;
            }
            catch { }
            return ps;
        }

        /// <summary>Glinting glass fragments: small, bright, spinning, falling.</summary>
        public static void Glitter(Vector3 pos, Vector3 dir, int count, float radius)
        {
            var ps = OneShot(pos, dir, true, count);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.92f, 1f, 1f), new Color(1f, 1f, 1f, 1f));
            m.gravityModifier = 0f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 70f; sh.radius = radius;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();   // twinkle then fade
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.85f, 1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.3f, 0.25f), new GradientAlphaKey(0.9f, 0.45f), new GradientAlphaKey(0.2f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 2f; lim.dampen = 0.08f;
            ps.Emit(count);
            UnityEngine.Object.Destroy(ps.gameObject, 3f);
        }

        /// <summary>Hot sparks from the det-cord / frame bolts.</summary>
        public static void Sparks(Vector3 pos, Vector3 dir, int count)
        {
            var ps = OneShot(pos, dir, true, count);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(6f, 18f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.5f, 0.1f, 1f));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1f;
            ps.Emit(count);
            UnityEngine.Object.Destroy(ps.gameObject, 1.5f);
        }

        /// <summary>Ring of smoke around the canopy frame (the det-cord line).</summary>
        public static void Ring(Vector3 pos, Vector3 dir, float radius)
        {
            var ps = OneShot(pos, dir, false, 70);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new Color(0.85f, 0.85f, 0.83f, 0.75f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.radiusThickness = 0.05f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 3f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ps.Emit(70);
            UnityEngine.Object.Destroy(ps.gameObject, 2.6f);
        }

        public static void Flash(Vector3 pos, float range, float duration)
        {
            var go = new GameObject("EjectionSeat_Flash");
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.75f, 0.45f); l.range = range; l.intensity = 6f;
            go.AddComponent<FadeLight>().duration = duration;
        }

        public static void Sound(string clipUrl, Vector3 pos, float volume)
        {
            if (string.IsNullOrEmpty(clipUrl) || !GameDatabase.Instance.ExistsAudioClip(clipUrl)) return;
            var clip = GameDatabase.Instance.GetAudioClip(clipUrl);
            var go = new GameObject("EjectionSeat_Sound");
            go.transform.position = pos;
            var a = go.AddComponent<AudioSource>();
            a.spatialBlend = 1f; a.dopplerLevel = 0f; a.minDistance = 5f; a.maxDistance = 800f;
            a.volume = volume * GameSettings.SHIP_VOLUME;
            a.PlayOneShot(clip);
            UnityEngine.Object.Destroy(go, clip.length + 0.5f);
        }
    }

    /// <summary>
    /// Canopy jettison: two small motors on the canopy frame fire for a moment (flame, glow, smoke trail),
    /// with a burst of glinting glass fragments and sparks at separation.
    /// </summary>
    public class CanopyJettisonFX : MonoBehaviour
    {
        public float burn = 1.4f;
        float t;
        readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        readonly List<Light> lights = new List<Light>();
        readonly List<SrbSmoke> smokes = new List<SrbSmoke>();
        float smokePower;

        public static void Attach(GameObject canopy, Bounds worldBounds, Vector3 dir, Vector3 vehicleFwd, float burn)
        {
            var fx = canopy.AddComponent<CanopyJettisonFX>();
            fx.burn = burn;
            fx.thrustLocal = canopy.transform.InverseTransformDirection((dir * 0.8f + vehicleFwd * -0.3f).normalized);
            Vector3 right = Vector3.Cross(dir, vehicleFwd).normalized;
            float halfW = Mathf.Max(0.15f, Vector3.Scale(worldBounds.extents, new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z))).magnitude * 0.8f);
            float halfL = Mathf.Max(0.15f, Vector3.Scale(worldBounds.extents, new Vector3(Mathf.Abs(vehicleFwd.x), Mathf.Abs(vehicleFwd.y), Mathf.Abs(vehicleFwd.z))).magnitude * 0.7f);
            // motors at the rear corners of the frame, exhausting down and aft
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 p = worldBounds.center + right * s * halfW - vehicleFwd * halfL;
                Vector3 exhaust = (-dir * 0.6f - vehicleFwd * 0.8f + right * s * 0.2f).normalized;
                var n = new GameObject("jettisonMotor").transform;
                n.position = p;
                n.rotation = Quaternion.LookRotation(exhaust);
                n.SetParent(canopy.transform, true);
                fx.AddMotor(n);
            }
            // glass fragments + sparks + det-cord ring
            Burst.Glitter(worldBounds.center, dir, 60, Mathf.Max(0.4f, worldBounds.extents.magnitude * 0.6f));
            Burst.Sparks(worldBounds.center, dir, 50);
            Burst.Ring(worldBounds.center, dir, Mathf.Max(0.4f, halfW));
        }

        void AddMotor(Transform n)
        {
            var flame = MakePS(n, "flame", true, false);
            var m = flame.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(8f, 12f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.16f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f, 1f), new Color(1f, 0.55f, 0.15f, 1f));
            var em = flame.emission; em.rateOverTime = 220f;
            var sh = flame.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.01f;

            var smoke = MakePS(n, "trail", false, true);
            m = smoke.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.9f, 0.88f, 0.7f), new Color(0.75f, 0.75f, 0.72f, 0.5f));
            em = smoke.emission; em.rateOverTime = 90f;
            sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10f; sh.radius = 0.02f;
            var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 7f));
            var col = smoke.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.6f), 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 0.2f) },
                      new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var lgo = new GameObject("glow");
            lgo.transform.SetParent(n, false);
            lgo.transform.localPosition = new Vector3(0f, 0f, 0.15f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.6f, 0.3f); l.range = 4f; l.intensity = 3f;
            lights.Add(l);
            flame.Play(); smoke.Play();
        }

        ParticleSystem MakePS(Transform parent, string name, bool additive, bool world)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.duration = 1f; m.loop = true; m.playOnAwake = false;
            m.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            m.maxParticles = 400;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = MatLib.Particle(additive);
            systems.Add(ps);
            return ps;
        }

        public Vector3 thrustLocal = Vector3.up;
        public float thrustAccel = 25f;   // m/s^2 while the jettison motors burn
        Rigidbody body;

        void FixedUpdate()
        {
            if (t > burn) return;
            if (body == null) body = GetComponent<Rigidbody>();
            if (body != null) body.AddForce(transform.TransformDirection(thrustLocal) * thrustAccel, ForceMode.Acceleration);
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(1f - t / burn);
            foreach (var l in lights) if (l != null) l.intensity = k * (2.5f + UnityEngine.Random.value);
            if (t > burn)
            {
                foreach (var ps in systems) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                foreach (var l in lights) if (l != null) Destroy(l.gameObject);
                lights.Clear();
                enabled = false;
            }
        }
    }

    public class FadeLight : MonoBehaviour
    {
        public float duration = 0.3f;
        float t; Light l; float i0;
        void Start() { l = GetComponent<Light>(); i0 = l.intensity; }
        void Update()
        {
            t += Time.deltaTime;
            if (l != null) l.intensity = Mathf.Lerp(i0, 0f, t / duration);
            if (t > duration) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Free-flying debris (canopies, hull panels, spent seats). Registered as a KSP physicalObject so
    /// floating-origin / Krakensbane handle it; gravity and aero drag are applied here.
    /// </summary>
    public class EjectionDebris : MonoBehaviour
    {
        public float lifetime = 45f;
        public float cdA = 0.3f;
        Rigidbody rb;
        float age;

        public static Rigidbody MakePhysical(GameObject go, Part reference, float mass, float lifetime, float cdA)
        {
            Rigidbody body = null;
            if (reference != null)
            {
                try
                {
                    var po = physicalObject.ConvertToPhysicalObject(reference, go);
                    po.maxDistance = 3000f;
                    body = po.rb;
                }
                catch (Exception e) { Log.Warn("physicalObject conversion failed: " + e.Message); }
            }
            if (body == null) body = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.useGravity = false;
            body.drag = 0f;
            body.angularDrag = 0.3f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var d = go.AddComponent<EjectionDebris>();
            d.lifetime = lifetime;
            d.cdA = cdA;
            d.rb = body;
            return body;
        }

        void FixedUpdate()
        {
            if (rb == null || !FlightGlobals.ready) return;
            rb.drag = 0f;   // our own aero model below; don't let anything else add Unity drag
            Vector3 pos = rb.worldCenterOfMass;
            CelestialBody body = FlightGlobals.getMainBody(pos);
            if (body == null) return;
            rb.AddForce(FlightGlobals.getGeeForceAtPosition(pos, body), ForceMode.Acceleration);

            double alt = body.GetAltitude(pos);
            if (body.atmosphere && alt < body.atmosphereDepth)
            {
                double p = body.GetPressure(alt);
                double tK = body.GetTemperature(alt);
                double rho = body.GetDensity(p, tK);
                Vector3 vSrf = rb.velocity + (Vector3)Krakensbane.GetFrameVelocity() - (Vector3)body.getRFrmVel(pos);
                float sp = vSrf.magnitude;
                if (sp > 0.1f)
                {
                    float fKN = (float)(0.5 * rho * sp * sp * cdA / 1000.0);
                    fKN = Mathf.Min(fKN, rb.mass * 9.81f * 25f);
                    rb.AddForce(-vSrf / sp * fKN, ForceMode.Force);
                }
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > lifetime) Destroy(gameObject);
        }
    }
}
