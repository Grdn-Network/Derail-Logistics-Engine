# DLE HTTP API v1

Served by the mod on the multiplayer host (or in single player) at
`http://127.0.0.1:7246`. Localhost only; RemoteDispatch will proxy it for
remote dispatchers. `GET /` serves the built-in dispatch board.

All bodies and responses are JSON. Cargo names are the game's CargoType enum
names; yard ids are station yard ids (SM, HB, FRS, ...).

## Read

### GET /api/v1/state
```json
{ "modVersion": "0.1.7", "lockEnabled": false, "stationCount": 14, "jobCount": 3 }
```

### GET /api/v1/economy
Array per facility:
```json
{
  "yardId": "SM",
  "outputs": ["SteelRolls", "SteelBillets"],
  "inputs": ["IronOre", "Coal"],
  "stock": [ { "cargo": "SteelRolls", "amount": 4.0, "cap": 60.0 } ],
  "recipes": [ { "inputs": [{ "cargo": "IronOre", "amount": 2.0 }],
                 "outputs": [{ "cargo": "SteelRolls", "amount": 2.0 }] } ]
}
```
Only cargoes with stock above zero appear in `stock`.

### GET /api/v1/jobs
Array per live Direct Haul:
```json
{
  "id": "FRS-SW-01", "origin": "FRS", "destination": "SW", "cargo": "Logs",
  "cars": 6, "plannedCars": 6, "awaitingEmpties": false,
  "wage": 6705.0, "pickupTrack": "FRS-B5O",
  "state": "Available", "assignedTo": null
}
```
`awaitingEmpties` is true for finite-mode jobs with no cars attached yet.

### GET /api/v1/options
What could be shipped right now:
```json
{ "origin": "FRS", "cargo": "Logs", "stock": 8.0, "consumers": ["SW"] }
```

### GET /api/v1/fleet?cargo=Logs&yard=FRS
Every freight car in the world with its track and availability. Both query
parameters are optional: `cargo` narrows to the car types that can load that
cargo, `yard` narrows to one station's tracks.
```json
{
  "cargo": "Logs", "total": 12, "usable": 7,
  "cars": [
    { "carId": "FLT-021", "type": "FlatbedStakes", "yard": "FRS",
      "track": "FRS-3-SP", "loadedCargo": null, "jobId": null,
      "reservedBy": null, "playerSpawned": false, "usable": true }
  ]
}
```
`usable` means empty, jobless, not reserved by a haul and not player-spawned.
Cars between stations report `"track": "in motion"`. 400 on an unknown cargo.

### GET /api/v1/history?limit=200
Session telemetry ring buffer (max 600), oldest first:
```json
{ "Utc": "...", "Type": "delivered", "Yard": "SW", "Cargo": "Logs",
  "Amount": 6.0, "JobId": null }
```
Types: `haul_created`, `delivered`, `converted`, `production`.

## Act

### POST /api/v1/hauls
Create a dispatcher-picked haul.
```json
{ "origin": "FRS", "destination": "SW", "cargo": "Logs", "cars": 4 }
```
201 with `{ "ok": true, "jobId": "FRS-SW-02", "finiteMode": false }`,
400 on bad input, 409 when stock or tracks do not allow it (see game log).
Default mode spawns a pre-loaded consist and debits stock; finite mode creates
a carless job and debits stock when empties attach at the warehouse.

### POST /api/v1/empties
Finite-mode fleet management: spawn empty pool cars suited to a cargo.
```json
{ "yardId": "FRS", "cargo": "Logs", "count": 6 }
```
Pool cars are never deleted by the game's unused-car cleanup.

### PUT /api/v1/assignments/{jobId}
```json
{ "player": "Guardian", "assignedBy": "dispatcher" }
```
### DELETE /api/v1/assignments/{jobId}

### PUT /api/v1/lock
```json
{ "enabled": true }
```
While enabled, a Direct Haul with no assignment cannot be accepted at the
job validator (honor system otherwise).

### Logistics runs (unpaid, no booklet; coordination data only)
- `GET /api/v1/logistics` list
- `POST /api/v1/logistics` `{ "from": "OWN", "to": "FRS", "cars": 4, "cargo": "Logs", "note": "stage empties" }`
- `PUT /api/v1/logistics/{id}` `{ "status": "InProgress" }` (Open, InProgress, Done)
- `DELETE /api/v1/logistics/{id}`
