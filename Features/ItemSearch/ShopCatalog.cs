using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Shops;
using StardewValley.Internal;

namespace PelicanMemory.Features.ItemSearch;

/// <summary>One shop selling an item today, and at what price.</summary>
/// <param name="ShopId">The shop's ID in <c>Data/Shops</c>, which names it in the translations.</param>
/// <param name="Price">The price in the shop's currency, when it's sold for money.</param>
/// <param name="Currency">The shop's currency: 0 gold, 1 star tokens, 2 Qi coins, 4 Qi gems.</param>
/// <param name="TradeItemId">The item asked in exchange, for shops which trade instead of selling.</param>
/// <param name="TradeCount">How many of the trade item are asked.</param>
internal record ShopOffer(string ShopId, int Price, int Currency, string? TradeItemId, int TradeCount);

/// <summary>What the shops the player has been to sell today.</summary>
/// <remarks>
/// Only shops the player has walked into, and only what they sell today, read with the same code the shop itself uses
/// to fill its shelves: a seed out of season or a cart that isn't in town today says nothing. That's what the player
/// would see by going there, no more. Reading a shop this way changes nothing in the game (checked in its code).
/// </remarks>
internal class ShopCatalog
{
    /*********
    ** Fields
    *********/
    /// <summary>The shops the search knows, with the place the player must have visited to know them.</summary>
    /// <remarks>
    /// Left out on purpose: furniture catalogues (not shops), tool upgrades, recovery of lost items, festival stalls,
    /// the cinema, and shops hidden behind a discovery the visit doesn't prove (the hat mouse, the raccoons, the
    /// volcano dwarf).
    /// </remarks>
    private static readonly (string ShopId, string Location, Func<bool> IsOpenToday)[] Shops =
    {
        ("SeedShop", "SeedShop", Always),
        ("Carpenter", "ScienceHouse", Always),
        ("AnimalShop", "AnimalShop", Always),
        ("Blacksmith", "Blacksmith", Always),
        ("AdventureShop", "AdventureGuild", Always),
        ("FishShop", "FishShop", Always),
        ("Saloon", "Saloon", Always),
        ("Joja", "JojaMart", Always),
        ("Hospital", "Hospital", Always),
        ("IceCreamStand", "Town", () => Game1.season == Season.Summer),
        ("Bookseller", "Town", () => Utility.getDaysOfBooksellerThisSeason().Contains(Game1.dayOfMonth)),
        ("Traveler", "Forest", () => Game1.dayOfMonth % 7 % 5 == 0),
        ("ShadowShop", "Sewer", Always),
        ("Dwarf", "Mine", () => Game1.player.canUnderstandDwarves),
        ("Sandy", "SandyHouse", Always),
        ("DesertTrade", "Desert", Always),
        ("Casino", "Club", Always),
        ("QiGemShop", "QiNutRoom", Always),
        ("IslandTrade", "IslandNorth", Always),
        ("ResortBar", "IslandSouth", Always)
    };

    /// <summary>A shop row drawn from the game's shared random each time the shop opens, so it can't be told in advance.</summary>
    private static readonly Regex UnsyncedRandom = new(@"(^|,)\s*!?RANDOM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IMonitor Monitor;

    /// <summary>The offers found today, by qualified item ID, or <c>null</c> if they must be read again.</summary>
    private Dictionary<string, List<ShopOffer>>? Cache;

    /// <summary>The day the cache was built, so it's rebuilt when the stock changes.</summary>
    private int CacheDay = -1;


    /*********
    ** Public methods
    *********/
    public ShopCatalog(IMonitor monitor)
    {
        this.Monitor = monitor;
    }

    /// <summary>Forget what was read, so it's read again next time.</summary>
    public void Invalidate()
    {
        this.Cache = null;
    }

    /// <summary>Get every item sold today in a shop the player has visited, with where and for how much.</summary>
    public IReadOnlyDictionary<string, List<ShopOffer>> GetAll()
    {
        if (this.Cache != null && this.CacheDay == Game1.Date.TotalDays)
            return this.Cache;

        this.CacheDay = Game1.Date.TotalDays;
        return this.Cache = this.Read();
    }

    /// <summary>Get where an item is sold today.</summary>
    public IReadOnlyList<ShopOffer> Get(string qualifiedItemId)
    {
        return this.GetAll().TryGetValue(qualifiedItemId, out List<ShopOffer>? offers) ? offers : Array.Empty<ShopOffer>();
    }


    /*********
    ** Private methods
    *********/
    private static bool Always() => true;

    /// <summary>Read today's stock of every shop the player knows.</summary>
    private Dictionary<string, List<ShopOffer>> Read()
    {
        Dictionary<string, List<ShopOffer>> offers = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ShopData> shops = DataLoader.Shops(Game1.content);

        foreach ((string shopId, string location, Func<bool> isOpenToday) in Shops)
        {
            try
            {
                if (!Game1.player.locationsVisited.Contains(location) || !shops.TryGetValue(shopId, out ShopData? data) || !isOpenToday())
                    continue;

                foreach ((ISalable salable, ItemStockInformation stock) in ShopBuilder.GetShopStock(shopId, WithoutUnsyncedRandom(data)))
                {
                    // recipes and licences aren't items to look for; sold-out rows aren't on the shelf
                    if (salable is not Item item || item.IsRecipe || stock.Stock <= 0)
                        continue;

                    if (!offers.TryGetValue(item.QualifiedItemId, out List<ShopOffer>? list))
                        offers[item.QualifiedItemId] = list = new List<ShopOffer>();

                    if (list.All(offer => offer.ShopId != shopId))
                        list.Add(new ShopOffer(shopId, stock.Price, data.Currency, stock.TradeItem, stock.TradeItemCount ?? 1));
                }
            }
            catch (Exception ex)
            {
                // one shop changed by another mod mustn't hide the others
                this.Monitor.LogOnce($"Failed to read the stock of shop '{shopId}':\n{ex}", LogLevel.Warn);
            }
        }

        return offers;
    }

    /// <summary>Get a shop's data without the rows the game draws at random each time the shop opens.</summary>
    /// <remarks>
    /// A few of Robin's furniture rows are picked from the game's shared random, unlike the travelling cart which
    /// draws from the day: reading them here would announce furniture the shop might not show, and use up the random.
    /// </remarks>
    private static ShopData WithoutUnsyncedRandom(ShopData data)
    {
        if (data.Items is null || !data.Items.Any(item => item.Condition != null && UnsyncedRandom.IsMatch(item.Condition)))
            return data;

        ShopData copy = (ShopData)typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(data, null)!;
        copy.Items = data.Items.Where(item => item.Condition is null || !UnsyncedRandom.IsMatch(item.Condition)).ToList();
        return copy;
    }
}
