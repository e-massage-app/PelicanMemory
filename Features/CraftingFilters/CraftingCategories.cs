using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Objects;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.CraftingFilters;

/// <summary>Sorts crafting recipes into a handful of readable families.</summary>
/// <remarks>
/// The game has no notion of a crafting category, so each recipe is placed by what it produces, read straight from
/// the game's data files: the item's category, the list of machines, its context tags. The few items the data can't
/// tell apart are matched on their internal English name, which doesn't change with the language.
/// </remarks>
internal static class CraftingCategories
{
    /*********
    ** Fields
    *********/
    /// <summary>Floors and paths share the furniture category.</summary>
    private const int FloorCategory = SObject.furnitureCategory;

    /// <summary>Fishing gear the data can't tell apart from general crafting.</summary>
    private static readonly HashSet<string> FishingGear = new() { "(O)710", "(BC)BaitMaker", "(BC)FishSmoker" };

    /// <summary>Farm equipment the data can't tell apart from general crafting.</summary>
    private static readonly HashSet<string> FarmGear = new() { "(BC)62", "(BC)25", "(BC)Hopper" };

    /// <summary>Internal names which give a family away when nothing in the data does.</summary>
    private static readonly (string Fragment, string Category)[] NameRules =
    {
        ("Fence", "farm"),
        ("Gate", "farm"),
        ("Sprinkler", "farm"),
        ("Scarecrow", "farm"),
        ("Grass Starter", "farm"),
        ("Brazier", "decor"),
        ("Lamp-post", "decor"),
        ("Statue", "decor"),
        ("Campfire", "decor"),
        ("Jukebox", "decor"),
        ("Lantern", "decor"),
        ("Block", "decor"),
        ("Staircase", "adventure"),
        ("Ammo", "adventure"),
        ("Kit", "adventure"),
        ("Snack", "adventure"),
        ("Steak", "adventure"),
        ("Musk", "adventure"),
        ("Chest", "misc"),
        ("Workbench", "misc"),
        ("Hopper", "misc"),
        ("Forge", "misc"),
        ("Computer", "misc")
    };

    /// <summary>The cached family per recipe name, since the same recipes are sorted on every filter change.</summary>
    private static readonly Dictionary<string, string> Cache = new();


    /*********
    ** Public methods
    *********/
    /// <summary>Every family, in the order the tabs are shown.</summary>
    public static readonly string[] Ids = { "all", "farm", "fishing", "machines", "decor", "adventure", "misc" };

    /// <summary>The item whose sprite stands for each family.</summary>
    public static string GetIcon(string id)
    {
        return id switch
        {
            "farm" => "(O)599",       // sprinkler
            "fishing" => "(O)685",    // bait
            "machines" => "(BC)13",   // furnace
            "decor" => "(O)328",      // wood floor
            "adventure" => "(O)287",  // bomb
            "misc" => "(BC)130",      // chest
            _ => "(O)388"             // wood, for "everything"
        };
    }

    /// <summary>Forget the sorted recipes, so a content pack's changes are picked up again.</summary>
    public static void Reset()
    {
        Cache.Clear();
    }

    /// <summary>Get which family a recipe belongs to.</summary>
    public static string Classify(string recipeName)
    {
        if (Cache.TryGetValue(recipeName, out string? cached))
            return cached;

        string category;
        try
        {
            category = Sort(recipeName);
        }
        catch
        {
            category = "misc";
        }

        return Cache[recipeName] = category;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get which family the output of a recipe belongs to.</summary>
    private static string Sort(string recipeName)
    {
        if (!DataLoader.CraftingRecipes(Game1.content).TryGetValue(recipeName, out string? raw))
            return "misc";

        // "ingredients/unused/output [count]/bigCraftable/unlock conditions/display name"
        string[] fields = raw.Split('/');
        if (fields.Length < 4)
            return "misc";

        string itemId = fields[2].Split(' ')[0];
        bool isBigCraftable = fields[3].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        string qualifiedId = (isBigCraftable ? ItemRegistry.type_bigCraftable : ItemRegistry.type_object) + itemId;

        string name;
        int category;
        string type;
        List<string>? tags;

        if (isBigCraftable)
        {
            if (!DataLoader.BigCraftables(Game1.content).TryGetValue(itemId, out BigCraftableData? data))
                return "misc";
            (name, category, type, tags) = (data.Name, 0, "", data.ContextTags);
        }
        else
        {
            if (!DataLoader.Objects(Game1.content).TryGetValue(itemId, out ObjectData? data))
                return "misc";
            (name, category, type, tags) = (data.Name, data.Category, data.Type ?? "", data.ContextTags);
        }

        if (category is SObject.baitCategory or SObject.tackleCategory || FishingGear.Contains(qualifiedId))
            return "fishing";

        if (category is SObject.fertilizerCategory or SObject.SeedsCategory || FarmGear.Contains(qualifiedId))
            return "farm";

        if (DataLoader.Machines(Game1.content).ContainsKey(qualifiedId))
            return "machines";

        if (category == FloorCategory)
            return "decor";

        if (type == "Ring" || category == SObject.ringCategory)
            return "adventure";

        if (HasTag(tags, "sign_item") || HasTag(tags, "light_source"))
            return "decor";

        if (HasTag(tags, "bomb_item") || HasTag(tags, "totem_item") || HasTag(tags, "potion_item"))
            return "adventure";

        foreach ((string fragment, string byName) in NameRules)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return byName;
        }

        // anything else you place on the farm is nearer to decoration than to a tool
        return isBigCraftable ? "decor" : "misc";
    }

    /// <summary>Get whether an item's data carries a context tag.</summary>
    private static bool HasTag(List<string>? tags, string tag)
    {
        return tags?.Any(entry => entry.Equals(tag, StringComparison.OrdinalIgnoreCase)) == true;
    }
}
