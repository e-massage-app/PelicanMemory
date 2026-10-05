using System;
using System.Collections.Generic;
using System.Linq;
using PelicanMemory.Core;
using StardewValley;

namespace PelicanMemory.Features.RecipeLookup;

/// <summary>One ingredient of a recipe, with what the player has.</summary>
/// <param name="Name">The ingredient's display name.</param>
/// <param name="Needed">How many the recipe needs.</param>
/// <param name="InBag">How many the player is carrying.</param>
/// <param name="Stashes">Which containers hold the rest, biggest first.</param>
/// <param name="SpriteId">The item ID whose sprite represents this ingredient, which differs for "any egg" style entries.</param>
internal record RecipeIngredient(string Name, int Needed, int InBag, IReadOnlyList<ItemStash> Stashes, string SpriteId)
{
    /// <summary>The total stored away.</summary>
    public int InChests => this.Stashes.Sum(stash => stash.Count);
}

/// <summary>A known recipe which uses the item being looked up.</summary>
/// <param name="Name">The recipe's display name.</param>
/// <param name="Ingredients">Everything it needs.</param>
/// <param name="CanMakeNow">How many can be made from the bag alone.</param>
/// <param name="CanMakeWithChests">How many can be made once everything stored is fetched.</param>
/// <param name="SpriteId">The cooked dish's item ID, for its sprite.</param>
/// <param name="IsCooking">Whether it's cooked rather than crafted.</param>
internal record KnownRecipe(string Name, IReadOnlyList<RecipeIngredient> Ingredients, int CanMakeNow, int CanMakeWithChests, string SpriteId, bool IsCooking = true);

/// <summary>Finds the recipes the player has learned around a given item, and where the ingredients are.</summary>
/// <remarks>
/// Recipes using an item are cooking only: crafting recipes are looked up from the crafting menu itself, and mixing
/// both made the list noisy. Recipes making an item cover both, since the question there is "how do I get one?".
/// Only recipes the player already knows are listed, and the only stock reported is the player's own bag and chests.
/// </remarks>
internal class RecipeFinder
{
    /*********
    ** Fields
    *********/
    /// <summary>The shared view of what's in the player's chests.</summary>
    private readonly StorageIndex Storage;


    /*********
    ** Public methods
    *********/
    public RecipeFinder(StorageIndex storage)
    {
        this.Storage = storage;
    }

    /// <summary>Get the known recipes using an item, with the state of each ingredient.</summary>
    public IReadOnlyList<KnownRecipe> Find(Item item)
    {
        return GetKnownRecipes()
            .Where(recipe => recipe.recipeList.Keys.Any(ingredient => CraftingRecipe.ItemMatchesForCrafting(item, ingredient)))
            .Select(this.Describe)
            .OrderByDescending(recipe => recipe.CanMakeNow)
            .ThenByDescending(recipe => recipe.CanMakeWithChests)
            .ThenBy(recipe => recipe.Name)
            .ToList();
    }

    /// <summary>Get the learned recipes, crafting and cooking, which make an item, with the state of each ingredient.</summary>
    /// <param name="qualifiedItemId">The item to make.</param>
    /// <param name="learned">The recipes the player has learned.</param>
    public IReadOnlyList<KnownRecipe> FindMaking(string qualifiedItemId, IEnumerable<CraftingRecipe> learned)
    {
        return learned
            .Where(recipe => string.Equals(recipe.GetItemData()?.QualifiedItemId, qualifiedItemId, StringComparison.OrdinalIgnoreCase))
            .Select(this.Describe)
            .OrderBy(recipe => recipe.Name)
            .ToList();
    }


    /// <summary>Count the known recipes using an item, without looking through any chest.</summary>
    /// <remarks>Tooltips ask for this while the cursor hovers, so it must stay cheap.</remarks>
    public int Count(Item item)
    {
        int count = 0;
        foreach (CraftingRecipe recipe in GetKnownRecipes())
        {
            if (recipe.recipeList.Keys.Any(ingredient => CraftingRecipe.ItemMatchesForCrafting(item, ingredient)))
                count++;
        }
        return count;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Describe a recipe: each ingredient with what the player has, and how many can be made.</summary>
    private KnownRecipe Describe(CraftingRecipe recipe)
    {
        List<RecipeIngredient> ingredients = new();
        int canMake = int.MaxValue;
        int canMakeWithChests = int.MaxValue;

        foreach ((string ingredientId, int needed) in recipe.recipeList)
        {
            int inBag = CountInBag(ingredientId);
            ingredients.Add(new RecipeIngredient(
                Name: GetIngredientName(recipe, ingredientId),
                Needed: needed,
                InBag: inBag,
                Stashes: this.Storage.GetStashes(stored => CraftingRecipe.ItemMatchesForCrafting(stored, ingredientId)),
                SpriteId: recipe.getSpriteIndexFromRawIndex(ingredientId)
            ));

            RecipeIngredient added = ingredients[^1];
            canMake = Math.Min(canMake, needed > 0 ? inBag / needed : 0);
            canMakeWithChests = Math.Min(canMakeWithChests, needed > 0 ? (inBag + added.InChests) / needed : 0);
        }

        return new KnownRecipe(
            Name: recipe.DisplayName,
            Ingredients: ingredients,
            CanMakeNow: canMake == int.MaxValue ? 0 : canMake,
            CanMakeWithChests: canMakeWithChests == int.MaxValue ? 0 : canMakeWithChests,
            SpriteId: recipe.GetItemData()?.QualifiedItemId ?? "",
            IsCooking: recipe.isCookingRecipe
        );
    }

    /// <summary>Get every cooking recipe the player has learned.</summary>
    private static IEnumerable<CraftingRecipe> GetKnownRecipes()
    {
        foreach (string name in Game1.player.cookingRecipes.Keys)
        {
            if (CraftingRecipe.cookingRecipes.ContainsKey(name))
                yield return new CraftingRecipe(name, isCookingRecipe: true);
        }
    }

    /// <summary>Count how many of an ingredient the player is carrying.</summary>
    private static int CountInBag(string ingredientId)
    {
        int count = 0;
        foreach (Item item in Game1.player.Items)
        {
            if (item != null && CraftingRecipe.ItemMatchesForCrafting(item, ingredientId))
                count += item.Stack;
        }
        return count;
    }

    /// <summary>Get an ingredient's display name, which the recipe itself can provide for categories like 'any egg'.</summary>
    private static string GetIngredientName(CraftingRecipe recipe, string ingredientId)
    {
        try
        {
            return recipe.getNameFromIndex(ingredientId);
        }
        catch
        {
            return ItemRegistry.GetDataOrErrorItem(ingredientId).DisplayName;
        }
    }
}
