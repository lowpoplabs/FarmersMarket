using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Farmers Market", "LowPopLabs", "0.1.0")]
    [Description("Stallholder NPCs who buy what a farm produces (crops, clones, eggs, honey, milk, meat) for scrap and sell seeds and farm supplies. Clones with good genes fetch more, prices dip as a stall fills up on one item, and a daily limit keeps the income steady.")]
    public class FarmersMarket : RustPlugin
    {
        private const string PermAdmin = "farmersmarket.admin";
        private const string PermUse = "farmersmarket.use";
        private const string ScrapShortname = "scrap";
        private const string UiPanelName = "FarmersMarket.Panel";
        private const double DaySeconds = 86400;
        private const int RowsPerPage = 11;

        private const string NpcPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cinematic.prefab";
        private const string NpcPrefabFallback = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roamtethered.prefab";

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = "<color=#8bc34a>Market</color>: ",
                ["NoPermission"] = "You don't have permission to do that.",

                ["Info.None"] = "The market has no stalls yet.",
                ["Info.Stalls"] = "The stalls buy farm produce for scrap and sell seeds and supplies. Walk up to a stallholder and press the use key.",
                ["Info.StallLine"] = "  {0} — {1}",
                ["Info.Allowance"] = "The stalls will pay you up to {0} scrap more today (resets in {1}).",
                ["Info.AllowanceFull"] = "The stalls will pay you up to {0} scrap a day.",

                ["Sell.Nothing"] = "You are not carrying anything this stall buys.",
                ["Sell.CapReached"] = "The stalls have paid you all they will today. Come back in {0}.",
                ["Sell.One"] = "Sold {0} {1} for {2} scrap.",
                ["Sell.Many"] = "Sold {0} item(s) of {1} kind(s) for {2} scrap.",
                ["Sell.Carried"] = "{0} scrap paid out; the odd {1} is carried to your next sale.",
                ["Sell.CapPartial"] = "That reached today's limit; the rest was not bought.",

                ["Buy.NeedScrap"] = "You need {0} scrap for that.",
                ["Buy.Done"] = "Bought {0} {1} for {2} scrap.",

                ["Ui.Subtitle"] = "Sell your produce for scrap, or buy what the farm needs.",
                ["Ui.SubtitleCap"] = "The stalls will pay you {0} scrap more today.",
                ["Ui.SubtitleCapOut"] = "The stalls have paid you all they will today. Back in {0}.",
                ["Ui.BuysHeader"] = "THE STALL BUYS",
                ["Ui.SellsHeader"] = "FOR SALE",
                ["Ui.Each"] = "{0} each",
                ["Ui.EachLow"] = "{0} each (glut)",
                ["Ui.Sell"] = "SELL",
                ["Ui.SellAll"] = "SELL EVERYTHING — {0} scrap",
                ["Ui.NothingHeld"] = "You are carrying nothing this stall buys.",
                ["Ui.WeBuy"] = "It buys: {0}",
                ["Ui.NothingSold"] = "This stall has nothing for sale.",
                ["Ui.Change"] = "Fractions of a scrap are carried to your next sale. Carried now: {0}",
                ["Ui.Scrap"] = "Your scrap: {0}",

                ["Stall.NotForRent"] = "This unit belongs to the market and can't be rented.",

                ["Admin.Usage"] = "/market set <stall> [npc] · remove <stall> · list · reset <player>",
                ["Admin.SetUnit"] = "{0} has taken over shop unit {1}. Stand somewhere else, or add 'npc' to the command, for a free-standing stallholder.",
                ["Admin.UnitRented"] = "Shop unit {0} is rented by a player. Pick an empty unit, or add 'npc' to the command for a free-standing stallholder.",
                ["Admin.UnitTaken"] = "Shop unit {0} is already the {1} stall.",
                ["Admin.UnitFailed"] = "{0} could not open shop unit {1}; see the server console.",
                ["Admin.NoStall"] = "No stall called '{0}'. Stalls: {1}",
                ["Admin.Set"] = "{0} now stands here, anchored to {1}.",
                ["Admin.SetWorld"] = "{0} now stands here. This is not inside a monument, so the spot will not survive a map wipe.",
                ["Admin.Removed"] = "{0} removed.",
                ["Admin.ListLine"] = "  {0} ({1}) — {2}",
                ["Admin.NotPlaced"] = "not placed",
                ["Admin.NoPlayer"] = "No player found for '{0}'.",
                ["Admin.Reset"] = "{0} can sell to the stalls again today.",

                ["Help.Title"] = "Farmers Market",
                ["Help.Description"] = "Stallholders who buy what your farm produces for scrap, and sell the seeds and supplies to grow more.",
                ["Help.HowTo"] = "Bring crops, clones, eggs, honey, milk or meat to the market stalls.\nWalk up to a stallholder and press the use key to open the stall.\nSELL takes that item from your inventory and pays scrap; the right-hand side sells seeds and farm supplies.\nClones with more G and Y genes are worth more to the seed merchant.",
                ["Help.Notes"] = "A stall pays less for an item it has just bought a lot of; the price recovers over the day.\nThe stalls only pay each player so much scrap a day.",
                ["Help.Cmd.Info"] = "Where the stalls are and how much they will still pay you today",
                ["Help.Cmd.Admin"] = "Admin: place, remove and list stallholders; reset a player's daily limit",
            }, this);
        }

        private string Msg(string key, string userId, params object[] args)
        {
            string text = lang.GetMessage(key, this, userId);
            return args != null && args.Length > 0 ? string.Format(text, args) : text;
        }

        private void Reply(BasePlayer player, string key, params object[] args) =>
            SendReply(player, Msg("Prefix", player.UserIDString) + Msg(key, player.UserIDString, args));

        // Unprefixed reply (admin command feedback, follow-on lines).
        private void ReplyRaw(BasePlayer player, string key, params object[] args) =>
            SendReply(player, Msg(key, player.UserIDString, args));

        private static string FormatSpan(double seconds)
        {
            if (seconds < 60) return "1m";
            int minutes = (int)(seconds / 60);
            int hours = minutes / 60, mins = minutes % 60;
            if (hours > 0) return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
            return $"{mins}m";
        }

        // Scrap amounts are fractional per unit; show at most two decimals.
        private static string FormatScrap(double scrap) =>
            scrap.ToString(scrap < 10 ? "0.##" : "0.#", System.Globalization.CultureInfo.InvariantCulture);

        #endregion

        #region Configuration

        private Configuration config;
        private bool configBroken;

        private class Goods
        {
            [JsonProperty("Item shortname")]
            public string Shortname = "";

            [JsonProperty("Stall pays, scrap each (0 = does not buy it)")]
            public float Pays;

            [JsonProperty("Stall charges, scrap each (0 = does not sell it)")]
            public float Charges;

            [JsonProperty("Traded")]
            public bool Traded = true;

            [JsonIgnore] public ItemDefinition Def;
            [JsonIgnore] public bool Buys;
            [JsonIgnore] public bool Sells;

            public Goods() { }

            public Goods(string shortname, float pays, float charges)
            {
                Shortname = shortname;
                Pays = pays;
                Charges = charges;
            }
        }

        private class Stall
        {
            [JsonProperty("Stall key (used with /market set)")]
            public string Key = "";

            [JsonProperty("Stallholder name")]
            public string Name = "";

            [JsonProperty("Stallholder outfit (item shortnames, optional @skinId)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Outfit = new List<string>();

            [JsonProperty("Stallholder face seed (steam-id-style number picks a fixed face; 0 = random each spawn)")]
            public ulong FaceSeed;

            [JsonProperty("Monument (empty = a fixed world position)")]
            public string Monument = "";

            [JsonProperty("Position (relative to the monument; empty = not placed, set with /market set)")]
            public string Position = "";

            [JsonProperty("Facing (degrees, relative to the monument)")]
            public float Facing;

            [JsonProperty("Uses a rentable shop unit at that position (set by /market set when you stand at one)")]
            public bool ShopUnit;

            [JsonProperty("Goods", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Goods> Goods = new List<Goods>();

            [JsonIgnore] public Dictionary<string, Goods> ByShortname = new Dictionary<string, Goods>();
        }

        private class Configuration
        {
            [JsonProperty("Most scrap the stalls pay one player per real-time day (0 = no limit)")]
            public int DailyCap = 300;

            [JsonProperty("Prices fall as a stall buys more of one item")]
            public bool FallingPrices = true;

            [JsonProperty("Falling prices: scrap paid for one item at which its price bottoms out")]
            public float GlutScrap = 150f;

            [JsonProperty("Falling prices: lowest price, as a percentage of the listed price")]
            public float FloorPercent = 50f;

            [JsonProperty("Falling prices: hours for a bottomed-out price to recover fully")]
            public float RecoveryHours = 24f;

            [JsonProperty("Clones: extra paid per good gene (G or Y), as a percentage of the listed price")]
            public float GeneBonusPercent = 25f;

            [JsonProperty("Require permission to trade (farmersmarket.use)")]
            public bool RequirePermission = false;

            [JsonProperty("Distance a player can talk to a stallholder from (meters)")]
            public float InteractRange = 3f;

            [JsonProperty("Stallholders turn to face players within this range (meters, 0 = disabled)")]
            public float FaceRange = 5f;

            [JsonProperty("Shop units: close them when the plugin unloads (the unit's painted sign is wiped each time)")]
            public bool CloseShopsOnUnload = false;

            [JsonProperty("Stallholder NPC prefab path")]
            public string NpcPrefab = FarmersMarket.NpcPrefab;

            [JsonProperty("Stallholder NPC fallback prefab path")]
            public string NpcPrefabFallback = FarmersMarket.NpcPrefabFallback;

            [JsonProperty("Stalls", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Stall> Stalls = DefaultStalls();
        }

        // Shortnames checked against the server's item list on 2026-10-03.
        private static List<Stall> DefaultStalls()
        {
            var greengrocer = new Stall
            {
                Key = "greengrocer",
                Name = "Greengrocer",
                Outfit = new List<string> { "hat.boonie", "burlap.shirt", "burlap.trousers", "shoes.boots" },
                Goods = new List<Goods>
                {
                    new Goods("corn", 0.5f, 0f),
                    new Goods("pumpkin", 0.5f, 0f),
                    new Goods("potato", 0.4f, 0f),
                    new Goods("wheat", 0.4f, 0f),
                    new Goods("sunflower", 0.5f, 0f),
                    new Goods("rose", 0.5f, 0f),
                    new Goods("orchid", 0.6f, 0f),
                    new Goods("red.berry", 0.3f, 0f),
                    new Goods("blue.berry", 0.3f, 0f),
                    new Goods("green.berry", 0.3f, 0f),
                    new Goods("yellow.berry", 0.3f, 0f),
                    new Goods("white.berry", 0.3f, 0f),
                    new Goods("black.berry", 0.3f, 0f),
                    new Goods("mushroom", 0.3f, 0f),
                    new Goods("cloth", 0.05f, 0f),
                }
            };

            var seeds = new Stall
            {
                Key = "seeds",
                Name = "Seed Merchant",
                Outfit = new List<string> { "hat.cap", "shirt.collared", "pants", "shoes.boots" },
                Goods = new List<Goods>
                {
                    new Goods("seed.corn", 0.1f, 2f),
                    new Goods("seed.pumpkin", 0.1f, 2f),
                    new Goods("seed.potato", 0.1f, 2f),
                    new Goods("seed.wheat", 0.1f, 2f),
                    new Goods("seed.hemp", 0.1f, 2f),
                    new Goods("seed.red.berry", 0.1f, 3f),
                    new Goods("seed.blue.berry", 0.1f, 3f),
                    new Goods("seed.green.berry", 0.1f, 3f),
                    new Goods("seed.yellow.berry", 0.1f, 3f),
                    new Goods("seed.white.berry", 0.1f, 3f),
                    new Goods("seed.black.berry", 0.1f, 3f),
                    new Goods("seed.sunflower", 0.1f, 4f),
                    new Goods("seed.rose", 0.1f, 4f),
                    new Goods("seed.orchid", 0.1f, 4f),
                    new Goods("clone.corn", 0.3f, 0f),
                    new Goods("clone.pumpkin", 0.3f, 0f),
                    new Goods("clone.potato", 0.3f, 0f),
                    new Goods("clone.wheat", 0.3f, 0f),
                    new Goods("clone.hemp", 0.3f, 0f),
                    new Goods("clone.red.berry", 0.3f, 0f),
                    new Goods("clone.blue.berry", 0.3f, 0f),
                    new Goods("clone.green.berry", 0.3f, 0f),
                    new Goods("clone.yellow.berry", 0.3f, 0f),
                    new Goods("clone.white.berry", 0.3f, 0f),
                    new Goods("clone.black.berry", 0.3f, 0f),
                    new Goods("clone.sunflower", 0.3f, 0f),
                    new Goods("clone.rose", 0.3f, 0f),
                    new Goods("clone.orchid", 0.3f, 0f),
                    new Goods("horsedung", 0.5f, 0f),
                    new Goods("fertilizer", 0.1f, 1f),
                    new Goods("planter.small", 0f, 35f),
                    new Goods("planter.large", 0f, 60f),
                    new Goods("planter.triangle", 0f, 30f),
                    new Goods("plantpot.single", 0f, 10f),
                    new Goods("electric.sprinkler", 0f, 25f),
                    new Goods("hosetool", 0f, 40f),
                    new Goods("water.barrel", 0f, 60f),
                    new Goods("waterjug", 0f, 15f),
                    new Goods("bucket.water", 0f, 10f),
                    new Goods("composter", 0f, 75f),
                    new Goods("chickencoop", 0f, 120f),
                    new Goods("beehive", 0f, 150f),
                    new Goods("nucleus", 0f, 60f),
                    new Goods("hitchtroughcombo", 0f, 100f),
                }
            };

            var butcher = new Stall
            {
                Key = "butcher",
                Name = "Butcher & Dairy",
                Outfit = new List<string> { "hat.beenie", "tshirt.long", "pants", "burlap.gloves", "shoes.boots" },
                Goods = new List<Goods>
                {
                    new Goods("egg", 1f, 0f),
                    new Goods("honey", 6f, 0f),
                    new Goods("honeycomb", 2f, 0f),
                    new Goods("milk", 3f, 0f),
                    new Goods("skimmedmilk", 2f, 0f),
                    new Goods("cream", 4f, 0f),
                    new Goods("wool", 0.3f, 0f),
                    new Goods("chicken.raw", 1f, 0f),
                    new Goods("meat.boar", 1f, 0f),
                    new Goods("deermeat.raw", 1f, 0f),
                    new Goods("horsemeat.raw", 1f, 0f),
                    new Goods("beefmeat", 1.5f, 0f),
                    new Goods("muttonmeat", 1.5f, 0f),
                    new Goods("bearmeat", 1.5f, 0f),
                    new Goods("wolfmeat.raw", 1.5f, 0f),
                    new Goods("bigcatmeat", 2f, 0f),
                    new Goods("crocodilemeat", 2f, 0f),
                    new Goods("snakemeat", 1f, 0f),
                    new Goods("leather", 0.2f, 0f),
                    new Goods("fat.animal", 0.1f, 0f),
                }
            };

            return new List<Stall> { greengrocer, seeds, butcher };
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
                configBroken = false;
            }
            catch
            {
                PrintError($"Config file is invalid JSON — running on defaults until it's fixed. " +
                           $"The file was left untouched: repair oxide/config/{Name}.json (check quotes and commas) and run oxide.reload {Name}.");
                LoadDefaultConfig();
                configBroken = true;
            }
            if (config.Stalls == null) config.Stalls = new List<Stall>();
            foreach (var stall in config.Stalls)
            {
                if (stall.Goods == null) stall.Goods = new List<Goods>();
                if (stall.Outfit == null) stall.Outfit = new List<string>();
                if (stall.Key == null) stall.Key = "";
            }
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            if (configBroken)
            {
                PrintWarning($"Not saving config — oxide/config/{Name}.json is invalid JSON and writing would overwrite it. Fix the file and reload the plugin.");
                return;
            }
            Config.WriteObject(config);
        }

        private double Floor => Mathf.Clamp(config.FloorPercent, 0f, 100f) / 100.0;
        private double GeneBonus => Math.Max(0f, config.GeneBonusPercent) / 100.0;

        // Item definitions only exist once the server has loaded them, so the price
        // table is checked in OnServerInitialized rather than with the config.
        private void IndexGoods()
        {
            anyBuyer.Clear();
            foreach (var stall in config.Stalls)
            {
                stall.ByShortname.Clear();
                foreach (var goods in stall.Goods)
                {
                    goods.Buys = goods.Sells = false;
                    goods.Def = null;
                    if (!goods.Traded || string.IsNullOrEmpty(goods.Shortname)) continue;

                    goods.Def = ItemManager.FindItemDefinition(goods.Shortname.Trim().ToLower());
                    if (goods.Def == null)
                    {
                        PrintWarning($"{stall.Name}: item '{goods.Shortname}' does not exist — skipped.");
                        continue;
                    }
                    if (stall.ByShortname.ContainsKey(goods.Def.shortname))
                    {
                        PrintWarning($"{stall.Name}: item '{goods.Shortname}' is listed twice — the second entry is ignored.");
                        goods.Def = null;
                        continue;
                    }
                    if (goods.Def.shortname == ScrapShortname)
                    {
                        PrintWarning($"{stall.Name}: scrap can't be traded for scrap — skipped.");
                        goods.Def = null;
                        continue;
                    }

                    goods.Buys = goods.Pays > 0f;
                    goods.Sells = goods.Charges > 0f;

                    // No loop: what a stall charges must stay above the most it would pay.
                    double mostPaid = goods.Pays * (IsClone(goods.Def) ? 1.0 + 6 * GeneBonus : 1.0);
                    if (goods.Buys && goods.Sells && goods.Charges <= mostPaid)
                    {
                        PrintWarning($"{stall.Name}: '{goods.Shortname}' is sold for {goods.Charges} but bought for up to {FormatScrap(mostPaid)} — buying it is switched off so it can't be traded in a loop.");
                        goods.Buys = false;
                    }

                    stall.ByShortname[goods.Def.shortname] = goods;
                    if (goods.Buys && !anyBuyer.ContainsKey(goods.Def.shortname))
                        anyBuyer[goods.Def.shortname] = goods;
                }
            }
        }

        // First stall that buys each item: used when another plugin sells on a player's behalf.
        private readonly Dictionary<string, Goods> anyBuyer = new Dictionary<string, Goods>();

        #endregion

        #region Data

        private class PlayerData
        {
            public double DayStart;   // epoch seconds of the first sale in the current day
            public double Earned;     // scrap value sold in that day
            public double Change;     // fraction of a scrap owed from earlier sales
        }

        private class DemandData
        {
            public double Paid;       // scrap paid for this item, as of At
            public double At;
        }

        private class StoredData
        {
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, PlayerData> Players = new Dictionary<ulong, PlayerData>();

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, DemandData> Demand = new Dictionary<string, DemandData>();
        }

        private StoredData data;

        private void LoadData()
        {
            try { data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name); }
            catch (Exception e)
            {
                PrintWarning($"Data file oxide/data/{Name}.json could not be read ({e.Message}). Daily limits and prices start fresh.");
            }
            if (data == null) data = new StoredData();
            if (data.Players == null) data.Players = new Dictionary<ulong, PlayerData>();
            if (data.Demand == null) data.Demand = new Dictionary<string, DemandData>();
        }

        // Written with the world save (and on unload), not on every sale: a crash then
        // rolls the day's takings and the players' inventories back to the same moment.
        private void SaveData()
        {
            double now = Now;
            var stale = new List<ulong>();
            foreach (var pair in data.Players)
                if (now - pair.Value.DayStart >= DaySeconds && pair.Value.Change < 0.005) stale.Add(pair.Key);
            foreach (var userId in stale) data.Players.Remove(userId);

            var settled = new List<string>();
            foreach (var pair in data.Demand)
                if (DemandPaid(pair.Key, now) <= 0) settled.Add(pair.Key);
            foreach (var shortname in settled) data.Demand.Remove(shortname);

            Interface.Oxide.DataFileSystem.WriteObject(Name, data);
        }

        private static double Now => DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

        private PlayerData GetPlayer(ulong userId, bool create)
        {
            PlayerData record;
            if (data.Players.TryGetValue(userId, out record))
            {
                if (Now - record.DayStart >= DaySeconds) record.Earned = 0;
                return record;
            }
            if (!create) return null;
            record = new PlayerData();
            data.Players[userId] = record;
            return record;
        }

        // Scrap the stalls will still pay this player today.
        private double Allowance(ulong userId)
        {
            if (config.DailyCap <= 0) return double.MaxValue;
            var record = GetPlayer(userId, false);
            return Math.Max(0, config.DailyCap - (record != null ? record.Earned : 0));
        }

        private double DayResetsIn(ulong userId)
        {
            var record = GetPlayer(userId, false);
            return record == null ? 0 : Math.Max(0, record.DayStart + DaySeconds - Now);
        }

        #endregion

        #region Pricing

        private static bool IsClone(ItemDefinition def) =>
            def != null && def.amountType == ItemDefinition.AmountType.Genetics && def.shortname.StartsWith("clone.");

        // Clones carry six genes in the item's data, five bits each (GrowableGeneEncoding).
        private double GeneMultiplier(Item item)
        {
            if (item.instanceData == null || !IsClone(item.info)) return 1.0;
            int genes = item.instanceData.dataInt, good = 0;
            for (int slot = 0; slot < GrowableGenetics.GeneSlotCount; slot++)
            {
                var type = (GrowableGenetics.GeneType)((genes >> (slot * 5)) & 31);
                if (type == GrowableGenetics.GeneType.GrowthSpeed || type == GrowableGenetics.GeneType.Yield) good++;
            }
            return 1.0 + good * GeneBonus;
        }

        // Scrap recently paid for this item, after recovery.
        private double DemandPaid(string shortname, double now)
        {
            DemandData demand;
            if (!config.FallingPrices || !data.Demand.TryGetValue(shortname, out demand)) return 0;
            double recoverySeconds = Math.Max(0.01, config.RecoveryHours) * 3600;
            return Math.Max(0, demand.Paid - (now - demand.At) * Math.Max(1f, config.GlutScrap) / recoverySeconds);
        }

        private double DemandMultiplier(double paid)
        {
            if (!config.FallingPrices) return 1.0;
            return 1.0 - (1.0 - Floor) * Math.Min(1.0, paid / Math.Max(1f, config.GlutScrap));
        }

        private class SaleLine
        {
            public ItemDefinition Def;
            public int Units;
            public double Scrap;
        }

        private class Sale
        {
            public readonly List<SaleLine> Lines = new List<SaleLine>();
            public double Total;
            public int Units;
            public bool CapHit;
        }

        // Price the given stacks one unit at a time, so the falling price and the daily
        // limit both bite part-way through a stack. With commit the units are taken
        // from the stacks and the takings recorded; without, nothing changes (a quote).
        // stall null = any stall that buys the item.
        private Sale Sell(ulong userId, List<Item> items, Stall stall, string onlyShortname, bool commit)
        {
            var sale = new Sale();
            double now = Now;
            double allowance = Allowance(userId);
            var paidSoFar = new Dictionary<string, double>();

            foreach (var item in items)
            {
                if (item == null || !item.IsValid() || item.amount <= 0) continue;
                string shortname = item.info.shortname;
                if (onlyShortname != null && shortname != onlyShortname) continue;

                Goods goods;
                if (stall != null) stall.ByShortname.TryGetValue(shortname, out goods);
                else anyBuyer.TryGetValue(shortname, out goods);
                if (goods == null || !goods.Buys) continue;

                double paid;
                if (!paidSoFar.TryGetValue(shortname, out paid)) paid = DemandPaid(shortname, now);

                double listed = goods.Pays * GeneMultiplier(item);
                double earned = 0;
                int units = 0;
                for (int i = 0; i < item.amount; i++)
                {
                    double price = listed * DemandMultiplier(paid);
                    if (price <= 0) break;
                    if (price > allowance + 0.000001) { sale.CapHit = true; break; }
                    allowance -= price;
                    paid += price;
                    earned += price;
                    units++;
                }
                paidSoFar[shortname] = paid;
                if (units == 0) continue;

                SaleLine line = null;
                foreach (var existing in sale.Lines)
                    if (existing.Def == item.info) { line = existing; break; }
                if (line == null)
                {
                    line = new SaleLine { Def = item.info };
                    sale.Lines.Add(line);
                }
                line.Units += units;
                line.Scrap += earned;
                sale.Units += units;
                sale.Total += earned;

                if (!commit) continue;
                if (units >= item.amount)
                {
                    item.RemoveFromContainer();
                    item.Remove();
                }
                else
                {
                    item.amount -= units;
                    item.MarkDirty();
                }
            }

            if (commit && sale.Units > 0)
            {
                if (config.FallingPrices)
                    foreach (var line in sale.Lines)
                        data.Demand[line.Def.shortname] = new DemandData { Paid = paidSoFar[line.Def.shortname], At = now };

                var record = GetPlayer(userId, true);
                if (now - record.DayStart >= DaySeconds) record.DayStart = now;
                record.Earned += sale.Total;
            }
            return sale;
        }

        // Turn a committed sale into whole scrap, carrying the fraction to the next
        // sale, and tell other plugins what was sold. Returns the scrap to hand over.
        private int Settle(ulong userId, BasePlayer player, Sale sale)
        {
            var record = GetPlayer(userId, true);
            double owed = record.Change;
            int paidOut = 0;
            foreach (var line in sale.Lines)
            {
                owed += line.Scrap;
                int whole = (int)Math.Floor(owed + 0.000001) - paidOut;
                paidOut += whole;
                Interface.CallHook("OnFarmersMarketSale", player, line.Def.shortname, line.Units, whole);
            }
            record.Change = Math.Max(0, owed - paidOut);
            return paidOut;
        }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermUse, this);
            LoadData();
            // only listened to while a stallholder is standing
            Unsubscribe(nameof(OnPlayerInput));
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnNpcTarget));
            UnsubscribeShopHooks();
            Puts($"FarmersMarket v{Version} loaded - by LowPopLabs - ko-fi.com/lowpoplabs");
        }

        private void OnServerInitialized()
        {
            IndexGoods();
            SpawnStalls();
        }

        private void OnServerSave() => SaveData();

        private void Unload()
        {
            faceTimer?.Destroy();
            foreach (var userId in new List<ulong>(sessions.Keys))
            {
                var player = BasePlayer.FindByID(userId);
                if (player != null) CuiHelper.DestroyUi(player, UiPanelName);
            }
            sessions.Clear();
            KillStalls(null, config.CloseShopsOnUnload);
            SaveData();
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            sessions.Remove((ulong)player.userID);
            lastUse.Remove((ulong)player.userID);
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (input == null || !input.WasJustPressed(BUTTON.USE)) return;
            if (player == null || !player.userID.IsSteamId() || player.IsDead() || player.IsSleeping()) return;

            var live = StallInFrontOf(player);
            if (live == null) return;

            float now = Time.realtimeSinceStartup;
            float last;
            if (lastUse.TryGetValue((ulong)player.userID, out last) && now - last < 0.5f) return;
            lastUse[(ulong)player.userID] = now;

            if (!MayUse(player)) { Reply(player, "NoPermission"); return; }
            sessions[(ulong)player.userID] = new Session { Live = live };
            ShowPanel(player);
        }

        private readonly Dictionary<ulong, float> lastUse = new Dictionary<ulong, float>();

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is NPCPlayer && IsStallholder(entity)) return true;   // stallholders can't be hurt
            return null;
        }

        private object OnNpcTarget(BaseEntity npc, BaseEntity target)
        {
            if (IsStallholder(npc) || IsStallholder(target)) return true;
            return null;
        }

        private bool MayUse(BasePlayer player) =>
            !config.RequirePermission || permission.UserHasPermission(player.UserIDString, PermUse);

        #endregion

        #region Stallholders

        // A stall standing on this map.
        private class LiveStall
        {
            public Stall Stall;
            public BasePlayer Npc;
            public RentableShop Shop;   // set when the stall is a rentable shop unit
            public float Range;         // how far a player may stand and still trade
            public Vector3 Position;
            public float HomeYaw;
            public float LastYaw;
        }

        private readonly List<LiveStall> liveStalls = new List<LiveStall>();
        private readonly HashSet<ulong> stallholderIds = new HashSet<ulong>();
        private Timer faceTimer;

        private bool IsStallholder(BaseEntity entity) =>
            entity != null && entity.net != null && stallholderIds.Contains(entity.net.ID.Value);

        private MonumentInfo FindMonumentAt(Vector3 position)
        {
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return null;
            foreach (var monument in TerrainMeta.Path.Monuments)
                if (monument != null && monument.IsInBounds(position))
                    return monument;
            return null;
        }

        private static bool TryParsePosition(string text, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float x, y, z;
            if (parts.Length != 3
                || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
                || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)
                || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z))
                return false;
            position = new Vector3(x, y, z);
            return true;
        }

        private static string FormatPosition(Vector3 v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2} {1:F2} {2:F2}", v.x, v.y, v.z);

        // A spot inside a monument applies to every copy of that monument on the map.
        private List<MonumentInfo> AnchorsFor(string monumentName)
        {
            var anchors = new List<MonumentInfo>();
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return anchors;
            foreach (var monument in TerrainMeta.Path.Monuments)
                if (monument != null && monument.name == monumentName) anchors.Add(monument);
            if (anchors.Count == 0)
            {
                // Fall back to a filename match in case the monument prefab variant differs this map.
                string fileName = monumentName.Substring(monumentName.LastIndexOf('/') + 1);
                foreach (var monument in TerrainMeta.Path.Monuments)
                    if (monument != null && monument.name.EndsWith(fileName)) anchors.Add(monument);
            }
            return anchors;
        }

        private void SpawnStalls()
        {
            KillStalls(null, false);
            int unplaced = 0;
            foreach (var stall in config.Stalls)
            {
                if (string.IsNullOrEmpty(stall.Position)) { unplaced++; continue; }
                SpawnStall(stall);
            }
            if (unplaced > 0)
                PrintWarning($"{unplaced} stall(s) not placed yet — an admin should stand where each stallholder belongs and run /market set <stall>");
            if (liveStalls.Count > 0) Puts($"{liveStalls.Count} stallholder(s) standing.");
            WatchStalls();
        }

        private void SpawnStall(Stall stall)
        {
            Vector3 stored;
            if (!TryParsePosition(stall.Position, out stored))
            {
                PrintWarning($"{stall.Name} has an unreadable position '{stall.Position}' — skipped.");
                return;
            }

            if (string.IsNullOrEmpty(stall.Monument))
            {
                Place(stall, stored, stall.Facing);
                return;
            }

            var anchors = AnchorsFor(stall.Monument);
            if (anchors.Count == 0)
                PrintWarning($"{stall.Name}: monument '{stall.Monument}' is not on this map — that stall is closed.");
            foreach (var anchor in anchors)
                Place(stall, anchor.transform.TransformPoint(stored), anchor.transform.eulerAngles.y + stall.Facing);
        }

        private void Place(Stall stall, Vector3 position, float yaw)
        {
            if (!stall.ShopUnit)
            {
                SpawnStallholder(stall, position, yaw);
                return;
            }

            var shop = ShopNear(position, 3f);
            if (shop == null)
                PrintWarning($"{stall.Name}: no rentable shop unit at its saved position — that stall is closed. Run /market set {stall.Key} again.");
            else if (LiveFor(shop) != null)
                PrintWarning($"{stall.Name}: shop unit {shop.ShopNumberId} is already another stall — skipped.");
            else if (!ClaimShop(stall, shop))
                PrintWarning(shop.IsOn()
                    ? $"{stall.Name}: shop unit {shop.ShopNumberId} is rented by a player — that stall is closed until the unit is free and the plugin reloads."
                    : $"{stall.Name}: shop unit {shop.ShopNumberId} could not be opened — that stall is closed.");
        }

        private void SpawnStallholder(Stall stall, Vector3 position, float yaw)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var entity = GameManager.server.CreateEntity(
                string.IsNullOrEmpty(config.NpcPrefab) ? NpcPrefab : config.NpcPrefab, position, rotation);
            if (entity == null)
                entity = GameManager.server.CreateEntity(
                    string.IsNullOrEmpty(config.NpcPrefabFallback) ? NpcPrefabFallback : config.NpcPrefabFallback, position, rotation);
            var npc = entity as BasePlayer;
            if (npc == null)
            {
                PrintError($"Failed to create the {stall.Name} NPC from both prefabs");
                if (entity != null) entity.Kill();
                return;
            }

            // Clients derive face and gender from userID — set it before Spawn() so it
            // networks with the create. Steam-range values only: small values would
            // flip the engine's IsBot logic.
            if (stall.FaceSeed != 0) npc.userID = stall.FaceSeed;

            npc.enableSaving = false;
            npc.Spawn();
            npc.displayName = stall.Name;
            Pacify(npc, stall.Name);
            Dress(npc, stall);

            var live = new LiveStall { Stall = stall, Npc = npc, Position = position, HomeYaw = yaw, LastYaw = yaw, Range = config.InteractRange + 2f };
            liveStalls.Add(live);
            stallholderIds.Add(npc.net.ID.Value);

            Pin(live);
            npc.SendNetworkUpdateImmediate();
            // The nav agent can warp an NPC to the nearest navmesh a moment after
            // spawn — re-assert the spot once settled.
            timer.Once(2f, () => Pin(live));
        }

        // The scientist prefabs come with a combat brain — shut it down so the stallholder
        // never targets, moves or shoots, and take its weapons away.
        private void Pacify(BasePlayer npc, string who)
        {
            try
            {
                var brain = npc.GetComponent<BaseAIBrain>();
                if (brain != null)
                {
                    brain.SetThinkMode(AIThinkMode.None);
                    brain.SetEnabled(false);
                }
            }
            catch (Exception e) { PrintWarning($"Could not disable the {who} brain: {e.Message}"); }

            try { npc.inventory?.Strip(); }
            catch (Exception e) { PrintWarning($"Could not strip the {who} inventory: {e.Message}"); }
        }

        // "shortname" or "shortname@skinId".
        private void Dress(BasePlayer npc, Stall stall)
        {
            var wear = npc.inventory?.containerWear;
            if (wear == null) return;
            foreach (var entry in stall.Outfit)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                string shortname = entry;
                ulong skin = 0;
                int at = entry.IndexOf('@');
                if (at > 0)
                {
                    shortname = entry.Substring(0, at);
                    ulong.TryParse(entry.Substring(at + 1), out skin);
                }
                var item = ItemManager.CreateByName(shortname, 1, skin);
                if (item == null) { PrintWarning($"{stall.Name} outfit item '{shortname}' unknown — skipped."); continue; }
                if (!item.MoveToContainer(wear)) item.Remove();
            }
            npc.SendNetworkUpdate();
        }

        private void Pin(LiveStall live)
        {
            var npc = live.Npc;
            if (npc == null || npc.IsDestroyed) return;
            try
            {
                var agent = npc as NPCPlayer;
                if (agent != null && agent.NavAgent != null) agent.NavAgent.enabled = false;
            }
            catch { }
            npc.ServerPosition = live.Position;
            npc.SendNetworkUpdate();
        }

        // releaseShops false leaves shop units open in the world, to be picked up again on the next load.
        private void KillStalls(Stall only, bool releaseShops)
        {
            for (int i = liveStalls.Count - 1; i >= 0; i--)
            {
                var live = liveStalls[i];
                if (only != null && live.Stall != only) continue;
                liveStalls.RemoveAt(i);
                CloseSessionsAt(live);
                if (live.Shop != null)
                {
                    if (releaseShops) ReleaseShop(live.Shop);
                    continue;
                }
                if (live.Npc == null || live.Npc.IsDestroyed) continue;
                if (live.Npc.net != null) stallholderIds.Remove(live.Npc.net.ID.Value);
                live.Npc.Kill();
            }
            if (only == null) stallholderIds.Clear();
        }

        private void WatchStalls()
        {
            faceTimer?.Destroy();
            faceTimer = null;

            bool standing = false, units = false;
            foreach (var live in liveStalls)
            {
                if (live.Shop != null) units = true;
                else standing = true;
            }
            if (units) SubscribeShopHooks();
            else UnsubscribeShopHooks();

            if (standing)
            {
                Subscribe(nameof(OnPlayerInput));
                Subscribe(nameof(OnEntityTakeDamage));
                Subscribe(nameof(OnNpcTarget));
                if (config.FaceRange > 0f) faceTimer = timer.Every(0.3f, FaceTick);
            }
            else
            {
                Unsubscribe(nameof(OnPlayerInput));
                Unsubscribe(nameof(OnEntityTakeDamage));
                Unsubscribe(nameof(OnNpcTarget));
            }
        }

        // Turn each stallholder toward the nearest player in range; back to the front when alone.
        private void FaceTick()
        {
            float rangeSqr = config.FaceRange * config.FaceRange;
            foreach (var live in liveStalls)
            {
                var npc = live.Npc;
                if (live.Shop != null || npc == null || npc.IsDestroyed) continue;   // shopkeepers greet on their own

                BasePlayer nearest = null;
                float bestSqr = rangeSqr;
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player == null || player.IsDead()) continue;
                    float sqr = (player.transform.position - live.Position).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; nearest = player; }
                }

                float targetYaw = live.HomeYaw;
                if (nearest != null)
                {
                    Vector3 dir = nearest.transform.position - live.Position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f)
                        targetYaw = Quaternion.LookRotation(dir.normalized).eulerAngles.y;
                }

                if (Mathf.Abs(Mathf.DeltaAngle(live.LastYaw, targetYaw)) < 3f) continue;
                live.LastYaw = targetYaw;
                npc.viewAngles = new Vector3(0f, targetYaw, 0f);
                npc.ServerRotation = Quaternion.Euler(0f, targetYaw, 0f);
                npc.SendNetworkUpdate();
            }
        }

        // The stallholder the player is standing at and looking toward.
        private LiveStall StallInFrontOf(BasePlayer player)
        {
            float rangeSqr = config.InteractRange * config.InteractRange;
            Vector3 position = player.transform.position;
            foreach (var live in liveStalls)
            {
                if (live.Shop != null || (live.Position - position).sqrMagnitude > rangeSqr) continue;
                Vector3 toNpc = (live.Position + Vector3.up * 1.4f) - player.eyes.position;
                if (Vector3.Dot(player.eyes.HeadForward(), toNpc.normalized) >= 0.6f) return live;
            }
            return null;
        }

        #endregion

        #region Shop units

        // A stall can take over one of the map's rentable shop units instead of
        // standing an NPC of its own: the game's shopkeeper serves from behind the
        // counter, and using the shop opens the stall panel instead of a vending screen.

        private static RentableShop ShopNear(Vector3 position, float range)
        {
            RentableShop nearest = null;
            float best = range * range;
            foreach (var shop in RentableShop.AllServerShops)
            {
                if (shop == null || shop.IsDestroyed) continue;
                float sqr = (shop.transform.position - position).sqrMagnitude;
                if (sqr <= best) { best = sqr; nearest = shop; }
            }
            return nearest;
        }

        private LiveStall LiveFor(RentableShop shop)
        {
            if (shop == null) return null;
            foreach (var live in liveStalls)
                if (live.Shop == shop) return live;
            return null;
        }

        // An open unit whose owner is not a player is one this plugin opened earlier.
        private static bool IsMarketUnit(RentableShop shop) =>
            shop.IsOn() && !shop.ShopOwnerId.IsSteamId();

        private bool ClaimShop(Stall stall, RentableShop shop)
        {
            if (shop.IsOn() && !IsMarketUnit(shop)) return false;   // a player's shop
            if (!shop.IsOn() && !OpenShop(shop)) return false;

            var machine = shop.GetServerVendingMachine();
            NPCShopKeeper keeper = null;
            foreach (var child in shop.children)
            {
                keeper = child as NPCShopKeeper;
                if (keeper != null) break;
            }

            if (machine != null)
            {
                machine.inventory.Clear();
                machine.shopName = stall.Name;
                machine.SendNetworkUpdate();
            }
            if (keeper != null)
            {
                keeper.displayName = stall.Name;
                keeper.SendNetworkUpdate();
            }

            liveStalls.Add(new LiveStall
            {
                Stall = stall,
                Shop = shop,
                Npc = keeper,
                Position = machine != null ? machine.transform.position : shop.transform.position,
                Range = Mathf.Max(config.InteractRange + 2f, 6f),
            });
            return true;
        }

        // The game only opens a shop for a player, so a throwaway NPC stands in as
        // the tenant: the unit ends up owned by no real player.
        private bool OpenShop(RentableShop shop)
        {
            var proxy = GameManager.server.CreateEntity(NpcPrefab, shop.transform.position) as BasePlayer;
            if (proxy == null)
                proxy = GameManager.server.CreateEntity(NpcPrefabFallback, shop.transform.position) as BasePlayer;
            if (proxy == null)
            {
                PrintError("Failed to create the stand-in tenant NPC");
                return false;
            }
            proxy.enableSaving = false;
            proxy.limitNetworking = true;
            proxy.Spawn();
            Pacify(proxy, "stand-in tenant");   // also empties its pockets, so no rent is paid in

            try
            {
                using (var scope = shop.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                {
                    scope.Set(BaseEntity.Flags.On, true);
                }
                shop.OnShopOpened(proxy);
            }
            catch (Exception e)
            {
                PrintError($"Opening shop unit {shop.ShopNumberId} failed: {e.Message}");
                if (shop.GetServerVendingMachine() == null)
                {
                    using (var scope = shop.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
                    {
                        scope.Set(BaseEntity.Flags.On, false);
                    }
                }
            }
            finally
            {
                if (!proxy.IsDestroyed) proxy.Kill();
            }
            return shop.IsOn() && shop.GetServerVendingMachine() != null;
        }

        // Hand the unit back: the shopkeeper and its machine go, and players can rent it again.
        private void ReleaseShop(RentableShop shop)
        {
            if (shop == null || shop.IsDestroyed || !IsMarketUnit(shop)) return;
            var machine = shop.GetServerVendingMachine();
            if (machine != null) machine.inventory.Clear();   // nothing to leave for a tenant who doesn't exist
            using (var scope = shop.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
            {
                scope.Set(BaseEntity.Flags.On, false);
            }
            shop.OnShopClosed(false);
        }

        private void SubscribeShopHooks()
        {
            Subscribe(nameof(CanUseVending));
            Subscribe(nameof(OnRentableShopOpen));
            Subscribe(nameof(OnRentableShopClose));
            Subscribe(nameof(OnRentableShopBreakInComplete));
            Subscribe(nameof(CanUpdateSign));
        }

        private void UnsubscribeShopHooks()
        {
            Unsubscribe(nameof(CanUseVending));
            Unsubscribe(nameof(OnRentableShopOpen));
            Unsubscribe(nameof(OnRentableShopClose));
            Unsubscribe(nameof(OnRentableShopBreakInComplete));
            Unsubscribe(nameof(CanUpdateSign));
        }

        // Using the shop: the stall panel instead of the vending screen.
        private object CanUseVending(BasePlayer player, VendingMachine machine)
        {
            if (player == null || machine == null) return null;
            var live = LiveFor(machine.GetParentEntity() as RentableShop);
            if (live == null) return null;

            float now = Time.realtimeSinceStartup;
            float last;
            if (lastUse.TryGetValue((ulong)player.userID, out last) && now - last < 0.5f) return false;
            lastUse[(ulong)player.userID] = now;

            if (!MayUse(player)) { Reply(player, "NoPermission"); return false; }
            sessions[(ulong)player.userID] = new Session { Live = live };
            ShowPanel(player);
            return false;
        }

        // Nobody rents a market unit out from under the stall...
        private object OnRentableShopOpen(RentableShop shop, BasePlayer player)
        {
            if (LiveFor(shop) == null) return null;
            if (player != null) Reply(player, "Stall.NotForRent");
            return false;
        }

        // ...it never closes for unpaid rent...
        private object OnRentableShopClose(RentableShop shop, bool notify)
        {
            if (LiveFor(shop) == null) return null;
            return false;
        }

        // ...and there is nothing inside to break in for.
        private object OnRentableShopBreakInComplete(RentableShop shop, BasePlayer player)
        {
            if (LiveFor(shop) == null) return null;
            return false;
        }

        // The unit's sign is locked to the stand-in tenant; admins may paint it.
        private object CanUpdateSign(BasePlayer player, Signage sign)
        {
            if (player == null || sign == null || LiveFor(sign.GetParentEntity() as RentableShop) == null) return null;
            return permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        #endregion

        #region Stall lookup

        private Stall FindStall(string key)
        {
            foreach (var stall in config.Stalls)
                if (string.Equals(stall.Key, key, StringComparison.OrdinalIgnoreCase)) return stall;
            return null;
        }

        private string StallKeys()
        {
            var keys = new List<string>();
            foreach (var stall in config.Stalls) keys.Add(stall.Key);
            return string.Join(", ", keys.ToArray());
        }

        private static string ShortMonument(string name)
        {
            int slash = name.LastIndexOf('/');
            string file = slash >= 0 ? name.Substring(slash + 1) : name;
            return file.EndsWith(".prefab") ? file.Substring(0, file.Length - 7) : file;
        }

        #endregion

        #region Trading

        private class Session
        {
            public LiveStall Live;
            public int BuyPage;
            public int SellPage;
        }

        private readonly Dictionary<ulong, Session> sessions = new Dictionary<ulong, Session>();

        // The stall this player has open, if they are still standing at it.
        private Session SessionOf(BasePlayer player)
        {
            Session session;
            if (!sessions.TryGetValue((ulong)player.userID, out session)) return null;
            float range = session.Live.Range;
            if (liveStalls.Contains(session.Live) && !player.IsDead()
                && (session.Live.Position - player.transform.position).sqrMagnitude <= range * range)
                return session;
            ClosePanel(player);
            return null;
        }

        private void ClosePanel(BasePlayer player)
        {
            sessions.Remove((ulong)player.userID);
            CuiHelper.DestroyUi(player, UiPanelName);
        }

        private void CloseSessionsAt(LiveStall live)
        {
            var closing = new List<ulong>();
            foreach (var pair in sessions)
                if (pair.Value.Live == live) closing.Add(pair.Key);
            foreach (var userId in closing)
            {
                sessions.Remove(userId);
                var player = BasePlayer.FindByID(userId);
                if (player != null) CuiHelper.DestroyUi(player, UiPanelName);
            }
        }

        // What the player has on them to sell: main inventory and belt, not what they wear.
        private static List<Item> Carried(BasePlayer player)
        {
            var items = new List<Item>();
            if (player.inventory == null) return items;
            if (player.inventory.containerMain != null) items.AddRange(player.inventory.containerMain.itemList);
            if (player.inventory.containerBelt != null) items.AddRange(player.inventory.containerBelt.itemList);
            return items;
        }

        private void GiveItems(BasePlayer player, ItemDefinition def, int amount)
        {
            int stack = Mathf.Max(1, def.stackable);
            while (amount > 0)
            {
                int batch = Mathf.Min(amount, stack);
                var item = ItemManager.Create(def, batch);
                if (item == null) return;
                amount -= batch;
                if (!player.inventory.GiveItem(item))
                    item.Drop(player.transform.position + Vector3.up, Vector3.zero);
            }
        }

        // shortname null = everything the stall buys.
        private void SellFromPlayer(BasePlayer player, Stall stall, string shortname)
        {
            ulong userId = (ulong)player.userID;
            var sale = Sell(userId, Carried(player), stall, shortname, true);
            if (sale.Units == 0)
            {
                if (sale.CapHit) Reply(player, "Sell.CapReached", FormatSpan(DayResetsIn(userId)));
                else Reply(player, "Sell.Nothing");
                return;
            }

            int scrap = Settle(userId, player, sale);
            var scrapDef = ItemManager.FindItemDefinition(ScrapShortname);
            if (scrap > 0 && scrapDef != null) GiveItems(player, scrapDef, scrap);

            if (sale.Lines.Count == 1)
                Reply(player, "Sell.One", sale.Units, sale.Lines[0].Def.displayName.english, FormatScrap(sale.Total));
            else
                Reply(player, "Sell.Many", sale.Units, sale.Lines.Count, FormatScrap(sale.Total));

            double change = GetPlayer(userId, true).Change;
            if (change >= 0.005 || Math.Abs(sale.Total - scrap) >= 0.005)
                ReplyRaw(player, "Sell.Carried", scrap, FormatScrap(change));
            if (sale.CapHit) ReplyRaw(player, "Sell.CapPartial");
        }

        private static int CostOf(Goods goods, int amount) =>
            Mathf.Max(1, Mathf.CeilToInt(goods.Charges * amount - 0.0001f));

        private void BuyFromStall(BasePlayer player, Stall stall, string shortname, int amount)
        {
            Goods goods;
            if (!stall.ByShortname.TryGetValue(shortname, out goods) || !goods.Sells) return;
            amount = Mathf.Clamp(amount, 1, Mathf.Max(1, goods.Def.stackable));

            int cost = CostOf(goods, amount);
            var scrapDef = ItemManager.FindItemDefinition(ScrapShortname);
            if (scrapDef == null) return;
            if (player.inventory.GetAmount(scrapDef.itemid) < cost)
            {
                Reply(player, "Buy.NeedScrap", cost);
                return;
            }
            player.inventory.Take(null, scrapDef.itemid, cost);
            GiveItems(player, goods.Def, amount);
            Reply(player, "Buy.Done", amount, goods.Def.displayName.english, cost);
            Interface.CallHook("OnFarmersMarketPurchase", player, shortname, amount, cost);
        }

        #endregion

        #region Calls for other plugins

        // int SellProduce(ulong ownerId, ItemContainer from) — sell everything in the
        // container that any stall buys, on that player's behalf and under their daily
        // limit. The goods are destroyed; the return value is the scrap owed, which the
        // caller hands over itself.
        [HookMethod("SellProduce")]
        public object SellProduce(ulong ownerId, ItemContainer from)
        {
            if (from == null || data == null) return 0;
            var sale = Sell(ownerId, new List<Item>(from.itemList), null, null, true);
            if (sale.Units == 0) return 0;
            return Settle(ownerId, BasePlayer.FindByID(ownerId), sale);
        }

        // double QuoteProduce(ulong ownerId, ItemContainer from) — what SellProduce would pay right now.
        [HookMethod("QuoteProduce")]
        public object QuoteProduce(ulong ownerId, ItemContainer from)
        {
            if (from == null || data == null) return 0.0;
            return Sell(ownerId, new List<Item>(from.itemList), null, null, false).Total;
        }

        // double GetBuyPrice(string shortname) — scrap a stall pays per unit right now; null = no stall buys it.
        [HookMethod("GetBuyPrice")]
        public object GetBuyPrice(string shortname)
        {
            Goods goods;
            if (shortname == null || !anyBuyer.TryGetValue(shortname, out goods)) return null;
            return goods.Pays * DemandMultiplier(DemandPaid(shortname, Now));
        }

        // double GetSellPrice(string shortname) — scrap a stall charges per unit; null = no stall sells it.
        [HookMethod("GetSellPrice")]
        public object GetSellPrice(string shortname)
        {
            if (shortname == null) return null;
            foreach (var stall in config.Stalls)
            {
                Goods goods;
                if (stall.ByShortname.TryGetValue(shortname, out goods) && goods.Sells) return (double)goods.Charges;
            }
            return null;
        }

        // double GetDailyAllowance(ulong userId) — scrap the stalls will still pay this player today; -1 = no limit.
        [HookMethod("GetDailyAllowance")]
        public object GetDailyAllowance(ulong userId) =>
            config.DailyCap <= 0 ? -1.0 : Allowance(userId);

        // Vector3 GetStallPosition(string key) — where a stallholder stands on this map; null = not placed.
        [HookMethod("GetStallPosition")]
        public object GetStallPosition(string key)
        {
            foreach (var live in liveStalls)
                if (string.Equals(live.Stall.Key, key, StringComparison.OrdinalIgnoreCase)) return live.Position;
            return null;
        }

        #endregion

        #region Commands

        [ChatCommand("market")]
        private void CmdMarket(BasePlayer player, string command, string[] args)
        {
            string sub = args.Length > 0 ? args[0].ToLower() : "";
            if (sub == "" || sub == "info")
            {
                if (!MayUse(player)) { Reply(player, "NoPermission"); return; }
                TellInfo(player);
                return;
            }

            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                Reply(player, "NoPermission");
                return;
            }

            switch (sub)
            {
                case "set":
                {
                    if (args.Length < 2) { ReplyRaw(player, "Admin.Usage"); return; }
                    var stall = FindStall(args[1]);
                    if (stall == null) { ReplyRaw(player, "Admin.NoStall", args[1], StallKeys()); return; }

                    Vector3 position = player.transform.position;
                    float yaw = player.eyes.rotation.eulerAngles.y;

                    // Standing at a rentable shop unit: the stall takes the unit over.
                    bool forceNpc = args.Length > 2 && args[2].ToLower() == "npc";
                    var shop = forceNpc ? null : ShopNear(position, 8f);
                    if (shop != null)
                    {
                        var holder = LiveFor(shop);
                        if (holder != null && holder.Stall != stall) { ReplyRaw(player, "Admin.UnitTaken", shop.ShopNumberId, holder.Stall.Name); return; }
                        if (shop.IsOn() && !IsMarketUnit(shop)) { ReplyRaw(player, "Admin.UnitRented", shop.ShopNumberId); return; }
                        position = shop.transform.position;
                        yaw = 0f;
                    }

                    // Let go of wherever the stall was before its spot is overwritten.
                    KillStalls(stall, true);
                    stall.ShopUnit = shop != null;

                    var monument = FindMonumentAt(position);
                    if (monument != null)
                    {
                        stall.Monument = monument.name;
                        stall.Position = FormatPosition(monument.transform.InverseTransformPoint(position));
                        stall.Facing = Mathf.Repeat(yaw - monument.transform.eulerAngles.y, 360f);
                    }
                    else
                    {
                        stall.Monument = "";
                        stall.Position = FormatPosition(position);
                        stall.Facing = yaw;
                    }
                    SaveConfig();
                    SpawnStall(stall);
                    WatchStalls();
                    if (shop != null) ReplyRaw(player, LiveFor(shop) != null ? "Admin.SetUnit" : "Admin.UnitFailed", stall.Name, shop.ShopNumberId);
                    else if (monument != null) ReplyRaw(player, "Admin.Set", stall.Name, ShortMonument(stall.Monument));
                    else ReplyRaw(player, "Admin.SetWorld", stall.Name);
                    return;
                }
                case "remove":
                {
                    if (args.Length < 2) { ReplyRaw(player, "Admin.Usage"); return; }
                    var stall = FindStall(args[1]);
                    if (stall == null) { ReplyRaw(player, "Admin.NoStall", args[1], StallKeys()); return; }
                    stall.Monument = "";
                    stall.Position = "";
                    stall.Facing = 0f;
                    stall.ShopUnit = false;
                    SaveConfig();
                    KillStalls(stall, true);
                    WatchStalls();
                    ReplyRaw(player, "Admin.Removed", stall.Name);
                    return;
                }
                case "list":
                {
                    foreach (var stall in config.Stalls)
                    {
                        string where = Msg("Admin.NotPlaced", player.UserIDString);
                        foreach (var live in liveStalls)
                            if (live.Stall == stall) { where = MapHelper.PositionToString(live.Position); break; }
                        ReplyRaw(player, "Admin.ListLine", stall.Key, stall.Name, where);
                    }
                    return;
                }
                case "reset":
                {
                    if (args.Length < 2) { ReplyRaw(player, "Admin.Usage"); return; }
                    var target = BasePlayer.Find(args[1]);
                    if (target == null) { ReplyRaw(player, "Admin.NoPlayer", args[1]); return; }
                    var record = GetPlayer((ulong)target.userID, false);
                    if (record != null) record.Earned = 0;
                    ReplyRaw(player, "Admin.Reset", target.displayName);
                    return;
                }
                default:
                    ReplyRaw(player, "Admin.Usage");
                    return;
            }
        }

        private void TellInfo(BasePlayer player)
        {
            if (liveStalls.Count == 0) { Reply(player, "Info.None"); return; }
            Reply(player, "Info.Stalls");
            foreach (var live in liveStalls)
                ReplyRaw(player, "Info.StallLine", live.Stall.Name, MapHelper.PositionToString(live.Position));
            if (config.DailyCap <= 0) return;

            ulong userId = (ulong)player.userID;
            double allowance = Allowance(userId);
            if (allowance >= config.DailyCap) ReplyRaw(player, "Info.AllowanceFull", config.DailyCap);
            else ReplyRaw(player, "Info.Allowance", FormatScrap(allowance), FormatSpan(DayResetsIn(userId)));
        }

        [ConsoleCommand("farmersmarket.ui")]
        private void CmdUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;

            string action = arg.GetString(0).ToLower();
            if (action == "close") { ClosePanel(player); return; }

            // The panel only opens at a stall, but re-check on every click.
            var session = SessionOf(player);
            if (session == null || !MayUse(player)) return;

            switch (action)
            {
                case "sell":
                    SellFromPlayer(player, session.Live.Stall, arg.GetString(1));
                    break;
                case "sellall":
                    SellFromPlayer(player, session.Live.Stall, null);
                    break;
                case "buy":
                    BuyFromStall(player, session.Live.Stall, arg.GetString(1), arg.GetInt(2, 1));
                    break;
                case "buypage":
                    session.BuyPage = Mathf.Max(0, arg.GetInt(1));
                    break;
                case "sellpage":
                    session.SellPage = Mathf.Max(0, arg.GetInt(1));
                    break;
                default:
                    return;
            }
            ShowPanel(player);
        }

        #endregion

        #region CUI

        private const string ColBg = "0.09 0.09 0.10 0.97";
        private const string ColRow = "0.15 0.15 0.16 1";
        private const string ColBtn = "0.25 0.25 0.25 1";
        private const string ColGo = "0.30 0.55 0.20 1";
        private const string ColClose = "0.45 0.18 0.18 1";
        private const string ColText = "0.88 0.88 0.88 1";
        private const string ColDim = "0.6 0.6 0.6 1";
        private const string ColTitle = "0.55 0.76 0.29 1";
        private const string ColScrap = "0.98 0.65 0.15 1";

        private static string Anchor(float x, float y) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.####} {1:0.####}", x, y);

        private static void AddLabel(CuiElementContainer ui, string parent, string text, int size, TextAnchor align, string color, float x0, float y0, float x1, float y1) =>
            ui.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Align = align, Color = color },
                RectTransform = { AnchorMin = Anchor(x0, y0), AnchorMax = Anchor(x1, y1) }
            }, parent);

        private static void AddButton(CuiElementContainer ui, string parent, string text, int size, string color, string command, float x0, float y0, float x1, float y1) =>
            ui.Add(new CuiButton
            {
                Button = { Color = color, Command = command },
                Text = { Text = text, FontSize = size, Align = TextAnchor.MiddleCenter, Color = ColText },
                RectTransform = { AnchorMin = Anchor(x0, y0), AnchorMax = Anchor(x1, y1) }
            }, parent);

        private static string AddRow(CuiElementContainer ui, string parent, ItemDefinition def, float x0, float y0, float x1, float y1)
        {
            string row = ui.Add(new CuiPanel
            {
                Image = { Color = ColRow },
                RectTransform = { AnchorMin = Anchor(x0, y0), AnchorMax = Anchor(x1, y1) }
            }, parent);
            ui.Add(new CuiElement
            {
                Parent = row,
                Components =
                {
                    new CuiImageComponent { ItemId = def.itemid },
                    new CuiRectTransformComponent { AnchorMin = "0.01 0.08", AnchorMax = "0.075 0.92" }
                }
            });
            return row;
        }

        private static void AddPager(CuiElementContainer ui, string parent, string command, int page, int pages, float x0, float y0, float x1, float y1)
        {
            if (pages <= 1) return;
            float mid = (x0 + x1) / 2f;
            if (page > 0) AddButton(ui, parent, "◀", 10, ColBtn, $"farmersmarket.ui {command} {page - 1}", x0, y0, x0 + 0.035f, y1);
            AddLabel(ui, parent, $"{page + 1} / {pages}", 10, TextAnchor.MiddleCenter, ColDim, mid - 0.04f, y0, mid + 0.04f, y1);
            if (page < pages - 1) AddButton(ui, parent, "▶", 10, ColBtn, $"farmersmarket.ui {command} {page + 1}", x1 - 0.035f, y0, x1, y1);
        }

        private void ShowPanel(BasePlayer player)
        {
            var session = SessionOf(player);
            if (session == null) return;
            var stall = session.Live.Stall;
            ulong userId = (ulong)player.userID;
            string uid = player.UserIDString;

            CuiHelper.DestroyUi(player, UiPanelName);
            var ui = new CuiElementContainer();
            var panel = ui.Add(new CuiPanel
            {
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.20 0.15", AnchorMax = "0.80 0.88" },
                CursorEnabled = true
            }, "Overlay", UiPanelName);

            string subtitle = Msg("Ui.Subtitle", uid);
            if (config.DailyCap > 0)
            {
                double allowance = Allowance(userId);
                if (allowance < 0.05) subtitle = Msg("Ui.SubtitleCapOut", uid, FormatSpan(DayResetsIn(userId)));
                else if (allowance < config.DailyCap) subtitle = Msg("Ui.SubtitleCap", uid, FormatScrap(allowance));
            }
            AddLabel(ui, panel, stall.Name.ToUpper(), 18, TextAnchor.MiddleCenter, ColTitle, 0f, 0.93f, 1f, 0.995f);
            AddLabel(ui, panel, subtitle, 11, TextAnchor.MiddleCenter, ColDim, 0f, 0.895f, 1f, 0.935f);
            // Added after the title label: the label spans the full width and would swallow the click otherwise.
            AddButton(ui, panel, "✕", 13, ColClose, "farmersmarket.ui close", 0.955f, 0.945f, 0.99f, 0.99f);

            const float top = 0.84f, rowH = 0.062f, gap = 0.006f;
            AddLabel(ui, panel, Msg("Ui.BuysHeader", uid), 12, TextAnchor.MiddleLeft, ColTitle, 0.02f, 0.845f, 0.49f, 0.89f);
            AddLabel(ui, panel, Msg("Ui.SellsHeader", uid), 12, TextAnchor.MiddleLeft, ColTitle, 0.51f, 0.845f, 0.98f, 0.89f);

            // Left: what the player is carrying that this stall buys, each priced as if sold on its own.
            var carried = Carried(player);
            var held = new List<SaleLine>();
            foreach (var goods in stall.Goods)
            {
                if (!goods.Buys) continue;
                bool has = false;
                foreach (var item in carried)
                    if (item != null && item.info == goods.Def) { has = true; break; }
                if (!has) continue;
                var quote = Sell(userId, carried, stall, goods.Def.shortname, false);
                held.Add(quote.Lines.Count > 0 ? quote.Lines[0] : new SaleLine { Def = goods.Def });
            }

            if (held.Count == 0)
            {
                AddLabel(ui, panel, Msg("Ui.NothingHeld", uid), 11, TextAnchor.UpperLeft, ColDim, 0.02f, 0.78f, 0.49f, 0.83f);
                var names = new List<string>();
                foreach (var goods in stall.Goods)
                    if (goods.Buys) names.Add(goods.Def.displayName.english);
                if (names.Count > 0)
                    AddLabel(ui, panel, Msg("Ui.WeBuy", uid, string.Join(", ", names.ToArray())), 10, TextAnchor.UpperLeft, ColDim, 0.02f, 0.30f, 0.49f, 0.78f);
            }
            else
            {
                int pages = (held.Count + RowsPerPage - 1) / RowsPerPage;
                session.BuyPage = Mathf.Clamp(session.BuyPage, 0, pages - 1);
                double now = Now;
                for (int i = 0; i < RowsPerPage; i++)
                {
                    int index = session.BuyPage * RowsPerPage + i;
                    if (index >= held.Count) break;
                    var line = held[index];
                    float y1 = top - i * rowH, y0 = y1 - rowH + gap;
                    string row = AddRow(ui, panel, line.Def, 0.02f, y0, 0.49f, y1);

                    int carriedUnits = 0;
                    foreach (var item in carried)
                        if (item != null && item.info == line.Def) carriedUnits += item.amount;
                    var goods = stall.ByShortname[line.Def.shortname];
                    double multiplier = DemandMultiplier(DemandPaid(line.Def.shortname, now));
                    string each = Msg(multiplier < 0.995 ? "Ui.EachLow" : "Ui.Each", uid, FormatScrap(goods.Pays * multiplier));

                    AddLabel(ui, row, $"{line.Def.displayName.english} ×{carriedUnits}", 11, TextAnchor.MiddleLeft, ColText, 0.09f, 0f, 0.50f, 1f);
                    AddLabel(ui, row, each, 9, TextAnchor.MiddleLeft, ColDim, 0.50f, 0f, 0.70f, 1f);
                    AddLabel(ui, row, FormatScrap(line.Scrap), 11, TextAnchor.MiddleRight, ColScrap, 0.70f, 0f, 0.82f, 1f);
                    if (line.Units > 0)
                        AddButton(ui, row, Msg("Ui.Sell", uid), 10, ColGo, $"farmersmarket.ui sell {line.Def.shortname}", 0.84f, 0.12f, 0.99f, 0.88f);
                }
                AddPager(ui, panel, "buypage", session.BuyPage, pages, 0.02f, 0.105f, 0.49f, 0.145f);

                var everything = Sell(userId, carried, stall, null, false);
                if (everything.Units > 0)
                    AddButton(ui, panel, Msg("Ui.SellAll", uid, FormatScrap(everything.Total)), 11, ColGo, "farmersmarket.ui sellall", 0.02f, 0.045f, 0.49f, 0.095f);
            }

            // Right: what the stall sells.
            var selling = new List<Goods>();
            foreach (var goods in stall.Goods)
                if (goods.Sells) selling.Add(goods);

            if (selling.Count == 0)
                AddLabel(ui, panel, Msg("Ui.NothingSold", uid), 11, TextAnchor.UpperLeft, ColDim, 0.51f, 0.78f, 0.98f, 0.83f);
            else
            {
                int pages = (selling.Count + RowsPerPage - 1) / RowsPerPage;
                session.SellPage = Mathf.Clamp(session.SellPage, 0, pages - 1);
                for (int i = 0; i < RowsPerPage; i++)
                {
                    int index = session.SellPage * RowsPerPage + i;
                    if (index >= selling.Count) break;
                    var goods = selling[index];
                    float y1 = top - i * rowH, y0 = y1 - rowH + gap;
                    string row = AddRow(ui, panel, goods.Def, 0.51f, y0, 0.98f, y1);

                    AddLabel(ui, row, goods.Def.displayName.english, 11, TextAnchor.MiddleLeft, ColText, 0.09f, 0f, 0.55f, 1f);
                    AddLabel(ui, row, FormatScrap(goods.Charges), 11, TextAnchor.MiddleRight, ColScrap, 0.55f, 0f, 0.68f, 1f);
                    AddButton(ui, row, "×1", 10, ColBtn, $"farmersmarket.ui buy {goods.Def.shortname} 1", 0.71f, 0.12f, 0.84f, 0.88f);
                    if (goods.Def.stackable >= 10)
                        AddButton(ui, row, "×10", 10, ColBtn, $"farmersmarket.ui buy {goods.Def.shortname} 10", 0.86f, 0.12f, 0.99f, 0.88f);
                }
                AddPager(ui, panel, "sellpage", session.SellPage, pages, 0.51f, 0.105f, 0.98f, 0.145f);
            }

            var scrapDef = ItemManager.FindItemDefinition(ScrapShortname);
            int scrapHeld = scrapDef != null ? player.inventory.GetAmount(scrapDef.itemid) : 0;
            AddLabel(ui, panel, Msg("Ui.Scrap", uid, scrapHeld), 11, TextAnchor.MiddleRight, ColScrap, 0.51f, 0.045f, 0.98f, 0.095f);

            var record = GetPlayer(userId, false);
            AddLabel(ui, panel, Msg("Ui.Change", uid, FormatScrap(record != null ? record.Change : 0)), 9, TextAnchor.MiddleCenter, ColDim, 0f, 0.005f, 1f, 0.04f);

            CuiHelper.AddUi(player, ui);
        }

        #endregion

        #region HelpMenu

        // object GetHelpInfo() — the HelpMenu plugin's entry (its hook schema).
        [HookMethod("GetHelpInfo")]
        private object GetHelpInfo()
        {
            var commands = new List<Dictionary<string, object>>
            {
                HelpCommand("/market", "", Msg("Help.Cmd.Info", null), ""),
                HelpCommand("/market", "set <stall> [npc] | remove <stall> | list | reset <player>", Msg("Help.Cmd.Admin", null), PermAdmin),
            };
            return new Dictionary<string, object>
            {
                ["Plugin"] = Name,
                ["Title"] = Msg("Help.Title", null),
                ["Description"] = Msg("Help.Description", null),
                ["Commands"] = commands,
                ["HowTo"] = Msg("Help.HowTo", null).Split('\n'),
                ["Notes"] = Msg("Help.Notes", null).Split('\n'),
                ["Order"] = 34,   // sidebar: after Public Works and Public Lockers
            };
        }

        private static Dictionary<string, object> HelpCommand(string command, string args, string description, string perm) =>
            new Dictionary<string, object>
            {
                ["Command"] = command,
                ["Args"] = args,
                ["Description"] = description,
                ["Permission"] = perm,
                ["AuthLevel"] = 0,
            };

        #endregion
    }
}
