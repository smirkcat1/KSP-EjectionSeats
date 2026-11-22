using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Development tool. Does nothing unless GameData/EjectionSeats/PluginData/render.flag exists; that file holds
    /// an output folder. At the main menu it photographs every seat in both looks with the rocket lit, plus stock
    /// reference parts for comparison, then closes the game.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class SeatDevRender : MonoBehaviour
    {
        const int Layer = 31;

        void Start()
        {
            string flag = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/EjectionSeats/PluginData/render.flag");
            if (File.Exists(flag)) StartCoroutine(Run(File.ReadAllText(flag).Trim()));
        }

        static void SetLayer(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        }

        IEnumerator Run(string outDir)
        {
            Directory.CreateDirectory(outDir);
            AudioListener.volume = 0f;
            yield return new WaitForSecondsRealtime(1f);
            var S = EjectionSettings.Get();
            Vector3 stage = new Vector3(0f, 8000f, 0f);

            var sun = new GameObject("devSun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.cullingMask = 1 << Layer; sun.intensity = 1.05f; sun.color = new Color(1f, 0.98f, 0.94f);
            sun.transform.rotation = Quaternion.Euler(42f, 145f, 0f);

            var fill = new GameObject("devFill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.cullingMask = 1 << Layer; fill.intensity = 0.42f; fill.color = new Color(0.82f, 0.88f, 1f);
            fill.transform.rotation = Quaternion.Euler(18f, 325f, 0f);

            var cam = new GameObject("devCam").AddComponent<Camera>();
            cam.enabled = false; cam.cullingMask = 1 << Layer; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.43f, 0.50f); cam.fieldOfView = 34f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 30f;
            var rt = new RenderTexture(1500, 760, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            SeatRocketFX.ViewCameraOverride = cam;

            foreach (var v in SeatVariant.All)
                foreach (bool rs in new[] { false, true })
                {
                    var m = SeatModel.Build(v, S.seatScale, rs);
                    m.root.transform.position = stage;
                    var fx = SeatRocketFX.Create(m.root, m.nozzles, S, v);
                    SetLayer(m.root);
                    fx.SetThrust(true);
                    yield return new WaitForSecondsRealtime(0.8f);
                    SetLayer(m.root);
                    Shoot(cam, rt, fx, stage + new Vector3(0f, -0.18f, 0f), 3.7f, Path.Combine(outDir, v.id + (rs ? "_restock.png" : "_stock.png")));
                    Shoot(cam, rt, fx, stage + new Vector3(0f, 0.16f, 0f), 2.05f, Path.Combine(outDir, v.id + (rs ? "_restock_close.png" : "_stock_close.png")));
                    DestroyImmediate(m.root);
                }

            foreach (var name in new[] { "mk1pod.v2", "Mark1Cockpit", "mk1pod" })
            {
                var ap = PartLoader.getPartInfoByName(name);
                if (ap == null || ap.partPrefab == null) continue;
                Transform model = ap.partPrefab.transform.Find("model");
                if (model == null) continue;
                var holder = new GameObject("devHolder"); holder.SetActive(false);
                var copy = Instantiate(model.gameObject, holder.transform);
                ParachuteModel.StripForBake(copy);
                copy.transform.SetParent(null, false);
                DestroyImmediate(holder);
                copy.transform.position = stage; copy.transform.rotation = Quaternion.identity; copy.SetActive(true);
                SetLayer(copy);
                yield return null;
                Shoot(cam, rt, null, stage + new Vector3(0f, 0.3f, 0f), 5f, Path.Combine(outDir, "reference_" + name + ".png"));
                DestroyImmediate(copy);
            }
            File.WriteAllText(Path.Combine(outDir, "done.txt"), "ok");
            SeatRocketFX.ViewCameraOverride = null;
            Application.Quit();
        }

        static void Shoot(Camera cam, RenderTexture rt, SeatRocketFX fx, Vector3 target, float dist, string file)
        {
            var dirs = new[] { new Vector3(0.62f, 0.33f, 0.72f), new Vector3(-0.66f, 0.18f, -0.72f) };
            for (int i = 0; i < 2; i++)
            {
                cam.rect = new Rect(i * 0.5f, 0f, 0.5f, 1f);
                cam.transform.position = target + dirs[i].normalized * dist;
                cam.transform.LookAt(target);
                if (fx != null) fx.UpdateFlame();
                cam.Render();
            }
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
