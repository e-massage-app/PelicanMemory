using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

// Runs the mod's "put away everywhere" logic on real chests and real game items, outside the game, and checks that
// nothing is ever lost or duplicated: per item, bag + chests must hold exactly the same total before and after.
System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
    return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
};
Run();

static void Run()
{
    const string gameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley";
    // the game's data first, then its item types (which read that data), as the game does on start
    Game1.content = new LocalizedContentManager(new GameServiceContainer(), Path.Combine(gameDir, "Content"));
    Game1.objectData = DataLoader.Objects(Game1.content);
    Game1.bigCraftableData = DataLoader.BigCraftables(Game1.content);
    Game1.toolData = DataLoader.Tools(Game1.content);
    Game1.weaponData = DataLoader.Weapons(Game1.content);
    Game1.pantsData = DataLoader.Pants(Game1.content);
    Game1.shirtData = DataLoader.Shirts(Game1.content);
    typeof(ItemRegistry).GetMethod("RegisterItemTypes", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
    ItemRegistry.ResetCache();

    // chests ask whose items to show: a bare player, with its synced fields created, is enough
    Farmer player = (Farmer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Farmer));
    foreach (FieldInfo field in typeof(Farmer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
    {
        if (field.GetValue(player) is null && field.FieldType.Namespace?.StartsWith("Netcode") == true && !field.FieldType.IsAbstract && field.FieldType.GetConstructor(Type.EmptyTypes) != null)
            field.SetValue(player, Activator.CreateInstance(field.FieldType));
    }
    typeof(Game1).GetField("_player", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, player);

    Assembly mod = Assembly.Load("PelicanMemory");
    MethodInfo store = mod.GetType("PelicanMemory.Features.DepositEverywhere.DepositEverywhereFeature", true)!
        .GetMethod("Store", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

    int failures = 0;
    void Check(bool ok, string label) { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {label}"); if (!ok) failures++; }

    Item Make(string id, int stack, int quality = 0) { Item item = ItemRegistry.Create(id, stack); item.Quality = quality; return item; }
    Chest NewChest(params Item[] items) { Chest chest = new(playerChest: true); foreach (Item item in items) chest.Items.Add(item); return chest; }
    int CountIn(IEnumerable<Item?> items, string id) => items.Where(i => i?.QualifiedItemId == id).Sum(i => i!.Stack);
    Dictionary<string, int> Totals(IList<Item> bag, IEnumerable<Chest> chests, string[] ids) =>
        ids.ToDictionary(id => id, id => CountIn(bag, id) + chests.Sum(c => CountIn(c.Items, id)));
    const int held = 0; // the item in hand, as with the first toolbar slot selected
    HashSet<string> blocked = new();
    Dictionary<string, int> Store(IList<Item> bag, List<Chest> chests)
    {
        blocked = new();
        return (Dictionary<string, int>)store.Invoke(null, new object[] { bag, held, chests, new HashSet<Chest>(), blocked })!;
    }

    string[] ids = { "(O)771", "(O)388", "(O)Carrot", "(O)390", "(T)Pickaxe" };

    // 1. the usual case
    {
        List<Item> bag = Enumerable.Repeat<Item>(null!, 36).ToList();
        bag[held] = Make("(O)388", 50);           // wood in hand: must stay
        bag[1] = Make("(O)771", 7);               // fibre just picked up, in the toolbar: must go
        bag[12] = Make("(O)771", 60);             // fibre
        bag[13] = Make("(O)388", 30);             // wood
        bag[14] = Make("(O)Carrot", 10, 1);       // silver carrots
        bag[15] = Make("(O)390", 20);             // stone: no chest holds any
        bag[16] = ItemRegistry.Create("(T)Pickaxe"); // a tool, even though a chest holds one

        Chest fewFibre = NewChest(Make("(O)771", 5)), mostFibre = NewChest(Make("(O)771", 100)), wood = NewChest(Make("(O)388", 10));
        Chest carrots = NewChest(Make("(O)Carrot", 3)), tools = NewChest(ItemRegistry.Create("(T)Pickaxe"));
        List<Chest> chests = new() { fewFibre, mostFibre, wood, carrots, tools };

        var before = Totals(bag, chests, ids);
        Store(bag, chests);
        var after = Totals(bag, chests, ids);

        Check(ids.All(id => before[id] == after[id]), "1. nothing lost or duplicated");
        Check(CountIn(mostFibre.Items, "(O)771") == 167 && CountIn(fewFibre.Items, "(O)771") == 5, "1. fibre went to the chest holding the most");
        Check(CountIn(wood.Items, "(O)388") == 40, "1. wood from the bag went to the wood chest");
        Check(bag[held]?.QualifiedItemId == "(O)388" && bag[held].Stack == 50, "1. the item in hand stayed");
        Check(bag[1] == null, "1. the rest of the toolbar row was put away");
        Check(blocked.Count == 0, "1. nothing reported as lacking room (stone simply has no chest)");
        Check(carrots.Items.Any(i => i?.QualifiedItemId == "(O)Carrot" && i.Quality == 1 && i.Stack == 10), "1. silver carrots joined the chest of normal carrots");
        Check(bag[15]?.QualifiedItemId == "(O)390" && bag[15].Stack == 20, "1. stone with no home stayed in the bag");
        Check(bag[16] is StardewValley.Tools.Pickaxe && CountIn(tools.Items, "(T)Pickaxe") == 1, "1. tools never move");
        Check(bag[12] == null && bag[13] == null && bag[14] == null, "1. emptied bag slots are cleared");
        Check(!chests.Any(c => c.Items.Any(i => bag.Contains(i))), "1. no object is in the bag and a chest at once");
    }

    // 2. the fullest chest has no room left: the rest goes to the next chest holding it
    {
        List<Item> bag = Enumerable.Repeat<Item>(null!, 36).ToList();
        bag[20] = Make("(O)771", 500);
        Chest full = NewChest(Enumerable.Range(0, 36).Select(_ => Make("(O)771", 999)).ToArray());
        Chest small = NewChest(Make("(O)771", 1));
        List<Chest> chests = new() { full, small };

        var before = Totals(bag, chests, ids);
        Store(bag, chests);
        var after = Totals(bag, chests, ids);

        Check(ids.All(id => before[id] == after[id]), "2. nothing lost or duplicated");
        Check(CountIn(full.Items, "(O)771") == 36 * 999 && CountIn(small.Items, "(O)771") == 501 && bag[20] == null, "2. a full chest is skipped for the next one");
    }

    // 3. no room anywhere: it stays in the bag, untouched
    {
        List<Item> bag = Enumerable.Repeat<Item>(null!, 36).ToList();
        bag[20] = Make("(O)771", 500);
        Chest full = NewChest(Enumerable.Range(0, 36).Select(_ => Make("(O)771", 999)).ToArray());
        List<Chest> chests = new() { full };

        var before = Totals(bag, chests, ids);
        Store(bag, chests);
        var after = Totals(bag, chests, ids);

        Check(ids.All(id => before[id] == after[id]), "3. nothing lost or duplicated");
        Check(bag[20]?.Stack == 500, "3. with no room anywhere, the stack stays in the bag");
        Check(blocked.Count == 1, "3. the player is told it found no room");
    }

    // 4. partial: a chest with room for only part of a stack keeps the rest in the bag
    {
        List<Item> bag = Enumerable.Repeat<Item>(null!, 36).ToList();
        bag[20] = Make("(O)771", 500);
        Item[] content = Enumerable.Range(0, 35).Select(_ => Make("(O)388", 999)).Append(Make("(O)771", 900)).ToArray();
        Chest almost = NewChest(content);
        List<Chest> chests = new() { almost };

        var before = Totals(bag, chests, ids);
        Store(bag, chests);
        var after = Totals(bag, chests, ids);

        Check(ids.All(id => before[id] == after[id]), "4. nothing lost or duplicated");
        Check(CountIn(almost.Items, "(O)771") == 999 && bag[20]?.Stack == 401, "4. only what fits is moved, the rest stays in the bag");
        Check(blocked.Count == 1, "4. the player is told the rest found no room");
    }

    // 5. moving objects (same game items): what may change tile, and what stays put
    {
        MethodInfo movable = mod.GetType("PelicanMemory.Features.MoveObjects.MoveObjectsFeature", true)!
            .GetMethod("IsMovable", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Movable(StardewValley.Object obj) => (bool)movable.Invoke(null, new object[] { obj })!;

        Check(Movable(NewChest(Make("(O)388", 10))), "5. a full chest can be moved");
        Check(Movable(ItemRegistry.Create<StardewValley.Object>("(BC)12")), "5. a keg can be moved");
        Check(Movable(ItemRegistry.Create<StardewValley.Object>("(O)599")), "5. a sprinkler can be moved");
        Check(!Movable(ItemRegistry.Create<StardewValley.Object>("(BC)105")), "5. a tapper stays on its tree");
        Check(!Movable(new CrabPot()), "5. a crab pot stays in its water");
        Check(!Movable((IndoorPot)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(IndoorPot))), "5. a garden pot stays with its plant"); // a real pot needs a world to build its soil
        Check(!Movable(ItemRegistry.Create<StardewValley.Object>("(O)390")), "5. a stone isn't something the player placed");
        Check(!Movable(new Chest(playerChest: false)), "5. a chest the game placed (not the player's) stays");
    }

    Console.WriteLine(failures == 0 ? "\nALL DEPOSIT CHECKS PASSED" : $"\n{failures} FAILURES");
    Environment.ExitCode = failures == 0 ? 0 : 1;
}
