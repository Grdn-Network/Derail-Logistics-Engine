using DV.Logic.Job;
using DV.ThingTypes;
using GRDNInterchange.Data;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GRDNInterchange.Jobs
{
    public static class JobUtils
    {
        private static int _idCounter = 0;

        public static string NextId(string prefix) =>
            $"GI-{prefix}-{System.Threading.Interlocked.Increment(ref _idCounter):D4}";

        public static StationsChainData Chain(string originYardId, string destYardId) =>
            new StationsChainData(originYardId, destYardId);

        // ── Wage / time estimates (rough; fine-tuned later) ───────────────────────

        public static float EstimateWage(int carCount, float distanceFactor = 1f) =>
            Mathf.Max(200f, carCount * 450f * distanceFactor);

        public static float EstimateTimeLimit(int carCount, float distanceFactor = 1f) =>
            Mathf.Max(600f, carCount * 120f * distanceFactor + 600f);

        // ── Track finders ─────────────────────────────────────────────────────────

        /// <summary>
        /// Pick the inbound track at the hub that has the most free length.
        /// Returns null if no inbound tracks exist.
        /// </summary>
        public static Track BestInboundTrack(StationController hub)
        {
            return TrackClassifier.GetInboundTracks(hub)
                .OrderByDescending(t => t.length - t.OccupiedLength)
                .FirstOrDefault();
        }

        /// <summary>
        /// Pick a storage track at the hub most suited to the given destination,
        /// preferring tracks that already hold cars going the same way.
        /// Falls back to the track with most free length.
        /// </summary>
        public static Track BestStorageTrack(StationController hub, string forDestYardId)
        {
            var candidates = TrackClassifier.GetStorageTracks(hub, allowPaxOverflow: true);
            if (candidates.Count == 0) return null;

            // Prefer a track that already has cars tagged for the same destination
            var store = CarDestinationStore.Instance;
            foreach (var t in candidates)
            {
                var carsOnTrack = t.GetCarsFullyOnTrack();
                if (carsOnTrack == null || carsOnTrack.Count == 0) continue;
                if (carsOnTrack.Any(c => store.Get(c.carGuid)?.TrueDestYardId == forDestYardId))
                    return t;
            }

            // No matching track; pick roomiest
            return candidates.OrderByDescending(t => t.length - t.OccupiedLength).First();
        }

        /// <summary>
        /// Pick the outbound track with the most cars already on it (building the block),
        /// or any outbound track if all are empty.
        /// </summary>
        public static Track BestOutboundTrack(StationController hub)
        {
            var candidates = TrackClassifier.GetOutboundTracks(hub);
            if (candidates.Count == 0) return null;
            return candidates.OrderByDescending(t => t.GetCarsFullyOnTrack()?.Count ?? 0).First();
        }

        // ── JobChainController builder ─────────────────────────────────────────────

        /// <summary>
        /// Wrap a StaticJobDefinition in a JobChainController, finalize it,
        /// and register it with the station's procedural controller.
        /// Note: JobChainController is NOT a MonoBehaviour — it is a plain class.
        /// </summary>
        private static readonly System.Reflection.FieldInfo _responsibleStationField =
            typeof(JobChainController).GetField(
                "responsibleStationForJobChain",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        public static JobChainController ActivateJobChain(
            StaticJobDefinition def,
            StationController station)
        {
            var jcc = new JobChainController(def.gameObject);
            _responsibleStationField?.SetValue(jcc, station);
            if (def is StaticTransportJobDefinition transport && transport.carsToTransport != null)
                jcc.carsForJobChain = transport.carsToTransport;
            jcc.AddJobDefinitionToChain(def);
            try
            {
                jcc.FinalizeSetupAndGenerateFirstJob(false);
            }
            catch (System.Exception ex)
            {
                // If job generation fails, destroy the partial JCC and its GameObject so it
                // doesn't corrupt autosave ("Uninitialized chain controller!").
                Main.Log($"[JobUtils] ActivateJobChain failed ({ex.GetType().Name}): {ex.Message}");
                try { jcc.DestroyChain(); } catch { }
                UnityEngine.Object.Destroy(def.gameObject);
                return null;
            }
            station.ProceduralJobsController.AddJobChainController(jcc);
            return jcc;
        }

        // ── Car helpers ────────────────────────────────────────────────────────────

        public static List<Car> ToLogicCars(IEnumerable<TrainCar> trainCars) =>
            TrainCar.ExtractLogicCars(trainCars.ToList());

        public static List<CargoType> GetCargoes(IEnumerable<TrainCar> cars) =>
            cars.Select(c => c.logicCar.CurrentCargoTypeInCar).ToList();

        public static List<float> GetCargoAmounts(IEnumerable<TrainCar> cars) =>
            cars.Select(c => c.logicCar.LoadedCargoAmount).ToList();

        /// <summary>
        /// Find the RailTrack the majority of cars in a list are sitting on.
        /// </summary>
        public static Track FindCommonTrack(List<TrainCar> cars)
        {
            if (cars == null || cars.Count == 0) return null;
            return cars
                .Select(c => c.logicCar.CurrentTrack)
                .Where(t => t != null)
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }
    }
}
