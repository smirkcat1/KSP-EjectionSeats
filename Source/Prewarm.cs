using System;
using System.Collections;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Builds everything an ejection needs that is the same every time (seat meshes and materials, procedural
    /// textures, baked parachute canopies) once, while the main menu is up, spread over a few frames. Before this,
    /// all of it was made on the spot in the first ejection of a session and showed up as a long hitch.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class EjectionPrewarm : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            yield return null;
            sw = new System.Diagnostics.Stopwatch();   // counts only our own work, not the frames waited in between
            var S = EjectionSettings.Get();
            bool restock = SeatTheme.UseReStock(S);

            Step(() =>
            {
                // procedural textures
                var t = MatLib.Hull(restock); t = MatLib.HullNormal(restock); t = MatLib.Hull(false); t = MatLib.HullNormal(false);
                t = MatLib.Fabric; t = MatLib.FabricNormal; t = MatLib.Stripes; t = MatLib.EjectDecal; t = MatLib.Canvas;
                t = MatLib.FlameCore; t = MatLib.FlameBody; t = MatLib.SoftDot; t = MatLib.Puff; t = MatLib.White;
                var m = MatLib.ParticleTex(true, MatLib.FlameBody); m = MatLib.ParticleTex(true, MatLib.FlameCore);
                m = MatLib.ParticleTex(true, MatLib.SoftDot); m = MatLib.ParticleTex(true, MatLib.Puff); m = MatLib.ParticleTex(false, MatLib.Puff);
                m = MatLib.Particle(true); m = MatLib.Particle(false);
            });
            yield return null;
            // one seat per frame: geometry and its materials
            foreach (var v in SeatVariant.All)
            {
                Step(() => { var r = SeatModel.Build(v, S.seatScale, restock); Destroy(r.root); });
                yield return null;
            }
            Step(() => ParachuteModel.Prewarm(S));
            Log.Info("prewarmed seats, textures and parachutes: " + sw.ElapsedMilliseconds + " ms of work, spread over " + steps + " main-menu frames");
            Destroy(gameObject);
        }

        static System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        static int steps;

        static void Step(Action a)
        {
            sw.Start();
            steps++;
            try { a(); }
            catch (Exception e) { Log.Warn("prewarm step failed (it will be built when first needed): " + e.Message); }
            finally { sw.Stop(); }
        }
    }
}
