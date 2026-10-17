using System.Text.RegularExpressions;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

internal sealed partial class RecipeOverlayDialog
{
    // A display entry retains source recipes and any correlated alternative frames.
    internal sealed record RecipeDisplayEntry(
        string Category,
        string Method,
        List<ItemStack?> Inputs,
        ItemStack Output,
        int? Temperature = null,
        List<List<ItemStack>>? InputAlternatives = null,
        List<ItemStack>? OutputAlternatives = null,
        List<GridRecipe>? SourceGridRecipes = null,
        ItemStack? SecondaryOutput = null,
        List<ItemStack>? SecondaryAlternatives = null,
        List<RecipeDisplayEntry>? Frames = null)
    {
        public string? FuelProcessCategory { get; init; }
    }
}
