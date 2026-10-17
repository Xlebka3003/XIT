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
    // Favorites retain the exact recipe context, not just the displayed item.
    internal static RecipeDisplayEntry? FavoritePreview(ICoreClientAPI api, ItemStack target, FavoriteRecipeState? saved = null)
    {
        if (saved is not null && FavoriteRecipeState.Decode(saved.TargetData, api.World) is { } savedTarget
            && Enum.TryParse<RecipeViewMode>(saved.Mode, out var savedMode))
        {
            var entry = CollectRecipes(api, savedTarget, savedMode)
                .FirstOrDefault(recipe => recipe.Category == saved.Category && UnifiedRecipePageIdentity(recipe) == saved.RecipeKey);
            if (entry is not null)
            {
                var frame = entry.Frames?.FirstOrDefault(recipe => RecipeIdentity(recipe) + CraftingLayoutIdentity(recipe) == saved.FrameKey)
                    ?? entry.Frames?.FirstOrDefault() ?? entry;
                return frame with { Inputs = frame.Inputs.Select((stack, slot) => saved.Ingredients.TryGetValue(slot, out var data)
                    ? FavoriteRecipeState.Decode(data, api.World) ?? stack : stack).ToList() };
            }
        }
        var preview = CollectRecipes(api, target, RecipeViewMode.Recipes).FirstOrDefault();
        if (preview is null)
            api.Logger.Notification("[XRB Preview] No recipe: {0}, attributes={1}, candidates={2}",
                target.Collectible.Code, target.Attributes.ToJsonToken(), IndexedCandidates(target, RecipeViewMode.Recipes).Count);
        return preview;
    }

    internal FavoriteRecipeState? CaptureFavoriteRecipe(ItemStack display)
    {
        foreach (var card in recipeCards)
            if (card.GetHoveredRecipeStack() is not null) return card.CaptureFavoriteRecipe(display);
        if (recipes.Count == 0 || GetHoveredRecipeStack() is null) return null;
        var state = new FavoriteRecipeState
        {
            TargetData = FavoriteRecipeState.Encode(target), DisplayData = FavoriteRecipeState.Encode(display), Mode = mode.ToString(),
            Category = categories[category], RecipeKey = UnifiedRecipePageIdentity(recipes[page]),
            FrameKey = RecipeIdentity(CurrentDisplayFrame()) + CraftingLayoutIdentity(CurrentDisplayFrame())
        };
        for (int slot = 0; slot < inputs.Count; slot++)
            if (inputs[slot].Itemstack is { } stack) state.Ingredients[slot] = FavoriteRecipeState.Encode(stack);
        if ((IsFirepitCategory || IsCrucibleCategory) && firepitFuel[0].Itemstack is { } fuel)
            state.Fuel = FavoriteRecipeState.Encode(fuel);
        if (IsSmithingCategory && smithingAnvil[0].Itemstack is { } anvil)
            state.Anvil = FavoriteRecipeState.Encode(anvil);
        return state;
    }

    internal bool RestoreFavoriteRecipe(FavoriteRecipeState state)
    {
        var savedTarget = FavoriteRecipeState.Decode(state.TargetData, capi.World);
        if (savedTarget is null || !Enum.TryParse<RecipeViewMode>(state.Mode, out var savedMode)) return false;
        if (!Load(savedTarget, savedMode, false)) return false;
        detailView = true;
        int savedCategory = categories.IndexOf(state.Category);
        if (savedCategory < 0) return false;
        category = savedCategory; ApplyCategories();
        int savedPage = recipes.FindIndex(entry => UnifiedRecipePageIdentity(entry) == state.RecipeKey);
        if (savedPage < 0) { category = 0; ApplyCategories(); SingleComposer?.Dispose(); Compose(); FillPage(); return false; }
        page = savedPage; SingleComposer?.Dispose(); Compose(); FillPage();
        if (recipes[page].Frames is { Count: > 0 } frames)
        {
            int savedFrame = frames.FindIndex(frame => RecipeIdentity(frame) + CraftingLayoutIdentity(frame) == state.FrameKey);
            if (savedFrame >= 0) ingredientVariantIndex = savedFrame;
        }
        SetDisplayedIngredientVariants(recipes[page]);
        foreach (var choice in state.Ingredients)
            if (choice.Key >= 0 && choice.Key < inputs.Count && FavoriteRecipeState.Decode(choice.Value, capi.World) is { } stack)
                SelectIngredientOption(choice.Key, stack);
        if (FavoriteRecipeState.Decode(state.Fuel, capi.World) is { } fuel)
        {
            int index = firepitFuels.FindIndex(stack => SameCollectible(stack, fuel));
            if (index >= 0) { firepitFuelIndex = index; SetDisplayedFirepitFuel(); }
        }
        if (FavoriteRecipeState.Decode(state.Anvil, capi.World) is { } anvil)
        {
            int index = smithingAnvils.FindIndex(stack => SameCollectible(stack, anvil));
            if (index >= 0) { smithingAnvilIndex = index; smithingAnvil[0].Itemstack = anvil; smithingAnvil[0].MarkDirty(); }
        }
        return true;
    }

    // History restores the page and selected station as well as the target item.
    private bool Previous()
    {
        detailView = false;
        if (recipes.Count > 0) { page = RecipePaging.Start(recipes.Count, page, CardsPerPage, -1); RefreshRecipePage(); }
        return true;
    }

    private bool Next()
    {
        detailView = false;
        if (recipes.Count > 0) { page = RecipePaging.Start(recipes.Count, page, CardsPerPage, 1); RefreshRecipePage(); }
        return true;
    }

    private bool SelectCategory(int selectedCategory)
    {
        if (detailView && selectedCategory == category) { detailView = false; RefreshRecipePage(); return true; }
        detailView = false;
        if (selectedCategory < 0 || selectedCategory >= categories.Count || selectedCategory == category) return true;
        category = selectedCategory;
        recipes = allRecipes.Where(recipe => recipe.Category == categories[category]).ToList();
        page = 0;
        SingleComposer?.Dispose();
        Compose();
        FillPage();
        return true;
    }

    private void BuildCategoryTabs()
    {
        categoryTabs = new BrowserInventory(categories.Count, capi) { SlotClicked = (slot, _) => SelectCategory(slot) };
        for (int i = 0; i < categories.Count; i++)
        {
            categoryTabs[i].Itemstack = CategoryIcon(categories[i]);
            categoryTabs[i].MarkDirty();
        }
    }

    private ItemStack? CategoryIcon(string recipeCategory)
    {
        if (recipeCategory == "Fuel")
        {
            var icon = target.Clone(); icon.Attributes.SetBool("xrb-flame-tab", true); return icon;
        }
        string[] fragments = recipeCategory switch
        {
            "Crafting" => new[] { "backpack-normal", "backpack-", "hunterbackpack", "handbasket-", "handbasket" },
            "Smithing" => new[] { "anvil-copper", "anvil-" },
            "Cementation" => new[] { "stonecoffinsection-andesite-north", "stonecoffinsection-andesite-" },
            "Barrel" => new[] { "barrel", "barrel-oak", "barrel-" },
            "Pit firing" => new[] { "pitkiln" },
            "Clay forming" => new[] { "clay-blue", "clay-" },
            "Knapping" => new[] { "stone-flint", "flint", "stone-granite" },
            "Cooking" => new[] { "cookingpot", "claypot-" },
            "Alloying" => new[] { "crucible-blue-fired", "crucible-" },
            "Casting" => new[] { "ingotmold-blue-fired", "ingotmold-" },
            "Baking" => new[] { "clayoven-north", "clayoven-" },
            "Bloomery" => new[] { "bloomery", "bloom-" },
            "Heating" => new[] { "firepit-cold", "forge" },
            "Grinding" => new[] { "quern-", "quern" },
            "Crushing" => new[] { "pulverizer", "hammer-" },
            "Pressing" => new[] { "fruitpress-ns" },
            "Drying" => new[] { "hide-oiled-", "dryingrack" },
            "Curing" => new[] { "barrel-oak", "barrel-" },
            "Ripening" => new[] { "cheeserack", "shelf-", "basket-" },
            "Hardening" => new[] { "forge", "firepit-cold" },
            "Burning" => new[] { "firepit-cold", "firewood" },
            "Melting" => new[] { "crucible-blue-fired", "crucible-" },
            "Conversion" => new[] { "barrel-oak", "bowl-", "firepit-cold" },
            "Processing" => new[] { "barrel-oak", "barrel-", "bowl-" },
            _ => Array.Empty<string>()
        };

        foreach (string fragment in fragments)
        {
            CollectibleObject? collectible = capi.World.Collectibles.FirstOrDefault(value =>
                value?.Code?.Path.Equals(fragment, StringComparison.OrdinalIgnoreCase) == true)
                ?? capi.World.Collectibles.FirstOrDefault(value =>
                    value?.Code?.Path.StartsWith(fragment, StringComparison.OrdinalIgnoreCase) == true);
            if (collectible is not null) return new ItemStack(collectible);
        }

        return target.Clone();
    }

    private void ApplyCategories()
    {
        categories = allRecipes.Select(recipe => recipe.Category).Distinct(StringComparer.Ordinal).ToList();
        category = Math.Clamp(category, 0, Math.Max(0, categories.Count - 1));
        recipes = categories.Count == 0 ? new() : allRecipes.Where(recipe => recipe.Category == categories[category]).ToList();
        page = 0;
    }

    private bool Close()
    {
        TryClose();
        return true;
    }

    internal bool Back()
    {
        if (history.Count == 0) return true;
        ViewState previous = history.Peek();
        if (Load(previous.Stack, previous.Mode, false, previous)) history.Pop();
        return true;
    }

    internal bool Navigate(ItemStack stack, RecipeViewMode newMode) => Load(stack, newMode, true);

    private void OnInputClicked(int slot, EnumMouseButton button)
    {
        ItemStack? stack = inputs[slot].Itemstack;
        if (stack is not null) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
    }

    private void OnOutputClicked(int slot, EnumMouseButton button)
    {
        ItemStack? stack = output[slot].Itemstack;
        if (stack is not null) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
    }

    private void OnFirepitInputClicked(int slot, EnumMouseButton button)
    {
        ItemStack? stack = firepitInput[slot].Itemstack;
        if (stack is not null) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
    }

    private void OnFirepitFuelClicked(int slot, EnumMouseButton button)
    {
        ItemStack? stack = firepitFuel[slot].Itemstack;
        if (stack is not null) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
    }

    private void OnSmithingMetalClicked(int slot, EnumMouseButton button)
    {
        ItemStack? stack = smithingMetal[slot].Itemstack;
        if (stack is not null) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
    }

    private void OnSmithingHammerClicked(int slot, EnumMouseButton button)
    {
        ItemStack? stack = smithingHammer[slot].Itemstack;
        if (stack is not null) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
    }

    public bool NavigateHovered(RecipeViewMode newMode)
    {
        ItemStack? stack = IsCementationCategory ? GetHoveredSingleRecipeStack() : IsFirepitCategory
            ? HoveredStack("firepit-input", firepitInput) ?? HoveredStack("firepit-fuel", firepitFuel) ?? HoveredStack("recipe-output", output)
            : IsSmithingCategory
                ? HoveredStack("smithing-metal", smithingMetal) ?? HoveredStack("smithing-hammer", smithingHammer) ?? HoveredStack("recipe-output", output)
            : IsGrindingCategory
                ? HoveredStack("grinding-input", firepitInput) ?? HoveredStack("recipe-output", output)
            : HoveredStack("recipe-secondary", secondaryOutput) ?? HoveredStack("recipe-inputs", inputs)
            ?? HoveredStack("recipe-output", output);
        if (stack is null) return false;
        Load(stack, newMode, true);
        return true;
    }

    public ItemStack? GetHoveredRecipeStack() => recipeCards.Select(card => card.GetHoveredRecipeStack()).FirstOrDefault(stack => stack is not null)
        ?? (compactCard ? HoveredStack("recipe-inputs", inputs) ?? HoveredStack("recipe-output", output) ?? HoveredStack("recipe-secondary", secondaryOutput) ?? GetHoveredSingleRecipeStack()
        : GetHoveredSingleRecipeStack());

    private ItemStack? GetHoveredSingleRecipeStack() => IsCementationCategory
        ? HoveredStack("cementation-iron", smithingMetal) ?? HoveredStack("cementation-carbon", smithingHammer) ?? HoveredStack("cementation-fuel", firepitFuel) ?? HoveredStack("recipe-output", output)
        : IsPitKilnCategory
        ? HoveredStack("pit-input", firepitInput) ?? HoveredStack("pit-materials", pitMaterials) ?? HoveredStack("recipe-output", output)
        : IsBarrelCategory
        ? HoveredStack("barrel-input", firepitInput) ?? HoveredStack("recipe-output", output)
        : IsBakingCategory || IsDryingCategory
        ? HoveredStack("baking-input", firepitInput) ?? HoveredStack("recipe-output", output)
        : IsPressingCategory
        ? HoveredStack("pressing-input", firepitInput) ?? HoveredStack("recipe-secondary", secondaryOutput) ?? HoveredStack("recipe-output", output)
        : IsCastingCategory
        ? HoveredStack("casting-crucible", smithingMetal) ?? HoveredStack("casting-mold", smithingHammer) ?? HoveredStack("recipe-output", output)
        : IsCrucibleCategory
        ? HoveredStack("crucible-inputs", crucibleInputs) ?? HoveredStack("firepit-input", firepitInput) ?? HoveredStack("firepit-fuel", firepitFuel) ?? HoveredStack("recipe-output", output)
        : IsFirepitCategory
        ? HoveredStack("firepit-input", firepitInput) ?? HoveredStack("firepit-fuel", firepitFuel) ?? HoveredStack("recipe-output", output)
        : IsSmithingCategory
            ? HoveredStack("smithing-metal", smithingMetal) ?? HoveredStack("smithing-anvil", smithingAnvil) ?? HoveredStack("smithing-hammer", smithingHammer) ?? HoveredStack("recipe-output", output)
        : IsGrindingCategory
            ? HoveredStack("grinding-input", firepitInput) ?? HoveredStack("recipe-output", output)
        : HoveredStack("recipe-inputs", inputs) ?? HoveredStack("recipe-output", output);

    public bool IsCategoryTabHovered() => categories.Count > 0
        && (SingleComposer.GetElement("recipe-category-tabs") as RecipeDisplayGridElement)?.HoveredSlotId >= 0;

    private ItemStack? HoveredStack(string key, BrowserInventory inventory)
    {
        int slot = (SingleComposer.GetElement(key) as RecipeDisplayGridElement)?.HoveredSlotId ?? -1;
        return slot >= 0 && slot < inventory.Count ? inventory[slot].Itemstack : null;
    }

    private bool Load(ItemStack stack, RecipeViewMode newMode, bool remember, ViewState? restore = null)
    {
        if (cardOwner is not null) return cardOwner.Load(stack, newMode, remember, restore);
        detailView = false;
        string cacheKey = $"{newMode}:{RecipeStackKey(stack)}:{stack.Attributes.ToJsonToken()}";
        if (!navigationCache.TryGetValue(cacheKey, out List<RecipeDisplayEntry>? nextRecipes))
        {
            nextRecipes = CollectRecipes(capi, stack, newMode);
            navigationCache[cacheKey] = nextRecipes;
        }
        // The current composer is deliberately left completely untouched.
        if (nextRecipes.Count == 0) return false;
        if (remember) history.Push(new ViewState(target.Clone(), mode,
            categories.ElementAtOrDefault(category) ?? "", page, ingredientVariantIndex, firepitFuelIndex, toolVariantIndex));
        target = stack.Clone();
        mode = newMode;
        allRecipes = nextRecipes;
        category = 0;
        ApplyCategories();
        if (restore is not null)
        {
            int savedCategory = categories.IndexOf(restore.Category);
            if (savedCategory >= 0) category = savedCategory;
            ApplyCategories();
            page = Math.Clamp(restore.Page, 0, Math.Max(0, recipes.Count - 1));
        }
        SingleComposer?.Dispose();
        Compose();
        FillPage();
        if (restore is not null && recipes.Count > 0)
        {
            int count = Math.Max(1, VariantCount(recipes[page]));
            ingredientVariantIndex = Math.Max(0, restore.Variant) % count;
            toolVariantIndex = Math.Max(0, restore.Tool);
            SetDisplayedIngredientVariants(recipes[page]);
            if ((IsFirepitCategory || IsCrucibleCategory) && firepitFuels.Count > 0)
            {
                firepitFuelIndex = Math.Clamp(restore.Fuel, 0, firepitFuels.Count - 1);
                SetDisplayedFirepitFuel();
            }
            lastIngredientVariantChangeMs = capi.ElapsedMilliseconds;
            lastFuelChangeMs = capi.ElapsedMilliseconds;
        }
        return true;
    }
}
