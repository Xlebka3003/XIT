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
    // World-scoped recipe indexes are cleared when the world changes.
    private static IWorldAccessor? recipeCatalogWorld;
    private static IReadOnlyList<IRecipeBase> recipeCatalog = Array.Empty<IRecipeBase>();
    private static readonly Dictionary<string, List<IRecipeBase>> recipesByOutput = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<IRecipeBase>> recipesByOutputGroup = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<IRecipeBase>> recipesByIngredient = new(StringComparer.Ordinal);
    private static readonly List<IRecipeBase> patternedOutputRecipes = new();
    private static readonly List<IRecipeBase> patternedIngredientRecipes = new();
    private static readonly Dictionary<string, List<IRecipeBase>> patternedOutputsByBucket = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<IRecipeBase>> patternedIngredientsByBucket = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string[]> tagConditionBuckets = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<RecipeDisplayEntry>> groupedRecipeCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<GridRecipe, string> reusableToolRouteKeys = new();
    private static readonly Dictionary<string, List<GridRecipe>> registeredToolRoutes = new(StringComparer.Ordinal);
    private static IWorldAccessor? processCatalogWorld;
    private static List<RecipeDisplayEntry>? processCatalog;
    private static readonly Dictionary<string, List<RecipeDisplayEntry>> processesByOutput = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<RecipeDisplayEntry>> processesByInput = new(StringComparer.Ordinal);
    private static readonly HashSet<string> cookingOutputKeys = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ItemStack> preferredCastingFeedstocks = new(StringComparer.Ordinal);

    // Build indexes once rather than scanning every registered recipe on each click.
    public static void PrepareRecipeCatalog(ICoreClientAPI api)
    {
        if (ReferenceEquals(recipeCatalogWorld, api.World) && recipeCatalog.Count > 0) return;

        var stopwatch = Stopwatch.StartNew();
        recipeCatalogWorld = api.World;
        RecipeRegistrySystem registry = api.ModLoader.GetModSystem<RecipeRegistrySystem>();
        recipeCatalog = api.World.GridRecipes.Cast<IRecipeBase>()
            .Concat(registry.SmithingRecipes.Where(recipe => recipe.Enabled).Cast<IRecipeBase>())
            .Concat(registry.BarrelRecipes.Where(recipe => recipe.Enabled).Cast<IRecipeBase>())
            .Concat(registry.ClayFormingRecipes.Where(recipe => recipe.Enabled).Cast<IRecipeBase>())
            .Concat(registry.KnappingRecipes.Where(recipe => recipe.Enabled).Cast<IRecipeBase>())
            .Where(recipe => recipe.Enabled)
            .Distinct<IRecipeBase>(ReferenceEqualityComparer.Instance)
            .ToArray();
        ingredientCacheWorld = api.World;
        ingredientDisplayCache.Clear();
        ingredientAlternativesCache.Clear();
        ingredientGroupDisplayCache.Clear();
        ingredientGroupAlternativesCache.Clear();
        ingredientMatchCache.Clear();
        BuildRecipeIndexes(api);

        processCatalogWorld = api.World;
        processCatalog = BuildProcessCatalog(api);
        BuildProcessIndexes();
        cookingOutputKeys.Clear();
        foreach (var cooking in registry.CookingRecipes.Where(r => r.Enabled))
            if (RecipeStackKey(cooking.CooksInto?.ResolvedItemstack) is { } key) cookingOutputKeys.Add(key);
        smithingHammerWorld = null;
        smithingHammers.Clear();
        api.Logger.Notification("[Recipe Browser] Indexed {0} crafting recipes in {1} ms", recipeCatalog.Count, stopwatch.ElapsedMilliseconds);
    }

    private static void BuildRecipeIndexes(ICoreClientAPI api)
    {
        recipesByOutput.Clear();
        recipesByOutputGroup.Clear();
        recipesByIngredient.Clear();
        patternedOutputRecipes.Clear();
        patternedIngredientRecipes.Clear();
        patternedOutputsByBucket.Clear();
        patternedIngredientsByBucket.Clear();
        tagConditionBuckets.Clear();
        groupedRecipeCache.Clear();
        reusableToolRouteKeys.Clear();
        registeredToolRoutes.Clear();

        foreach (IRecipeBase recipe in recipeCatalog)
        {
            if (recipe is GridRecipe toolRecipe && toolRecipe.ResolvedIngredients?.Any(ingredient => ingredient is not null
                && IngredientRules.IsReusable(ingredient) && !ingredient.Tags.IsEmpty) == true)
            {
                string route = reusableToolRouteKeys[toolRecipe] = RecipeRouteIdentity.Key(toolRecipe);
                if (!registeredToolRoutes.TryGetValue(route, out var variants)) registeredToolRoutes[route] = variants = new();
                variants.Add(toolRecipe);
            }
            string? outputKey = RecipeStackKey(recipe.RecipeOutput?.ResolvedItemStack);
            string outputPattern = recipe switch
            {
                GridRecipe grid => grid.Output.Code?.Path ?? "",
                SmithingRecipe smithing => smithing.Output.Code?.Path ?? "",
                _ => recipe.RecipeOutput?.ResolvedItemStack?.Collectible?.Code?.Path ?? ""
            };
            if (outputKey is not null) AddIndexedRecipe(recipesByOutput, outputKey, recipe);
            string? outputGroup = CustomOutputGroup(outputPattern);
            if (outputGroup is not null) AddIndexedRecipe(recipesByOutputGroup, outputGroup, recipe);
            if (recipe.RecipeOutput?.ResolvedItemStack is { } outputStack && LogicalOutputGroup(outputStack) is { } logicalGroup)
                AddIndexedRecipe(recipesByOutputGroup, logicalGroup, recipe);
            if (outputKey is null || outputPattern.Contains('*') || outputPattern.Contains('{'))
            {
                patternedOutputRecipes.Add(recipe);
                AddIndexedRecipe(patternedOutputsByBucket, PatternBucket(outputPattern), recipe);
            }

            bool hasPattern = false;
            foreach (IRecipeIngredient ingredient in recipe.RecipeIngredients ?? Array.Empty<IRecipeIngredient>())
            {
                string? ingredientKey = ingredient is IRecipeIngredientBase baseIngredient
                    ? RecipeStackKey(baseIngredient.ResolvedItemStack)
                    : null;
                string pattern = ingredient.Code?.Path ?? "";
                bool isPatterned = ingredientKey is null || pattern.Contains('*') || pattern.Contains('{')
                    || (ingredient is IRecipeIngredientBase tagged && !tagged.Tags.IsEmpty);
                // ResolvedItemStack on a wildcard/tag ingredient is merely the
                // game's display example (often hammer-copper). Indexing that
                // example as an exact ingredient polluted U with thousands of
                // unrelated recipes.
                if (!isPatterned && ingredientKey is not null) AddIndexedRecipe(recipesByIngredient, ingredientKey, recipe);
                if (isPatterned) hasPattern = true;
            }
            if (hasPattern)
            {
                patternedIngredientRecipes.Add(recipe);
                string[] buckets = (recipe.RecipeIngredients ?? Array.Empty<IRecipeIngredient>())
                    .SelectMany(ingredient => IngredientPatternBuckets(api, ingredient))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                foreach (string bucket in buckets) AddIndexedRecipe(patternedIngredientsByBucket, bucket, recipe);
            }
        }
    }

    private static string PatternBucket(string path)
    {
        if (string.IsNullOrEmpty(path)) return "*";
        int end = path.IndexOfAny(new[] { '-', '*', '{' });
        return end <= 0 ? (path.Contains('*') || path.Contains('{') ? "*" : path) : path[..end];
    }

    private static IEnumerable<string> IngredientPatternBuckets(ICoreClientAPI api, IRecipeIngredient ingredient)
    {
        string path = ingredient.Code?.Path ?? "";
        if (!string.IsNullOrEmpty(path) && (ingredient is not IRecipeIngredientBase value || value.Tags.IsEmpty))
            return new[] { PatternBucket(path) };
        if (ingredient is not CraftingRecipeIngredient tagged || !IsReusableToolIngredient(tagged) || tagged.Tags.IsEmpty)
            return new[] { "*" };

        string conditionKey = IngredientGroupKey(tagged);
        if (tagConditionBuckets.TryGetValue(conditionKey, out string[]? cached)) return cached;
        var matches = new List<string>();
        foreach (CollectibleObject? tool in api.World.Collectibles)
        {
            if (tool?.Code is null || tool.ItemClass != tagged.Type) continue;
            ItemStack representative = new(tool);
            try
            {
                bool accepted = IngredientAcceptsDisplay(tagged, representative);
                ingredientMatchCache[$"{IngredientGroupKey(tagged)}|{RecipeStackKey(representative)}"] = accepted;
                if (accepted)
                {
                    matches.Add(PatternBucket(tool.Code.Path));
                    ingredientGroupDisplayCache.TryAdd(IngredientGroupKey(tagged), representative.Clone());
                }
            }
            catch { }
        }
        string[] result = matches.Count > 0 ? matches.Distinct(StringComparer.Ordinal).ToArray() : new[] { "*" };
        tagConditionBuckets[conditionKey] = result;
        return result;
    }

    private static void AddIndexedRecipe(Dictionary<string, List<IRecipeBase>> index, string key, IRecipeBase recipe)
    {
        if (!index.TryGetValue(key, out List<IRecipeBase>? entries)) index[key] = entries = new();
        entries.Add(recipe);
    }

    private static string? RecipeStackKey(ItemStack? stack)
    {
        if (stack?.Collectible?.Code is null) return null;
        string key = $"{stack.Class}:{DirectionalCatalog.Key(stack.Collectible)}";
        if (stack.Collectible is BlockSmeltedContainer && recipeCatalogWorld is not null)
        {
            var contents = ((BlockSmeltedContainer)stack.Collectible).GetContents(recipeCatalogWorld, stack);
            key = "molten:" + contents.Key?.Collectible.Code;
        }
        return key;
    }

    internal static bool HasObtainingRecipe(ICoreClientAPI api, ItemStack target)
    {
        PrepareRecipeCatalog(api);
        string? key = RecipeStackKey(target);
        if (key is not null && cookingOutputKeys.Contains(key)) return true;
        if (key is not null && processesByOutput.TryGetValue(key, out var processes)
            && processes.Any(entry => entry.Category is "Heating" or "Grinding" or "Bloomery" or "Casting"
                or "Crushing" or "Baking" or "Pressing" or "Melting" or "Pit firing" or "Cementation"
                || (entry.Category == "Drying" && entry.Inputs.FirstOrDefault()?.Collectible.Code.Path.StartsWith("hide-oiled-") == true)))
            return true;
        foreach (var recipe in IndexedCandidates(target, RecipeViewMode.Recipes))
            if (recipe is GridRecipe grid ? GridOutputMatchesExtended(grid, target) : RecipeOutputMatches(recipe, target))
                return true;
        return false;
    }

    // Catalog lifetime follows the current world.
    private static IReadOnlyList<IRecipeBase> IndexedCandidates(ItemStack target, RecipeViewMode mode)
    {
        string? key = RecipeStackKey(target);
        Dictionary<string, List<IRecipeBase>> index = mode == RecipeViewMode.Recipes ? recipesByOutput : recipesByIngredient;
        string bucket = PatternBucket(target.Collectible?.Code?.Path ?? "");
        Dictionary<string, List<IRecipeBase>> patternIndex = mode == RecipeViewMode.Recipes
            ? patternedOutputsByBucket
            : patternedIngredientsByBucket;
        IEnumerable<IRecipeBase> patterned = patternIndex.TryGetValue(bucket, out List<IRecipeBase>? bucketRecipes)
            ? bucketRecipes
            : Array.Empty<IRecipeBase>();
        // Unclassified tag recipes must also be considered for tools. A failed
        // representative classification must not remove recipes from U.
        IEnumerable<IRecipeBase> wildcard = patternIndex.TryGetValue("*", out List<IRecipeBase>? wildcardRecipes)
            ? wildcardRecipes
            : Array.Empty<IRecipeBase>();
        IEnumerable<IRecipeBase> exact = key is not null && index.TryGetValue(key, out List<IRecipeBase>? found)
            ? found
            : Array.Empty<IRecipeBase>();
        if (mode == RecipeViewMode.Recipes && CeramicOutputGroup(target.Collectible.Code.Path) is { } ceramic
            && recipesByOutputGroup.TryGetValue(ceramic, out var ceramicRecipes)) exact = exact.Concat(ceramicRecipes);
        if (mode == RecipeViewMode.Recipes && IsStoneSnowball(target.Collectible.Code.Path)
            && recipesByOutputGroup.TryGetValue("stone-snowballs", out var snowRecipes)) exact = exact.Concat(snowRecipes);
        return exact.Concat(patterned).Concat(wildcard)
            .Distinct<IRecipeBase>(ReferenceEqualityComparer.Instance).ToArray();
    }

    public static void ClearRecipeCatalog()
    {
        // Cached stacks and registered recipes must not leak into the next world.
        fuelThermalCatalog = null; fuelThermalWorld = null;
        reusableToolRouteKeys.Clear();
        registeredToolRoutes.Clear();
        recipeCatalogWorld = null;
        recipeCatalog = Array.Empty<IRecipeBase>();
        recipesByOutput.Clear();
        recipesByOutputGroup.Clear();
        recipesByIngredient.Clear();
        patternedOutputRecipes.Clear();
        patternedIngredientRecipes.Clear();
        patternedOutputsByBucket.Clear();
        patternedIngredientsByBucket.Clear();
        tagConditionBuckets.Clear();
        groupedRecipeCache.Clear();
        processCatalogWorld = null;
        processCatalog = null;
        processesByOutput.Clear();
        processesByInput.Clear();
        ingredientCacheWorld = null;
        ingredientDisplayCache.Clear();
        ingredientAlternativesCache.Clear();
        ingredientGroupDisplayCache.Clear();
        ingredientGroupAlternativesCache.Clear();
        ingredientMatchCache.Clear();
    }
}
