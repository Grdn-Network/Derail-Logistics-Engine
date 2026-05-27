using DV.Common;
using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using GRDNInterchange.Jobs;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace GRDNInterchange
{
    public static class Main
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }
        public static Settings Settings { get; private set; }
        public static InterchangeConfig Config { get; private set; }

        private static Harmony _harmony;

        // ── DVMP API reflection handles ────────────────────────────────────────────
        private static PropertyInfo _isHostProp;
        private static PropertyInfo _isSinglePlayerProp;
        private static object       _mpApiInstance;
        private static bool         _dvmpResolved;

        // ── UMM entry point ────────────────────────────────────────────────────────

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;

            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
            Config   = InterchangeConfig.Load(modEntry.Path);

            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnGUI     = OnGUI;

            _harmony = new Harmony(modEntry.Info.Id);
            _harmony.PatchAll();

            // Hook world-ready event so HubRegistry and CarDestinationStore can init
            // after all stations are loaded.
            WorldStreamingInit.LoadingFinished += OnWorldLoaded;

            // Resolve DVMP API at load time (LoadAfter: ["Multiplayer"] guarantees
            // DVMultiplayerAPI.dll is already in the AppDomain when we run).
            ResolveDvmpApi();

            Log("[Main] GRDN Interchange loaded.");
            return true;
        }

        // ── World ready ────────────────────────────────────────────────────────────

        private static void OnWorldLoaded()
        {
            Log("[Main] World loaded — initialising interchange systems.");

            HubRegistry.Initialize(Config, Settings);

            // Load car store from save data (instance field on SaveGameManager singleton)
            try
            {
                var saveData = SaveGameManager.Instance?.data;
                if (saveData != null)
                    CarDestinationStore.Instance.LoadFromSave(saveData);
            }
            catch (Exception ex)
            {
                Log($"[Main] Could not load CarDestinationStore from save: {ex.Message}");
            }

            // Subscribe to save event so we persist before the game writes to disk
            SaveGameManager.AboutToSave += OnAboutToSave;

            if (IsHostOrSingleplayer())
            {
                // Intercept vanilla FH jobs that were loaded from save before HubRegistry
                // was ready — the Harmony patch saw !registry.IsReady and bailed on them.
                InterceptSavedVanillaJobs();

                // Scan for any cars with cargo but no job at all (e.g. mod installed mid-save)
                ScanAndRecoverOrphanedFeeders();
            }
        }

        private static void OnAboutToSave(SaveType saveType)
        {
            try
            {
                var data = SaveGameManager.Instance?.data;
                if (data != null)
                {
                    CarDestinationStore.Instance.SaveTo(data);
                    Log("[Main] CarDestinationStore saved.");
                }
            }
            catch (Exception ex)
            {
                Log($"[Main] Error saving CarDestinationStore: {ex.Message}");
            }
        }

        /// <summary>
        /// Retroactively intercept vanilla FH jobs that were loaded from a save file.
        /// DV calls FinalizeSetupAndGenerateFirstJob during world streaming, before
        /// OnWorldLoaded fires, so HubRegistry isn't ready and the Harmony patch skips them.
        /// This sweep runs once immediately after HubRegistry is initialised.
        /// </summary>
        private static void InterceptSavedVanillaJobs()
        {
            var registry = HubRegistry.Instance;
            var store    = CarDestinationStore.Instance;
            var tcReg    = TrainCarRegistry.Instance;
            if (registry == null || !registry.IsReady || tcReg == null) return;

            var excluded = Config.ExcludedYardIds;

            // jobChainControllers is private — reflect it once
            var jccField = typeof(StationProceduralJobsController).GetField(
                "jobChainControllers",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (jccField == null)
                Log("[Main] WARNING: jobChainControllers field not found via reflection — InterceptSavedVanillaJobs will miss all jobs");

            int intercepted = 0;

            foreach (var sc in StationController.allStations)
            {
                var originYardId = sc.stationInfo.YardID;
                if (registry.IsHub(originYardId)) continue;
                if (excluded.Contains(originYardId)) continue;

                var assignedHubId = registry.GetAssignedHubId(originYardId);
                var hubStation    = registry.GetHub(assignedHubId);
                if (hubStation == null) continue;

                var chains = jccField?.GetValue(sc.ProceduralJobsController)
                             as List<JobChainController>;

                Log($"[Main] Sweep {originYardId}: {chains?.Count ?? -1} chain(s) found (jccField={(jccField != null ? "ok" : "null")})");

                if (chains == null || chains.Count == 0) continue;

                foreach (var jcc in chains.ToList()) // ToList — DestroyChain modifies the list
                {
                    var job = jcc.currentJobInChain;
                    if (job == null)
                    {
                        Log($"[Main] {originYardId}: JCC has null currentJobInChain — skipping");
                        continue;
                    }

                    Log($"[Main] {originYardId}: found {job.ID} type={job.jobType}({(int)job.jobType}) managed={Jobs.JobUtils.ManagedJobIds.Contains(job.ID)}");

                    if (Jobs.JobUtils.ManagedJobIds.Contains(job.ID)) continue;

                    var destYardId = job.chainData?.chainDestinationYardId;
                    if (string.IsNullOrEmpty(destYardId))
                    {
                        Log($"[Main] {originYardId}: {job.ID} has no dest in chainData — skip");
                        continue;
                    }
                    if (excluded.Contains(destYardId))
                    {
                        Log($"[Main] {originYardId}: {job.ID} dest {destYardId} is excluded — skip");
                        continue;
                    }

                    // Non-Transport types at spokes are always killed, regardless of destination.
                    // Check before the hub-dest check so SL/SU going to hub are also caught.
                    if (job.jobType != JobType.Transport)
                    {
                        Log($"[Main] {originYardId}: {job.ID} type={job.jobType}({(int)job.jobType}) non-transport at spoke — destroying");
                        jcc.DestroyChain();
                        continue;
                    }

                    // Transport going to hub: still convert to a GRDN feeder.
                    // Cars must be in CarDestinationStore so sort/final-mile can fire after delivery.
                    // (SortJobSpawner treats hub-local cars — trueDest==hub — as immediately Delivered.)

                    // Collect cars
                    var go = jcc.jobChainGO;
                    // Use Unity's overloaded == (not C# ?.) so destroyed fake-null GOs are caught.
                    // go?.GetComponent throws NullReferenceException on a destroyed GameObject.
                    if (go == null)
                    {
                        Log($"[Main] {originYardId}: {job.ID} jobChainGO is null/destroyed — destroying chain");
                        jcc.DestroyChain();
                        continue;
                    }
                    var transport = go.GetComponent<StaticTransportJobDefinition>();
                    if (transport?.carsToTransport == null)
                    {
                        Log($"[Main] {originYardId}: {job.ID} no StaticTransportJobDefinition or carsToTransport null — destroying");
                        jcc.DestroyChain();
                        continue;
                    }

                    var cars = new List<TrainCar>();
                    foreach (var lc in transport.carsToTransport)
                        if (tcReg.logicCarToTrainCar.TryGetValue(lc, out var tc))
                            cars.Add(tc);
                    if (cars.Count == 0)
                    {
                        Log($"[Main] {originYardId}: {job.ID} zero cars resolved from {transport.carsToTransport.Count} logic cars — destroying");
                        jcc.DestroyChain();
                        continue;
                    }

                    var startTrack = Jobs.JobUtils.FindCommonTrack(cars);
                    if (startTrack == null)
                    {
                        Log($"[Main] {originYardId}: {job.ID} FindCommonTrack null ({cars.Count} cars) — destroying");
                        jcc.DestroyChain();
                        continue;
                    }

                    // Hub inbound full → destroy anyway (no vanilla routing allowed)
                    if (Jobs.JobUtils.BestInboundTrack(hubStation) == null)
                    {
                        Log($"[Main] {assignedHubId} inbound full — destroying {job.ID} at {originYardId}");
                        jcc.DestroyChain();
                        continue;
                    }

                    Log($"[Main] Late-intercepting saved job {job.ID} ({originYardId}→{destYardId})");
                    jcc.DestroyChain();

                    int max = Mathf.Max(1, Settings.MaxCarsPerFeeder);
                    for (int i = 0; i < cars.Count; i += max)
                    {
                        int take  = Math.Min(max, cars.Count - i);
                        var batch = cars.GetRange(i, take);

                        // ID uses hub as destination — route-consistent with feeder chain data
                        var jobId = Jobs.JobUtils.NextId(originYardId, assignedHubId);

                        foreach (var car in batch)
                            if (!store.IsInterchangeCar(car.CarGUID))
                                store.Register(car.CarGUID, destYardId, originYardId, assignedHubId, jobId);

                        Jobs.FeederJobSpawner.SpawnFeeder(batch, startTrack, sc, hubStation, jobId);
                    }
                    intercepted++;
                }
            }

            Log($"[Main] InterceptSavedVanillaJobs: {intercepted} job chain(s) replaced with feeders.");
        }

        private static void ScanAndRecoverOrphanedFeeders()
        {
            var registry = HubRegistry.Instance;
            if (registry == null || !registry.IsReady) return;

            var excluded = Config.ExcludedYardIds;

            foreach (var sc in StationController.allStations)
            {
                var yardId = sc.stationInfo.YardID;
                if (registry.IsHub(yardId)) continue;
                if (excluded.Contains(yardId)) continue;
                FeederJobSpawner.ScanAndSpawnMissingFeeders(sc);
            }
        }

        // ── UMM settings GUI ──────────────────────────────────────────────────────

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            Settings.Draw(entry);

            UnityEngine.GUILayout.Space(8);
            if (UnityEngine.GUILayout.Button("Reload config.json", UnityEngine.GUILayout.Width(200)))
            {
                Config = InterchangeConfig.Load(ModEntry.Path);
                if (HubRegistry.Instance != null)
                    HubRegistry.Initialize(Config, Settings);
                Log("[Main] config.json reloaded and HubRegistry reinitialised.");
            }
            UnityEngine.GUILayout.Space(4);
            if (UnityEngine.GUILayout.Button("Dump Interchange State to Log", UnityEngine.GUILayout.Width(280)))
                DumpState();
        }

        private static void DumpState()
        {
            var store = CarDestinationStore.Instance;
            if (store == null) { Log("[Debug] CarDestinationStore not initialised"); return; }

            var all = store.GetAll();
            Log($"[Debug] ── Interchange State ── ({all.Count} tracked cars)");

            var byPhase = all.Values
                .GroupBy(r => r.Phase)
                .OrderBy(g => (int)g.Key);
            foreach (var g in byPhase)
                Log($"[Debug]   {g.Key}: {g.Count()} car(s)");

            foreach (var kv in all)
            {
                var r = kv.Value;
                Log($"[Debug]   {kv.Key.Substring(0, 8)}… " +
                    $"{r.TrueOriginYardId}→{r.TrueDestYardId} via {r.AssignedHubYardId} [{r.Phase}]");
            }

            var reg = HubRegistry.Instance;
            if (reg != null)
            {
                Log("[Debug] Registered hubs:");
                foreach (var hub in reg.AllHubs)
                    Log($"[Debug]   {hub.stationInfo.YardID}");
            }
        }

        private static void OnSaveGUI(UnityModManager.ModEntry entry)
        {
            Settings.Save(entry);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        public static void Log(string message) => ModEntry?.Logger?.Log(message);

        /// <summary>
        /// Returns true when we should run server-side logic.
        /// In single-player this is always true; in multiplayer only the host.
        /// Uses reflection against the public DVMP MultiplayerAPI so there is no
        /// hard compile-time dependency on DVMultiplayerAPI.dll.
        /// </summary>
        public static bool IsHostOrSingleplayer()
        {
            ResolveDvmpApi(); // idempotent — resolves once then returns immediately
            if (_mpApiInstance == null) return true; // no DVMP present → single-player
            try
            {
                bool isHost = (bool)(_isHostProp?.GetValue(_mpApiInstance) ?? true);
                bool isSP   = (bool)(_isSinglePlayerProp?.GetValue(_mpApiInstance) ?? true);

                if (!isHost && !isSP)
                {
                    // Both false in single-player DVMP mode is a known edge case — the server
                    // may not have propagated IsHost/IsSinglePlayer yet at job-generation time.
                    // Log every call that hits this path so we can see how often it fires.
                    Log($"[Main] IsHost={isHost} IsSinglePlayer={isSP} — both false; defaulting to HOST");
                    return true;
                }

                return isHost || isSP;
            }
            catch { return true; }
        }

        /// <summary>
        /// Locate DVMP's MultiplayerAPI assembly in the AppDomain and cache reflection
        /// handles for IsHost and IsSinglePlayer. Also registers this mod as Host-only
        /// so clients can join a GRDN-hosted server without installing the mod themselves.
        /// Safe to call multiple times — resolves only on the first call.
        /// </summary>
        private static void ResolveDvmpApi()
        {
            if (_dvmpResolved) return;
            _dvmpResolved = true;

            try
            {
                // MultiplayerAPI.dll is loaded by DVMP before GRDNInterchange (LoadAfter: ["Multiplayer"])
                var mpApiType = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "MultiplayerAPI")
                    ?.GetType("MPAPI.MultiplayerAPI");

                if (mpApiType == null)
                {
                    Log("[Main] DVMP MultiplayerAPI not found — assuming single-player");
                    return;
                }

                var isLoaded = (bool)(mpApiType.GetProperty("IsMultiplayerLoaded")?.GetValue(null) ?? false);
                if (!isLoaded)
                {
                    Log("[Main] DVMP present but multiplayer not loaded");
                    return;
                }

                _mpApiInstance = mpApiType.GetProperty("Instance")?.GetValue(null);
                if (_mpApiInstance == null)
                {
                    Log("[Main] DVMP Instance is null");
                    return;
                }

                var instanceType = _mpApiInstance.GetType();
                _isHostProp        = instanceType.GetProperty("IsHost");
                _isSinglePlayerProp = instanceType.GetProperty("IsSinglePlayer");

                // Register as Host-only: clients don't need this mod installed
                // MultiplayerCompatibility.Host = byte value 3
                var compatType = mpApiType.Assembly.GetType("MPAPI.Types.MultiplayerCompatibility");
                if (compatType != null)
                {
                    var hostCompat = Enum.ToObject(compatType, (byte)3);
                    instanceType.GetMethod("SetModCompatibility")
                        ?.Invoke(_mpApiInstance, new[] { ModEntry.Info.Id, hostCompat });
                }

                Log("[Main] DVMP API hooked — registered as Host-only mod");
            }
            catch (Exception ex)
            {
                Log($"[Main] DVMP API hook failed: {ex.Message}");
            }
        }
    }
}
