using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EjectionSeats
{
    /// <summary>
    /// Added (via ModuleManager) to every crewed command part. Dormant until the player right-clicks the
    /// pod in the editor and installs ejection seats; then it exposes the seat options and an "Eject Crew"
    /// action (Abort group by default).
    /// </summary>
    public class ModuleEjectionSeat : PartModule, IPartMassModifier, IPartCostModifier
    {
        const string GROUP = "ejectionSeats";
        const string GROUP_TITLE = "Ejection Seats";

        [KSPField(isPersistant = true)] public bool installed = false;
        [KSPField(isPersistant = true)] public bool expended = false;
        [KSPField(isPersistant = true)] public bool canopyGone = false;

        [KSPField(isPersistant = true, guiName = "Era", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_ChooseOption(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public string seatEra = "modern";

        [KSPField(isPersistant = true, guiName = "Seat", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_ChooseOption(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public string seatVariant = "modern";

        [KSPField(isPersistant = true, guiName = "Ejection force", guiUnits = " g", guiFormat = "F0", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_FloatRange(minValue = 5f, maxValue = 50f, stepIncrement = 1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public float ejectionG = 14f;

        [KSPField(isPersistant = true, guiName = "Rocket burn", guiUnits = " s", guiFormat = "F2", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_FloatRange(minValue = 0.2f, maxValue = 1.5f, stepIncrement = 0.05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public float burnTime = 0.5f;

        [KSPField(isPersistant = true, guiName = "Pitch angle", guiUnits = "°", guiFormat = "F1", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public float ejectPitch = 0f;

        [KSPField(isPersistant = true, guiName = "Yaw angle", guiUnits = "°", guiFormat = "F1", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_FloatRange(minValue = -10f, maxValue = 10f, stepIncrement = 0.5f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public float ejectYaw = 0f;

        [KSPField(isPersistant = true, guiName = "Chute delay", guiUnits = " s", guiFormat = "F1", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_FloatRange(minValue = 1f, maxValue = 2f, stepIncrement = 0.1f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public float chuteDelay = 1.5f;

        [KSPField(isPersistant = true, guiName = "Parachute", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_ChooseOption(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor,
            options = new[] { "auto", "seat", "eva" }, display = new[] { "Auto", "Seat chute", "Kerbal's EVA chute" })]
        public string chuteType = "auto";

        [KSPField(isPersistant = true, guiName = "Chute open time", guiUnits = " s", guiFormat = "F1", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_FloatRange(minValue = 1f, maxValue = 10f, stepIncrement = 0.25f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public float chuteOpenTime = 5f;

        [KSPField(isPersistant = true, guiName = "Blow canopy first", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_Toggle(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public bool blowCanopy = true;

        [KSPField(isPersistant = true, guiName = "Show interior after", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        [UI_Toggle(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.Editor)]
        public bool showInterior = true;

        [KSPField(guiName = "Ejection", guiActive = true, guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        public string status = "";

        static readonly string[] tweakFields = { "seatEra", "seatVariant", "ejectionG", "burnTime", "ejectPitch", "ejectYaw", "chuteDelay", "chuteType", "chuteOpenTime", "blowCanopy", "showInterior" };

        bool ejecting;
        bool analysisDirty = true;
        float nextAnalysis;
        bool previewOn;
        GameObject preview;

        // cabin damage state (flight)
        List<HullCutter.Column> localCols;          // escape columns in part space
        GameObject interiorCopy;
        InternalModel cutIM;
        readonly Dictionary<Kerbal, KerbalMirror> mirrors = new Dictionary<Kerbal, KerbalMirror>();
        float nextCabinPoll;
        readonly HashSet<string> helmetTried = new HashSet<string>();   // one re-seat attempt per kerbal per IVA
        bool cabinOpen;
        bool portraitResetPending;
        [NonSerialized] public bool ejectionInProgress;
        float imStableSince;
        string lastCabinState = "";

        // ------------------------------------------------------------------ lifecycle
        public override void OnStart(StartState state)
        {
            // a part cloned in the editor carries copies of our runtime objects; drop them
            foreach (var g in part.GetComponentsInChildren<EjectionGenerated>(true))
                if (g != null && g.gameObject != part.gameObject) Destroy(g.gameObject);

            seatVariant = SeatVariant.Get(seatVariant).id;   // craft saved with a retired seat model keep working
            seatEra = SeatVariant.Get(seatVariant).era;
            var eraOpt = Fields["seatEra"].uiControlEditor as UI_ChooseOption;
            if (eraOpt != null) { eraOpt.options = SeatVariant.EraIds; eraOpt.display = SeatVariant.EraTitles; }
            ApplySeatOptions();
            foreach (var f in tweakFields)
                Fields[f].OnValueModified += OnTweaked;

            if (HighLogic.LoadedSceneIsEditor) GameEvents.onEditorShipModified.Add(OnShipModified);
            if (HighLogic.LoadedSceneIsFlight && installed && canopyGone) StartCoroutine(RestoreDamage());
            RefreshUI();
        }

        void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnShipModified);
            ClearPreview();
        }

        string optionsEra;

        /// <summary>Limits the Seat list to the chosen era.</summary>
        void ApplySeatOptions()
        {
            var opt = Fields["seatVariant"].uiControlEditor as UI_ChooseOption;
            var seats = SeatVariant.ForEra(seatEra);
            if (seats.Count == 0) { seatEra = SeatVariant.EraIds[0]; seats = SeatVariant.ForEra(seatEra); }
            if (SeatVariant.Get(seatVariant).era != seatEra) seatVariant = seats[0].id;
            if (opt != null)
            {
                var ids = new List<string>(); var titles = new List<string>();
                foreach (var v in seats) { ids.Add(v.id); titles.Add(v.title); }
                opt.options = ids.ToArray();
                opt.display = titles.ToArray();
            }
            optionsEra = seatEra;
        }

        void OnShipModified(ShipConstruct s) { analysisDirty = true; }
        void OnTweaked(object o)
        {
            analysisDirty = true;
            if (previewOn) BuildPreview();
        }

        public override string GetInfo()
        {
            return "Ejection seats can be installed from this part's right-click menu in the editor.";
        }

        public override string GetModuleDisplayName() { return "Ejection Seats (optional)"; }

        void Update()
        {
            if (!installed) return;
            if (HighLogic.LoadedSceneIsEditor && optionsEra != seatEra)
            {
                ApplySeatOptions();
                analysisDirty = true;
                if (previewOn) BuildPreview();
                MonoUtilities.RefreshPartContextWindow(part);
            }
            if (HighLogic.LoadedSceneIsEditor && analysisDirty && Time.realtimeSinceStartup > nextAnalysis)
            {
                analysisDirty = false;
                nextAnalysis = Time.realtimeSinceStartup + 0.5f;
                try
                {
                    var plan = CockpitAnalyzer.Analyze(part, ejectPitch, ejectYaw, EjectionSettings.Get());
                    status = plan.Summary();
                }
                catch (Exception e) { status = "analysis failed"; Log.Warn(e.ToString()); }
            }
            // The interior copy and mirrored kerbals exist for the outside view only. From inside the craft the
            // real IVA is what should be seen; a mirrored head would otherwise show above the real seat
            // (KSP hides the viewing kerbal's head, and the mirror deliberately ignores IVA hide states).
            if (HighLogic.LoadedSceneIsFlight && interiorCopy != null)
            {
                var cam = CameraManager.Instance;
                bool insideView = cam != null && vessel == FlightGlobals.ActiveVessel &&
                    (cam.currentCameraMode == CameraManager.CameraMode.IVA || cam.currentCameraMode == CameraManager.CameraMode.Internal);
                if (interiorCopy.activeSelf == insideView) interiorCopy.SetActive(!insideView);
            }
            // a new IVA (e.g. someone just boarded) is handled on the very next frame
            if (HighLogic.LoadedSceneIsFlight && cabinOpen && part.internalModel != null && part.internalModel != cutIM)
                nextCabinPoll = 0f;
            if (HighLogic.LoadedSceneIsFlight && cabinOpen && Time.realtimeSinceStartup > nextCabinPoll)
            {
                // check often right after the IVA changes (boarding), lazily otherwise
                bool recent = Time.realtimeSinceStartup - imStableSince < 3f;
                nextCabinPoll = Time.realtimeSinceStartup + (recent ? 0.05f : 0.5f);
                try { RefreshCabin(false); } catch (Exception e) { Log.Warn("cabin refresh: " + e.Message); }
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (expended) status = "Expended";
                else if (part.protoModuleCrew.Count == 0) status = "Armed (no crew)";
                else status = "ARMED - " + part.protoModuleCrew.Count + " seat(s)";
            }
        }

        // ------------------------------------------------------------------ UI
        public void RefreshUI()
        {
            foreach (var f in tweakFields) { Fields[f].guiActiveEditor = installed; Fields[f].guiActive = false; }
            Fields["status"].guiActiveEditor = installed;
            Fields["status"].guiActive = installed;
            Events["InstallSeats"].guiActiveEditor = !installed;
            Events["RemoveSeats"].guiActiveEditor = installed;
            Events["TogglePreview"].guiActiveEditor = installed;
            Events["TogglePreview"].guiName = previewOn ? "Hide ejection preview" : "Show ejection preview";
            Events["EjectEvent"].guiActive = installed && !expended;
            Events["EjectEvent"].guiActiveUncommand = installed && !expended;
            BaseAction a = Actions["ActionEject"];
            a.active = installed;
            a.activeEditor = installed;
            MonoUtilities.RefreshPartContextWindow(part);
        }

        void SetInstalled(bool v)
        {
            installed = v;
            analysisDirty = true;
            if (!v) { previewOn = false; ClearPreview(); }
            RefreshUI();
        }

        [KSPEvent(guiName = "Install ejection seats", guiActiveEditor = true, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        public void InstallSeats()
        {
            SetInstalled(true);
            foreach (Part p in part.symmetryCounterparts)
            {
                var m = p.FindModuleImplementing<ModuleEjectionSeat>();
                if (m != null) m.SetInstalled(true);
            }
            if (EditorLogic.fetch != null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        [KSPEvent(guiName = "Remove ejection seats", guiActiveEditor = false, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        public void RemoveSeats()
        {
            SetInstalled(false);
            foreach (Part p in part.symmetryCounterparts)
            {
                var m = p.FindModuleImplementing<ModuleEjectionSeat>();
                if (m != null) m.SetInstalled(false);
            }
            if (EditorLogic.fetch != null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        [KSPEvent(guiName = "Show ejection preview", guiActiveEditor = false, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        public void TogglePreview()
        {
            previewOn = !previewOn;
            if (previewOn) BuildPreview(); else ClearPreview();
            RefreshUI();
        }

        [KSPEvent(guiName = "EJECT", guiActive = false, groupName = GROUP, groupDisplayName = GROUP_TITLE)]
        public void EjectEvent() { Eject(); }

        [KSPAction(guiName = "Eject crew", actionGroup = KSPActionGroup.Abort)]
        public void ActionEject(KSPActionParam p)
        {
            if (p.type == KSPActionType.Activate || p.type == KSPActionType.Toggle) Eject();
        }

        // ------------------------------------------------------------------ ejection
        public void Eject()
        {
            if (!HighLogic.LoadedSceneIsFlight || !installed || expended || ejecting) return;
            if (part.protoModuleCrew.Count == 0) { ScreenMessages.PostScreenMessage("No crew in " + part.partInfo.title + " to eject.", 3f); return; }
            if (vessel.packed || (TimeWarp.CurrentRate > 1f && TimeWarp.WarpMode == TimeWarp.Modes.HIGH))
            {
                TimeWarp.SetRate(0, true);
                ScreenMessages.PostScreenMessage("Ejection needs physics - dropping out of warp, try again.", 3f);
                return;
            }
            ejecting = true;
            ejectionInProgress = true;
            ScreenMessages.PostScreenMessage("<color=#ffaa00>EJECT EJECT EJECT</color>", 2f, ScreenMessageStyle.UPPER_CENTER);
            EjectionDirector.Run(this);
        }

        /// <summary>After a reload, re-create the missing canopy / hole and the visible interior (no debris).</summary>
        IEnumerator RestoreDamage()
        {
            for (int i = 0; i < 10; i++) yield return null;
            var S = EjectionSettings.Get();
            try
            {
                var plan = CockpitAnalyzer.Analyze(part, ejectPitch, ejectYaw, S);
                if (blowCanopy) EjectionDirector.CutHull(part, plan, S, null);
                ApplyCabinDamage(plan);
            }
            catch (Exception e) { Log.Warn("Could not restore canopy state: " + e.Message); }
        }

        /// <summary>
        /// Called once the canopy is gone: remembers the escape columns (in part space, since the craft keeps
        /// moving), cuts the real IVA, builds the visible interior and starts tracking boarded kerbals.
        /// </summary>
        public void ApplyCabinDamage(EjectionPlan plan)
        {
            if (blowCanopy && plan != null)
            {
                canopyGone = true;
                localCols = new List<HullCutter.Column>();
                localOpening = plan.openingLocal.Count > 0 ? new List<Vector3>(plan.openingLocal) : null;
                foreach (var c in plan.columns)
                {
                    var l = c;
                    l.origin = part.transform.InverseTransformPoint(c.origin);
                    l.dir = part.transform.InverseTransformDirection(c.dir);
                    localCols.Add(l);
                }
            }
            cabinOpen = blowCanopy || showInterior;
            RefreshCabin(true);
        }

        List<Vector3> localOpening;   // hull points the glass canopy took with it (part space)

        PointGrid WorldOpening()
        {
            if (localOpening == null || localOpening.Count == 0) return null;
            var w = new List<Vector3>(localOpening.Count);
            foreach (var p in localOpening) w.Add(part.transform.TransformPoint(p));
            return new PointGrid(w, 0.1f);
        }

        List<HullCutter.Column> WorldCols()
        {
            if (localCols == null) return null;
            var w = new List<HullCutter.Column>();
            foreach (var c in localCols)
            {
                var x = c;
                x.origin = part.transform.TransformPoint(c.origin);
                x.dir = part.transform.TransformDirection(c.dir).normalized;
                w.Add(x);
            }
            return w;
        }

        /// <summary>
        /// Keeps the open cabin consistent: KSP respawns the IVA when crew board/leave, so re-cut it and
        /// rebuild the world interior when that happens; mirror any seated kerbal and put their helmet on.
        /// </summary>
        void RefreshCabin(bool force)
        {
            var S = EjectionSettings.Get();
            // (an empty cockpit may have its IVA despawned; keep the existing copy rather than rebuilding)
            bool imChanged = part.internalModel != null && part.internalModel != cutIM;
            if (force || imChanged || (showInterior && interiorCopy == null && part.internalModel != null))
            {
                if (showInterior)
                {
                    if (interiorCopy != null) Destroy(interiorCopy);
                    foreach (var kv in mirrors) if (kv.Value != null) Destroy(kv.Value.gameObject);
                    mirrors.Clear();
                    interiorCopy = InteriorReveal.Build(part, WorldCols(), S, WorldOpening());
                }
                cutIM = part.internalModel;
                imStableSince = Time.realtimeSinceStartup;
                helmetTried.Clear();
                if (cutIM != null && localCols != null) InteriorReveal.CutIVA(part, WorldCols(), S, WorldOpening());
            }

            if (portraitResetPending && !force)
            {
                portraitResetPending = false;
                try
                {
                    var gallery = KSP.UI.Screens.Flight.KerbalPortraitGallery.Instance;
                    if (gallery != null && vessel != null && vessel == FlightGlobals.ActiveVessel) gallery.StartReset(vessel);
                }
                catch (Exception e) { Log.Dbg("portrait reset failed: " + e.Message); }
            }

            InternalModel im = part.internalModel;
            if (im == null || im.seats == null) return;

            // no canopy, no pressure: helmets on. Seats are flagged so any kerbal KSP spawns from now on keeps
            // its helmet. A kerbal already spawned without one (KSP deletes the helmet object) gets respawned -
            // but only once the IVA has been stable for a moment, so we never interfere with KSP's own
            // boarding/crew setup (doing that mid-boarding left the seat empty and the portrait blank).
            bool needRespawn = false;
            foreach (var seat in im.seats)
            {
                if (seat == null || localCols == null) continue;
                seat.allowCrewHelmet = true;
                Kerbal k = seat.kerbalRef;
                if (k == null || k.protoCrewMember == null) continue;
                k.protoCrewMember.hasHelmetOn = true;
                k.protoCrewMember.hasNeckRingOn = true;
                if ((k.helmetTransform == null || !k.showHelmet) && !helmetTried.Contains(k.protoCrewMember.name)) needRespawn = true;
            }
            // never while the crew is being ejected (they're leaving anyway, and swapping IVA objects mid-ejection
            // is exactly when other IVA-rendering mods are busiest); can be switched off entirely in Settings.cfg
            if (needRespawn && S.ivaHelmetRespawn && !ejectionInProgress && Time.realtimeSinceStartup - imStableSince > 0.25f)
            {
                var gallery = KSP.UI.Screens.Flight.KerbalPortraitGallery.Instance;
                foreach (var seat in im.seats)
                {
                    if (seat == null) continue;
                    Kerbal old = seat.kerbalRef;
                    if (old == null || old.protoCrewMember == null) continue;
                    if (old.helmetTransform != null && old.showHelmet) continue;
                    ProtoCrewMember pcm = seat.crew ?? old.protoCrewMember;
                    helmetTried.Add(pcm.name);
                    try
                    {
                        // InternalSeat.SpawnCrew decides the helmet from allowCrewHelmet. DespawnCrew only
                        // *schedules* destruction, and SpawnCrew then reuses that same doomed kerbal - which left
                        // the seat empty. So remove the old one immediately and clear every reference first.
                        if (gallery != null) gallery.UnregisterActiveCrew(old);
                        seat.allowCrewHelmet = true;
                        seat.kerbalRef = null;
                        if (pcm.KerbalRef == old) pcm.KerbalRef = null;
                        // Never DestroyImmediate KSP's kerbal: other mods (e.g. RealWindows) draw IVA renderers
                        // through command buffers, and drawing an immediately-destroyed renderer is a native
                        // crash. Hide it now, let Unity destroy it at the end of the frame. (Clearing the two
                        // references above already stops SpawnCrew from reusing it.)
                        old.gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(old.gameObject);
                        seat.SpawnCrew();
                        portraitResetPending = true;   // new Kerbal.Start registers its portrait; then refresh the gallery
                        Log.Dbg("respawned " + pcm.name + " with helmet: " + (seat.kerbalRef != null ? "ok" : "FAILED"));
                    }
                    catch (Exception e) { Log.Dbg("helmet respawn failed: " + e.Message); }
                }
            }

            if (S.debug)
            {
                string st = "";
                foreach (var seat in im.seats)
                {
                    if (seat == null) continue;
                    Kerbal k = seat.kerbalRef;
                    KerbalMirror mm = null;
                    if (k != null) mirrors.TryGetValue(k, out mm);
                    st += "[" + (k == null ? "empty" : k.name + (k.helmetTransform != null ? " helmet" : " no-helmet") +
                          (mm == null ? " no-mirror" : (mm.Valid ? " mirror-ok" : " mirror-stale"))) + "]";
                }
                if (st != lastCabinState) { lastCabinState = st; Log.Info("cabin: " + st); }
            }

            foreach (var seat in im.seats)
            {
                if (seat == null) continue;
                Kerbal k = seat.kerbalRef;
                if (k != null && showInterior && interiorCopy != null)
                {
                    // KSP builds an IVA kerbal's model a frame or two after the Kerbal object exists, so a
                    // mirror made too early is empty - rebuild until it actually has meshes
                    KerbalMirror existing;
                    if (mirrors.TryGetValue(k, out existing) && existing != null && existing.Valid) continue;
                    if (existing != null) Destroy(existing.gameObject);
                    var km = KerbalMirror.Create(k, interiorCopy.transform, im, S);
                    mirrors[k] = km;
                    Log.Dbg("mirrored IVA kerbal " + k.name + ": " + km.RendererCount + " renderers");
                }
            }
            var dead = new List<Kerbal>();
            foreach (var kv in mirrors) if (kv.Key == null || kv.Value == null) dead.Add(kv.Key);
            foreach (var k in dead) mirrors.Remove(k);
        }

        // ------------------------------------------------------------------ editor preview
        void ClearPreview()
        {
            if (preview != null) Destroy(preview);
            preview = null;
        }

        void BuildPreview()
        {
            ClearPreview();
            if (!HighLogic.LoadedSceneIsEditor) return;
            var S = EjectionSettings.Get();
            EjectionPlan plan;
            try { plan = CockpitAnalyzer.Analyze(part, ejectPitch, ejectYaw, S); }
            catch (Exception e) { Log.Warn(e.ToString()); return; }
            status = plan.Summary();

            preview = new GameObject("EjectionSeat_Preview");
            preview.AddComponent<EjectionGenerated>();
            preview.transform.SetParent(part.transform, false);
            var variant = SeatVariant.Get(seatVariant);

            foreach (var seat in plan.seats)
            {
                bool clear = seat.obstructions.Count == 0;
                float len = Mathf.Max(3f, seat.exitDistance + 2.5f);

                var lgo = new GameObject("path");
                lgo.transform.SetParent(preview.transform, false);
                var lr = lgo.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = 2;
                lr.SetPosition(0, lgo.transform.InverseTransformPoint(seat.pos));
                lr.SetPosition(1, lgo.transform.InverseTransformPoint(seat.pos + seat.ejectDir * len));
                lr.startWidth = 0.06f; lr.endWidth = 0.015f;
                lr.sharedMaterial = MatLib.Emissive(clear ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.2f, 0.15f));

                // ghost of the chosen seat at the end of the path, plus one in the cockpit
                var ghost = SeatModel.Build(variant, S.seatScale).root;
                ghost.transform.position = seat.pos + seat.ejectDir * len + seat.Rotation * (S.kerbalSeatOffset);
                ghost.transform.rotation = seat.Rotation * Quaternion.Euler(S.kerbalSeatRotation);
                ghost.transform.SetParent(preview.transform, true);

                var inSeat = SeatModel.Build(variant, S.seatScale).root;
                inSeat.transform.position = seat.pos + seat.Rotation * S.kerbalSeatOffset;
                inSeat.transform.rotation = seat.Rotation * Quaternion.Euler(S.kerbalSeatRotation);
                inSeat.transform.SetParent(preview.transform, true);
            }
            // highlight the meshes we think are the canopy
            foreach (var mf in plan.canopy)
            {
                var o = new GameObject("canopyMarker");
                o.transform.SetParent(preview.transform, false);
                var lr = o.AddComponent<LineRenderer>();
                Bounds b = mf.GetComponent<Renderer>().bounds;
                lr.useWorldSpace = false;
                lr.loop = true;
                Vector3 e = b.extents;
                var pts = new[] {
                    b.center + new Vector3(-e.x, e.y, -e.z), b.center + new Vector3(e.x, e.y, -e.z),
                    b.center + new Vector3(e.x, e.y, e.z), b.center + new Vector3(-e.x, e.y, e.z) };
                lr.positionCount = pts.Length;
                for (int i = 0; i < pts.Length; i++) lr.SetPosition(i, o.transform.InverseTransformPoint(pts[i]));
                lr.startWidth = lr.endWidth = 0.025f;
                lr.sharedMaterial = MatLib.Emissive(new Color(0.3f, 0.7f, 1f));
            }
        }

        // ------------------------------------------------------------------ mass / cost
        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
        {
            return installed ? EjectionSettings.Get().seatMassPerCrew * Mathf.Max(1, part.CrewCapacity) : 0f;
        }
        public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.FIXED; }

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit)
        {
            return installed ? EjectionSettings.Get().seatCostPerCrew * Mathf.Max(1, part.CrewCapacity) : 0f;
        }
        public ModifierChangeWhen GetModuleCostChangeWhen() { return ModifierChangeWhen.FIXED; }
    }
}
