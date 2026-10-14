# Farmers Market

*Grow it, bring it to market, get paid.*

An [Oxide/uMod](https://umod.org/) plugin for **Rust**. Stallholder NPCs buy what a farm
produces (crops, clones, eggs, honey, milk, wool and meat) for scrap, and sell the seeds
and supplies to grow more. Farming becomes a trade: grow it, bring it to market, get paid.

It is meant as a steady income rather than a fast one: slower per hour than a monument
run, but safe and repeatable. A daily limit per player and prices that dip when a stall
is flooded with one item keep it that way.

> **Status: 0.1.1.** It runs on a live server, where placing stalls and
> opening their panels have been checked. Trading has had little play so far: expect to
> tune the default prices, and please report anything odd.

<!-- lpl:links -->
**[Download v0.1.1](https://github.com/lowpoplabs/FarmersMarket/releases/latest)** · **Flyer:** [web](https://lowpoplabs.github.io/flyers/FarmersMarket.html) / [PDF](FarmersMarket-Flyer.pdf) · **[Changelog](CHANGELOG.md)** · **[Ko-fi](https://ko-fi.com/lowpoplabs)**
<!-- /lpl:links -->

## How to use (in-game)

1. Bring your produce to the market. `/market` tells you where the stalls are.
2. Walk up to a stallholder and press the **use** key.
3. On the left is everything you are carrying that the stall buys. **SELL** sells that
   item; **SELL EVERYTHING** sells the lot. The scrap goes straight into your inventory.
4. On the right is what the stall sells. **×1** and **×10** buy with the scrap you carry.

Things worth knowing:

- **Good genes pay.** The seed merchant pays 25% more for a clone for every G or Y gene it
  carries, so a GGGYYY clone is worth two and a half times a plain one.
- **Variety pays.** A stall that has just bought a lot of one item pays less for more of
  it (down to half). The price recovers over the day.
- **There is a daily limit.** The stalls pay each player up to 300 scrap per real-time
  day. The panel shows how much is left.
- Prices are in fractions of a scrap. Whatever doesn't make a whole scrap is carried to
  your next sale, not lost.

## Installation

1. Copy `FarmersMarket.cs` into your server's `oxide/plugins/` (or `carbon/plugins/`) folder.
2. The plugin compiles and loads automatically and writes `oxide/config/FarmersMarket.json`.
3. Place each stall with `/market set greengrocer`, `/market set seeds` and
   `/market set butcher`. Where you stand decides what kind of stall it is:
   - **At a rentable shop unit** (within 8 m of one): the stall takes the unit over. The
     game's own shopkeeper appears behind the counter, and using the shop opens the stall
     panel instead of a vending screen.
   - **Anywhere else**, or with `npc` added to the command: a free-standing stallholder
     stands where you are standing, facing the way you face.

A spot inside a monument is stored relative to it, so it survives map wipes. The stalls
were designed for the Apartment Complex, inside its safe zone, but any spot works. A spot
outside a monument is a fixed world position and will not survive a map wipe.

### Shop units

A unit the market has taken over:

- can't be rented, taken over or broken into by players, and never closes for unpaid rent;
- costs nothing: no scrap is taken from anyone to open it;
- belongs to no player. Its sign can be painted by anyone with `farmersmarket.admin`;
- is handed back, empty and rentable, by `/market remove <stall>`.

The unit stays open in the world when the plugin unloads, so its sign survives reloads
and restarts, and the plugin picks it up again on load. While the plugin is not loaded the
game treats it as an ordinary shop with unpaid rent and closes it within the hour; the
plugin reopens it on the next load, with a blank sign. Set
`Shop units: close them when the plugin unloads` to `true` to hand the units back on every
unload instead.

A unit that a player is renting can't be taken over: pick an empty one. The stallholder
outfit and face seed in the config apply to free-standing stallholders only.

## Commands

| Command | Who | What it does |
| --- | --- | --- |
| `/market` | everyone | Where the stalls are and how much they will still pay you today |
| `/market set <stall>` | admin | Place the stall: takes over the shop unit you are standing at, or stands a stallholder where you are |
| `/market set <stall> npc` | admin | Always a free-standing stallholder, even next to a shop unit |
| `/market remove <stall>` | admin | Take the stall away, hand back its shop unit and clear the spot |
| `/market list` | admin | The stalls and where each one stands |
| `/market reset <player>` | admin | Clear a player's daily limit |

`<stall>` is the stall's key from the config: `greengrocer`, `seeds` or `butcher` by default.

## Permissions

- `farmersmarket.admin` — the admin subcommands above.
- `farmersmarket.use` — trading at the stalls, only checked when
  `Require permission to trade` is on.

```bash
oxide.grant group admin farmersmarket.admin
```

## Configuration

`oxide/config/FarmersMarket.json` (the goods lists are shortened here):

```json
{
  "Most scrap the stalls pay one player per real-time day (0 = no limit)": 300,
  "Prices fall as a stall buys more of one item": true,
  "Falling prices: scrap paid for one item at which its price bottoms out": 150.0,
  "Falling prices: lowest price, as a percentage of the listed price": 50.0,
  "Falling prices: hours for a bottomed-out price to recover fully": 24.0,
  "Clones: extra paid per good gene (G or Y), as a percentage of the listed price": 25.0,
  "Require permission to trade (farmersmarket.use)": false,
  "Distance a player can talk to a stallholder from (meters)": 3.0,
  "Stallholders turn to face players within this range (meters, 0 = disabled)": 5.0,
  "Shop units: close them when the plugin unloads (the unit's painted sign is wiped each time)": false,
  "Stallholder NPC prefab path": "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cinematic.prefab",
  "Stallholder NPC fallback prefab path": "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roamtethered.prefab",
  "Stalls": [
    {
      "Stall key (used with /market set)": "greengrocer",
      "Stallholder name": "Greengrocer",
      "Stallholder outfit (item shortnames, optional @skinId)": ["hat.boonie", "burlap.shirt", "burlap.trousers", "shoes.boots"],
      "Stallholder face seed (steam-id-style number picks a fixed face; 0 = random each spawn)": 0,
      "Monument (empty = a fixed world position)": "",
      "Position (relative to the monument; empty = not placed, set with /market set)": "",
      "Facing (degrees, relative to the monument)": 0.0,
      "Uses a rentable shop unit at that position (set by /market set when you stand at one)": false,
      "Goods": [
        {
          "Item shortname": "corn",
          "Stall pays, scrap each (0 = does not buy it)": 0.5,
          "Stall charges, scrap each (0 = does not sell it)": 0.0,
          "Traded": true
        }
      ]
    }
  ]
}
```

Notes:

- **Goods** — one entry per item. Set what the stall pays to buy it, what it charges to
  sell it, or both. `Traded: false` takes the item off the stall without deleting the
  entry. Unknown shortnames are skipped with a warning in the console.
- **No loops** — if a stall both buys and sells an item, the selling price must be above
  the most it would ever pay (for clones, that includes the full gene bonus). Otherwise
  buying that item is switched off at load, with a warning.
- **Stalls** — add, remove or rename stalls freely; the key is what `/market set` takes.
- **Falling prices** — every scrap a stall pays for an item pushes that item's price
  down, reaching the floor once the configured amount has been paid; it climbs back at a
  steady rate. The glut is shared by all players. Turn it off for fixed prices.
- **The daily limit** counts the value of what a player sold, from their first sale of
  the day, for 24 real hours.
- Bought produce is destroyed, and what the stalls sell is unlimited.

### Default prices

| Stall | Buys (scrap each) | Sells (scrap each) |
| --- | --- | --- |
| Greengrocer | corn, pumpkin, sunflower, rose 0.5 · orchid 0.6 · potato, wheat 0.4 · berries, mushroom 0.3 · cloth 0.05 | — |
| Seed Merchant | clones 0.3 (plus gene bonus) · seeds 0.1 · dung 0.5 · fertiliser 0.1 | seeds 2 to 4 · fertiliser 1 · planters 30 to 60 · plant pot 10 · sprinkler 25 · hose tool 40 · water barrel 60 · water jug 15 · bucket 10 · composter 75 · chicken coop 120 · beehive 150 · beehive nucleus 60 · hitch & trough 100 |
| Butcher & Dairy | egg 1 · honey 6 · honeycomb 2 · milk 3 · skimmed milk 2 · cream 4 · wool 0.3 · raw chicken, pork, deer, horse, snake 1 · beef, mutton, bear, wolf 1.5 · big cat, crocodile 2 · leather 0.2 · animal fat 0.1 | — |

## For plugin developers

Calls (`FarmersMarket.Call("Name", ...)`):

| Call | Returns |
| --- | --- |
| `SellProduce(ulong ownerId, ItemContainer from)` | `int` — sells everything in the container that any stall buys, on that player's behalf and under their daily limit. The goods are destroyed; the return value is the scrap owed, which the caller hands over itself. |
| `QuoteProduce(ulong ownerId, ItemContainer from)` | `double` — what `SellProduce` would pay right now; nothing changes. |
| `GetBuyPrice(string shortname)` | `double` — what a stall pays per unit right now, or `null` if no stall buys it. |
| `GetSellPrice(string shortname)` | `double` — what a stall charges per unit, or `null` if no stall sells it. |
| `GetDailyAllowance(ulong userId)` | `double` — scrap the stalls will still pay that player today; `-1` for no limit. |
| `GetStallPosition(string key)` | `Vector3` — where that stallholder stands, or `null` if not placed. |

Hooks:

```csharp
// After each item sold. player is null when another plugin sold for an offline owner.
void OnFarmersMarketSale(BasePlayer player, string shortname, int amount, int scrap)

// After each purchase from a stall.
void OnFarmersMarketPurchase(BasePlayer player, string shortname, int amount, int scrap)
```

## Support

Provided as-is. Bug reports welcome via GitHub Issues. No Discord, no custom work, no promises on turnaround. If it saved you time or you and your players enjoy it:

[![Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/lowpoplabs)

## License

[MIT](LICENSE)
