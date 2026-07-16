using DLE.Jobs;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using System;
using System.Linq;

namespace DLE.Dispatch
{
    /// <summary>
    /// Remote job lifecycle for the dispatch board and API (#30): take a Company Haul on
    /// behalf of a crew and turn it in when it is really delivered. The turn-in check is
    /// economic, not paperwork: cars attached, empty, and standing on the destination
    /// warehouse track. (The scoped WarehouseTask patch makes task states unreliable for
    /// DLE jobs, so validating those would let an untouched job "complete".)
    /// Host or singleplayer only; the dispatcher's authority stands in for license checks.
    /// </summary>
    public static class DispatchLifecycle
    {
        public struct Result
        {
            public bool Ok;
            public string Message;
            public static Result Fail(string m) => new Result { Ok = false, Message = m };
            public static Result Done(string m) => new Result { Ok = true, Message = m };
        }

        /// <summary>
        /// Lock-on purge: every Available haul with no assignment expires. Their office
        /// papers are already swept when the lock is on; the jobs follow the paper so the
        /// board matches the world. Assigned or taken hauls survive: dispatch prepared
        /// those on purpose. Expiry tears the chain down, which returns the job's
        /// pre-allocated supply to the stockpile.
        /// </summary>
        public static int ExpireUnassignedAvailable()
        {
            var doomed = new System.Collections.Generic.List<Job>();
            foreach (var kv in StaticDirectHaulJobDefinition.jobDefinitions)
            {
                var job = kv.Value?.LiveJob;
                if (job == null || job.State != JobState.Available) continue;
                if (AssignmentStore.Instance.Get(kv.Key) != null) continue;
                doomed.Add(job);
            }
            int expired = 0;
            foreach (var job in doomed)
            {
                try { job.ExpireJob(); expired++; }
                catch (Exception ex) { Main.LogAlways($"[Dispatch] could not expire {job.ID}: {ex.Message}"); }
            }
            if (expired > 0)
                Main.LogAlways($"[Dispatch] lock ON expired {expired} unassigned open booklet(s); supply returned.");
            return expired;
        }

        public static Result TakeJob(string jobId, string player)
        {
            if (!Main.IsHostOrSingleplayer()) return Result.Fail("host or singleplayer only");
            if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def) || def.LiveJob == null)
                return Result.Fail($"unknown job '{jobId}'");
            var job = def.LiveJob;
            if (job.State != JobState.Available)
                return Result.Fail($"job is {job.State}, not available");

            // Accept-time supply check (#67): open paper holds soft; taking hardens the
            // hold. Paper whose supply was promised away since printing is stale and
            // expires here instead of lying to the crew.
            if (!Economy.EconomyState.Instance.HardenReservation(jobId))
            {
                try { job.ExpireJob(); } catch (Exception ex) { Main.Log($"[Dispatch] stale-paper expire failed: {ex.Message}"); }
                return Result.Fail($"{jobId} is stale: its supply went to other hauls; the booklet expired");
            }

            if (AssignmentStore.Instance.LockEnabled)
            {
                var assignment = AssignmentStore.Instance.Get(jobId);
                if (assignment == null)
                    return Result.Fail("assignment lock is ON and this haul has no assigned crew");
                // Without this check a blank name skips the match, taking a locked haul
                // out from under its assigned crew with no identity at all.
                if (string.IsNullOrEmpty(player))
                    return Result.Fail("assignment lock is ON; enter the crew name to take this haul");
                if (!string.Equals(assignment.Player, player, StringComparison.OrdinalIgnoreCase))
                    return Result.Fail($"assignment lock is ON and this haul is assigned to {assignment.Player}");
            }
            else if (!string.IsNullOrEmpty(player) && AssignmentStore.Instance.Get(jobId) == null)
            {
                // Keep the board honest about who is running the haul.
                AssignmentStore.Instance.Assign(jobId, player, "board-take");
            }

            SingletonBehaviour<JobsManager>.Instance.TakeJob(job, false);
            Main.LogAlways($"[Dispatch] {jobId} taken via board{(string.IsNullOrEmpty(player) ? "" : $" for {player}")}.");
            return Result.Done($"{jobId} taken{(string.IsNullOrEmpty(player) ? "" : $" for {player}")}");
        }

        public static Result CompleteJob(string jobId)
        {
            if (!Main.IsHostOrSingleplayer()) return Result.Fail("host or singleplayer only");
            if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def) || def.LiveJob == null)
                return Result.Fail($"unknown job '{jobId}'");
            var job = def.LiveJob;
            if (job.State != JobState.InProgress)
                return Result.Fail($"job is {job.State}, not in progress");

            var cars = def.carsToTransport;
            if (cars == null || cars.Count == 0)
                return Result.Fail("no cars attached yet; bring empties to the loading track first");
            if (def.loadedCarloads <= 0)
                return Result.Fail("this haul was never loaded; empty cars at the destination do not count as a delivery");

            // Delivered = empty and anywhere in the destination station's yard; the exact
            // track only matters to the terminal.
            var destTrack = def.unloadMachine?.WarehouseTrack;
            var destSc = StationController.GetStationByYardID(def.chainData?.chainDestinationYardId);
            var allowed = DispatchServicing.StationTracks(destSc, destTrack);
            var notDelivered = cars.Where(c =>
                c.LoadedCargoAmount > 0f || c.CurrentTrack == null || !allowed.Contains(c.CurrentTrack)).ToList();
            if (notDelivered.Count > 0)
                return Result.Fail($"{notDelivered.Count}/{cars.Count} car(s) not unloaded at " +
                                   $"{def.chainData?.chainDestinationYardId} yet ({string.Join(", ", notDelivered.Take(4).Select(c => c.ID))})");

            var state = SingletonBehaviour<JobsManager>.Instance.TryToCompleteAJob(job);
            if (state != JobState.Completed)
                return Result.Fail($"game refused completion (state {state})");

            // Completion fired the chain, and DirectHaulCompletionPatch is the single
            // gated payout: it pays deliveryPayment scaled to the cargo the destination
            // accepted. Paying here as well would double it.
            Main.LogAlways($"[Dispatch] {jobId} turned in via board.");
            return Result.Done($"{jobId} turned in; delivery pay up to ${def.deliveryPayment:0}");
        }
    }
}
