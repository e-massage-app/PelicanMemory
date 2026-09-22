using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace PelicanMemory.Features.ChestSearch;

/// <summary>Tells the player how many of an item they have put away, and in which chest.</summary>
/// <remarks>
/// Nothing here is game knowledge: it only reports what the player themselves stored. The tooltip carries the total,
/// which is what you need most of the time, and the window says which container holds what when you want to go and
/// fetch it.
/// </remarks>
internal class ChestSearchFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The config key for the keybind.</summary>
    private const string KeybindKey = "chest-search";

    /// <summary>The colour of the tooltip line, matching the "in your chests" colour used elsewhere.</summary>
    private static readonly Color HintColor = new(150, 95, 25);

    /// <summary>The shared view of what's in the player's chests.</summary>
    private readonly StorageIndex Storage;


    /*********
    ** Public methods
    *********/
    public override string Id => "chest-search";

    public ChestSearchFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, StorageIndex storage)
        : base(helper, monitor, harmony, settings)
    {
        this.Storage = storage;
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
        TooltipBadges.AddProvider(this.GetTooltipBadge);
    }

    protected override void OnDisable()
    {
        this.Helper.Events.Input.ButtonsChanged -= this.OnButtonsChanged;
        TooltipBadges.RemoveProvider(this.GetTooltipBadge);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Stamp the tooltip with how many are stored away, and the key which says where.</summary>
    private TooltipBadge? GetTooltipBadge(Item item)
    {
        if (!Context.IsWorldReady)
            return null;

        int count = this.CountStored(item);
        if (count <= 0)
            return null;

        return new TooltipBadge(
            ChestIcon.Texture,
            ChestIcon.Source,
            Label: this.Settings.GetKeybind(KeybindKey, "O").ToString(),
            Text: this.Helper.Translation.Get("storage.tooltip", new { count }),
            TextColor: HintColor
        );
    }

    /// <summary>Open the window listing which containers hold the item under the cursor.</summary>
    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !this.Settings.GetKeybind(KeybindKey, "O").JustPressed())
            return;

        try
        {
            if (HoveredItem.Get(this.Helper) is not Item item)
                return;

            this.Helper.Input.SuppressActiveKeybinds(this.Settings.GetKeybind(KeybindKey, "O"));

            // the contents may have changed since the last scan without a chest menu closing (a hopper, another player)
            this.Storage.Invalidate();

            IReadOnlyList<ItemStash> stashes = this.Storage.GetStashes(stored => Matches(stored, item), GetOpenContainer());
            Game1.activeClickableMenu = new ItemLocationMenu(this.Helper.Translation, item, CountInBag(item), stashes);
            Game1.playSound("bigSelect");
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to open the storage window:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Count how many of an item are stored away, other than in the container the player is looking into.</summary>
    private int CountStored(Item item)
    {
        return this.Storage.Count(stored => Matches(stored, item), GetOpenContainer());
    }

    /// <summary>Get the contents of the open container, if any.</summary>
    /// <remarks>Whatever is in front of the player is already on screen: repeating it would just be noise.</remarks>
    private static IList<Item>? GetOpenContainer()
    {
        return Game1.activeClickableMenu is ItemGrabMenu { ItemsToGrabMenu: not null } grab
            ? grab.ItemsToGrabMenu.actualInventory
            : null;
    }

    /// <summary>Count how many of an item the player is carrying.</summary>
    private static int CountInBag(Item item)
    {
        int count = 0;
        foreach (Item entry in Game1.player.Items)
        {
            if (entry != null && Matches(entry, item))
                count += entry.Stack;
        }
        return count;
    }

    /// <summary>Get whether a stored stack is the same item, whatever its quality.</summary>
    private static bool Matches(Item stored, Item wanted)
    {
        return stored.QualifiedItemId == wanted.QualifiedItemId;
    }


    /// <summary>The chest sprite, cropped to its lower half so it fits a square badge.</summary>
    private static class ChestIcon
    {
        private static ParsedItemData Data => ItemRegistry.GetDataOrErrorItem("(BC)130");

        public static Microsoft.Xna.Framework.Graphics.Texture2D Texture => Data.GetTexture();

        public static Rectangle Source
        {
            get
            {
                Rectangle source = Data.GetSourceRect();
                return source.Height >= 32
                    ? new Rectangle(source.X, source.Y + source.Height - 16, 16, 16)
                    : source;
            }
        }
    }
}
