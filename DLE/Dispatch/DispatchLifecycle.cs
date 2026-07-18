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
        /// Hauls with cars attached or cargo loaded are NEVER purged regardless of what
        /// their state claims (#94): their supply lives on real cars, not in the pile, so
        /// expiring them is data loss, not cleanup. State said Available on a crew's
        /// loaded haul once already (the restore demotion) and this purge ate it.
        /// </summary>
        public static int ExpireUnassignedAvailable()
        {
            var doomed = new System.Collections.Generic.List<Job>();
            foreach (var kv in StaticDirectHaulJobDefinition.jobDefinitions)
            {
                var def = kv.Value;
                var job = def?.LiveJob;
                if (job == null || job.State != JobState.Available) continue;
                if (AssignmentStore.Instance.Get(kv.Key) != null) continue;
                if (def.carsToTransport != null && def.carsToTransport.Count > 0) continue;
                if (def.loadedCarloads > 0) continue;
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

        /// <summary>
        /// Dispatcher deletes one haul from the board. Open paper expires exactly like
        /// the lock-on purge does. A TAKEN haul with no cars attached is abandoned
        /// through the game's own path (the crew's booklet voids itself); per the supply
        /// rules, any cancel before loading returns the hold, taken or not. Only a haul
        /// with cars already attached is refused: its supply was consumed onto real
        /// cars, and that cargo must be delivered or crew-abandoned in-game.
        /// </summary>
        public static Result DeleteHaul(string jobId)
        {
            if (!Main.IsHostOrSingleplayer()) return Result.Fail("host or singleplayer only");
            if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def) || def.LiveJob == null)
                return Result.Fail($"unknown job '{jobId}'");
            var job = def.LiveJob;

            // Cars attached means the supply is on those cars, whatever the job state
            // says (#94 hardening): deleting would strand real cargo behind a dead job.
            bool hasCars = def.carsToTransport != null && def.carsToTransport.Count > 0;
            if (hasCars)
                return Result.Fail($"{jobId} has cars attached; its supply is on those cars. Deliver it or have the crew abandon it in-game.");

            if (job.State == JobState.Available)
            {
                try { job.ExpireJob(); }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Dispatch] {jobId} delete failed: {ex.GetType().Name}: {ex.Message}");
                    return Result.Fail($"the game refused to expire {jobId}; see the log");
                }
                AssignmentStore.Instance.Unassign(jobId);
                Main.LogAlways($"[Dispatch] {jobId} deleted via board (was open); supply returned.");
                return Result.Done($"{jobId} deleted; its supply returned to the pile");
            }
            if (job.State == JobState.InProgress)
            {
                try { SingletonBehaviour<JobsManager>.Instance.AbandonJob(job); }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Dispatch] {jobId} abandon-delete failed: {ex.GetType().Name}: {ex.Message}");
                    return Result.Fail($"the game refused to abandon {jobId}; see the log");
                }
                AssignmentStore.Instance.Unassign(jobId);
                Main.LogAlways($"[Dispatch] {jobId} deleted via board (was taken, never loaded); supply returned.");
                return Result.Done($"{jobId} deleted; the crew's booklet is void and its supply returned");
            }
            return Result.Fail($"{jobId} is {job.State}; nothing to delete");
        }

        public static Result TakeJob(string jobId, string player)
        {
            if (!Main.IsHostOrSingleplayer()) return Result.Fail("host or singleplayer only");
            if (!StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(jobId, out var def) || def.LiveJob == null)
                return Result.Fail($"unknown job '{jobId}'");
            var job = def.LiveJob;
            if (job.State != JobState.Available)
                return Result.Fail($"job is {job.State}, not available");

            // The board IS dispatch: a board take is the dispatcher acting, so it needs no
            // crew name and ignores the lock (the lock exists to stop CREWS at the
            // validator from taking unassigned work; the validator still enforces it).
            // A typed name records who the haul is for; a blank take runs it unassigned.

            // Accept-time supply check (#67): open paper holds soft; taking hardens the
            // hold, now that the take is actually going ahead. Paper whose supply was
            // promised away since printing is stale and expires here instead of lying.
            if (!Economy.EconomyState.Instance.HardenReservation(jobId))
            {
                try { job.ExpireJob(); }
                catch (Exception ex) { Main.LogAlways($"[Dispatch] {jobId} stale-paper expire failed: {ex.GetType().Name}: {ex.Message}"); }
                return Result.Fail($"{jobId} is stale: its supply went to other hauls; the booklet expired");
            }

            // Keep the board honest about who is running the haul.
            if (!string.IsNullOrEmpty(player) && AssignmentStore.Instance.Get(jobId) == null)
                AssignmentStore.Instance.Assign(jobId, player, "board-take");

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

            // Nothing is ever destroyed: closing with less room than cargo would eat
            // the excess unpaid, so the job waits (the auto-close sweep retries) until
            // consumption frees space at the destination.
            int deliverable = Math.Min(cars.Count, def.loadedCarloads);
            float room = Economy.EconomyState.Instance.GetRoom(def.chainData?.chainDestinationYardId, def.transportedCargo);
            if (room + 0.001f < deliverable)
                return Result.Fail($"{def.chainData?.chainDestinationYardId} has room for {(int)Math.Floor(room + 0.001f)} of {deliverable} carload(s); waiting for the station to consume");

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
