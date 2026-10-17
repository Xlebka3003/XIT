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
    // Group presentation without discarding source recipes used for validation.
    private static List<RecipeDisplayEntry> CollectRecipes(ICoreClientAPI api, ItemStack target, RecipeViewMode mode)
    {
        target = DirectionalCatalog.Normalize(api.World, target);
        PrepareRecipeCatalog(api);
        if (target.Collectible.Code.Domain == "game" && target.Collectible.Code.Path == "fruitpress-we")
            target = new ItemStack(api.World.GetBlock(new AssetLocation("game:fruitpress-ns")));
        if (mode == RecipeViewMode.Uses && target.Collectible.Tool is not null)
        {
            target = target.Clone();
            target.Attributes.RemoveAttribute("durability");
        }
        string cacheKey = mode == RecipeViewMode.Uses
            ? $"{mode}:{RecipeStackKey(target)}:{(target.Collectible is BlockSmeltedContainer ? target.Attributes.ToJsonToken() : "")}"
            : $"{mode}:{RecipeStackKey(target)}:{target.Attributes.ToJsonToken()}";
        if (groupedRecipeCache.TryGetValue(cacheKey, out List<RecipeDisplayEntry>? cached)) return cached;

        List<RecipeDisplayEntry> collected = CollectAndGroupRecipes(api, target, mode);
        groupedRecipeCache[cacheKey] = collected;
        return collected;
    }

    private static List<RecipeDisplayEntry> CollectAndGroupRecipes(ICoreClientAPI api, ItemStack target, RecipeViewMode mode)
    {
        var timing = Stopwatch.StartNew();
        // Only inventory crafting is currently enabled. Walking every hidden
        // station and the fast-search index made a click scale with the entire
        // mod pack and could lock the render thread for several seconds.
        IReadOnlyList<IRecipeBase> allRecipes = IndexedCandidates(target, mode);
        string RouteKey(GridRecipe grid)
        {
            if (!reusableToolRouteKeys.TryGetValue(grid, out var key))
                reusableToolRouteKeys[grid] = key = RecipeRouteIdentity.Key(grid);
            return key;
        }
        HashSet<string> matchingToolRoutes = mode == RecipeViewMode.Uses
            ? allRecipes.OfType<GridRecipe>().Where(grid => grid.RecipeIngredients.Any(ingredient => IngredientMatches(ingredient, target)))
                .Select(RouteKey).ToHashSet(StringComparer.Ordinal) : new(StringComparer.Ordinal);
        if (matchingToolRoutes.Count > 0)
            allRecipes = allRecipes.Concat(matchingToolRoutes.SelectMany(key => registeredToolRoutes.TryGetValue(key, out var variants)
                ? variants : Enumerable.Empty<GridRecipe>())).Distinct<IRecipeBase>(ReferenceEqualityComparer.Instance).ToArray();
        long indexMs = timing.ElapsedMilliseconds;

        var result = new List<RecipeDisplayEntry>();
        foreach (IRecipeBase recipe in allRecipes)
        {
            List<IRecipeIngredient> ingredients = recipe.RecipeIngredients?.ToList() ?? new();
            if (mode == RecipeViewMode.Recipes)
            {
                if (recipe is GridRecipe grid)
                {
                    if (!GridOutputMatchesExtended(grid, target)) continue;
                }
                else if (!RecipeOutputMatches(recipe, target)) continue;
            }
            else if (!ingredients.Any(ingredient => IngredientMatches(ingredient, target))
                && !(recipe is GridRecipe toolVariant && matchingToolRoutes.Contains(RouteKey(toolVariant)))) continue;

            // Generate the concrete output only after the cheap code/ingredient
            // filter. This normally runs for one recipe instead of thousands.
            ItemStack? recipeOutput = ResolveRecipeOutput(api, recipe);
            if (recipeOutput is null) continue;
            if (mode == RecipeViewMode.Recipes && recipe is SmithingRecipe)
            {
                recipeOutput = target.Clone();
                recipeOutput.StackSize = Math.Max(1, ((SmithingRecipe)recipe).Output.StackSize);
            }

            if (mode == RecipeViewMode.Recipes && recipe is GridRecipe matchedGrid && GridOutputMatches(matchedGrid, target))
            {
                recipeOutput = target.Clone();
                recipeOutput.StackSize = Math.Max(1, matchedGrid.Output.Quantity);
            }
            ItemStack? requestedOutput = mode == RecipeViewMode.Recipes
                ? target
                : null;
            List<ItemStack?> inputStacks = BuildInputStacks(api, recipe, ingredients, requestedOutput);
            List<List<ItemStack>>? directAlternatives = null;
            if (recipe is SmithingRecipe smithing)
            {
                ItemStack? ingot = ResolveSmithingIngredient(api, smithing, mode == RecipeViewMode.Recipes ? target : recipeOutput);
                List<ItemStack> hammers = FindSmithingHammers(api);
                ItemStack? hammer = hammers.FirstOrDefault()?.Clone();
                inputStacks = new List<ItemStack?> { ingot, hammer };
                directAlternatives = new List<List<ItemStack>>
                {
                    ingot is null ? new List<ItemStack>() : new List<ItemStack> { ingot.Clone() },
                    hammers
                };
            }
            else if (recipe is not GridRecipe)
            {
                directAlternatives = ingredients.Select(ingredient =>
                {
                    bool variable = ingredient.Code?.Path.IndexOfAny(new[] { '*', '{' }) >= 0
                        || ingredient is IRecipeIngredientBase tagged && !tagged.Tags.IsEmpty;
                    bool linked = ingredient is CraftingRecipeIngredient named && !string.IsNullOrEmpty(named.Name);
                    if (variable && !linked && ingredient is IRecipeIngredientBase value)
                        return FindIngredientAlternatives(api, value);
                    return ingredient is IRecipeIngredientBase fixedIngredient && ResolveIngredientDisplayStack(api, fixedIngredient) is { } stack
                        ? new List<ItemStack> { stack } : new List<ItemStack>();
                }).Take(9).ToList();
            }
            List<GridRecipe>? sourceGridRecipes = recipe is GridRecipe sourceGrid ? new List<GridRecipe> { sourceGrid } : null;
            result.Add(new RecipeDisplayEntry(CategoryName(recipe), MethodName(recipe), inputStacks, recipeOutput.Clone(),
                InputAlternatives: directAlternatives, SourceGridRecipes: sourceGridRecipes));
        }
        long recipesMs = timing.ElapsedMilliseconds - indexMs;
        AppendCookingAndAlloyRecipes(api, target, mode, result);
        if (mode == RecipeViewMode.Uses) AppendFuelUses(api, target, result);
        AppendCollectibleProcesses(api, target, mode, result);
        if (mode == RecipeViewMode.Uses) result = RemoveDuplicateFuelUses(result, target);
        long processesMs = timing.ElapsedMilliseconds - indexMs - recipesMs;
        // Every actual crafting/processing station is visible. Passive item
        // transitions (spoilage, ripening, etc.) remain hidden until they get
        // a dedicated presentation instead of masquerading as a station.
        List<RecipeDisplayEntry> visible = result
            .Where(recipe => recipe.Category is
                "Crafting" or "Heating" or "Grinding" or "Bloomery" or "Smithing" or
                "Barrel" or "Clay forming" or "Knapping" or "Cooking" or "Alloying" or
                "Casting" or "Crushing" or "Baking" or "Pressing" or "Melting" or "Pit firing" or "Fuel" or "Cementation"
                || (recipe.Category == "Drying" && recipe.Inputs.FirstOrDefault()?.Collectible.Code.Path.StartsWith("hide-oiled-") == true))
            .Where(entry => mode != RecipeViewMode.Recipes || SameRequestedResult(entry.Output, target)
                || MeltingProducesIngot(api.World, entry, target)
                || entry.Category == "Cementation" && target.Collectible.Code.ToString() == "game:ingot-steel"
                || entry.SecondaryOutput is { } secondary && SameRequestedResult(secondary, target))
            .GroupBy(entry => RecipeIdentity(entry) + CraftingLayoutIdentity(entry), StringComparer.Ordinal)
            .Select(MergeEquivalentDisplays)
            .ToList();
        List<RecipeDisplayEntry> collapsed = CollapseInterchangeableRecipes(visible);
        for (int i = 0; i < collapsed.Count; i++)
        {
            if (collapsed[i].Category != "Smithing") continue;
            var alternatives = collapsed[i].InputAlternatives?.Select(list => new List<ItemStack>(list)).ToList()
                ?? new List<List<ItemStack>> { new() { collapsed[i].Inputs[0]!.Clone() }, new() };
            while (alternatives.Count < 2) alternatives.Add(new());
            alternatives[1] = FindSmithingHammers(api).Select(stack => stack.Clone()).ToList();
            collapsed[i] = collapsed[i] with { InputAlternatives = alternatives };
        }
        long groupingMs = timing.ElapsedMilliseconds - indexMs - recipesMs - processesMs;
        LogSlowOperation(api, target, mode, timing.ElapsedMilliseconds,
            $"index={indexMs}ms/{allRecipes.Count} candidates, resolve={recipesMs}ms, processes={processesMs}ms, grouping={groupingMs}ms, result={collapsed.Count}");
        api.Logger.Notification("[XRB Recipe Grouping] {0} {1}: source entries={2}, pages={3}, complete variants={4}",
            mode, target.Collectible.Code, visible.Count, collapsed.Count,
            collapsed.Sum(entry => entry.Frames?.Count ?? 1));
        return collapsed;
    }

    private static List<RecipeDisplayEntry> CollapseInterchangeableRecipes(List<RecipeDisplayEntry> recipes)
    {
        return recipes.GroupBy(UnifiedRecipePageIdentity, StringComparer.Ordinal).Select(group =>
        {
            var frames = group.GroupBy(IndependentDisplayIdentity, StringComparer.Ordinal)
                .Select(MergeEquivalentDisplays).OrderBy(MeltingDisplayRank).ToList();
            var first = frames[0];
            var sources = frames.SelectMany(frame => frame.SourceGridRecipes ?? Enumerable.Empty<GridRecipe>()).Distinct().ToList();
            return frames.Count == 1 ? frames[0] : first with { Frames = frames, SourceGridRecipes = sources,
                InputAlternatives = null, OutputAlternatives = null, SecondaryAlternatives = null };
        }).ToList();
    }

    private static int MeltingDisplayRank(RecipeDisplayEntry entry)
    {
        if ((entry.FuelProcessCategory ?? entry.Category) != "Melting" || entry.Output.Collectible is not BlockSmeltedContainer) return 0;
        var components = entry.Inputs.OfType<ItemStack>().Where(stack => !stack.Collectible.Code.Path.StartsWith("crucible-")).ToList();
        if (components.Count > 1) return 0;
        return components.Count == 1 ? 1 + CastingFeedstockRank(components[0], entry.Output) : 200;
    }

    private static bool MeltingProducesIngot(IWorldAccessor world, RecipeDisplayEntry entry, ItemStack target) =>
        entry.Category == "Melting" && IsCastIngot(target) && entry.Output.Collectible is BlockSmeltedContainer filled
        && filled.GetContents(world, entry.Output).Key is { } metal && SameCollectible(metal, target);

    private static string IndependentDisplayIdentity(RecipeDisplayEntry entry)
    {
        // Same output/process/layout needs one display, not one frame per material.
        // Original game recipes remain references for validating named constraints and autofill.
        if (entry.Category == "Crafting") return entry.Category + "|" + DisplayStackIdentity(entry.Output)
            + "|" + CraftingLayoutIdentity(entry);
        // Processing frames lack source-grid validators. Keep their full pairing
        // unless already represented by explicit ingredient-alternative lists.
        return RecipeIdentity(entry);
    }

    private static RecipeDisplayEntry MergeEquivalentDisplays(IEnumerable<RecipeDisplayEntry> entries)
    {
        var variants = entries.ToList();
        var first = variants[0];
        var sources = variants.SelectMany(entry => entry.SourceGridRecipes ?? Enumerable.Empty<GridRecipe>()).Distinct().ToList();
        if (sources.Count > 0) return first with { SourceGridRecipes = sources, InputAlternatives = null };
        if (!variants.Any(entry => entry.InputAlternatives is not null)) return first;
        int slots = Math.Max(9, variants.Max(entry => entry.Inputs.Count));
        var alternatives = Enumerable.Range(0, slots).Select(slot => variants.SelectMany(entry =>
            entry.InputAlternatives?.ElementAtOrDefault(slot) ?? (entry.Inputs.ElementAtOrDefault(slot) is { } stack
                ? new List<ItemStack> { stack } : new List<ItemStack>()))
            .GroupBy(DisplayStackIdentity, StringComparer.Ordinal).Select(group => group.First().Clone()).ToList()).ToList();
        return first with { InputAlternatives = alternatives };
    }

    private static string UnifiedRecipePageIdentity(RecipeDisplayEntry recipe)
    {
        if (IsSnowballThaw(recipe)) return recipe.Category + "|snowball-thaw";
        if (recipe.Category == "Fuel") return "Fuel|" + recipe.FuelProcessCategory + "|"
            + (recipe.FuelProcessCategory == "Cooking" ? recipe.Method : RecipeStackKey(recipe.Output));
        // Pages describe a process, not concrete grid permutations. Complete
        // frames retain quantities, layouts, temperatures and paired outputs.
        if (recipe.Category == "Crafting")
            return (GetSynchronizedVariantGroupKey(recipe) is { } craftingGroup ? "Crafting|" + craftingGroup
                : LogicalOutputGroup(recipe.Output) is { } logical ? "Crafting|" + logical : VariantRecipeIdentityCore(recipe))
                + "|trait:" + recipe.SourceGridRecipes?.FirstOrDefault()?.RequiresTrait
                + "|self:" + IsSelfTransformRecipe(recipe.SourceGridRecipes?.FirstOrDefault());
        if (recipe.Category == "Casting") return "Casting|" + RecipeStackKey(recipe.Inputs.FirstOrDefault());
        if (recipe.Category == "Melting") return "Melting|" + RecipeStackKey(recipe.Output);
        string? special = GetSynchronizedVariantGroupKey(recipe);
        if (special is not null) return recipe.Category + "|" + special + "|secondary:" + (recipe.SecondaryOutput is not null);
        string output = LogicalOutputGroup(recipe.Output) ?? CustomOutputGroup(recipe.Output.Collectible.Code.Path)
            ?? (recipe.Output.Class + ":" + recipe.Output.Collectible.Code.Domain + ":"
                + ExplicitOutputKey(recipe.Output.Collectible.Code.Path, GetVariantFamily(recipe.Output)) + "|" + OutputShapeKey(recipe.Output));
        return recipe.Category + "|" + output + "|secondary:" + (recipe.SecondaryOutput is not null);
    }

    private static bool IsSnowballThaw(RecipeDisplayEntry recipe) =>
        (recipe.Category == "Melting" || recipe.Category == "Fuel" && recipe.FuelProcessCategory == "Melting")
        && recipe.Inputs.Any(stack => stack?.Collectible.Code.Path.StartsWith("snowball-", StringComparison.Ordinal) == true);

    private static bool IsSelfTransformRecipe(GridRecipe? grid) => grid?.ResolvedIngredients?.Where(i => i is not null
        && i.Code is not null && !i.Code.Path.Contains('*') && !i.Code.Path.Contains('{') && i.Tags.IsEmpty).Any(i =>
        i.ResolvedItemStack?.Collectible is { } input && grid.Output?.ResolvedItemStack?.Collectible is { } output
        && DirectionalCatalog.Key(input) == DirectionalCatalog.Key(output)) == true;

    private static string? LogicalOutputGroup(ItemStack stack)
    {
        string path = stack.Collectible.Code.Path;
        if (stack.Collectible is BlockSmeltedContainer || path.StartsWith("ingot-") || path.StartsWith("nugget-")
            || path.StartsWith("ore-") || path.StartsWith("metalbit-") || path.StartsWith("shield-")) return null;
        if (CustomOutputGroup(path) is { } custom) return "custom:" + custom;
        var family = GetVariantFamily(stack);
        string canonicalPath = DirectionalCatalog.Key(stack.Collectible).Split(':', 2)[1];
        string normalized = ExplicitOutputKey(canonicalPath, family);
        var attributes = stack.Attributes.Clone();
        // Crates encode wood inside "type", unlike cabinets' separate wood attribute.
        // Keep lid/label states distinct; normalize only the wooden material token.
        if (path == "crate" && attributes.GetString("type") is { } crateType && crateType.StartsWith("wood-"))
            attributes.SetString("type", NormalizeVariant(crateType, VariantFamily.Wood));
        foreach (string key in GlobalConstants.IgnoredStackAttributes) attributes.RemoveAttribute(key);
        attributes.RemoveAttribute("durability");
        attributes.RemoveAttribute("temperature");
        bool attributedVariant = false;
        foreach (string key in new[] { "wood", "metal", "material", "rock", "stone", "color", "colour", "grain", "fruit" })
        {
            if (!attributes.HasAttribute(key)) continue;
            attributedVariant = true;
            attributes.RemoveAttribute(key);
        }
        if (family == VariantFamily.Wood) { attributes.RemoveAttribute("wood"); attributes.RemoveAttribute("material"); }
        if (family is VariantFamily.Metal or VariantFamily.ToolMaterial) { attributes.RemoveAttribute("metal"); attributes.RemoveAttribute("material"); }
        if (family == VariantFamily.Stone) { attributes.RemoveAttribute("stone"); attributes.RemoveAttribute("rock"); attributes.RemoveAttribute("material"); }
        if (family == VariantFamily.Color) { attributes.RemoveAttribute("color"); attributes.RemoveAttribute("colour"); }
        foreach (var variant in (IEnumerable<KeyValuePair<string, string>>?)stack.Collectible.Variant ?? Array.Empty<KeyValuePair<string, string>>())
        {
            if (variant.Key is not ("wood" or "metal" or "rock" or "stone" or "color" or "colour" or "grain" or "fruit" or "material")) continue;
            normalized = Regex.Replace(normalized, "(?<=^|-|\\{)" + Regex.Escape(variant.Value) + "(?=$|-|\\})", "{" + variant.Key + "}");
            attributes.RemoveAttribute(variant.Key);
        }
        if (normalized == path && family == VariantFamily.None && !attributedVariant) return null;
        return $"variant:{stack.Class}:{stack.Collectible.Code.Domain}:{normalized}|{FavoriteIdentity.AttributesKey(attributes)}";
    }


    private static string MaterialFrameIdentity(RecipeDisplayEntry frame)
    {
        string inputs = string.Join("|", frame.Inputs.Select((stack, slot) => stack is null ? "-"
            : frame.Category == "Crafting" || IsToolSlot(frame, slot) ? InterchangeableIngredientKey(stack)
            : $"{RecipeStackKey(stack)}:{stack.StackSize}:{FavoriteIdentity.AttributesKey(stack.Attributes)}"));
        var grid = frame.SourceGridRecipes?.FirstOrDefault();
        return $"{frame.Category}|{inputs}|{RecipeStackKey(frame.Output)}:{frame.Output.StackSize}:{FavoriteIdentity.AttributesKey(frame.Output.Attributes)}|{CraftingLayoutIdentity(frame)}";
    }
}
