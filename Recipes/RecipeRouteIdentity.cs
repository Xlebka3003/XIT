using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VsRecipeBrowser;

/// <summary>Groups actual registered crafting routes differing only in tagged
/// reusable tools. It never broadens an ingredient's native acceptance condition.</summary>
internal static class RecipeRouteIdentity
{
    internal static string Key(GridRecipe recipe)
    {
        string Ingredient(CraftingRecipeIngredient? ingredient, bool output = false)
        {
            if (ingredient is null) return "empty";
            using var data = new MemoryStream();
            using (var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, true)) ingredient.Tags.ToBytes(writer);
            string tags = Convert.ToBase64String(data.ToArray());
            bool interchangeableTool = !output && IngredientRules.IsReusable(ingredient) && !ingredient.Tags.IsEmpty;
            var consume = ingredient.GetConsumeProperties();
            return $"{ingredient.Type}|{(interchangeableTool ? "tagged-tool" : ingredient.Code?.ToString())}|{tags}|{ingredient.Quantity}"
                + $"|{consume.Consume}|{consume.DurabilityChange}|{consume.BreakOnZeroDurability}"
                + $"|{string.Join(',', ingredient.AllowedVariants ?? Array.Empty<string>())}|{string.Join(',', ingredient.SkipVariants ?? Array.Empty<string>())}"
                + $"|{ingredient.Name}|{FavoriteIdentity.AttributesKey(ingredient.ResolvedItemStack?.Attributes ?? new TreeAttribute())}"
                + $"|{ingredient.ReturnedStack?.ToString()}|{ingredient.RecipeAttributes}";
        }
        var cells = (recipe.ResolvedIngredients ?? Array.Empty<CraftingRecipeIngredient>()).Select(value => Ingredient(value)).ToList();
        if (recipe.Shapeless) cells.Sort(StringComparer.Ordinal);
        return $"{recipe.GetType().FullName}|{recipe.Width}x{recipe.Height}|{recipe.Shapeless}|{recipe.RequiresTrait}|"
            + Ingredient(recipe.Output, true) + "|" + string.Join(";", cells);
    }
}
