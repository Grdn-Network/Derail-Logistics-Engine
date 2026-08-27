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

            // Cars attached means the supply is on those cars. The dispatcher may still
            // close it out, accepting the loss: cargo aboard is dumped and does NOT
            // return to any pile (the board confirms with "Abandon supply?" first; the
            // return flow at the origin is the no-loss alternative).
            bool hasCars = def.carsToTransport != null && def.carsToTransport.Count > 0;
            if (hasCars)
            {
                // The attach debited EVERY car's worth of supply the moment the cars
                // committed. Cargo physically aboard is dumped and lost; the NEVER
                // LOADED remainder was only ever a ledger debit, so it returns to the
                // origin pile (counted BEFORE the dump wipes the cargo state).
                var originYard = def.chainData?.chainOriginYardId;
                int loaded = def.carsToTransport.Count(c => c.LoadedCargoAmount > 0f);
                int returned = 0;
                if (!string.IsNullOrEmpty(originYard))
                {
                    if (def.manifest != null)
                    {
                        foreach (var line in def.manifest)
                        {
                            if (line.Cargo == DV.ThingTypes.CargoType.None) continue;
                            int lineTotal = line.CarIds?.Count ?? 0;
                            int lineLoaded = def.carsToTransport.Count(c =>
                                line.CarIds != null && line.CarIds.Contains(c.ID) && c.LoadedCargoAmount > 0f);
                            int back = lineTotal - lineLoaded;
                            if (back > 0)
                            {
                                Economy.EconomyState.Instance.ReturnSupply(originYard, line.Cargo, back, paid: !line.Unpaid);
                                returned += back;
                            }
                        }
                    }
                    else
                    {
                        int back = def.carsToTransport.Count - loaded;
                        if (back > 0)
                        {
                            Economy.EconomyState.Instance.ReturnSupply(originYard, def.transportedCargo, back, paid: !def.unpaidMove);
                            returned = back;
                        }
                    }
                }
                try
                {
                    if (job.State == JobState.InProgress) SingletonBehaviour<JobsManager>.Instance.AbandonJob(job);
                    else job.ExpireJob();
                }
                catch (Exception ex)
                {
                    Main.LogAlways($"[Dispatch] {jobId} abandon failed: {ex.GetType().Name}: {ex.Message}");
                    return Result.Fail($"the game refused to close {jobId}; see the log" +
                        (returned > 0 ? $" (WARNING: {returned} carload(s) were already credited back)" : ""));
                }
                // Abandon fires the cargo dump through the job event; expire does not,
                // so dump here too (idempotent: already-empty cars just lose plates).
                try { Patches.DleWarehouseLoadAttachPatch.DumpJobCargo(job); } catch { }
                AssignmentStore.Instance.Unassign(jobId);
                Main.LogAlways($"[Dispatch] {jobId} abandoned via board; {loaded} loaded carload(s) lost, {returned} returned to {originYard}, cars freed.");
                return Result.Done($"{jobId} abandoned; {loaded} loaded carload(s) lost" +
                    (returned > 0 ? $", {returned} returned to {originYard}" : "") + ", cars freed");
            }

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

            // The validator's concurrent-order gate and its reprint both read
            // jm.currentJobs (verified against build 2702), and our pseudo-booklet flow
            // could leave a finished haul in that list forever: the tester got DENIED
            // for order slots two completed hauls were still holding (#216). Complete
            // through the vanilla path (adding first when the player never formally
            // took it, since vanilla's CompleteTheJob throws otherwise), then sweep any
            // same-id straggler so a closed haul can never occupy a slot.
            var jm = SingletonBehaviour<JobsManager>.Instance;
            if (!jm.currentJobs.Contains(job)) jm.currentJobs.Add(job);
            var state = jm.TryToCompleteAJob(job);
            if (state != JobState.Completed)
                return Result.Fail($"game refused completion (state {state})");
            PurgeTakenStragglers(jm, jobId, job);

            // Completion fired the chain, and DirectHaulCompletionPatch is the single
            // gated payout: it pays deliveryPayment scaled to the cargo the destination
            // accepted. Paying here as well would double it.
            Main.LogAlways($"[Dispatch] {jobId} turned in via board.");
            return Result.Done($"{jobId} turned in; delivery pay up to ${def.deliveryPayment:0}");
        }

        /// <summary>Remove every leftover taken-orders entry carrying this DLE job id
        /// that is not the given instance (#216). DLE ids only; vanilla jobs are never
        /// touched.</summary>
        private static void PurgeTakenStragglers(JobsManager jm, string jobId, Job keep)
        {
            if (jm == null) return;
            for (int i = jm.currentJobs.Count - 1; i >= 0; i--)
            {
                var j = jm.currentJobs[i];
                if (j == null) { jm.currentJobs.RemoveAt(i); continue; }
                if (ReferenceEquals(j, keep) || !string.Equals(j.ID, jobId, StringComparison.Ordinal)) continue;
                jm.currentJobs.RemoveAt(i);
                Main.LogAlways($"[Dispatch] {jobId}: dropped a stale duplicate from the taken-orders list; it was holding a concurrent-order slot (#216).");
            }
        }

        /// <summary>
        /// World-load reconcile (#216): a taken-orders entry with a DLE-managed id that
        /// is not its definition's live instance is a ghost from a previous world
        /// (Direct Hauls are filtered out of the vanilla job save, so vanilla can never
        /// legitimately restore a taken one). Ghosts eat concurrent-order slots and
        /// resurrect at every validator reprint. Vanilla ids are never touched.
        /// </summary>
        public static void ReconcileTakenOrders()
        {
            var jm = SingletonBehaviour<JobsManager>.Instance;
            if (jm == null) return;
            int dropped = 0;
            for (int i = jm.currentJobs.Count - 1; i >= 0; i--)
            {
                var j = jm.currentJobs[i];
                if (j?.ID == null || !JobUtils.ManagedJobIds.Contains(j.ID)) continue;
                bool legit = StaticDirectHaulJobDefinition.jobDefinitions.TryGetValue(j.ID, out var d)
                             && ReferenceEquals(d.LiveJob, j);
                if (legit) continue;
                jm.currentJobs.RemoveAt(i);
                dropped++;
            }
            if (dropped > 0)
                Main.LogAlways($"[Dispatch] dropped {dropped} ghost taken order(s) from a previous world; they were eating concurrent-order slots (#216).");
        }
    }
}
