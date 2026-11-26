# Development

## Layout

Three assemblies, all built into `build/`:

- `DLE/` is the mod, `DerailLogisticsEngine.dll`.
- `DleMpBridge/` is the only code that touches the Multiplayer API. DLE loads it by
  reflection when Multiplayer is running, so single player never needs it.
- `DleSignalsBridge/` is the only code that touches DV Signals (the WhistleWiz framework,
  pinned to 1.1.3). DLE loads it by reflection when the framework is installed.

Inside `DLE/`:

| Folder | What's in it |
| --- | --- |
| `Economy/` | The simulation. `EconomyState` holds stock and runs production, consumption and the clock. `RecipeProvider` builds each station from the game's cargo groups and applies `economy.json`. `EconomyModels` has the config classes and their defaults. `EconomyDirector` turns stock into hauls. `DleDirectorBehaviour` is the host's main loop. |
| `Jobs/` | The Company Haul. `DirectHaulGenerator` builds it, `StaticDirectHaulJobDefinition` holds its data, and `JobUtils.ManagedJobIds` tells DLE jobs from vanilla ones. |
| `Patches/` | Harmony patches into the game. Each file's header says what it changes and why. |
| `Dispatch/` | The board and what it drives. `DleHttpServer` is the API, `BoardPage` the page. `DispatchLifecycle` takes, completes and auto-closes hauls. `DispatchServicing` is staff loading. `TrackMap`, `JunctionBook` and `SignalsLink` feed the map. `DleMpChannel` syncs host data to clients. |
| `Data/` | Persistence. `DleJobStore` saves DLE's jobs itself, `DleCarPool` is the persistent fleet, `CarDormancy` stores far-away cars as data. |

`DeskInbox`, `PacketStore`, `YardGeometry` and `OrderBook` belong to the dispatch desk and
are dormant in 0.8. Nothing calls them until the desk comes back.

## How a haul moves through the code

1. `DleDirectorBehaviour` ticks every `directorTickSeconds` (120 real seconds). It advances
   `EconomyState` on in-game time and asks `EconomyDirector` for a haul.
2. `EconomyDirector` finds a producer with produced stock and a consumer with room on a
   vanilla route. `DirectHaulGenerator` builds a carless Company Haul, and the stock gets a
   soft hold.
3. `DirectHaulBookletPatch` renders the booklet, since vanilla can't render this job type.
   `JobPaperOnAddPatch` prints office paper for anyone already standing in the office.
4. Taking it at the office goes through `JobValidatorLockPatch`, which checks the lock and
   the stock, then hardens the hold. Taking it from the board goes through
   `DispatchLifecycle.TakeJob`.
5. Loading at the terminal goes through `DirectHaulLoadServicingPatch`, which attaches the
   cars. `CommitAttach` debits the producer the moment cars commit. Cars elsewhere in the
   yard are loaded by `DispatchServicing`.
6. Unloading is the terminal or `DispatchServicing`. Staff stop when the destination is
   full. The terminal doesn't (#239).
7. The auto-close sweep in `DispatchLifecycle` completes the job once the cars are empty in
   the destination yard and the station has room. Completion fires
   `DirectHaulCompletionPatch`, which credits the destination and pays. That's the only
   place DLE pays.

Around that loop:

- `VanillaGenerationSuppressorPatch` stops vanilla freight jobs at economy stations.
- `DirectHaulSaveFilterPatch` keeps Company Hauls out of the vanilla save, which can't
  reload them. `DleJobStore` saves and restores them instead.
- `ManagedJobExpiryGuardPatch` keeps hauls alive when the last player leaves a station.
- `CarPoolDeleterPatch` stops the game despawning pool cars.

## Host and client

Everything that changes the world runs on the host or in single player. Check
`Main.IsHostOrSingleplayer()` in any patch that mutates state. Clients run DLE only to render
booklets. dv-mp sends a job's cars once, when the job is created, so `DleMpChannel` carries
everything after that. Changing a packet's layout means host and clients need the same DLE
build.

## Building

Copy these into `DLE/lib/`:

- The game assemblies DLE references, from `DerailValley_Data/Managed/` and
  `DerailValley_Data/Managed/UnityModManager/`
- `MultiplayerAPI.dll` from the Multiplayer mod folder
- `Signals.Common.dll` and `Signals.Game.dll` from the DV Signals mod folder

Then build both bridges. Each one references DLE, so DLE builds with them.

```
dotnet build DleMpBridge/DleMpBridge.csproj
dotnet build DleSignalsBridge/DleSignalsBridge.csproj
```

The three DLLs end up in `build/`.

## Testing in game

The mod folder is `Mods/DerailLogisticsEngine/`. It needs the three DLLs, `Info.json` and
`economy.default.json`. UMM zip installs replace the whole folder, so a release zip without
`economy.default.json` leaves the mod with no default config.

DLE logs to the game's `Player.log` (`%USERPROFILE%\AppData\LocalLow\Altfuture\Derail Valley\`)
with prefixes like `[Economy]`, `[Director]` and `[Dispatch]`. Turn on verbose logging in the
mod settings to see `Main.Log` lines. `Main.LogAlways` always prints, so use it for errors
and for anything that explains why something didn't happen.

In multiplayer the economy runs on the host. Read the host's log, not the client's.

`company.respawn` rebuilds the car pools and clears wrecks. `company.resupply` resets all
station stock.

## Config changes

When `economy.default.json` changes shape, bump its `configVersion` and
`CurrentConfigVersion` in `RecipeProvider.cs` together. A user's `economy.json` older than
that gets replaced by the default, and the old file is kept as `economy.json.bak`. The two
are out of step right now (the file says 3, the code says 2), so a version 2 file never
migrates.

## Versions and releases

DLE uses [SemVer](https://semver.org): `MAJOR.MINOR.PATCH`. A minor is a milestone, a patch
is fixes and features inside it.

- `Info.json` always holds a plain `X.Y.Z`. UMM strips everything but digits when it compares
  versions, so `1.0.0-beta.2` would rank above `1.0.0`.
- While a beta line stabilizes, the number stays put and each test build gets the next tag,
  `vX.Y.Z-beta.1`, `-beta.2` and so on. The patch moves when the line settles.
- Only tag checkpoints worth going back to. Only publish a GitHub release for a milestone or a
  build other players need to download.
- Versions never go backwards. Check `git tag --sort=-creatordate` before tagging.
- Tags from before SemVer (`v0.10.000` to `v0.44.005`) stay as they are.

## Code rules

- Patches only touch DLE jobs. Check `JobUtils.ManagedJobIds` and leave vanilla jobs alone.
- Guard patches with `Prepare` where a game update could rename the target, so a rename
  skips the patch instead of breaking the mod load.
- Multiplayer API types stay in `DleMpBridge`. DV Signals types stay in `DleSignalsBridge`.
- The publicizer needs full namespaced names (`DV.Logic.Job.X`). Readonly fields still need
  `Traverse`.
- Game internals we've already worked out are in
  [design/internals-notes.md](design/internals-notes.md).
