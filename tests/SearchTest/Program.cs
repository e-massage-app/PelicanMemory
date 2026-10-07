using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Shops;

// Checks the item search outside the game:
// - finding names the way a player types them (accents, ligatures, capitals, word starts);
// - leaving out the shop rows the game rerolls each time a shop opens;
// - laying every menu patch on the real game DLL, so a renamed method or parameter fails here, not in the game.
System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
    return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
};
Run();

static void Run()
{
    const string gameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley";
    Assembly mod = Assembly.Load("PelicanMemory");
    Type T(string name) => mod.GetType(name, true)!;

    int failures = 0;
    void Check(bool ok, string label) { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {label}"); if (!ok) failures++; }

    // 1. names
    {
        Type matcher = T("PelicanMemory.Features.ItemSearch.NameMatcher");
        Type entryType = T("PelicanMemory.Features.ItemSearch.SearchEntry");
        MethodInfo find = matcher.GetMethod("Find")!;
        MethodInfo normalize = matcher.GetMethod("Normalize")!;

        string[] names = { "Pêche", "Œuf", "Grand œuf brun", "Pierre", "Pierre de lune", "Pierreries de feu", "Céleri-rave", "Plat d'épinards", "Mayonnaise d’œuf de canard", "Huître" };
        IList entries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType))!;
        foreach (string name in names)
            entries.Add(Activator.CreateInstance(entryType, "(O)" + name, name));

        string[] Find(string query) => ((IEnumerable)find.Invoke(null, new object[] { entries, query, 50 })!)
            .Cast<object>().Select(e => (string)entryType.GetProperty("Name")!.GetValue(e)!).ToArray();

        Check(Find("peche").SequenceEqual(new[] { "Pêche" }), "1. no accents typed: « peche » finds « Pêche »");
        Check(Find("OEUF").Take(2).SequenceEqual(new[] { "Œuf", "Grand œuf brun" }) && Find("oeuf").Contains("Mayonnaise d’œuf de canard"), "1. ligature typed as two letters: « oeuf » finds the eggs, the name starting with it first");
        Check(Find("pierre").SequenceEqual(new[] { "Pierre", "Pierre de lune", "Pierreries de feu" }), "1. shortest name starting with it comes first");
        Check(Find("lune").SequenceEqual(new[] { "Pierre de lune" }), "1. a word inside the name is found");
        Check(Find("d'oeuf").SequenceEqual(new[] { "Mayonnaise d’œuf de canard" }), "1. a straight apostrophe finds a curly one");
        Check(Find("  ").Length == 0 && Find("").Length == 0, "1. nothing typed finds nothing");
        Check(Find("xyz").Length == 0, "1. an unknown name finds nothing");
        Check((string)normalize.Invoke(null, new object[] { "  Céleri   RAVE " })! == "celeri rave", "1. spaces and capitals are evened out");
    }

    // 2. shop rows rerolled each time the shop opens
    {
        Game1.content = new LocalizedContentManager(new GameServiceContainer(), Path.Combine(gameDir, "Content"));
        Dictionary<string, ShopData> shops = DataLoader.Shops(Game1.content);
        MethodInfo without = T("PelicanMemory.Features.ItemSearch.ShopCatalog").GetMethod("WithoutUnsyncedRandom", BindingFlags.Static | BindingFlags.NonPublic)!;

        ShopData robin = shops["Carpenter"];
        ShopData filtered = (ShopData)without.Invoke(null, new object[] { robin })!;
        int removed = robin.Items.Count - filtered.Items.Count;
        Check(removed == 5 && filtered.Items.All(item => item.Condition is null || !item.Condition.Contains("RANDOM ") || item.Condition.Contains("SYNCED_RANDOM")), $"2. Robin's 5 rerolled furniture rows are left out ({removed} removed)");
        Check(!ReferenceEquals(filtered, robin) && robin.Items.Count == filtered.Items.Count + removed && filtered.Currency == robin.Currency, "2. the game's own shop data is untouched (a copy is filtered)");

        ShopData cart = shops["Traveler"];
        Check(ReferenceEquals(without.Invoke(null, new object[] { cart }), cart), "2. the travelling cart, drawn from the day, is read as is");
    }

    // 3. the menu patches, laid on the real game DLL
    {
        Harmony harmony = new("PelicanMemory.SearchTest");
        object registry = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(T("PelicanMemory.Core.FeatureRegistry"));
        MethodInfo apply = T("PelicanMemory.UI.GameMenuTab").GetMethod("Apply")!;

        try
        {
            apply.Invoke(null, new object[] { harmony, new SilentMonitor(), registry });
            string[] patched = harmony.GetPatchedMethods().Select(method => $"{method.DeclaringType?.Name}.{method.Name}").OrderBy(name => name).ToArray();
            Check(patched.Contains("GameMenu.changeTab") && patched.Contains("GameMenu.receiveKeyPress") && patched.Contains("GameMenu..ctor") && patched.Contains("GameMenu.getTabNumberFromName") && patched.Contains("GameMenu.draw"),
                $"3. every menu patch applies to the game ({string.Join(", ", patched)})");
        }
        catch (Exception ex)
        {
            Check(false, $"3. menu patches failed: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    // 4. where museum items come from: the data carried in the mod matches the game's own data
    {
        Type sources = T("PelicanMemory.Features.CollectionHints.CollectionSources");
        MethodInfo getSources = sources.GetMethod("GetSources")!;
        MethodInfo getGeode = sources.GetMethod("GetGeodeItems")!;

        Dictionary<string, StardewValley.GameData.Objects.ObjectData> objects = DataLoader.Objects(Game1.content);
        Dictionary<string, string> monsters = DataLoader.Monsters(Game1.content);
        Dictionary<string, StardewValley.GameData.Locations.LocationData> locations = DataLoader.Locations(Game1.content);
        string[] museum = objects.Where(pair => pair.Value.Type is "Minerals" or "Arch").Select(pair => pair.Key).ToArray();
        string[] kinds = { "geode", "mineNode", "artifactSpot", "fishingTreasure", "panning", "monster", "fishPond", "shop", "special" };

        List<string> missing = new(), badKinds = new(), badGeodes = new(), badMonsters = new(), badPlaces = new();
        foreach (string id in museum)
        {
            IEnumerable list = (IEnumerable)getSources.Invoke(null, new object[] { id })!;
            List<object> entries = list.Cast<object>().ToList();
            if (entries.Count == 0)
                missing.Add(id);

            foreach (object entry in entries)
            {
                string kind = (string)entry.GetType().GetProperty("Kind")!.GetValue(entry)!;
                string detail = (string)entry.GetType().GetProperty("Detail")!.GetValue(entry)!;
                System.Text.Json.JsonElement requires = (System.Text.Json.JsonElement)entry.GetType().GetProperty("Requires")!.GetValue(entry)!;

                if (!kinds.Contains(kind))
                    badKinds.Add($"{id}:{kind}");
                if (kind == "geode" && !objects.ContainsKey(detail.Replace("(O)", "")))
                    badGeodes.Add($"{id}:{detail}");
                if (requires.ValueKind == System.Text.Json.JsonValueKind.Object && requires.TryGetProperty("monsterKilled", out var killed))
                    badMonsters.AddRange(killed.EnumerateArray().Select(n => n.GetString()!).Where(n => !monsters.ContainsKey(n)));
                if (kind == "artifactSpot" && detail != "Farm" && !locations.ContainsKey(detail)) // the farm is "Farm" in game, stored per farm type in the data
                    badPlaces.Add($"{id}:{detail}");
            }
        }

        Check(museum.Length == 95 && missing.Count == 0, $"4. every museum item ({museum.Length}) has at least one source ({missing.Count} without)");
        Check(badKinds.Count == 0, $"4. every source is a kind the tooltip knows ({string.Join(", ", badKinds.Distinct())})");
        Check(badGeodes.Count == 0, $"4. every geode named is a real game item ({string.Join(", ", badGeodes.Distinct())})");
        Check(badMonsters.Distinct().All(n => n == "Haunted Skull"), $"4. every monster named exists in the game's data, except the haunted skull, a bat variant ({string.Join(", ", badMonsters.Distinct())})");
        Check(badPlaces.Count == 0, $"4. every artifact spot place exists in the game's data ({string.Join(", ", badPlaces.Distinct())})");

        int magma = ((IEnumerable)getGeode.Invoke(null, new object[] { "(O)537" })!).Cast<object>().Count();
        int omni = ((IEnumerable)getGeode.Invoke(null, new object[] { "(O)749" })!).Cast<object>().Count();
        Check(magma > 0 && omni > magma && getGeode.Invoke(null, new object[] { "(O)388" }) is null, $"4. geodes know their contents (magma {magma}, omni {omni}), and wood isn't a geode");

        try
        {
            Harmony harmony = new("PelicanMemory.SearchTest.Collections");
            harmony.Patch(AccessTools.Method(typeof(StardewValley.Menus.CollectionsPage), nameof(StardewValley.Menus.CollectionsPage.performHoverAction)),
                postfix: new HarmonyMethod(T("PelicanMemory.Features.CollectionHints.CollectionHintsFeature").GetMethod("After_PerformHoverAction", BindingFlags.Static | BindingFlags.NonPublic)!));
            Check(true, "4. the collections tooltip hook applies to the game");
        }
        catch (Exception ex)
        {
            Check(false, $"4. collections hook: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    Console.WriteLine(failures == 0 ? "\nALL SEARCH CHECKS PASSED" : $"\n{failures} FAILURES");
    Environment.ExitCode = failures == 0 ? 0 : 1;
}

/// <summary>A monitor which writes nothing, for code which logs.</summary>
internal class SilentMonitor : IMonitor
{
    public bool IsVerbose => false;
    public void Log(string message, LogLevel level = LogLevel.Trace) => Console.WriteLine($"  [{level}] {message}");
    public void LogOnce(string message, LogLevel level = LogLevel.Trace) => this.Log(message, level);
    public void VerboseLog(string message) { }
    public void VerboseLog(ref StardewModdingAPI.Framework.Logging.VerboseLogStringHandler message) { }
}
