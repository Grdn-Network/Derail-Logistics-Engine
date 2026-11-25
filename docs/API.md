# DLE HTTP API

The host serves the board and this API at `http://127.0.0.1:7246`. The port
is a mod setting. Requests and responses are JSON. Cargo names are the game's
`CargoType` names, and yards use station ids like `SM`, `HB` and `FRS`.

## Access

With no board password set, the API only listens on the host PC. Set a
password and it listens on the network too. Requests from another machine
then need the password, sent as an `X-DLE-Key` header or a `key` query
parameter. Browser requests coming from another site need it as well.

The board is read-only by default. Anything other than a GET gets a 403
unless Board controls is turned on in the mod settings.

## Reading

| Route | Returns |
| --- | --- |
| `GET /api/v1/state` | Mod version, lock and controls state, game time, station and job counts, dormant car count |
| `GET /api/v1/economy` | Every station's inputs, outputs, stock, caps and recipes |
| `GET /api/v1/jobs` | Every live Company Haul with its cars, pay, pickup track, state and crew |
| `GET /api/v1/jobs/{id}/cars` | The cars on one haul |
| `GET /api/v1/jobs/{id}/candidates` | Empties that suit a carless haul, nearest to the loading track first |
| `GET /api/v1/options` | What can be shipped right now, and where to |
| `GET /api/v1/fleet` | Every freight car, its track, and whether it's free to use. Optional `cargo` and `yard` filters. |
| `GET /api/v1/history` | This session's events: hauls created, deliveries, conversions, production. Optional `limit`. |
| `GET /api/v1/players` | Players in the session |
| `GET /api/v1/logistics` | Logistics runs |
| `GET /api/v1/trackmap` | The railway's geometry, stations and switch legs |
| `GET /api/v1/traffic` | Where every car is on the railway |
| `GET /api/v1/junctions` | Every switch and which way it's set |
| `GET /api/v1/signals` | Signal states. `available` is false without DV Signals. |

## Changing things

These only work with Board controls on.

| Route | Does |
| --- | --- |
| `POST /api/v1/hauls` | Creates a haul from `origin`, `destination`, `cargo` and `cars` |
| `DELETE /api/v1/jobs/{id}` | Deletes an open haul and puts its stock back |
| `POST /api/v1/jobs/{id}/take` | Takes a haul for a crew |
| `POST /api/v1/jobs/{id}/complete` | Completes a haul from the board |
| `POST /api/v1/jobs/{id}/load` | Runs the loading machine. The body can name the exact cars. |
| `POST /api/v1/jobs/{id}/unload` | Runs the unloading machine |
| `POST /api/v1/jobs/{id}/return` | Cancels a haul that's back at its origin and returns its stock |
| `POST /api/v1/jobs/{id}/fax` | Faxes the booklet to a player |
| `PUT`, `DELETE /api/v1/assignments/{id}` | Sets or clears a haul's crew |
| `PUT /api/v1/lock` | Turns the assignment lock on or off |
| `POST /api/v1/empties` | Spawns empty pool cars for a cargo at a yard |
| `POST /api/v1/logistics`, `PUT`/`DELETE /api/v1/logistics/{id}` | Creates, updates or removes a logistics run |

## Desk routes

The dispatch desk code ships in the mod, but the board doesn't use it yet.
These routes still answer.

| Route | Returns or does |
| --- | --- |
| `GET /api/v1/yard`, `/yard/geometry` | A yard's tracks with cars in order, and its real rail geometry |
| `GET /api/v1/orders` | Factories short of their next batch, and who could supply them |
| `GET /api/v1/crews` | Crews with their loco and jobs |
| `GET /api/v1/estimate` | Pay, weight and length for a haul before it's made |
| `GET /api/v1/inbox`, `POST /api/v1/inbox/ack` | Reply faxes, and marking them read |
| `GET`, `POST /api/v1/packets`, `DELETE /api/v1/packets/{id}` | Draft runs of booklets |
| `PUT /api/v1/jobs/{id}/note`, `/label` | A note printed on the booklet, or a board-only label |
| `POST /api/v1/jobs/{id}/sign-request`, `/amend` | Asks the consignee to sign the paper, or unsigns it to edit |
| `POST /api/v1/wake` | Wakes a yard's dormant cars |

## How stock is counted

See [design/economy.md](design/economy.md).
