# Economy

How the economy works in 0.8. Numbers are the defaults in
`economy.default.json`. If this doc and that file disagree, the file is right.

## Getting paid

You're paid when cargo comes off at its destination. The booklet pays nothing
and neither does loading. If the destination only has room for part of the
load, you're paid for that part and the rest stays on the cars.

Each station tracks its stock in two piles, what it produced and what was
delivered to it. The director only writes paid hauls from the produced pile.
A factory uses up the delivered pile first and adds what it makes to
produced. So ore pays on the run to the steel mill, the steel pays again on
the run out, and moving that ore anywhere else pays nothing.

## Production

Mines, forests, the farm and the oil wells produce cargo. The steel mill,
sawmill, goods factory, food factory, machine factory and refinery turn
inputs into other cargo. CW, CS and the power plant (CP) use cargo up.

Sources need machines. Mines need mining trucks and excavators, forests need
forestry trailers, and the farm needs tractors. Oil wells don't need any. A
source missing any of its machines runs at a quarter speed. Each machine type
wears out one unit per 50 carloads produced, and a new world starts every
source with 2 of each. The machine factory builds all of them out of steel
and electronics. If the mines lose their machines, steel slows down, which
slows the machine factory that builds the replacements.

Tools and a few other cargos count as catalysts. At a factory a carload of
tools doubles batch speed. At a source it makes the machines last 7 times
longer. Mines also take crane parts, forests take tractors, the farm takes
ammonium nitrate and the refinery takes methane. One carload lasts 24 game
hours of production and doesn't burn while the site is idle. The goods
factory makes tools, and the harbor imports some.

CW, CS and CP each use about 3 carloads a game day. The two cities leave 1
scrap for every 4 carloads they use, alternating scrap metal and scrap wood
for the steel mill and sawmill.

Everything consumed across the map in the last 24 game hours speeds up all
production, up to 1.5x at 6 carloads.

The harbor imports half a carload for every carload delivered to it. Every
8th import is tools.

## Rates

Everything runs on in-game time. At DV's default day length a game day is
about 2 real hours, and changing the day length changes all of this.

| What | Default |
| --- | --- |
| Source | 3 carloads a game day |
| Factory | 1 batch every 3 game days |
| Factory with tools and full consumption boost | About 1 batch a game day |
| City or power plant | 3 carloads a game day |
| Starting stock | 6 carloads of each output |

`paceFactor` scales production, consumption, imports and catalyst burn
together.

## Storage

Each station has one limit for all its cargo together (`totalCap`). Mines,
forests and oil wells hold 50. The steel mill, sawmill, farm and CW hold 200.
The goods, machine and food factories hold 300, and the harbor 600.
Everything else holds 100. A full station won't take deliveries.

## Booklets

Every 2 minutes the director writes Company Hauls from produced stock, up to
10 open booklets per station (a mod setting). An open booklet only holds its
stock loosely. Taking it checks the stock is still there and locks it in.
If the stock has gone to other hauls by then, the booklet expires instead.
Untaken booklets stay open, even when nobody's at the station.

## Loading

Load or unload at the warehouse machine with the cars on the warehouse track,
or leave them anywhere in the yard and the staff do it at 45 seconds a car.
Every station has staff for both by default, the harbor included. Empty
containers can only go through staff. The warehouse machine won't take them.
