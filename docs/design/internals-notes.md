# Game and neighbor-mod internals notes

What we've learned reading Derail Valley's code and the mods DLE runs next to (SelfShunter,
Persistent Jobs, Multiplayer). Every game name here was checked against the assemblies in
`DLE/lib` on 2026-11-25.

## Warehouse machine (DV.Logic.Job.WarehouseMachine + WarehouseMachineController)

- The physical terminal is `WarehouseMachineController` (static `allControllers` lists every
  machine in the world; match by its `warehouseMachine` to find the controller for a job's
  machine).
- **`ActivateExternally()`** runs the machine as if a player pulled the lever (own coroutine,
  screen text, sounds). **`DelayedLoadUnload(bool isLoading, float delayBetweenActions, bool
  play2D)`** is the underlying sequence with a configurable per-car delay
  (`DELAY_BETWEEN_MACHINE_ACTIONS` is the vanilla value). `LoadAllInstant()` /
  `UnloadAllInstant()` skip the pacing. DLE doesn't use any of these. Staff loading paces
  itself and moves cargo car by car (see below).
- Logic-layer: `machine.LoadOneCarOfTask(task)` / `UnloadOneCarOfTask(task)` move one car's
  cargo with the real rules; `TryLoadCargoToAllCarsInstant()` / `TryUnloadCargoToAllCarsInstant()`
  do a whole track's worth. All of these only consider cars ON the warehouse track
  (`AtLeastOneCarOnWarehouseTrack` / `CarsPresentOnWarehouseTrack`), so servicing cars parked
  elsewhere in a yard must move cargo via `Car.LoadCargo`/`UnloadCargo` directly.
- `machine.currentTasks` / `currentJobToTasks` track which warehouse tasks are checked in.
- The terminal's unload and the office turn-in know nothing about DLE storage. A full
  destination still unloads and turns in, and the economy drops whatever doesn't fit (#239).

## Job lifecycle (DV.Logic.Job)

- `JobsManager.TakeJob(job, takenViaLoadGame)` and `JobsManager.TryToCompleteAJob(job)`
  (validates via `job.ValidateJobFinished()`, returns the resulting `JobState`) are the
  programmatic take/turn-in. `job.GetWageForTheJob()` for payment.
- **`TrainCar.ExtractLogicCars(list)` returns NULL for an empty list** (logs "Passed null or
  empty list of trainCars"). Never route a carless job through it.
- `JobDebtController.RegisterGeneratedJob` ends in `Dictionary<Job,...>.Add`: registering a job's
  debt twice throws. Check `GetExistingJobDebtForJob(job) == null` first; restored jobs may have
  registered during rebuild.
- Office paper: `StationController.spawnedJobOverviews` (publicized) holds each station's
  `JobOverview` papers; `DestroyJobOverview()` despawns one.
- The validator's concurrent-job limit and its reprint both read `JobsManager.currentJobs`. A
  job finished outside the normal take flow can stay in that list and hold a slot forever
  (#216). If the player never took it, add it to `currentJobs` before `TryToCompleteAJob`
  (vanilla's `CompleteTheJob` throws otherwise), then remove any same-id leftovers.

## Car spawning (CarSpawner)

- `SpawnCarTypesOnTrack[RandomOrientation]` builds placement from `GetTrackMiddleBasedSpawnData`:
  every cut is centered on the track midpoint with NO overlap check. Two cuts on one track always
  interpenetrate. This was the stacking/save-corruption root cause.
- `SpawnCarTypesOnTrackStrict(liveries, railTrack, preventAutoCouple, applyHandbrake, startSpan,
  flipConsist, randomOrientation, playerSpawned)` overlap-checks per car (`IsBoxOverlapping`) and
  returns `Blocked`/`CannotFitOnTrack` instead of placing. Vanilla never calls it.
- The Strict physics check is **blind at unstreamed cells** (no colliders far from the player), so
  the only streaming-proof anti-stacking guarantee is logic-layer: spawn onto tracks with zero
  cars (`GetCarsFullyOnTrack`), one cut per track per sweep.
- The game natively sleeps stationary cars (`TrainCarPositionMonitorSystem` marks eligibility,
  `DV.Optimizers.TrainsOptimizer` force-sleeps whole tracks, wakes on live traffic).
  `TrainCar.ForceSleep(true)` opts a car in immediately. Derailed or interpenetrating cars never
  become stationary and never sleep; detect wrecks geometrically (centers under 5m =
  interpenetration; tilt over 8 degrees = resting on something).

## Neighbor mods

- **SelfShunter** (used with permission): our warehouse attach and booklet redraw are adaptations
  of its `JobMechanics`. It additionally prefixes `Job.ExpireJob` to block expiry of
  ComplexTransport jobs while the mod is active. It has the same latent debt double-registration
  its flow never triggers (no remote take, no debt-registering restore).
- **Multiplayer**: `NetworkedJob` subscribes to the job's own `JobTaken` / `JobCompleted` /
  `JobAbandoned` / `JobExpired` events and marks the job dirty for sync. Anything that drives the
  lifecycle through those events (our remote take/turn-in does) syncs to clients with no MP
  changes. Job-keyed MP dictionaries use indexers, not `.Add`.
- In Multiplayer, a client taking a job goes through the host's validator. If the host refuses,
  the client is never told why (dv-mp's validate-response send is commented out), so the
  host log is the only record.
- **Persistent Jobs**: avoids stacking via the YardTracksOrganizer reservation ledger plus
  near-player generation, not overlap checks; it would stack under map-wide seeding. Its
  perf value is persistence discipline (stable fleet, reservation hygiene), which DLE already has
  via the pool; nothing further worth porting as of 3.5.x.
