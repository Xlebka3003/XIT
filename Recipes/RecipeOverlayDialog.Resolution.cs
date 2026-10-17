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
    // Display caches never replace native ingredient validation.
    private static IWorldAccessor? ingredientCacheWorld;
    private static readonly Dictionary<IRecipeIngredientBase, ItemStack?> ingredientDisplayCache = new();
    private static readonly Dictionary<IRecipeIngredientBase, List<ItemStack>> ingredientAlternativesCache = new();
    private static readonly Dictionary<string, ItemStack?> ingredientGroupDisplayCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<ItemStack>> ingredientGroupAlternativesCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, bool> ingredientMatchCache = new(StringComparer.Ordinal);
    private static IWorldAccessor? smithingHammerWorld;
    private static List<ItemStack> smithingHammers = new();

    // Resolve previews from cloned stacks; never mutate the player's inventory.
    private static ItemStack? ResolveRecipeOutput(ICoreClientAPI api, IRecipeBase recipe)
    {
        ItemStack? fallback = recipe.RecipeOutput?.ResolvedItemStack?.Clone();
        if (recipe is not GridRecipe grid || grid.ResolvedIngredients is not { Length: > 0 }) return fallback;

        try
        {
            int width = Math.Max(1, grid.Width);
            int height = Math.Max(1, grid.Height);
            var slots = new ItemSlot[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // RecipeBase expects (row, column), not (x, y). Passing these in
                // the opposite order dropped every ingredient below the first
                // row in narrow recipes such as pickaxe head + stick.
                CraftingRecipeIngredient? ingredient = grid.GetElementInGrid(y, x, grid.ResolvedIngredients, width);
                ItemStack? stack = ingredient is null ? null : ResolveIngredientDisplayStack(api, ingredient);
                if (stack is not null) stack.StackSize = Math.Max(1, ingredient!.Quantity);
                slots[y * width + x] = new DummySlot(stack);
            }
            var outputSlot = new DummySlot();
            grid.GenerateOutputStack(slots, outputSlot);
            return outputSlot.Itemstack?.Clone() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static RecipeDisplayEntry ProcessEntry(string category, string method, ItemStack input, ItemStack output) =>
        new(category, method, new List<ItemStack?> { input.Clone() }, output.Clone());

    private static bool RecipeOutputMatches(IRecipeBase recipe, ItemStack target)
    {
        ItemStack? resolved = recipe.RecipeOutput?.ResolvedItemStack;
        if (SameCeramicFamily(resolved, target)) return true;
        if (resolved is not null && SameCollectible(resolved, target)) return true;
        if (resolved is not null && DirectionalOutputMatches(resolved, target)) return true;
        string pattern = recipe is SmithingRecipe smithing ? smithing.Output.Code?.Path ?? "" : "";
        return TryExtractVariantMappings(pattern, target.Collectible?.Code?.Path, out _);
    }

    private static ItemStack? ResolveSmithingIngredient(ICoreClientAPI api, SmithingRecipe recipe, ItemStack output)
    {
        string ingredientPath = recipe.Ingredient.Code?.Path ?? "";
        string outputPattern = recipe.Output.Code?.Path ?? "";
        if (TryExtractVariantMappings(outputPattern, output.Collectible?.Code?.Path, out Dictionary<string, string> mappings))
        {
            if (!string.IsNullOrEmpty(recipe.Ingredient.Name)
                && mappings.TryGetValue(recipe.Ingredient.Name, out string? namedVariant))
                ingredientPath = ingredientPath.Replace("*", namedVariant, StringComparison.Ordinal);
            foreach ((string name, string value) in mappings)
                ingredientPath = ingredientPath.Replace($"{{{name}}}", value, StringComparison.Ordinal);
        }
        if (!ingredientPath.Contains('*') && !ingredientPath.Contains('{'))
        {
            string domain = recipe.Ingredient.Code?.Domain ?? output.Collectible?.Code?.Domain ?? "game";
            CollectibleObject? collectible = recipe.Ingredient.Type == EnumItemClass.Block
                ? api.World.GetBlock(new AssetLocation(domain, ingredientPath))
                : api.World.GetItem(new AssetLocation(domain, ingredientPath));
            if (collectible is not null)
            {
                var stack = new ItemStack(collectible);
                stack.StackSize = SmithingInputQuantity(recipe, stack);
                return stack;
            }
        }
        var fallback = ResolveIngredientDisplayStack(api, recipe.Ingredient);
        if (fallback is not null) fallback.StackSize = SmithingInputQuantity(recipe, fallback);
        return fallback;
    }

    private static int SmithingInputQuantity(SmithingRecipe recipe, ItemStack material)
    {
        int explicitQuantity = Math.Max(1, recipe.Ingredient.Quantity);
        int requiredVoxels = recipe.Voxels?.Cast<bool>().Count(voxel => voxel) ?? 0;
        int suppliedVoxels = 0;
        try
        {
            suppliedVoxels = material.Collectible is IAnvilWorkable workable ? workable.VoxelCountForHandbook(material) : 0;
        }
        catch { }
        return suppliedVoxels > 0 ? Math.Max(explicitQuantity, (int)Math.Ceiling(requiredVoxels / (double)suppliedVoxels)) : explicitQuantity;
    }

    private static List<ItemStack> FindSmithingHammers(ICoreClientAPI api)
    {
        if (!ReferenceEquals(smithingHammerWorld, api.World))
        {
            smithingHammerWorld = api.World;
            smithingHammers = api.World.Collectibles
                .Where(value => value?.Code?.Path.StartsWith("hammer-", StringComparison.OrdinalIgnoreCase) == true)
                .GroupBy(value => value.Code)
                .Select(group => new ItemStack(group.First()))
                .ToList();
        }
        return smithingHammers;
    }

    private static List<ItemStack?> BuildInputStacks(ICoreClientAPI api, IRecipeBase recipe, List<IRecipeIngredient> ingredients, ItemStack? requestedOutput)
    {
        if (recipe is GridRecipe grid && grid.ResolvedIngredients is { Length: > 0 })
        {
            var positioned = Enumerable.Repeat<ItemStack?>(null, 9).ToList();
            int width = Math.Clamp(grid.Width, 1, 3);
            int height = Math.Clamp(grid.Height, 1, 3);
            int offsetX = (3 - width) / 2;
            int offsetY = (3 - height) / 2;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                CraftingRecipeIngredient? ingredient = grid.GetElementInGrid(y, x, grid.ResolvedIngredients, width);
                ItemStack? stack = ingredient is null ? null : ResolveGridIngredientForOutput(api, grid, ingredient, requestedOutput)
                    ?? ResolveIngredientDisplayStack(api, ingredient);
                if (stack is not null) stack.StackSize = Math.Max(1, ingredient!.Quantity);
                positioned[(y + offsetY) * 3 + x + offsetX] = stack;
            }
            return positioned;
        }

        return ingredients.Select(ingredient =>
        {
            ItemStack? stack = ingredient is IRecipeIngredientBase baseIngredient
                ? ResolveIngredientDisplayStack(api, baseIngredient)
                : null;
            if (stack is not null) stack.StackSize = Math.Max(1, ingredient.Quantity);
            return stack;
        }).Take(9).ToList();
    }

    private static bool GridOutputMatches(GridRecipe grid, ItemStack target)
    {
        try
        {
            if (grid.Output.SatisfiesAsIngredient(target, false)) return true;
            if (grid.Output.ResolvedItemStack is { } blockOutput && DirectionalOutputMatches(blockOutput, target)) return true;
            if (target.Class == EnumItemClass.Block && grid.Output.ResolvedItemStack is { Class: EnumItemClass.Block } resolved
                && DirectionalCatalog.Key(resolved.Collectible) == DirectionalCatalog.Key(target.Collectible))
            {
                var sameDirection = new ItemStack(resolved.Collectible, target.StackSize) { Attributes = target.Attributes.Clone() };
                if (grid.Output.SatisfiesAsIngredient(sameDirection, false)) return true;
                if (target.Collectible.Attributes?["inventoryClassName"].AsString() == "chest")
                {
                    string? actualType = target.Attributes.GetString("type")
                        ?? target.Collectible.Attributes?["defaultType"].AsString();
                    string? outputType = resolved.Attributes.GetString("type")
                        ?? resolved.Collectible.Attributes?["defaultType"].AsString();
                    if (actualType == outputType && actualType is not null) return true;
                }
            }
        }
        catch { }
        return TryExtractVariantMappings(grid.Output.Code?.Path, target.Collectible?.Code?.Path, out _);
    }

    private static bool GridOutputMatchesExtended(GridRecipe grid, ItemStack target)
        => GridOutputMatches(grid, target) || SameCeramicFamily(grid.Output.ResolvedItemStack, target)
            || SameStoneSnowballFamily(grid.Output.ResolvedItemStack, target);

    private static bool SameRequestedResult(ItemStack result, ItemStack requested)
    {
        if (SameCeramicFamily(result, requested)) return true;
        if (SameStoneSnowballFamily(result, requested)) return true;
        if (RecipeStackKey(result) != RecipeStackKey(requested)) return false;
        foreach (string key in new[] { "wood", "metal", "material", "color", "colour", "deco", "type", "recipeCode" })
        {
            if (!requested.Attributes.HasAttribute(key) || !result.Attributes.HasAttribute(key)) continue;
            if (!result.Attributes[key].Equals(recipeCatalogWorld!, requested.Attributes[key])) return false;
        }
        return true;
    }

    private static bool DirectionalOutputMatches(ItemStack output, ItemStack target) =>
        output.Class == EnumItemClass.Block && target.Class == EnumItemClass.Block
        && DirectionalCatalog.Key(output.Collectible) == DirectionalCatalog.Key(target.Collectible)
        && output.Attributes.IsSubSetOf(recipeCatalogWorld!, target.Attributes);

    private static ItemStack? ResolveGridIngredientForOutput(
        ICoreClientAPI api,
        GridRecipe grid,
        CraftingRecipeIngredient ingredient,
        ItemStack? requestedOutput)
    {
        if (requestedOutput?.Collectible?.Code is null
            || !TryExtractVariantMappings(grid.Output.Code?.Path, requestedOutput.Collectible.Code.Path, out Dictionary<string, string> mappings))
            return null;

        string path = ingredient.Code?.Path ?? "";
        if (path.Length == 0) return null;
        if (!string.IsNullOrEmpty(ingredient.Name) && mappings.TryGetValue(ingredient.Name, out string? namedVariant))
            path = path.Replace("*", namedVariant, StringComparison.Ordinal);
        foreach ((string name, string value) in mappings)
            path = path.Replace($"{{{name}}}", value, StringComparison.Ordinal);
        if (path.Contains('*') || path.Contains('{')) return null;

        string domain = ingredient.Code?.Domain ?? requestedOutput.Collectible.Code.Domain;
        CollectibleObject? collectible = ingredient.Type == EnumItemClass.Block
            ? api.World.GetBlock(new AssetLocation(domain, path))
            : api.World.GetItem(new AssetLocation(domain, path));
        return collectible is null ? null : PrepareDisplayIngredient(api.World, ingredient, new ItemStack(collectible, Math.Max(1, ingredient.Quantity)));
    }

    private static bool TryExtractVariantMappings(string? pattern, string? actual, out Dictionary<string, string> mappings)
    {
        mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(actual)) return false;
        MatchCollection placeholders = Regex.Matches(pattern, "\\{([^}]+)\\}");
        if (placeholders.Count == 0) return false;

        int cursor = 0;
        string regex = "^";
        foreach (Match placeholder in placeholders)
        {
            regex += Regex.Escape(pattern.Substring(cursor, placeholder.Index - cursor));
            regex += $"(?<{placeholder.Groups[1].Value}>[^/]+)";
            cursor = placeholder.Index + placeholder.Length;
        }
        regex += Regex.Escape(pattern.Substring(cursor)) + "$";

        Match valueMatch = Regex.Match(actual, regex, RegexOptions.CultureInvariant);
        if (!valueMatch.Success) return false;
        foreach (Match placeholder in placeholders)
        {
            string name = placeholder.Groups[1].Value;
            mappings[name] = valueMatch.Groups[name].Value;
        }
        return true;
    }

    private static ItemStack? ResolveIngredientDisplayStack(ICoreClientAPI api, IRecipeIngredientBase ingredient)
    {
        ItemStack? resolved = ingredient.ResolvedItemStack?.Clone();
        if (resolved is not null)
        {
            resolved = PrepareDisplayIngredient(api.World, ingredient, resolved);
            if (ingredient is IRecipeIngredient sized) resolved.StackSize = Math.Max(1, sized.Quantity);
            try { if (IngredientAcceptsDisplay(ingredient, resolved)) return resolved; }
            catch { }
        }

        if (!ReferenceEquals(ingredientCacheWorld, api.World))
        {
            ingredientCacheWorld = api.World;
            ingredientDisplayCache.Clear();
            ingredientAlternativesCache.Clear();
        }
        if (ingredientDisplayCache.TryGetValue(ingredient, out ItemStack? cached)) return cached?.Clone();
        string groupKey = IngredientGroupKey(ingredient);
        if (ingredientGroupDisplayCache.TryGetValue(groupKey, out ItemStack? groupedCached))
        {
            ingredientDisplayCache[ingredient] = groupedCached?.Clone();
            return groupedCached?.Clone();
        }

        ItemStack? match = null;
        foreach (CollectibleObject collectible in api.World.Collectibles.Where(value => value?.Code is not null))
        {
            List<ItemStack>? variants = null;
            try { variants = collectible.GetHandBookStacks(api); }
            catch { }
            IEnumerable<ItemStack> candidates = variants is { Count: > 0 }
                ? variants.Where(stack => stack?.Collectible?.Code is not null)
                : new[] { new ItemStack(collectible) };

            foreach (ItemStack candidate in candidates)
            {
                try
                {
                    var display = PrepareDisplayIngredient(api.World, ingredient, candidate);
                    if (!IngredientAcceptsDisplay(ingredient, display)) continue;
                    match = display.Clone();
                    break;
                }
                catch { }
            }
            if (match is not null) break;
        }

        ingredientDisplayCache[ingredient] = match?.Clone();
        ingredientGroupDisplayCache[groupKey] = match?.Clone();
        return match;
    }

    private static bool IngredientMatches(IRecipeIngredient ingredient, ItemStack target)
    {
        if (ingredient is IRecipeIngredientBase baseIngredient)
        {
            string cacheKey = $"{IngredientGroupKey(baseIngredient)}|{RecipeStackKey(target)}";
            if (ingredientMatchCache.TryGetValue(cacheKey, out bool cached)) return cached;
            bool matches;
            try
            {
                matches = IngredientAcceptsDisplay(baseIngredient, target);
                if (!matches && target.Class == EnumItemClass.Block && baseIngredient.ResolvedItemStack is { } resolved
                    && resolved.Class == target.Class && RecipeStackKey(resolved) == RecipeStackKey(target))
                    matches = DirectionalCatalog.Normalize(recipeCatalogWorld!, resolved)
                        .Equals(recipeCatalogWorld!, target, Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes);
            }
            catch { matches = baseIngredient.ResolvedItemStack is { } resolved && SameCollectible(resolved, target); }
            ingredientMatchCache[cacheKey] = matches;
            return matches;
        }
        return false;
    }

    private static ItemStack PrepareDisplayIngredient(IWorldAccessor world, IRecipeIngredientBase ingredient, ItemStack candidate)
    {
        if (ingredient is not CraftingRecipeIngredient crafting || candidate.Collectible is not BlockLiquidContainerBase container
            || crafting.RecipeAttributes?["requiresContent"].Exists != true) return candidate;
        try
        {
        var liquid = crafting.RecipeAttributes["requiresContent"].AsObject<JsonItemStack>();
        if (liquid is null || !liquid.Resolve(world, "XRB required liquid display")) return candidate;
        double litres = crafting.RecipeAttributes["requiresLitres"].AsDouble(0);
        double perLitre = liquid.ResolvedItemstack.Collectible.Attributes?["waterTightContainerProps"]["itemsPerLitre"].AsDouble(0) ?? 0;
        if (litres <= 0 || perLitre <= 0) return candidate;
        var display = candidate.Clone();
        var content = liquid.ResolvedItemstack.Clone();
        content.StackSize = Math.Max(1, (int)Math.Ceiling(litres * perLitre));
        container.SetContent(display, content);
        return display;
        }
        catch
        {
            // Malformed third-party recipe metadata must not break the browser.
            return candidate;
        }
    }

    private static bool SameCollectible(ItemStack left, ItemStack right) =>
        left.Class == right.Class && left.Collectible?.Code == right.Collectible?.Code;

    private static string MethodName(IRecipeBase recipe)
    {
        if (recipe is BarrelRecipe barrel) return barrel.SealHours > 0
            ? barrel.SealHours + BrowserText.Choose(" ч · Запечатать бочку", " h · Seal the barrel")
            : BrowserText.Choose("Без запечатывания", "No sealing required");
        if (recipe is GridRecipe) return "Crafting grid";
        string name = recipe.GetType().Name.Replace("Recipe", "");
        return Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
    }

    private static string CategoryName(IRecipeBase recipe)
    {
        if (recipe is GridRecipe) return "Crafting";
        string name = recipe.GetType().Name;
        if (name.Contains("Barrel", StringComparison.OrdinalIgnoreCase)) return "Barrel";
        if (name.Contains("Clay", StringComparison.OrdinalIgnoreCase)) return "Clay forming";
        if (name.Contains("Knapp", StringComparison.OrdinalIgnoreCase)) return "Knapping";
        if (name.Contains("Smith", StringComparison.OrdinalIgnoreCase)) return "Smithing";
        if (name.Contains("Cook", StringComparison.OrdinalIgnoreCase) || name.Contains("Meal", StringComparison.OrdinalIgnoreCase)) return "Cooking";
        if (name.Contains("Smelt", StringComparison.OrdinalIgnoreCase) || name.Contains("Bake", StringComparison.OrdinalIgnoreCase) || name.Contains("Alloy", StringComparison.OrdinalIgnoreCase)) return "Heating";
        if (name.Contains("Grind", StringComparison.OrdinalIgnoreCase) || name.Contains("Crush", StringComparison.OrdinalIgnoreCase) || name.Contains("Quern", StringComparison.OrdinalIgnoreCase)) return "Grinding";
        return MethodName(recipe);
    }
}
