using Vintagestory.API.Common;

namespace VsRecipeBrowser;

/// <summary>Shared ingredient semantics for lookup, display and transfer planning.
/// Never changes a registered game recipe. Whole-grid validation remains native.</summary>
internal static class IngredientRules
{
    internal static bool IsReusable(CraftingRecipeIngredient ingredient) => !ingredient.GetConsumeProperties().Consume;

    internal static int RequiredQuantity(CraftingRecipeIngredient ingredient, int sets) =>
        Math.Max(1, ingredient.Quantity) * (IsReusable(ingredient) ? 1 : sets);

    internal static int UsesBeforeBreak(int durability, int cost, bool breaks) => !breaks || cost <= 0
        ? int.MaxValue : durability <= 0 ? 0 : (int)(((long)durability + cost - 1) / cost);

    internal static int AvailableUses(CraftingRecipeIngredient ingredient, ItemStack stack)
    {
        var properties = ingredient.GetConsumeProperties();
        if (properties.Consume || properties.DurabilityCost <= 0 || !properties.BreakOnZeroDurability
            || stack.Collectible.GetMaxDurability(stack) <= 0) return int.MaxValue;
        return UsesBeforeBreak(stack.Collectible.GetRemainingDurability(stack), properties.DurabilityCost, true);
    }

    internal static bool Accepts(IRecipeIngredientBase ingredient, ItemStack candidate)
    {
        if (ingredient is CraftingRecipeIngredient crafting && crafting.MatchingType == EnumRecipeMatchType.Exact
            && !crafting.Tags.IsEmpty && (crafting.Code is null || crafting.Code.Path.Contains('*')))
        {
            var matcher = new FastSearchCraftingRecipeIngredient(crafting)
            {
                MatchingType = crafting.Code is null ? EnumRecipeMatchType.TagsOnly : EnumRecipeMatchType.Wildcard
            };
            return matcher.SatisfiesAsIngredient(candidate, false);
        }
        return ingredient.SatisfiesAsIngredient(candidate, false);
    }
}
