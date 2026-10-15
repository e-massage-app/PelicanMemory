using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.SecretNotesDone;

/// <summary>Ticks the secret notes and journal scraps whose thing is done, in the collections tab.</summary>
/// <remarks>
/// Treasures dug up, scenes seen, riddles solved: the game records most of them (<see cref="SecretNoteChecks"/>), and
/// those notes tick themselves. The others can be ticked by hand with a right click, saved in the player's save.
/// The tooltip says which kind a note is, and for the gift notes how many of their gifts were given.
///
/// Anti-spoil: only notes the player has read are touched; nothing is said about what a note leads to.
/// </remarks>
internal class SecretNotesDoneFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static SecretNotesDoneFeature? Instance;

    /// <summary>The page whose secret notes are being drawn right now, if any.</summary>
    private static CollectionsPage? DrawingPage;

    /// <summary>The <see cref="PlayerStore"/> key of the notes ticked by hand.</summary>
    private const string StoreKey = "secret-notes-done";

    /// <summary>How large the tick is drawn over a note's icon.</summary>
    private const float TickScale = 3f;

    private readonly PlayerStore Store;

    /// <summary>The notes ticked by hand, read once per save.</summary>
    private HashSet<int>? ManualDone;


    /*********
    ** Public methods
    *********/
    public override string Id => "secret-notes-done";

    public SecretNotesDoneFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, PlayerStore store)
        : base(helper, monitor, harmony, settings)
    {
        this.Store = store;
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;

        this.Prefix(
            AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.draw), new[] { typeof(SpriteBatch) }),
            typeof(SecretNotesDoneFeature),
            nameof(Before_Draw)
        );
        this.Finalizer(
            AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.draw), new[] { typeof(SpriteBatch) }),
            typeof(SecretNotesDoneFeature),
            nameof(After_Draw)
        );
        this.Postfix(
            AccessTools.Method(typeof(ClickableTextureComponent), nameof(ClickableTextureComponent.draw), new[] { typeof(SpriteBatch), typeof(Color), typeof(float), typeof(int), typeof(int), typeof(int) }),
            typeof(SecretNotesDoneFeature),
            nameof(After_DrawComponent)
        );
        this.Postfix(
            AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.receiveRightClick)),
            typeof(SecretNotesDoneFeature),
            nameof(After_ReceiveRightClick)
        );
        this.Postfix(
            AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.performHoverAction)),
            typeof(SecretNotesDoneFeature),
            nameof(After_PerformHoverAction)
        );
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.SaveLoaded -= this.OnSaveLoaded;
        this.ManualDone = null;
        DrawingPage = null;
        Instance = null;
    }


    /*********
    ** Patches
    *********/
    /// <summary>Note that the notes tab is being drawn, so the icons drawn next can be ticked.</summary>
    private static void Before_Draw(CollectionsPage __instance)
    {
        DrawingPage = __instance.currentTab == CollectionsPage.secretNotesTab && __instance.letterviewerSubMenu == null ? __instance : null;
    }

    /// <summary>Stop ticking icons once the page is drawn, even if drawing failed.</summary>
    private static Exception? After_Draw(Exception? __exception)
    {
        DrawingPage = null;
        return __exception;
    }

    /// <summary>Draw the game's ticked checkbox over the icon of a note whose thing is done.</summary>
    private static void After_DrawComponent(ClickableTextureComponent __instance, SpriteBatch b)
    {
        if (Instance is null || DrawingPage is null || !__instance.visible)
            return;

        try
        {
            if (TryReadNote(__instance, out int noteId) && Instance.GetState(noteId).Done)
            {
                Rectangle source = OptionsCheckbox.sourceRectChecked;
                Vector2 position = new(__instance.bounds.Right - source.Width * TickScale + 4, __instance.bounds.Bottom - source.Height * TickScale + 4);

                // the page draws its icons sorted by depth: just above the icon (0.86), under the tooltip drawn afterwards
                b.Draw(Game1.mouseCursors, position, source, Color.White, 0f, Vector2.Zero, TickScale, SpriteEffects.None, 0.87f);
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to tick a secret note:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Tick or untick a note by hand, when the game keeps no trace of it.</summary>
    private static void After_ReceiveRightClick(CollectionsPage __instance, int x, int y)
    {
        if (Instance is null || !Context.IsWorldReady || __instance.currentTab != CollectionsPage.secretNotesTab || __instance.letterviewerSubMenu != null)
            return;

        try
        {
            foreach (ClickableTextureComponent component in __instance.collections[__instance.currentTab][__instance.currentPage])
            {
                if (!component.containsPoint(x, y) || !TryReadNote(component, out int noteId))
                    continue;

                if (Instance.GetState(noteId).Automatic)
                {
                    Game1.playSound("cancel");
                    return;
                }

                HashSet<int> done = Instance.GetManualDone();
                bool ticked = !done.Remove(noteId);
                if (ticked)
                    done.Add(noteId);
                Instance.Store.Write(Game1.player, StoreKey, done);
                Game1.playSound(ticked ? "coin" : "smallSelect");
                return;
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to tick a secret note by hand:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Add to a note's title whether it's done, and how it gets ticked.</summary>
    private static void After_PerformHoverAction(CollectionsPage __instance, int x, int y)
    {
        if (Instance is null || !Context.IsWorldReady || __instance.currentTab != CollectionsPage.secretNotesTab || __instance.letterviewerSubMenu != null)
            return;

        try
        {
            foreach (ClickableTextureComponent component in __instance.collections[__instance.currentTab][__instance.currentPage])
            {
                if (!component.containsPoint(x, y) || !TryReadNote(component, out int noteId))
                    continue;

                IReflectedField<string> hoverText = Instance.Helper.Reflection.GetField<string>(__instance, "hoverText");
                string text = hoverText.GetValue();
                if (string.IsNullOrEmpty(text))
                    return;

                // on the title line, so a note shown as a picture keeps its picture where the game puts it
                int titleEnd = text.IndexOf(Environment.NewLine, StringComparison.Ordinal);
                string status = $" — {Instance.GetStatusText(noteId)}";
                hoverText.SetValue(titleEnd < 0 ? text + status : text.Insert(titleEnd, status));
                return;
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to describe a secret note:\n{ex}", LogLevel.Error);
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Forget the previous save's ticks.</summary>
    private void OnSaveLoaded(object? sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
    {
        this.ManualDone = null;
    }

    /// <summary>Read the ID of a note the player has read from its icon, named <c>"&lt;id&gt; True"</c> by the game.</summary>
    private static bool TryReadNote(ClickableTextureComponent component, out int noteId)
    {
        string[] parts = ArgUtility.SplitBySpace(component.name);
        return int.TryParse(parts.Length > 0 ? parts[0] : null, out noteId) && parts.Length > 1 && parts[1] == "True";
    }

    /// <summary>Get the notes ticked by hand for the local player.</summary>
    private HashSet<int> GetManualDone()
    {
        return this.ManualDone ??= this.Store.Read<HashSet<int>>(Game1.player, StoreKey) ?? new HashSet<int>();
    }

    /// <summary>Get whether a note is done, and whether the game decides it.</summary>
    private (bool Done, bool Automatic) GetState(int noteId)
    {
        bool? automatic = SecretNoteChecks.IsDone(Game1.player, noteId, GetNoteText(noteId));
        return automatic.HasValue
            ? (automatic.Value, true)
            : (this.GetManualDone().Contains(noteId), false);
    }

    /// <summary>Get the line added to a note's title.</summary>
    private string GetStatusText(int noteId)
    {
        var (given, total) = SecretNoteChecks.GetGiftProgress(Game1.player, GetNoteText(noteId));
        if (total > 0)
            return this.Helper.Translation.Get(given == total ? "secret-notes-done.gifts-done" : "secret-notes-done.gifts", new { given, total });

        var (done, automatic) = this.GetState(noteId);
        string key = (done, automatic) switch
        {
            (true, true) => "secret-notes-done.done",
            (false, true) => "secret-notes-done.auto",
            (true, false) => "secret-notes-done.manual-done",
            _ => "secret-notes-done.manual"
        };
        return this.Helper.Translation.Get(key);
    }

    /// <summary>Get a note's raw text, with its gift reveals.</summary>
    private static string? GetNoteText(int noteId)
    {
        return DataLoader.SecretNotes(Game1.content).TryGetValue(noteId, out string? text) ? text : null;
    }
}
