# Derail Logistics Engine

A Derail Valley mod that gives every freight station a working supply chain:
recipes, stockpiles, and demand. Jobs are generated from real deficits and
surpluses as Direct Haul jobs (load at origin, haul, unload at destination,
all in one job), and a dispatcher can monitor and assign trains through
RemoteDispatch.

Built for multiplayer (host-authoritative with the Multiplayer mod); works in
single player too.

Under heavy development toward 0.1. See the issues and the DLE 0.1 milestone
for progress.

## Building

DLL references are read from `DLE/lib/`, which is not committed. Copy the
referenced assemblies from your Derail Valley install
(`DerailValley_Data/Managed/` and `DerailValley_Data/Managed/UnityModManager/`)
into `DLE/lib/`, then:

```
dotnet build DLE/DLE.csproj
```

The built `DerailLogisticsEngine.dll` is copied to `build/`.

## Credits

- Persistent Jobs by Banjobeni and contributors (MIT): portions of the car
  lifecycle and job utilities are adapted from it. See THIRD-PARTY-NOTICES.md.
- Direct Haul concept from SelfShunter by Chump_the_Lump, used with permission.
