# Derail Logistics Engine

DLE turns Derail Valley multiplayer into a finite logistics operation run by a
dispatcher. Cargo is real: sources produce it slowly, factories convert it by
recipe, stations can only hold so much, and money is only made when real cargo
reaches real demand. Cars are a persistent, finite fleet. When stock runs dry
or empties are in the wrong yard, that is not a bug; that is the job.

Persistent Jobs asks "what if jobs were persistent?" DLE asks "what if the
cargo was real?"

## How it plays

**Crews** see Company Haul jobs: paperwork that says what to bring and where.
Bring suitable empties to the producer, load at the warehouse terminal (or let
station staff do it slowly), haul to the destination, unload. That is the whole
loop: there is no turn-in, the job closes itself the moment the cargo is off
the cars at the destination. The booklet itself pays nothing; the delivery is
what pays, exactly once per production stage: delivered goods relocate payless
until a factory consumes them and makes something new. A full destination
never destroys cargo; the remainder waits aboard until room frees.

**Dispatchers** run the board at `http://127.0.0.1:7246/` on the host, or from
any machine on the network behind a password (mod settings). It opens on a live
map of the economy: every station's recipe, stock and boosters, with arrows for
what is shippable right now; click a route and the create form fills itself.
Create hauls from live stock, assign crews (names autocomplete from the
session), fax booklets (faxing a named crew assigns them the job; every loco
has a fax machine), pick the exact cars staff will load with a distance-sorted
car picker, and watch the dispatch log tick as the world produces, converts,
loads and delivers. With the assignment lock ON, public paper expires, the
generator pauses, and operations run entirely from the board: crews drive,
dispatch decides.

**The economy** breathes on its own: a director generates hauls from real
produced stock, sources run on the production clock (tools delivered to them
act as boosters: any one brand speeds production and slowly wears out),
factories chew their input buffers by recipe, and full consumers stop paying
until they drain. Open paper never freezes stock: holds harden when a booklet
is taken, and stale paper expires instead of lying. Harbor is the big storage
hub; a handful of major stations hold more than the rest.

## Servicing: terminal or staff

Loading and unloading always use the real cargo rules. The choice is the
gameplay:

- **Terminal**: spot the cars on the warehouse track and run the machine
  (in person or remotely from the board). Fast.
- **Station staff**: cars parked anywhere in the yard get worked one car at a
  time on a per-car timer, where the station has staff for it. Slow, but no
  shunting to the loading track.

Times, staff availability, station roles and storage capacities are all
configured per station in `economy.json`.

## Install

Host-only: in multiplayer, only the host installs DLE; jobs sync to clients
through the Multiplayer mod natively. Works in single player too.

1. Install [UnityModManager](https://www.nexusmods.com/site/mods/21).
2. Drop the `DerailLogisticsEngine` folder (from the release zip) into the
   game's `Mods` folder.
3. Optional multiplayer: install the Multiplayer mod; DLE loads after it and
   registers itself host-only.

On first run `economy.default.json` is copied to `economy.json`; edit that and
reload from the mod settings.

## Configuration

`economy.json` controls the world:

| Key | Meaning |
| --- | --- |
| `settings` | World tuning: starting stock, production and generation clocks, pool packing and cap, excluded cargos |
| `defaults` | Baseline for every station: `role`, `remoteLoad`, `remoteUnload`, `remoteSecondsPerCar`, `defaultCap` |
| `stations.<YARD>.source` / `boosters` | Source industries produce on the clock; boosters multiply their rate |
| `stations.<YARD>.recipes` | Input and output carloads for that station's conversion |
| `stations.<YARD>.defaultCap` / `caps` | Storage capacity (also the demand cap) |
| `stations.<YARD>.role` | `load`, `unload`, or `both` |
| `stations.<YARD>.remoteLoad` / `remoteUnload` | Whether station staff can service cars parked off the warehouse track |
| `stations.<YARD>.remoteSecondsPerCar` | Staff time per car |

Default storage tiers: small pickup yards 25, standard stations 50, major
industry (FF, CW, GF) 100, Steel Mill 100, Machine Factory 150, harbor 200.

Mod settings hold the host preferences (public booklet caps, network board and
its password, verbose logging); everything about the world lives in
`economy.json` and hot-reloads. Console recovery commands exist for bad days:
`company.respawn` rebuilds the car pools and clears wreckage, and
`company.resupply` resets stockpiles, input buffers included.

## API

Everything the board does is a local HTTP API (`docs/API.md`), built for
RemoteDispatch integration: state, economy, jobs, options, hauls, assignments,
lock, take, complete, load, unload, fax, empties, logistics runs.

## Status

Pre-release. The 0.2 and 0.3 lines are feature-complete for the core loop and
are being hardened for a community beta; expect sharp edges. Report issues
with the UMM log and steps to reproduce.

Planned beyond the beta: contracts (paid car loans to production chains,
routes with return legs), an AI dispatcher with autonomy tiers that humans
always outrank, express hauls, and in-game economy visibility.

## Building from source

DLL references load from `DLE/lib/` (not committed): copy the referenced
assemblies from `DerailValley_Data/Managed/` and
`DerailValley_Data/Managed/UnityModManager/`, then `dotnet build
DLE/DLE.csproj`. The built DLL lands in `build/`.

## Support

DLE is free and always will be. If it made your sessions better and you feel
like fueling the next wave: [ko-fi.com/grdn23](https://ko-fi.com/grdn23).

## Credits

- Direct Haul mechanics and booklet rendering adapted from SelfShunter by
  Chump_the_Lump, used with permission.
- Portions of the car lifecycle and job utilities adapted from Persistent Jobs
  by Banjobeni and contributors (MIT). See THIRD-PARTY-NOTICES.md.
- Staff loading pacing inspired by Immersive Cargo Loading by t0stiman
  (behavior reimplemented, no code used).
