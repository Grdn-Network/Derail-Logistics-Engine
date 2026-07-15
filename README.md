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
station staff do it slowly), haul to the destination, unload, turn in. The
booklet itself pays nothing; the delivery is what pays, and only for cargo the
destination actually has room to accept.

**Dispatchers** run the board at `http://127.0.0.1:7246/` on the host: create
hauls from live stock, assign crews, take and turn in jobs remotely, fax
booklets to specific players (every loco has a fax machine), and trigger
loading or unloading through the station's own terminal or its staff. With the
assignment lock ON, Company Haul paperwork disappears from station offices and
operations run entirely from the board: crews drive, dispatch decides.

**The economy** breathes on its own: a director generates hauls from real
stockpiles, production ticks at the sources, deliveries convert inputs into
outputs at factories, and full consumers stop paying until they drain. Harbor
is the big storage hub; a handful of major stations hold more than the rest.

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
| `defaults` | Baseline for every station: `role`, `remoteLoad`, `remoteUnload`, `remoteSecondsPerCar`, `defaultCap` |
| `stations.<YARD>.recipes` | Input and output carloads for that station's conversion |
| `stations.<YARD>.defaultCap` / `caps` | Storage capacity (also the demand cap) |
| `stations.<YARD>.role` | `load`, `unload`, or `both` |
| `stations.<YARD>.remoteLoad` / `remoteUnload` | Whether station staff can service cars parked off the warehouse track |
| `stations.<YARD>.remoteSecondsPerCar` | Staff time per car |

Default storage tiers: small pickup yards 25, standard stations 50, major
industry (FF, CW, GF, SM) 100, harbor 200.

Mod settings add the generation knobs (starting stock, haul sizes, tick rate,
hard cap on pool cars) and console recovery commands exist for bad days:
`company.respawn` rebuilds the car pools and clears wreckage, and
`company.resupply` resets stockpiles.

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

## Credits

- Direct Haul mechanics and booklet rendering adapted from SelfShunter by
  Chump_the_Lump, used with permission.
- Portions of the car lifecycle and job utilities adapted from Persistent Jobs
  by Banjobeni and contributors (MIT). See THIRD-PARTY-NOTICES.md.
- Staff loading pacing inspired by Immersive Cargo Loading by t0stiman
  (behavior reimplemented, no code used).
