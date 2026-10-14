# Changelog

All notable changes to the Farmers Market plugin.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.1] — 2026-10-13

### Fixed
- Stallholders are no longer shot by NPCs from KpucTaJl's NpcSpawn (BetterNpc and the
  other plugins built on it). Those NPCs choose targets without asking the game's usual
  hook, so the stallholder guard now answers their own one too.

## [0.1.0] — 2026-10-04

First release. Runs on a live server, where placing a stall at a shop unit and opening its
panel have been checked; trading and the default prices have had little play yet.

### Added
- **Three stalls** — a greengrocer (crops, flowers, mushrooms, cloth), a seed merchant
  (seeds, clones, dung, fertiliser, and farm supplies for sale) and a butcher & dairy
  (eggs, honey, milk, wool, raw meat, leather, fat). Each is a stallholder NPC placed with
  `/market set <stall>`; the spot is stored relative to the monument, so it survives map
  wipes.
- **Shop units** — run `/market set <stall>` at one of the map's rentable shop units and
  the stall takes it over: the game's shopkeeper serves from behind the counter, and using
  the shop opens the stall panel. A market unit can't be rented, broken into or closed for
  rent, costs no scrap, and is handed back by `/market remove <stall>`.
- **Stall panel** — press the use key on a stallholder. The left side lists what you are
  carrying that the stall buys, with the price each and a SELL button per item, plus SELL
  EVERYTHING; the right side lists what the stall sells for scrap.
- **Per-item prices** in the config: what the stall pays, what it charges, and whether it
  trades the item at all. An item that would be bought for as much as it is sold for has
  its buying switched off at load.
- **Quality pays** — clones are worth 25% more per G or Y gene.
- **Falling prices** — a stall pays less for an item it has just bought a lot of, down to
  half the listed price, recovering over a real-time day. Can be turned off.
- **Daily limit** — the stalls pay each player at most 300 scrap per real-time day
  (configurable; 0 removes it).
- **Fractional prices** — produce is priced in fractions of a scrap; the odd fraction from
  a sale is carried to the player's next one.
- **Calls for other plugins** — `SellProduce`, `QuoteProduce`, `GetBuyPrice`,
  `GetSellPrice`, `GetDailyAllowance`, `GetStallPosition`, and the hooks
  `OnFarmersMarketSale` and `OnFarmersMarketPurchase`.
- Stallholders are spawned unsaved, can't be hurt, and are removed on unload.
- `GetHelpInfo` hook for the HelpMenu plugin.
