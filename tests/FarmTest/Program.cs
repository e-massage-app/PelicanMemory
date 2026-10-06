using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using StardewValley;
using StardewValley.Menus;

// Checks the farm timings outside the game, against cases worked out by hand from the game's own code:
// - crops: days until harvest from the game's counters, and death at the change of season;
// - machines: when the remaining minutes run out, with the game's night jump;
// - casks: days until the next quality;
// and lays the map pin patches on the real game DLL, so a renamed method fails here, not in the game.
System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
    return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
};
Run();

static void Run()
{
    Assembly mod = Assembly.Load("PelicanMemory");
    Type timing = mod.GetType("PelicanMemory.Core.FarmTiming", true)!;

    int failures = 0;
    void Check(bool ok, string label) { Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {label}"); if (!ok) failures++; }

    int Days(int[] phases, int phase, int day, bool regrowing) =>
        (int)timing.GetMethod("GetDaysUntilHarvest")!.Invoke(null, new object[] { phases.ToList(), phase, day, regrowing })!;
    bool Dies(int daysLeft, int dayOfMonth, Season season, params Season[] seasons) =>
        (bool)timing.GetMethod("WillDieBeforeHarvest")!.Invoke(null, new object[] { daysLeft, dayOfMonth, season, seasons.ToList() })!;
    (int Day, int? Time) Ready(int minutes, int now, bool overnightOnly)
    {
        object result = timing.GetMethod("GetReadyTime")!.Invoke(null, new object[] { minutes, now, overnightOnly })!;
        return ((int)result.GetType().GetProperty("DayOffset")!.GetValue(result)!, (int?)result.GetType().GetProperty("Time")!.GetValue(result));
    }
    int Cask(float daysToMature, float rate, float atQuality) =>
        (int)timing.GetMethod("GetCaskDays")!.Invoke(null, new object[] { daysToMature, rate, atQuality })!;

    // 1. crops (parsnip: 1 day per phase, then the game's 99999 sentinel)
    int[] parsnip = { 1, 1, 1, 1, 99999 };
    Check(Days(parsnip, 0, 0, false) == 4, "1. a parsnip just planted: 4 days");
    Check(Days(parsnip, 3, 0, false) == 1, "1. in its last growing phase: 1 day");
    Check(Days(parsnip, 4, 0, false) == 0, "1. in the final phase: ready now");
    Check(Days(new[] { 2, 3, 3, 99999 }, 1, 1, false) == 5, "1. midway (2 of 3 days left, then 3): 5 days");
    Check(Days(new[] { 1, 2, 2, 2, 99999 }, 2, 2, false) == 3, "1. a phase already full still costs its night: 1 + 2 = 3 days");
    Check(Days(new[] { 1, 2, 0, 2, 99999 }, 1, 0, false) == 4, "1. a zero-day phase is skipped: 2 + 0 + 2 = 4 days");
    Check(Days(new[] { 1, 2, 2, 99999 }, 3, 3, true) == 3, "1. regrowing after harvest: its own countdown, 3 days");
    Check(Days(new[] { 1, 2, 2, 99999 }, 3, 0, true) == 0, "1. regrown: ready now");

    // 2. seasons: the season changes before crops grow overnight, so a crop out of season dies on the 1st
    Check(Dies(4, 25, Season.Spring, Season.Spring), "2. spring crop on the 25th needing 4 days: dies on the 1st of summer");
    Check(!Dies(4, 24, Season.Spring, Season.Spring), "2. on the 24th needing 4 days: harvested on the 28th");
    Check(!Dies(10, 27, Season.Summer, Season.Summer, Season.Fall), "2. summer + fall crop crossing into fall: survives");
    Check(Dies(5, 27, Season.Fall, Season.Summer, Season.Fall), "2. summer + fall crop crossing into winter: dies");
    Check(!Dies(40, 1, Season.Summer, Season.Summer, Season.Fall), "2. 40 days from the 1st of summer: harvested on the 13th of fall");
    Check(Dies(60, 1, Season.Summer, Season.Summer, Season.Fall), "2. 60 days from the 1st of summer: dies at the start of winter");
    Check(!Dies(30, 20, Season.Spring, Season.Spring, Season.Summer, Season.Fall, Season.Winter), "2. an all-year crop never dies");

    // 3. machines: 10 minutes per 10 minutes, overnight the minutes until 2:00 plus 400 at once
    Check(Ready(120, 1200, false) == (0, 1400), "3. 120 min at 12:00: today at 14:00");
    Check(Ready(55, 1200, false) == (0, 1300), "3. 55 min at 12:00: rounded to the next tick, 13:00");
    Check(Ready(400, 2300, false) == (1, null), "3. 400 min at 23:00: done overnight, ready on waking");
    Check(Ready(1900, 1000, false) == (1, 1500), "3. 1900 min at 10:00: 960 today, 400 overnight, 540 more: tomorrow at 15:00");
    Check(Ready(3200, 600, false) == (2, null), "3. a 2-day machine started at 6:00 (3200 min): ready on waking in 2 days");
    Check(Ready(9000, 1000, true) == (6, null), "3. an incubator (9000 min, overnight only) at 10:00: ready on waking in 6 days");
    Check(Ready(1360, 1000, true) == (1, null), "3. overnight-only machine running out at night: tomorrow morning");
    Check(Ready(30, 1000, true) == (1, null), "3. overnight-only machine running out today: still only tomorrow morning");
    Check(Ready(0, 1500, false) == (0, 1500), "3. no minutes left: now");

    // 4. casks: silver at 42 days left, gold at 28, iridium at 0 (the game's cask)
    Check(Cask(56, 1, 42) == 14, "4. a fresh cask: silver in 14 days");
    Check(Cask(56, 1, 0) == 56, "4. a fresh cask: iridium in 56 days");
    Check(Cask(30, 2, 28) == 1, "4. ageing 2 days a night, 30 left: gold tomorrow");
    Check(Cask(20, 1.5f, 0) == 14, "4. ageing 1.5 a night, 20 left: iridium in 14 days");

    // 5. map pin patches, on the real game DLL
    {
        Type pins = mod.GetType("PelicanMemory.Features.MapPins.MapPinsFeature", true)!;
        MethodInfo P(string name) => pins.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        Harmony harmony = new("PelicanMemory.FarmTest");

        try
        {
            harmony.Patch(AccessTools.Method(typeof(GameMenu), nameof(GameMenu.receiveRightClick)), prefix: new HarmonyMethod(P("Before_GameMenuRightClick")));
            harmony.Patch(AccessTools.Method(typeof(MapPage), nameof(MapPage.receiveLeftClick)), prefix: new HarmonyMethod(P("Before_MapLeftClick")));
            harmony.Patch(AccessTools.Method(typeof(MapPage), nameof(MapPage.drawMiniPortraits)), prefix: new HarmonyMethod(P("Before_DrawMiniPortraits")));
            harmony.Patch(AccessTools.Method(typeof(MapPage), nameof(MapPage.performHoverAction)), postfix: new HarmonyMethod(P("After_PerformHoverAction")));

            string[] patched = harmony.GetPatchedMethods().Select(method => $"{method.DeclaringType?.Name}.{method.Name}").OrderBy(name => name).ToArray();
            Check(patched.Length == 4 && patched.Contains("GameMenu.receiveRightClick") && !patched.Contains("IClickableMenu.receiveRightClick"),
                $"5. the 4 map pin patches apply, on the game menu's own right click ({string.Join(", ", patched)})");
        }
        catch (Exception ex)
        {
            Check(false, $"5. map pin patches failed: {ex.Message}");
        }
    }

    // 6. crafting from chests and phone orders: every hook, laid on the real game DLL (parameter names are checked here)
    {
        Harmony harmony = new("PelicanMemory.FarmTest.Orders");
        Type craft = mod.GetType("PelicanMemory.Features.CraftFromChests.CraftFromChestsFeature", true)!;
        Type phone = mod.GetType("PelicanMemory.Features.PhoneOrders.PhoneOrdersFeature", true)!;
        HarmonyMethod H(Type type, string name) => new(type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!);
        Type[] draw = { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) };

        (string Label, Action Apply)[] patches =
        {
            ("CraftingPage.getContainerContents", () => harmony.Patch(AccessTools.Method(typeof(CraftingPage), "getContainerContents"), prefix: H(craft, "Before_GetContainerContents"), postfix: H(craft, "After_GetContainerContents"))),
            ("CraftingRecipe.consumeIngredients", () => harmony.Patch(AccessTools.Method(typeof(CraftingRecipe), nameof(CraftingRecipe.consumeIngredients)), postfix: H(craft, "After_Consume"))),
            ("CraftingRecipe.ConsumeAdditionalIngredients", () => harmony.Patch(AccessTools.Method(typeof(CraftingRecipe), nameof(CraftingRecipe.ConsumeAdditionalIngredients)), postfix: H(craft, "After_Consume"))),
            ("DefaultPhoneHandler.CallBlacksmith", () => harmony.Patch(AccessTools.Method(typeof(StardewValley.Objects.DefaultPhoneHandler), "CallBlacksmith"), prefix: H(phone, "Before_CallBlacksmith"))),
            ("DefaultPhoneHandler.CallCarpenter", () => harmony.Patch(AccessTools.Method(typeof(StardewValley.Objects.DefaultPhoneHandler), "CallCarpenter"), prefix: H(phone, "Before_CallCarpenter"))),
            ("Game1.DrawDialogue(npc, key)", () => harmony.Patch(AccessTools.Method(typeof(Game1), nameof(Game1.DrawDialogue), new[] { typeof(NPC), typeof(string) }), prefix: H(phone, "Before_DrawDialogue"))),
            ("Game1.DrawDialogue(npc, key, args)", () => harmony.Patch(AccessTools.Method(typeof(Game1), nameof(Game1.DrawDialogue), new[] { typeof(NPC), typeof(string), typeof(object[]) }), prefix: H(phone, "Before_DrawDialogue"))),
            ("GameLocation.answerDialogueAction", () => harmony.Patch(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.answerDialogueAction)), prefix: H(phone, "Before_AnswerDialogueAction"), postfix: H(phone, "After_AnswerDialogueAction"))),
            ("ShopMenu.HasTradeItem", () => harmony.Patch(AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.HasTradeItem)), prefix: H(phone, "Before_PhonePayment"), finalizer: H(phone, "After_PhonePayment"))),
            ("ShopMenu.ConsumeTradeItem", () => harmony.Patch(AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.ConsumeTradeItem)), prefix: H(phone, "Before_PhonePayment"), finalizer: H(phone, "After_PhonePayment"))),
            ("CarpenterMenu.DoesFarmerHaveEnoughResourcesToBuild", () => harmony.Patch(AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.DoesFarmerHaveEnoughResourcesToBuild), Type.EmptyTypes), prefix: H(phone, "Before_PhonePayment"), finalizer: H(phone, "After_PhonePayment"))),
            ("CarpenterMenu.ConsumeResources", () => harmony.Patch(AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.ConsumeResources), Type.EmptyTypes), prefix: H(phone, "Before_PhonePayment"), finalizer: H(phone, "After_PhonePayment"))),
            ("CarpenterMenu.draw", () => harmony.Patch(AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.draw), draw), prefix: H(phone, "Before_PhonePayment"), finalizer: H(phone, "After_PhonePayment"))),
            ("GameLocation.houseUpgradeAccept", () => harmony.Patch(AccessTools.Method(typeof(GameLocation), "houseUpgradeAccept"), prefix: H(phone, "Before_HouseUpgradeAccept"), finalizer: H(phone, "After_HouseUpgradeAccept"))),
            ("Inventory.ContainsId", () => harmony.Patch(AccessTools.Method(typeof(StardewValley.Inventories.Inventory), "ContainsId", new[] { typeof(string), typeof(int) }), postfix: H(phone, "After_ContainsId"))),
            ("Inventory.ReduceId", () => harmony.Patch(AccessTools.Method(typeof(StardewValley.Inventories.Inventory), "ReduceId", new[] { typeof(string), typeof(int) }), postfix: H(phone, "After_ReduceId"))),
            ("CarpenterMenu.tryToBuild", () => harmony.Patch(AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.tryToBuild)), prefix: H(phone, "Before_TryToBuild")))
        };

        int applied = 0;
        foreach ((string label, Action apply) in patches)
        {
            try
            {
                apply();
                applied++;
            }
            catch (Exception ex)
            {
                Check(false, $"6. {label}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
        Check(applied == patches.Length, $"6. all {patches.Length} crafting and phone hooks apply to the game ({applied}/{patches.Length})");
    }

    Console.WriteLine(failures == 0 ? "\nALL FARM CHECKS PASSED" : $"\n{failures} FAILURES");
    Environment.ExitCode = failures == 0 ? 0 : 1;
}
