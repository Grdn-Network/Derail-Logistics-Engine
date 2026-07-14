# DLE Servicing and Economy Model (targeting 0.2)

Status: planning, agreed with owner. Locked before implementation.

## Goal

Make cargo servicing the core gameplay loop and the economy tamper-proof. Booklets stop being the point; delivering cargo to real demand is. Players choose between doing it properly (drive to the terminal) or paying a time cost to let station staff handle it.

## Core principles

1. Booklets are faux: directional only, they never pay. They give a road crew a task and a destination.
2. Money is minted only on a delivery unload that meets real demand. Loading and storage never pay.
3. All jobs are synthetic and economy-driven (already true).
4. Scarcity stays sacred: no cars or stock from nothing, and demand is finite (storage capacity).

## Servicing: two paths, one machine

Both paths use the game's warehouse cargo machine (real load/unload logic and animation). The only difference is speed.

- **Terminal service:** cars spotted on the station's actual load/unload track. Normal speed. The "do it properly" path.
- **Station staff (remote):** dispatch services cars parked anywhere in the yard. Slower, a per-car time penalty, representing staff walking out to the cars. The "leave it to them" path.
- A station can lack staff for load, unload, or both (see config). Harbor has no unload staff: you must bring cars to the quay and use the terminal, because unloading means craning onto a boat.

Lore framing: remote unload is dispatch telling staff "cargo arrived on track X, go get it." Remote load is dispatch telling staff "we are ready, load these cars on track X." Terminal is the crew pulling up and helping.

## Per-station config (economy.json)

```json
{
  "defaults": {
    "role": "both",
    "remoteLoad": true,
    "remoteUnload": true,
    "remoteSecondsPerCar": 45,
    "storageCapacityPerCargo": 24
  },
  "stations": {
    "HB":  { "role": "unload", "remoteUnload": false },
    "CSW": { "remoteSecondsPerCar": 30 }
  }
}
```

- `role`: `load` | `unload` | `both`. Also tells the director which stations are sources vs sinks.
- `remoteLoad` / `remoteUnload`: does the station have staff to service parked cars remotely. If false, only the terminal path works for that action.
- `remoteSecondsPerCar`: staff time penalty per car. The terminal path ignores it.
- `storageCapacityPerCargo`: how much of a cargo the station will hold and accept (see capacity below).

Anything omitted uses `defaults`.

## Payment and the anti-scam gate

Three cases, one pays:

1. **Load** (anywhere allowed): never pays. Moves cargo from the station reserve into the cars (debits the reserve).
2. **Delivery unload** at a station that demands the cargo and is not at capacity: pays the wallet, consumes the cargo (demand satisfied).
3. **Storage unload** anywhere else with permission: no pay, the cargo drops into that station's reserve, the car is freed.

Loading debits the reserve and storage-unload credits it, so shuffling nets to zero. Delivered cargo is consumed, so it cannot be re-sold. The only income is meeting real end-demand.

Guardrail: suppress the vanilla job payout; pay only at the gated delivery unload. One payout point, no double-dip.

MP: payment goes to the shared wallet (current DVMP has a shared wallet only). Per-crew attribution is deferred.

## Storage capacity and demand caps (0.2 / 0.3)

Every station holds a finite amount of each cargo (`storageCapacityPerCargo`).

- A consumer pays and accepts delivery only while it has room. Once full it stops accepting that cargo (no pay). This kills the source-to-consumer grind: you cannot deliver the same cargo forever.
- Storage unloads also respect capacity; a full yard refuses more.
- Gives harbor and hubs a real job: hold and buffer cargo for redistribution.

## Faux booklets and fax (#33)

- Company Haul booklets are faux: purpose and direction, no pay.
- Dispatch can fax a faux booklet to a specific player to send them somewhere with a real in-hand task (#33). Also a booklet path around the office-print bug (item 4 of #40).

## Optional reward premium (future, needs justification)

Rewards may scale up for long hauls or specific contracts, justified in lore (a logistics contract pays a premium), not a raw distance multiplier with no story. Deferred until we have contracts.

## Deferred to 1.0

- **Express hauls:** staff pre-load committed cargo that cannot be dispatch-unloaded en route, tying up cars until delivered. A scarcity lever and a way for a company to guarantee a shipment moves.
- **Paid storage contracts:** buy the right to store at a station; smaller stations have less room.
- **Physical named storage**, if we ever want loaded cars to persist as storage rather than the stock ledger standing in for it.

## Build order for 0.2

1. Per-station config: schema, parsing, defaults, apply roles to the director (sources/sinks).
2. Servicing: load/unload through the warehouse machine at both speeds, gated by config (terminal vs staff, per-station roles/staff). Board buttons and API (#43).
3. Payment rework: suppress vanilla payout, pay only on gated delivery unload into the shared wallet; storage-unload and load do not pay.
4. Storage capacity: cap demand and storage; consumers stop paying and accepting when full.
5. Faux booklets: booklets never pay; fax path (#33) to hand out direction.
