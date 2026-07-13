# Derail Logistics Engine

Derail Logistics Engine (DLE) brings economy management to Derail Valley. Every
freight station has stockpiles and recipes derived from the game's own cargo
data: producers hold stock, consumers need it, and deliveries convert inputs
into outputs. Jobs are generated from that economy as Company Haul jobs: a
loaded consist appears at the producer and the job is to deliver and unload it
at the consumer, all in one job.

Built for multiplayer (host-authoritative with the Multiplayer mod; clients do
not need DLE installed). Works in single player too.

## Status: 0.1

The 0.1 loop: facilities seed with starting stock, hauls are generated from
real stock (debug button for now), the producer is debited, the consumer is
credited on unload and converts by recipe. Stockpiles, live jobs and dispatcher
assignments all survive save and load.

Planned next: an automatic generation tick, dispatcher UI in RemoteDispatch,
dispatcher-gated acceptance (see issue #11), and the finite-car mode where
players load cars themselves (0.5 era).

## How it works

- Recipes: derived per station from its input and output cargo groups, then
  overlaid by `economy.json` (created from `economy.default.json` on first
  run; reload in game from the mod settings). Military yards are excluded.
- Direct Haul jobs: `JobType.ComplexTransport` with vanilla warehouse tasks, so
  the Multiplayer mod syncs them to clients natively. Booklets show the cargo,
  payment (vanilla Transport rates for the distance), and the pickup track.
- Dispatch: a local HTTP API on `127.0.0.1:7246` exposes the economy, the job
  board and assignments for RemoteDispatch integration:
  `GET /api/v1/state`, `GET /api/v1/economy`, `GET /api/v1/jobs`,
  `GET /api/v1/options` (what could be shipped right now),
  `POST /api/v1/hauls` (dispatcher-picked priority haul),
  `POST /api/v1/empties` (spawn empty pool cars, finite mode),
  `GET/POST /api/v1/logistics` and `PUT/DELETE /api/v1/logistics/{id}`
  (unpaid, bookletless coordination runs),
  `PUT/DELETE /api/v1/assignments/{jobId}`, `PUT /api/v1/lock`.
  With the lock enabled, unassigned DLE jobs cannot be accepted.
- Finite cars mode (experimental, off by default): jobs spawn carless and show
  what to bring; players deliver empty pool cars to the producer warehouse and
  the load sequence attaches and loads them, debiting stock at that moment.
  Pool cars never despawn. Warehouse servicing is scoped strictly to DLE jobs.

## Building

DLL references are read from `DLE/lib/`, which is not committed. Copy the
referenced assemblies from your Derail Valley install
(`DerailValley_Data/Managed/` and `DerailValley_Data/Managed/UnityModManager/`)
into `DLE/lib/`, then:

```
dotnet build DLE/DLE.csproj
```

The built `DerailLogisticsEngine.dll` is copied to `build/`.

## Install

Copy a folder named `DerailLogisticsEngine` containing
`DerailLogisticsEngine.dll`, `Info.json` and `economy.default.json` into the
game's `Mods` folder (UnityModManager). Only the multiplayer host needs it.

## Credits

- Persistent Jobs by Banjobeni and contributors (MIT): portions of the car
  lifecycle and job utilities are adapted from it. See THIRD-PARTY-NOTICES.md.
- Direct Haul concept from SelfShunter by Chump_the_Lump, used with permission.
