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

    // 5. secret notes ticked, and Gil's goals in the search tab
    {
        Game1.content = new LocalizedContentManager(new GameServiceContainer(), Path.Combine(gameDir, "Content"));
        Game1.objectData = DataLoader.Objects(Game1.content);
        Game1.bigCraftableData = DataLoader.BigCraftables(Game1.content);
        Game1.toolData = DataLoader.Tools(Game1.content);
        Game1.weaponData = DataLoader.Weapons(Game1.content);
        Game1.pantsData = DataLoader.Pants(Game1.content);
        Game1.shirtData = DataLoader.Shirts(Game1.content);
        typeof(ItemRegistry).GetMethod("RegisterItemTypes", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        ItemRegistry.ResetCache();

        // a bare player, with its synced fields created (as in the deposit bench)
        Farmer player = (Farmer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Farmer));
        foreach (FieldInfo field in typeof(Farmer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.GetValue(player) is null && field.FieldType.Namespace?.StartsWith("Netcode") == true && !field.FieldType.IsAbstract && field.FieldType.GetConstructor(Type.EmptyTypes) != null)
                field.SetValue(player, Activator.CreateInstance(field.FieldType));
        }
        player.giftedItems = new();

        Type checks = T("PelicanMemory.Features.SecretNotesDone.SecretNoteChecks");
        MethodInfo isDone = checks.GetMethod("IsDone")!;
        MethodInfo giftProgress = checks.GetMethod("GetGiftProgress")!;
        Dictionary<int, string> notes = DataLoader.SecretNotes(Game1.content);
        bool? Done(int id) => (bool?)isDone.Invoke(null, new object?[] { player, id, notes[id] });
        (int Given, int Total) Gifts(int id) => ((int, int))giftProgress.Invoke(null, new object?[] { player, notes[id] })!;

        // what each note is: decided by the game, or ticked by hand
        List<int> automatic = new(), manual = new();
        foreach (int id in notes.Keys.Where(id => id != 1009)) // the flute tune reads the team, which a bare player hasn't
            (Done(id).HasValue ? automatic : manual).Add(id);
        Console.WriteLine($"     ticked by the game: {string.Join(", ", automatic)}");
        Console.WriteLine($"     ticked by hand: {string.Join(", ", manual)}");
        Check(notes.Count == 38 && automatic.Count == 26 && manual.Count == 11, $"5. the 38 notes split into 26 the game records (+ the flute tune) and 11 ticked by hand ({automatic.Count} / {manual.Count})");
        Check(automatic.All(id => Done(id) == false), "5. a new player has done none of them");

        // the gift notes: every gift named is a real villager and item, and only a gift given counts
        HashSet<string> villagers = DataLoader.Characters(Game1.content).Keys.ToHashSet();
        bool giftsParsed = Enumerable.Range(1, 9).All(id => Gifts(id).Total > 0);
        var revealed = System.Text.RegularExpressions.Regex.Matches(string.Join(" ", Enumerable.Range(1, 9).Select(id => notes[id])), @"%revealtaste:([^:\s#%$\{\^\*]+):([^\s#%$\{\^\*""]+)")
            .Select(m => (Npc: m.Groups[1].Value, Item: m.Groups[2].Value)).Distinct().ToList();
        Check(giftsParsed && revealed.All(g => villagers.Contains(g.Npc) && ItemRegistry.GetMetadata(g.Item)?.Exists() == true), $"5. notes 1 to 9 each name gifts, all real villagers and items ({revealed.Count} gifts)");

        foreach (var (npc, item) in revealed.Where(g => notes[3].Contains($"{g.Npc}:{g.Item}")))
            player.revealGiftTaste(npc, item);
        Check(Done(3) == false && Gifts(3).Given == 0, "5. reading a gift note (which reveals its tastes at 0) doesn't count as giving");
        var note3 = revealed.Where(g => notes[3].Contains($"{g.Npc}:{g.Item}")).ToList();
        player.giftedItems[note3[0].Npc][note3[0].Item] = 1;
        Check(Done(3) == false && Gifts(3) == (1, note3.Count), $"5. one gift given out of {note3.Count}: counted, not done yet");
        foreach (var (npc, item) in note3)
            player.giftedItems[npc][item] = 2;
        Check(Done(3) == true, "5. every gift given: done");

        // flags, including the one spelled with a small 's', and one waiting for tomorrow
        player.mailReceived.Add("SecretNote16_done");
        player.mailReceived.Add("secretNote21_done");
        player.mailForTomorrow.Add("Island_W_BuriedTreasure%&NL&%");
        Check(Done(16) == true && Done(21) == true && Done(17) == false, "5. a dug-up treasure and the bush scene tick their notes, and only theirs");
        Check(Done(1004) == true, "5. a treasure only flagged for tomorrow already counts");
        player.mailReceived.Add("TH_Tunnel");
        Check(Done(22) == false, "5. the hunt's first step isn't the end of it");
        player.mailReceived.Add("carolinesNecklace");
        Check(Done(25) == true, "5. the necklace found and no quest left about it: done");

        // the hooks, laid on the real game DLL
        try
        {
            Harmony harmony = new("PelicanMemory.SearchTest.Notes");
            Type feature = T("PelicanMemory.Features.SecretNotesDone.SecretNotesDoneFeature");
            MethodInfo Patch(string name) => feature.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
            Type page = typeof(StardewValley.Menus.CollectionsPage);
            harmony.Patch(AccessTools.Method(page, "draw", new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) }), prefix: new HarmonyMethod(Patch("Before_Draw")), finalizer: new HarmonyMethod(Patch("After_Draw")));
            harmony.Patch(AccessTools.Method(typeof(StardewValley.Menus.ClickableTextureComponent), "draw", new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch), typeof(Color), typeof(float), typeof(int), typeof(int), typeof(int) }), postfix: new HarmonyMethod(Patch("After_DrawComponent")));
            harmony.Patch(AccessTools.Method(page, "receiveRightClick"), postfix: new HarmonyMethod(Patch("After_ReceiveRightClick")));
            harmony.Patch(AccessTools.Method(page, "performHoverAction"), postfix: new HarmonyMethod(Patch("After_PerformHoverAction")));
            Check(true, "5. the secret notes hooks apply to the game (draw, icons, right click, tooltip)");
        }
        catch (Exception ex)
        {
            Check(false, $"5. secret notes hooks: {ex.InnerException?.Message ?? ex.Message}");
        }

        // Gil's goals: the same data and reward rules as the game
        Dictionary<string, StardewValley.GameData.MonsterSlayerQuestData> goals = DataLoader.MonsterSlayerQuests(Game1.content);
        HashSet<string> monsters = DataLoader.Monsters(Game1.content).Keys.ToHashSet();
        Check(goals.Count == 12 && goals.Values.All(goal => goal.Targets?.All(monsters.Contains) == true), $"5. Gil's {goals.Count} goals all name monsters the game knows");
        Check(goals.Values.All(goal => goal.DisplayName != null), "5. every goal has a name to show");
    }

    // 6. the monsters tab
    {
        Type data = T("PelicanMemory.Features.Bestiary.BestiaryData");
        IEnumerable<object> entries = ((IEnumerable)data.GetMethod("GetMonsters")!.Invoke(null, null)!).Cast<object>();
        List<(string Key, string Texture, int[] Icon, object[] Places)> monsters = entries
            .Select(e => ((string)e.GetType().GetProperty("Key")!.GetValue(e)!, (string)e.GetType().GetProperty("Texture")!.GetValue(e)!, (int[])e.GetType().GetProperty("Icon")!.GetValue(e)!, ((IEnumerable)e.GetType().GetProperty("Places")!.GetValue(e)!).Cast<object>().ToArray()))
            .ToList();

        HashSet<string> gameMonsters = DataLoader.Monsters(Game1.content).Keys.ToHashSet();
        string[] notInData = { "Haunted Skull", "Stick Bug" }; // counted under their own name, missing from Data/Monsters
        List<string> unknownKeys = monsters.Select(m => m.Key).Where(key => !gameMonsters.Contains(key) && !notInData.Contains(key)).ToList();
        Check(monsters.Count == 46 && unknownKeys.Count == 0 && monsters.Select(m => m.Key).Distinct().Count() == monsters.Count, $"6. the {monsters.Count} monsters are counted under names the game uses, once each ({string.Join(", ", unknownKeys)})");

        List<string> missingTextures = monsters.Where(m => !File.Exists(Path.Combine(gameDir, "Content", m.Texture + ".xnb"))).Select(m => m.Texture).ToList();
        Check(missingTextures.Count == 0, $"6. every face comes from a sheet of the game ({string.Join(", ", missingTextures)})");
        Check(monsters.All(m => m.Icon.Length == 4 && m.Icon[2] > 0 && m.Icon[3] > 0 && m.Places.Length > 0), "6. every monster has a face and at least one place");

        // Gil's targets the game can really count are all in the tab (a mage counts as a skeleton; the shadow guy never spawns)
        string[] neverCounted = { "Skeleton Mage", "Shadow Guy", "Shadow Girl" };
        List<string> missingTargets = DataLoader.MonsterSlayerQuests(Game1.content).Values.SelectMany(goal => goal.Targets).Where(target => !neverCounted.Contains(target) && monsters.All(m => m.Key != target)).Distinct().ToList();
        Check(missingTargets.Count == 0, $"6. every monster Gil counts is in the tab ({string.Join(", ", missingTargets)})");

        string[] kinds = { "mine", "cavern", "volcano", "quarry", "farm", "location" };
        bool placesOk = monsters.SelectMany(m => m.Places).All(place =>
        {
            string kind = (string)place.GetType().GetProperty("Kind")!.GetValue(place)!;
            int from = (int)place.GetType().GetProperty("From")!.GetValue(place)!, to = (int)place.GetType().GetProperty("To")!.GetValue(place)!;
            string[]? names = (string[]?)place.GetType().GetProperty("Names")!.GetValue(place);
            return kinds.Contains(kind) && (kind != "mine" || (from >= 1 && to >= from && to <= 120)) && (kind != "location" || names?.Length > 0);
        });
        Check(placesOk, "6. every place is of a kind the tab knows, with sane floors");

        // the volcano dungeon, which the game never lists among the places visited
        {
            Farmer player = (Farmer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Farmer));
            foreach (FieldInfo field in typeof(Farmer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.GetValue(player) is null && field.FieldType.Namespace?.StartsWith("Netcode") == true && !field.FieldType.IsAbstract && field.FieldType.GetConstructor(Type.EmptyTypes) != null)
                    field.SetValue(player, Activator.CreateInstance(field.FieldType));
            }
            player.stats = new Stats();
            typeof(Game1).GetField("_player", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, player);

            MethodInfo volcano = T("PelicanMemory.Core.GeneratedLevels").GetMethod("HasBeenInVolcano")!;
            bool Been() => (bool)volcano.Invoke(null, new object[] { player })!;
            player.locationsVisited.Add("IslandNorth");
            bool before = Been();
            player.mailForTomorrow.Add("Saw_Flame_Sprite_Volcano%&NL&%");
            bool flagged = Been();
            player.mailForTomorrow.Clear();
            player.stats.monsterKilled("Hot Head");
            Check(!before && flagged && Been(), "6. the volcano dungeon counts as visited from the flame sprite's flag, or a monster only met there");
        }

        try
        {
            Harmony harmony = new("PelicanMemory.SearchTest.Bestiary");
            Type feature = T("PelicanMemory.Features.Bestiary.BestiaryFeature");
            MethodInfo Patch(string name) => feature.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
            Type page = typeof(StardewValley.Menus.CollectionsPage);
            harmony.Patch(AccessTools.Constructor(page, new[] { typeof(int), typeof(int), typeof(int), typeof(int) }), postfix: new HarmonyMethod(Patch("After_Constructor")));
            harmony.Patch(AccessTools.Method(page, "performHoverAction"), prefix: new HarmonyMethod(Patch("Before_PerformHoverAction")));
            harmony.Patch(AccessTools.Method(page, "receiveLeftClick"), postfix: new HarmonyMethod(Patch("After_ReceiveLeftClick")));
            Check(true, "6. the monsters tab hooks apply to the game (constructor, tooltip, click)");
        }
        catch (Exception ex)
        {
            Check(false, $"6. monsters tab hooks: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    // 7. presets, luck on the clock, TV recipes
    {
        // a bare player with a bag, as the game's inventory
        Farmer player = (Farmer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Farmer));
        foreach (FieldInfo field in typeof(Farmer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.GetValue(player) is null && (field.FieldType.Namespace?.StartsWith("Netcode") == true || field.FieldType.Namespace == "StardewValley.Network") && !field.FieldType.IsAbstract && field.FieldType.GetConstructor(Type.EmptyTypes) != null)
                field.SetValue(player, Activator.CreateInstance(field.FieldType));
        }
        player.stats = new Stats();
        player.netItems.Value = new StardewValley.Inventories.Inventory();
        typeof(Farmer).GetField("buffs")!.SetValue(player, new StardewValley.Buffs.BuffManager());   // equipping marks the buffs dirty
        player.maxItems.Value = 36;
        for (int i = 0; i < 36; i++)
            player.Items.Add(null);
        typeof(Game1).GetField("_player", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, player);

        // luck: the TV's five faces and thresholds
        MethodInfo face = T("PelicanMemory.Features.LuckIndicator.LuckIndicatorFeature").GetMethod("GetFace", BindingFlags.Static | BindingFlags.NonPublic)!;
        int X(double luck) => ((Rectangle)face.Invoke(null, new object[] { luck })!).X + ((Rectangle)face.Invoke(null, new object[] { luck })!).Y * 1000;
        Check(X(-0.1) == 592 + 346000 && X(-0.05) == 540 + 346000 && X(0) == 540 + 333000 && X(0.05) == 592 + 333000 && X(0.1) == 644 + 333000 && X(-0.02) == 540 + 333000,
            "7. luck: skull, bat, wisp, gem, star at the TV's own thresholds (±0.02, ±0.07)");

        // TV recipes
        MethodInfo count = T("PelicanMemory.Features.TvRecipes.TvRecipesFeature").GetMethod("CountRecipes", BindingFlags.Static | BindingFlags.NonPublic)!;
        (int, int) Count() => ((int, int))count.Invoke(null, new object[] { player })!;
        var before = Count();
        string first = DataLoader.Tv_CookingChannel(Game1.content)["1"].Split('/')[0];
        player.cookingRecipes.Add(first, 0);
        Check(before == (0, 32) && Count() == (1, 32), $"7. TV recipes: 32 in the show, learned ones counted ({before} then {Count()})");

        // fingerprints
        Type loadoutType = T("PelicanMemory.Features.Loadouts.Loadout");
        MethodInfo keyOf = loadoutType.GetMethod("KeyOf")!, match = loadoutType.GetMethod("Match")!, describe = loadoutType.GetMethod("Describe")!;
        Item coffee = ItemRegistry.Create("(O)395", 10), goldCoffee = ItemRegistry.Create("(O)395", 3, 2), stone = ItemRegistry.Create("(O)390", 5);
        object coffeeWanted = describe.Invoke(null, new object?[] { coffee })!;
        int M(object wanted, Item item) => (int)match.Invoke(null, new[] { wanted, item })!;
        Check((string)keyOf.Invoke(null, new object[] { coffee })! != (string)keyOf.Invoke(null, new object[] { goldCoffee })! && M(coffeeWanted, coffee) == 2 && M(coffeeWanted, goldCoffee) == 1 && M(coffeeWanted, stone) == 0,
            "7. presets: the very same item before one of the same kind, never another item");

        // a whole round: a preset taken from a chest, then an empty preset puts everything back where it came from
        Type itemType = T("PelicanMemory.Features.Loadouts.LoadoutItem");
        object Want(string id, int n) => Activator.CreateInstance(itemType, id, n, (string)keyOf.Invoke(null, new object[] { ItemRegistry.Create(id) })!, id)!;
        Type listType = typeof(List<>).MakeGenericType(itemType);
        Type dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), itemType);
        object Loadout(Dictionary<string, object?> equip, params object?[] slots)
        {
            IDictionary d = (IDictionary)Activator.CreateInstance(dictType)!;
            foreach (var (k, v) in equip)
                d[k] = v;
            IList l = (IList)Activator.CreateInstance(listType)!;
            foreach (object? s in slots)
                l.Add(s);
            return Activator.CreateInstance(loadoutType, "Essai", d, l)!;
        }

        StardewValley.Objects.Chest chest = new(true);
        chest.Items.Add(ItemRegistry.Create("(O)395", 30));
        chest.Items.Add(ItemRegistry.Create("(B)506"));
        chest.Items.Add(ItemRegistry.Create("(O)530"));
        player.Items[0] = ItemRegistry.Create("(O)390", 5);
        player.Items[3] = ItemRegistry.Create("(W)4");
        player.Equip((StardewValley.Objects.Ring)ItemRegistry.Create("(O)529"), player.leftRing);

        Type applier = T("PelicanMemory.Features.Loadouts.LoadoutApplier");
        Type refType = T("PelicanMemory.Features.Loadouts.ChestRef");
        IDictionary origins = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), refType))!;
        object Apply(object loadout) => applier.GetMethod("Apply")!.Invoke(null, new object[] { player, loadout, new List<StardewValley.Objects.Chest> { chest }, origins })!;
        int InChest(string id) => chest.Items.Where(i => i?.QualifiedItemId == id).Sum(i => i!.Stack);
        int InBag(string id) => player.Items.Where(i => i?.QualifiedItemId == id).Sum(i => i!.Stack);

        object report = Apply(Loadout(new() { ["Boots"] = Want("(B)506", 1), ["LeftRing"] = Want("(O)530", 1) }, Want("(O)395", 20), Want("(W)4", 1)));
        IList missing = (IList)report.GetType().GetProperty("Missing")!.GetValue(report)!;
        Check(player.Items[0]?.QualifiedItemId == "(O)395" && player.Items[0]!.Stack == 20 && player.Items[1]?.QualifiedItemId == "(W)4"
              && player.boots.Value?.QualifiedItemId == "(B)506" && player.leftRing.Value?.QualifiedItemId == "(O)530" && InChest("(O)395") == 10 && missing.Count == 0,
            "7. presets: each slot and piece of equipment taken from the bag or the chest, in the preset's order and quantities");
        IList homeless = (IList)report.GetType().GetProperty("Homeless")!.GetValue(report)!;
        Check(InBag("(O)529") == 1 && InBag("(O)390") == 5 && homeless.Count == 2,
            "7. presets: what has no chest of its own stays in the bag and is listed for the player to choose a chest (never a random one)");
        report = Apply(Loadout(new()));
        Check(InChest("(O)395") == 30 && InChest("(B)506") == 1 && InChest("(O)530") == 1 && player.boots.Value is null && InBag("(O)395") == 0,
            "7. presets: an empty preset puts every taken item back in the chest it came from");

        report = Apply(Loadout(new(), Want("(O)395", 50)));
        missing = (IList)report.GetType().GetProperty("Missing")!.GetValue(report)!;
        Check(InBag("(O)395") == 30 && missing.Count == 1, "7. presets: what's missing is reported, never made up");

        try
        {
            Harmony harmony = new("PelicanMemory.SearchTest.Presets");
            Type luck = T("PelicanMemory.Features.LuckIndicator.LuckIndicatorFeature"), loadouts = T("PelicanMemory.Features.Loadouts.LoadoutsFeature");
            harmony.Patch(AccessTools.Method(typeof(StardewValley.Menus.DayTimeMoneyBox), "draw", new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) }), postfix: new HarmonyMethod(luck.GetMethod("After_Draw", BindingFlags.Static | BindingFlags.NonPublic)!));
            harmony.Patch(AccessTools.Method(typeof(StardewValley.Menus.InventoryPage), "draw", new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) }), postfix: new HarmonyMethod(loadouts.GetMethod("After_Draw", BindingFlags.Static | BindingFlags.NonPublic)!));
            harmony.Patch(AccessTools.Method(typeof(StardewValley.Menus.InventoryPage), "receiveLeftClick"), prefix: new HarmonyMethod(loadouts.GetMethod("Before_ReceiveLeftClick", BindingFlags.Static | BindingFlags.NonPublic)!));
            Type pause = T("PelicanMemory.Features.PauseTogether.PauseTogetherFeature");
            harmony.Patch(AccessTools.Method(typeof(Game1), "Update", new[] { typeof(Microsoft.Xna.Framework.GameTime) }), postfix: new HarmonyMethod(pause.GetMethod("After_Update", BindingFlags.Static | BindingFlags.NonPublic)!));
            harmony.Patch(AccessTools.Method(typeof(Farmer), "takeDamage"), prefix: new HarmonyMethod(pause.GetMethod("Before_TakeDamage", BindingFlags.Static | BindingFlags.NonPublic)!));
            MethodInfo under = pause.GetMethod("IsUnderground", BindingFlags.Static | BindingFlags.NonPublic)!;
            Check((bool)under.Invoke(null, new object?[] { System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(StardewValley.Locations.MineShaft)) })!
                  && (bool)under.Invoke(null, new object?[] { System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(StardewValley.Locations.VolcanoDungeon)) })!
                  && !(bool)under.Invoke(null, new object?[] { System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(StardewValley.Farm)) })!,
                  "7. dungeon mode: the mines' levels (cavern included) and the volcano count as underground, the farm doesn't");
            Check(AccessTools.Method(typeof(StardewValley.Objects.TV), "getFortuneForecast", new[] { typeof(Farmer) }) != null, "7. the hooks apply to the game (clock, inventory, dungeon pause, damage), and the TV's forecast is there to read");
        }
        catch (Exception ex)
        {
            Check(false, $"7. presets / luck hooks: {ex.InnerException?.Message ?? ex.Message}");
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
