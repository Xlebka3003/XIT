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
    // Fuel examples share a world-scoped thermal catalog.
    private static List<RecipeDisplayEntry>? fuelThermalCatalog;
    private static IWorldAccessor? fuelThermalWorld;

    // Alloy examples respect registered ratios and integer item quantities.
    private static void AppendCookingAndAlloyRecipes(ICoreClientAPI api, ItemStack target, RecipeViewMode mode, List<RecipeDisplayEntry> result, bool allCooking = false)
    {
        RecipeRegistrySystem registry = api.ModLoader.GetModSystem<RecipeRegistrySystem>();
        var fatRecipe = registry.CookingRecipes.FirstOrDefault(recipe => recipe.Enabled
            && recipe.CooksInto?.ResolvedItemstack?.Collectible.Code.Path == "fat-rendered"
            && recipe.CooksInto.ResolvedItemstack.StackSize == 1
            && recipe.Ingredients is { Length: 1 } && recipe.Ingredients[0].MinQuantity == 1 && recipe.Ingredients[0].MaxQuantity == 1);
        foreach (CookingRecipe recipe in registry.CookingRecipes.Where(recipe => recipe.Enabled))
        {
            ItemStack? recipeOutput = recipe.CooksInto?.ResolvedItemstack;
            if (fatRecipe is not null && recipeOutput?.Collectible.Code.Path == "fat-rendered")
            {
                if (ReferenceEquals(recipe, fatRecipe) && (allCooking || mode == RecipeViewMode.Recipes && SameCollectible(recipeOutput, target)
                    || mode == RecipeViewMode.Uses && fatRecipe.Ingredients[0].GetMatchingStack(target) is not null))
                    result.AddRange(RenderedFatBatches(fatRecipe));
                continue;
            }
            if (recipeOutput is null && !allCooking) continue;
            CookingRecipeIngredient[] cookingIngredients = recipe.Ingredients ?? Array.Empty<CookingRecipeIngredient>();
            // MinQuantity counts occupied cooking slots, not units in one slot.
            // Liquid slots each contain PortionSizeLitres, converted to native portions.
            var displayIngredients = cookingIngredients.SelectMany(ingredient => Enumerable.Repeat(ingredient, Math.Clamp(ingredient.MinQuantity, 0, 4))).Take(4).ToArray();
            List<ItemStack?> inputs = displayIngredients.Select(ingredient =>
            {
                var accepted = ingredient.ValidStacks ?? Array.Empty<CookingRecipeStack>();
                ItemStack? stack = (mode == RecipeViewMode.Uses
                    ? accepted.FirstOrDefault(value => value.ResolvedItemStack is { } item && SameCollectible(item, target))
                    : null)?.ResolvedItemStack?.Clone()
                    ?? accepted.FirstOrDefault()?.ResolvedItemStack?.Clone();
                if (stack is not null) stack.StackSize = CookingIngredientQuantity(ingredient, stack, 1);
                return stack;
            }).Take(9).ToList();
            if (!recipe.Matches(inputs.ToArray())) continue;
            if (recipeOutput is null)
            {
                var pot = api.World.Blocks.OfType<BlockCookingContainer>().FirstOrDefault(block => block.Code?.Domain == "game" && block.Variant.ContainsKey("type"));
                var cookedPot = pot is null ? null : api.World.GetBlock(pot.CodeWithVariant("type", "cooked")) as BlockCookedContainer;
                int servings = recipe.GetQuantityServings(inputs.OfType<ItemStack>().ToArray());
                if (cookedPot is null || servings < 1) continue;
                var contents = inputs.OfType<ItemStack>().Select(stack => cookingIngredients
                    .Select(ingredient => ingredient.GetMatchingStack(stack)?.CookedStack?.ResolvedItemstack).FirstOrDefault(value => value is not null)?.Clone() ?? stack.Clone()).ToArray();
                recipeOutput = new ItemStack(cookedPot);
                cookedPot.SetContents(recipe.Code, servings, recipeOutput, contents);
            }
            bool matches = mode == RecipeViewMode.Recipes ? SameCollectible(recipeOutput, target)
                : cookingIngredients.Any(ingredient => ingredient.ValidStacks?.Any(stack => stack.ResolvedItemStack is { } item && SameCollectible(item, target)) == true);
            if (matches || allCooking)
            {
                var variants = displayIngredients.Take(4).Select(ingredient => ingredient.ValidStacks?
                    .Where(value => value.ResolvedItemStack is not null)
                    .OrderByDescending(value => mode == RecipeViewMode.Uses && SameCollectible(value.ResolvedItemStack, target))
                    .Select(value => { var stack = value.ResolvedItemStack.Clone(); stack.StackSize = CookingIngredientQuantity(ingredient, stack, 1); return stack; })
                    .ToList() ?? new List<ItemStack>()).ToList();
                int temperature = Math.Max(100, inputs.Select(stack => stack?.Collectible.CombustibleProps?.MeltingPoint ?? 0).DefaultIfEmpty(0).Max());
                result.Add(new RecipeDisplayEntry("Cooking", recipe.Code ?? "Cooking pot", inputs, recipeOutput.Clone(), Temperature: temperature, InputAlternatives: variants));
            }
        }

    }

    private static IEnumerable<RecipeDisplayEntry> RenderedFatBatches(CookingRecipe recipe)
    {
        var source = recipe.Ingredients[0].ValidStacks?.FirstOrDefault(stack => stack.ResolvedItemStack?.Collectible.Code.Path == "fat")?.ResolvedItemStack;
        if (source is null || recipe.CooksInto?.ResolvedItemstack is not { } output) yield break;
        for (int amount = 1; amount <= 6; amount++)
        {
            var input = source.Clone(); input.StackSize = amount;
            if (!recipe.Matches(new[] { input })) continue;
            int servings = recipe.GetQuantityServings(new[] { input });
            if (servings <= 0 || servings > 6) continue;
            var result = output.Clone(); result.StackSize *= servings;
            yield return new RecipeDisplayEntry("Cooking", recipe.Code, new() { input }, result,
                Temperature: Math.Max(100, input.Collectible.CombustibleProps?.MeltingPoint ?? 0),
                InputAlternatives: new() { new() { input.Clone() } });
        }
    }

    private static int CookingIngredientQuantity(CookingRecipeIngredient ingredient, ItemStack stack, int portions)
    {
        double itemsPerLitre = stack.Collectible.Attributes?["waterTightContainerProps"]["itemsPerLitre"].AsDouble(0) ?? 0;
        return itemsPerLitre > 0 && ingredient.PortionSizeLitres > 0
            ? Math.Max(1, (int)Math.Round(itemsPerLitre * ingredient.PortionSizeLitres * portions)) : portions;
    }

    private static void AppendFuelUses(ICoreClientAPI api, ItemStack fuel, List<RecipeDisplayEntry> result)
    {
        if (!FuelFits(fuel, 0)) return;
        if (!ReferenceEquals(fuelThermalWorld, api.World) || fuelThermalCatalog is null)
        {
            if (!ReferenceEquals(processCatalogWorld, api.World) || processCatalog is null)
            {
                processCatalogWorld = api.World; processCatalog = BuildProcessCatalog(api); BuildProcessIndexes();
            }
            fuelThermalCatalog = processCatalog.Where(entry => entry.Category is "Heating" or "Melting").ToList();
            AppendCookingAndAlloyRecipes(api, fuel, RecipeViewMode.Recipes, fuelThermalCatalog, allCooking: true);
            fuelThermalWorld = api.World;
        }
        foreach (var entry in fuelThermalCatalog)
        {
            if (!FuelFits(fuel, entry.Temperature ?? 0)) continue;
            var alternatives = entry.InputAlternatives;
            if (entry.Category == "Cooking" && alternatives is not null)
            {
                alternatives = alternatives.Select(options => options.Where(stack => FuelFits(fuel,
                    Math.Max(100, stack.Collectible.CombustibleProps?.MeltingPoint ?? 0))).ToList()).ToList();
                if (alternatives.Where((options, index) => entry.Inputs.ElementAtOrDefault(index) is not null && options.Count == 0).Any()) continue;
            }
            result.Add(entry with { Category = "Fuel", FuelProcessCategory = entry.Category, InputAlternatives = alternatives });
        }
    }

    private static List<RecipeDisplayEntry> RemoveDuplicateFuelUses(List<RecipeDisplayEntry> entries, ItemStack target)
    {
        string Identity(RecipeDisplayEntry entry) => RecipeIdentity(entry with {
            Category = entry.FuelProcessCategory ?? entry.Category, FuelProcessCategory = null
        });
        var fuelProcesses = entries.Where(entry => entry.Category == "Fuel").Select(Identity).ToHashSet(StringComparer.Ordinal);
        return entries.Where(entry => entry.Category == "Fuel" || !fuelProcesses.Contains(Identity(entry))
            // Heating slot 4 is fuel. Retain true ingredient uses (e.g. wood
            // processed into charcoal), even when the same item also burns.
            || entry.Inputs.Where((stack, slot) => entry.Category != "Heating" || slot != 4)
                .Any(stack => stack is not null && SameCollectible(stack, target))).ToList();
    }

    private static int[] FindValidAlloyNuggetQuantities(MetalAlloyIngredient[] ingredients)
    {
        int count = ingredients.Length;
        if (count == 0) return Array.Empty<int>();
        var quantities = new int[count];
        // A normal metal nugget contributes five units and an ingot takes 100
        // units, hence twenty pieces. Try one ingot first, then larger valid
        // batches for alloys whose ratios cannot be expressed in twentieths.
        for (int total = 20; total <= 100; total += 20)
        {
            if (FindAlloyCombination(ingredients, quantities, 0, total, total)) return (int[])quantities.Clone();
        }
        return FindValidAlloyQuantities(ingredients);
    }

    private static int[] FindValidAlloyQuantities(MetalAlloyIngredient[] ingredients)
    {
        int count = ingredients.Length;
        if (count == 0) return Array.Empty<int>();
        var quantities = new int[count];
        for (int total = count; total <= 64; total++)
        {
            if (FindAlloyCombination(ingredients, quantities, 0, total, total)) return (int[])quantities.Clone();
        }
        return Enumerable.Repeat(1, count).ToArray();
    }

    private static bool FindAlloyCombination(MetalAlloyIngredient[] ingredients, int[] quantities, int index, int remaining, int total)
    {
        if (index == ingredients.Length - 1)
        {
            if (remaining < 1) return false;
            float ratio = remaining / (float)total;
            if (ratio + 0.0001f < ingredients[index].MinRatio || ratio - 0.0001f > ingredients[index].MaxRatio) return false;
            quantities[index] = remaining;
            return true;
        }

        int remainingIngredients = ingredients.Length - index - 1;
        for (int amount = 1; amount <= remaining - remainingIngredients; amount++)
        {
            float ratio = amount / (float)total;
            if (ratio + 0.0001f < ingredients[index].MinRatio || ratio - 0.0001f > ingredients[index].MaxRatio) continue;
            quantities[index] = amount;
            if (FindAlloyCombination(ingredients, quantities, index + 1, remaining - amount, total)) return true;
        }
        return false;
    }
}
