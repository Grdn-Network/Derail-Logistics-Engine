# Derail Logistics Engine

A Derail Valley mod that adds a cargo economy. Mines, farms and other sources
produce cargo over time, factories turn their inputs into other goods, and
every station has a storage limit. You get paid when cargo is delivered to a
station that uses it.

Cars are persistent and don't respawn on their own, so getting empties to the
right station is part of the work.

Works in single player and multiplayer.

## Features

- Company Haul jobs, generated from cargo that's actually in stock
- Production, recipes and storage limits for every station, set in `economy.json`
- Load at the warehouse machine, or park cars anywhere in the yard and let
  station staff load them (slower)
- Sources need their equipment delivered to run at full speed, and it wears
  out. Delivered tools make it last longer, and make factories work faster.
- Cities and the power plant use up cargo over time, so demand keeps coming
- The harbor imports goods based on how much you export through it
- Jobs close when the cargo is unloaded, with no turn-in
- If the destination is full, the leftover cargo stays on the cars until
  there's room
- Jobs nobody takes expire after a while
- A dispatch board in the browser with jobs, stock, crews and a live map of
  the railway
- Optional DV Signals support, which adds signals to the map

## How a job works

1. Take a Company Haul booklet at the station office.
2. Bring empty cars of the right type to the pickup station.
3. Load them at the warehouse machine, or park them in the yard for the staff.
4. Haul them to the destination and unload.

You're paid when the cargo comes off at the destination. Whoever took the
booklet shows on the board as the crew.

## Installation

Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21), then
extract the `DerailLogisticsEngine` folder from the release zip into your
Derail Valley `Mods` folder. In multiplayer, everyone in the session needs
DLE installed.

## Dispatch board

While in game, open `http://127.0.0.1:7246/` on the host PC. To open it from
another PC on your network, set a board password in the mod settings. You can
change the port there too.

## Configuration

Station and economy settings are in `economy.json` in the mod folder. It's
created from `economy.default.json` on first run. After editing, reload it
from the mod settings.

`settings` holds the world tuning, like production speed, overall pace,
equipment wear and harbor imports. `defaults` holds the values every station
starts with. Each station can override them under `stations.<YARD>`:

| Key | Controls |
| --- | --- |
| `totalCap` | How much the station can store, all cargo combined |
| `role` | `load`, `unload` or `both` |
| `remoteLoad`, `remoteUnload`, `remoteSecondsPerCar` | Staff loading away from the warehouse track, and seconds per car |
| `source` | Produces cargo over time |
| `machines` | Equipment the station needs to produce at full speed |
| `catalysts` | Cargo that slows equipment wear, or speeds up a factory |
| `recipes` | A factory's inputs and outputs, replacing the built-in ones |
| `consumesStock`, `emitsScrap` | Uses up cargo over time, and whether that leaves scrap |
| `importHub` | Imports goods based on the exports it receives |
| `routes` | Extra destinations for a cargo, added to the vanilla ones |

The mod settings also have the open booklet limit, the board password and
port, and verbose logging.

Console commands:

- `company.respawn` rebuilds the car pools and clears wrecks
- `company.resupply` resets all station stock

## API

The board runs on a local HTTP API. See [docs/API.md](docs/API.md).

## Bugs

DLE is in beta. Open an
[issue](https://github.com/Grdn-Network/Derail-Logistics-Engine/issues) with
your UMM log and what you were doing when it happened.

## Building

Copy the assemblies DLE references from `DerailValley_Data/Managed/` and
`DerailValley_Data/Managed/UnityModManager/` into `DLE/lib/`, then run
`dotnet build DLE/DLE.csproj`. The DLL ends up in `build/`.

## Support

If you want to support development: [ko-fi.com/grdn23](https://ko-fi.com/grdn23)

## Credits

- Direct Haul mechanics and booklet rendering adapted from SelfShunter by
  Chump_the_Lump, used with permission.
- Portions of the car lifecycle and job utilities adapted from Persistent Jobs
  by Banjobeni and contributors (MIT). See THIRD-PARTY-NOTICES.md.
- Staff loading pacing inspired by Immersive Cargo Loading by t0stiman
  (behavior reimplemented, no code used).
