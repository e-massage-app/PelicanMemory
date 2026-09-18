using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.FarmAnimals;
using StardewValley.ItemTypeDefinitions;

namespace PelicanMemory.Features.AnimalCare;

/// <summary>Shows a small icon above farm animals which still need something today, and nothing otherwise.</summary>
/// <remarks>Only the player's own animals in the current location, so there's nothing to spoil.</remarks>
internal class AnimalCareFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The filled heart sprite used on the social page.</summary>
    private static readonly Rectangle HeartSource = new(211, 428, 7, 6);

    /// <summary>How far the icon floats above the animal, in pixels.</summary>
    private const int HoverHeight = 52;


    /*********
    ** Public methods
    *********/
    public override string Id => "animal-care";

    public AnimalCareFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
    }

    protected override void OnDisable()
    {
        this.Helper.Events.Display.RenderedWorld -= this.OnRenderedWorld;
    }


    /*********
    ** Private methods
    *********/
    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.currentLocation is null || Game1.eventUp)
            return;

        try
        {
            foreach (FarmAnimal animal in Game1.currentLocation.animals.Values)
            {
                // the produce icon takes priority: petting is a nice-to-have, milking is the thing you came for
                if (this.TryGetProduceIcon(animal, out Texture2D? texture, out Rectangle source))
                    DrawIcon(e.SpriteBatch, animal, texture!, source, 2.5f);
                else if (!animal.wasPet.Value)
                    DrawIcon(e.SpriteBatch, animal, Game1.mouseCursors, HeartSource, 4f);
            }
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to draw the animal icons:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Get the tool sprite for an animal with produce waiting, if it needs a tool.</summary>
    private bool TryGetProduceIcon(FarmAnimal animal, out Texture2D? texture, out Rectangle source)
    {
        texture = null;
        source = Rectangle.Empty;

        FarmAnimalData? data = animal.GetAnimalData();
        if (animal.currentProduce.Value is null || data is null || data.HarvestType != FarmAnimalHarvestType.HarvestWithTool || string.IsNullOrWhiteSpace(data.HarvestTool))
            return false;

        // the tool the animal needs, drawn from its own sprite: milk pail, shears, or whatever a mod uses
        ParsedItemData? tool = ItemRegistry.GetData("(T)" + data.HarvestTool.Replace(" ", ""));
        if (tool is null)
            return false;

        texture = tool.GetTexture();
        source = tool.GetSourceRect();
        return true;
    }

    /// <summary>Draw an icon floating above an animal.</summary>
    private static void DrawIcon(SpriteBatch b, FarmAnimal animal, Texture2D texture, Rectangle source, float scale)
    {
        Vector2 animalCenter = new(
            animal.Position.X + animal.Sprite.getWidth() * 4f / 2f,
            animal.Position.Y
        );
        Vector2 position = Game1.GlobalToLocal(Game1.viewport, animalCenter) + new Vector2(-source.Width * scale / 2f, -HoverHeight);

        // a soft shadow so the icon reads against grass and wood alike
        b.Draw(Game1.staminaRect, new Rectangle((int)position.X - 4, (int)position.Y - 4, (int)(source.Width * scale) + 8, (int)(source.Height * scale) + 8), Color.Black * 0.25f);
        b.Draw(texture, position, source, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
    }
}
