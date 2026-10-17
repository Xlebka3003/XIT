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
    // Input handling and compact-card rendering belong to the same dialog instance.
    public override double DrawOrder => 0.60;
    public override string ToggleKeyCombinationCode => null!;
    public override bool PrefersUngrabbedMouse => true;
    public override bool CaptureAllInputs() => false;
    public bool HasRecipes => allRecipes.Count > 0;

    public override void OnMouseDown(MouseEvent args)
    {
        foreach (var card in recipeCards.ToArray())
        {
            card.OnMouseDown(args);
            if (args.Handled) return;
        }
        if (IsBarrelCategory && barrelLiquid is not null
            && SingleComposer.GetElement("barrel-liquid") is BarrelLiquidElement gauge
            && gauge.Bounds.PointInside(args.X, args.Y))
        {
            if (args.Button is EnumMouseButton.Left or EnumMouseButton.Right)
            {
                Load(barrelLiquid, args.Button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
                args.Handled = true;
                return;
            }
        }
        string[] keys = IsPitKilnCategory ? new[] { "recipe-category-tabs", "pit-input", "pit-materials", "recipe-output" }
            : IsCementationCategory ? new[] { "recipe-category-tabs", "cementation-iron", "cementation-carbon", "cementation-fuel", "recipe-output" }
            : IsBarrelCategory ? new[] { "recipe-category-tabs", "barrel-input", "recipe-output" }
            : IsBakingCategory || IsDryingCategory ? new[] { "recipe-category-tabs", "baking-input", "recipe-output" }
            : IsPressingCategory ? new[] { "recipe-category-tabs", "pressing-input", "recipe-secondary", "recipe-output" }
            : IsCastingCategory ? new[] { "recipe-category-tabs", "casting-crucible", "casting-mold", "recipe-output" }
            : IsCrucibleCategory ? new[] { "recipe-category-tabs", "crucible-inputs", "firepit-input", "firepit-fuel", "recipe-output" }
            : IsFirepitCategory
            ? new[] { "recipe-category-tabs", "firepit-input", "firepit-fuel", "recipe-output" }
            : IsSmithingCategory
                ? new[] { "recipe-category-tabs", "smithing-metal", "smithing-anvil", "smithing-hammer", "recipe-output" }
            : IsGrindingCategory
                ? new[] { "recipe-category-tabs", "grinding-input", "recipe-output" }
            : new[] { "recipe-category-tabs", "recipe-inputs", "recipe-output" };
        foreach (string key in compactCard ? keys.Concat(new[] { "recipe-secondary" }) : keys)
        {
            if (SingleComposer.GetElement(key) is RecipeDisplayGridElement grid && grid.TryClick(args.X, args.Y, args.Button))
            {
                args.Handled = true;
                return;
            }
        }
        if (previousHitBounds?.PointInside(args.X, args.Y) == true)
        {
            Previous();
            args.Handled = true;
            return;
        }
        if (nextHitBounds?.PointInside(args.X, args.Y) == true)
        {
            Next();
            args.Handled = true;
            return;
        }
        if (closeHitBounds?.PointInside(args.X, args.Y) == true)
        {
            Close();
            args.Handled = true;
            return;
        }
        if (autofillHitBounds?.PointInside(args.X, args.Y) == true)
        {
            AutoFillCraftingRecipe();
            args.Handled = true;
            return;
        }
        // Do not let the higher-draw-order recipe dialog consume clicks over
        // the item browser or favorites panels behind it.
        if (dialogHitBounds?.PointInside(args.X, args.Y) == true) base.OnMouseDown(args);
    }

    public override void OnMouseUp(MouseEvent args)
    {
        foreach (var card in recipeCards.ToArray()) { card.OnMouseUp(args); if (args.Handled) return; }
        if (dialogHitBounds?.PointInside(args.X, args.Y) == true) base.OnMouseUp(args);
    }

    public override void OnMouseMove(MouseEvent args)
    {
        foreach (var card in recipeCards) card.OnMouseMove(args);
        base.OnMouseMove(args);
        if (dialogHitBounds?.PointInside(args.X, args.Y) == true) return;

        // This modal dialog is rendered above the HUD and therefore receives
        // mouse movement before the still-visible browser and hotbar. Forward
        // movement outside the recipe window so those panels can refresh
        // CurrentHoveredSlot instead of leaving the originally clicked item
        // stuck as the R/U target.
        foreach (GuiDialog dialog in capi.OpenedGuis.OfType<GuiDialog>().ToArray())
        {
            if (ReferenceEquals(dialog, this) || !dialog.IsOpened()) continue;
            string name = dialog.GetType().FullName ?? dialog.GetType().Name;
            if (name == "VsRecipeBrowser.InventoryBrowserDialog"
                || name.Contains("Hotbar", StringComparison.OrdinalIgnoreCase))
                dialog.OnMouseMove(args);
        }
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (MultiPage)
        {
            base.OnRenderGUI(deltaTime);
            foreach (var card in recipeCards) card.OnRenderGUI(deltaTime);
            foreach (var card in recipeCards) card.RenderAlternativeOverlays();
            return;
        }
        bool cyclePaused = capi.Input.KeyboardKeyState[(int)GlKeys.LShift]
            || capi.Input.KeyboardKeyState[(int)GlKeys.RShift]
            || ingredientOptions?.IsOpened() == true || selectedIngredients.Count > 0;
        if (cyclePaused)
        {
            lastIngredientVariantChangeMs = capi.ElapsedMilliseconds;
            lastFuelChangeMs = capi.ElapsedMilliseconds;
        }
        if ((IsFirepitCategory || IsCrucibleCategory) && recipes.Count > 0)
        {
            long now = capi.ElapsedMilliseconds;
            if (!cyclePaused && firepitFuels.Count > 1 && now - lastFuelChangeMs >= 2000)
            {
                lastFuelChangeMs = now;
                firepitFuelIndex = (firepitFuelIndex + 1) % firepitFuels.Count;
                SetDisplayedFirepitFuel();
            }
        }
        if (currentPageHasAnimatedVariants && recipes.Count > 0 && !cyclePaused
            && capi.ElapsedMilliseconds - lastIngredientVariantChangeMs >= 2000)
        {
            RecipeDisplayEntry recipe = recipes[page];
            lastIngredientVariantChangeMs = capi.ElapsedMilliseconds;
            ingredientVariantIndex++;
            toolVariantIndex++;
            SetDisplayedIngredientVariants(recipe);
        }
        base.OnRenderGUI(deltaTime);
        if (!compactCard) RenderAlternativeOverlays();
    }

    private void RenderAlternativeOverlays()
    {
        string[] keys = { "cementation-iron", "cementation-carbon", "cementation-fuel", "recipe-inputs", "crucible-inputs", "firepit-input", "firepit-fuel", "pressing-input",
            "smithing-metal", "smithing-anvil", "smithing-hammer", "casting-crucible", "casting-mold",
            "baking-input", "grinding-input", "barrel-input", "pit-input", "pit-materials" };
        foreach (string key in keys)
            (SingleComposer?.GetElement(key) as RecipeDisplayGridElement)?.RenderAlternativesOverlay();
    }

    private bool IsResultHovered() => SingleComposer?.GetElement("recipe-output") is RecipeDisplayGridElement grid
        && grid.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY);

    private string? HoveredVariantGrid()
    {
        if (IsBarrelCategory && SingleComposer?.GetElement("barrel-liquid") is BarrelLiquidElement liquid
            && liquid.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY)) return "barrel-liquid";
        string[] keys = { "cementation-iron", "cementation-carbon", "cementation-fuel", "recipe-output", "recipe-inputs", "crucible-inputs", "firepit-input", "firepit-fuel",
            "pressing-input", "recipe-secondary", "smithing-metal", "smithing-anvil", "smithing-hammer", "casting-crucible", "casting-mold", "baking-input", "grinding-input", "barrel-input", "pit-input", "pit-materials" };
        return keys.FirstOrDefault(key => SingleComposer?.GetElement(key) is RecipeDisplayGridElement grid
            && grid.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY));
    }

    private static int VariantCount(RecipeDisplayEntry recipe) => recipe.Frames is { Count: > 0 } frames
        ? Math.Max(frames.Count, frames.Max(MaterialAlternativeCount))
        : Math.Max(recipe.OutputAlternatives?.Count ?? 1, MaterialAlternativeCount(recipe));

    private static int MaterialAlternativeCount(RecipeDisplayEntry recipe) =>
        Math.Max(1, recipe.InputAlternatives?.Select((list, slot) => IsToolSlot(recipe, slot) ? 1 : list.Count).DefaultIfEmpty(1).Max() ?? 1);

    private static int ToolAlternativeCount(RecipeDisplayEntry recipe) => recipe.Frames is { Count: > 0 } frames
        ? frames.Max(ToolAlternativeCount)
        : Math.Max(1, recipe.InputAlternatives?.Select((list, slot) => IsToolSlot(recipe, slot) ? list.Count : 1).DefaultIfEmpty(1).Max() ?? 1);

    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        foreach (var card in recipeCards.ToArray())
        {
            if (card.HoveredVariantGrid() is null) continue;
            card.OnMouseWheel(args);
            if (args.IsHandled) return;
        }
        string? hovered = HoveredVariantGrid();
        bool insideWindow = hovered is not null
            || dialogHitBounds?.PointInside(capi.Input.MouseX, capi.Input.MouseY) == true
            || IsCategoryTabHovered();
        if (!insideWindow) return;
        if (hovered is not null && ingredientGridSlots.TryGetValue(hovered, out var mapSlot)
            && SingleComposer.GetElement(hovered) is RecipeDisplayGridElement ingredientGrid
            && ingredientGrid.HoveredSlotId is int visibleSlot && visibleSlot >= 0 && mapSlot(visibleSlot) is int ingredientSlot && ingredientSlot >= 0)
        {
            var variants = CurrentIngredientOptions(ingredientSlot);
            double delta = args.deltaPrecise != 0 ? args.deltaPrecise : args.delta;
            if (variants.Count > 1 && delta != 0)
            {
                var displayed = selectedIngredients.TryGetValue(ingredientSlot, out var pinned) ? pinned : inputs[ingredientSlot].Itemstack;
                int next = IngredientCycle.Next(variants.Select(DisplayStackIdentity).ToArray(), DisplayStackIdentity(displayed), delta > 0 ? 1 : -1);
                SelectIngredientOption(ingredientSlot, variants[next]);
                args.SetHandled(true);
                return;
            }
        }
        if (hovered == "smithing-anvil" && smithingAnvils.Count > 1)
        {
            double delta = args.deltaPrecise != 0 ? args.deltaPrecise : args.delta;
            if (delta != 0)
            {
                smithingAnvilIndex = (smithingAnvilIndex + (delta > 0 ? 1 : -1) + smithingAnvils.Count) % smithingAnvils.Count;
                smithingAnvil[0].Itemstack = smithingAnvils[smithingAnvilIndex].Clone();
                smithingAnvil[0].MarkDirty();
                args.SetHandled(true);
                return;
            }
        }
        if (hovered == "firepit-fuel" && firepitFuels.Count > 1)
        {
            double delta = args.deltaPrecise != 0 ? args.deltaPrecise : args.delta;
            if (delta != 0)
            {
                firepitFuelIndex = (firepitFuelIndex + (delta > 0 ? 1 : -1) + firepitFuels.Count) % firepitFuels.Count;
                lastFuelChangeMs = capi.ElapsedMilliseconds;
                SetDisplayedFirepitFuel();
                args.SetHandled(true);
                return;
            }
        }
        double pageDelta = args.deltaPrecise != 0 ? args.deltaPrecise : args.delta;
        if (pageDelta != 0)
        {
            var owner = cardOwner ?? this;
            if (pageDelta > 0) owner.Next(); else owner.Previous();
            args.SetHandled(true); return;
        }
        base.OnMouseWheel(args);
    }

    private void Compose()
    {
        ClearRecipeCards();
        if (compactCard) { ComposeCompactCard(); return; }
        if (MultiPage) { page = page / CardsPerPage * CardsPerPage; ComposeRecipePage(); return; }
        autofillHitBounds = null;
        if (IsCementationCategory) { ComposeCementation(); return; }
        if (IsPitKilnCategory) { ComposePitKiln(); return; }
        if (IsBarrelCategory) { ComposeBarrel(); return; }
        if (IsBakingCategory || IsDryingCategory) { ComposeBaking(); return; }
        if (IsPressingCategory) { ComposePressing(); return; }
        if (IsCastingCategory) { ComposeCasting(); return; }
        if (IsCrucibleCategory) { ComposeCrucible(); return; }
        if (IsFirepitCategory)
        {
            ComposeFirepit();
            return;
        }
        if (IsGrindingCategory)
        {
            ComposeGrinding();
            return;
        }
        if (IsSmithingCategory)
        {
            ComposeSmithing();
            return;
        }

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        // Keep the whole composed surface painted. Leaving the tab strip outside
        // the dialog background caused an opaque black rectangle on some GPUs.
        ElementBounds bg = ElementBounds.Fixed(0, 12, 620, 388);
        dialogHitBounds = bg;
        ElementBounds inputGrid = ElementBounds.Fixed(70, 122, 3 * 48.8, 3 * 48.8);
        ElementBounds arrow = ElementBounds.Fixed(270, 175, 70, 30);
        ElementBounds outputGrid = ElementBounds.Fixed(395, 149, 48.8, 48.8);
        ElementBounds temperature = ElementBounds.Fixed(235, 225, 210, 28);
        ElementBounds previous = ElementBounds.Fixed(155, 323, 80, 42);
        ElementBounds counter = ElementBounds.Fixed(245, 333, 130, 24);
        ElementBounds next = ElementBounds.Fixed(385, 323, 80, 42);
        ElementBounds close = ElementBounds.Fixed(536, 323, 60, 42);
        ElementBounds autofill = ElementBounds.Fixed(466, 153, 46, 42);
        previousHitBounds = previous;
        nextHitBounds = next;
        closeHitBounds = close;
        autofillHitBounds = IsCraftingCategory ? autofill : null;

        GuiComposer composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", dialogBounds)
            .AddShadedDialogBG(bg, true);

        if (categories.Count > 0)
        {
            BuildCategoryTabs();
            ElementBounds tabBounds = ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8);
            composer.AddRecipeDisplayGrid(categoryTabs, categories.Count, tabBounds, "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        }

        composer = composer
            .AddRecipeDisplayGrid(inputs, 3, inputGrid, "recipe-inputs", OnInputClicked)
            .AddDynamicText("→", CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center), arrow, "recipe-arrow")
            .AddRecipeDisplayGrid(output, 1, outputGrid, "recipe-output", OnOutputClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), temperature, "recipe-temperature");
        if (categories[category] == "Pressing") composer.AddRecipeDisplayGrid(secondaryOutput, 1,
            ElementBounds.Fixed(448, 149, 48.8, 48.8), "recipe-secondary", (slot, button) => {
                if (secondaryOutput[slot].Itemstack is { } stack) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
            });
        if (IsCraftingCategory) composer.AddButton("+", AutoFillCraftingRecipe, autofill,
            CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center), EnumButtonStyle.Normal, "recipe-autofill");
        SingleComposer = composer
            .AddButton("<", Previous, previous)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), counter, "recipe-counter")
            .AddButton(">", Next, next)
            .AddButton("×", Close, close)
            .Compose();
        if (IsCraftingCategory && SingleComposer.GetElement("recipe-inputs") is RecipeDisplayGridElement ingredientGrid)
        {
            ingredientGrid.AlternativeCount = slot => CurrentIngredientOptions(slot).Count;
            ingredientGrid.ShowAlternatives = OpenIngredientOptions;
        }
    }

    private void ClearRecipeCards()
    {
        foreach (var card in recipeCards) card.Dispose();
        recipeCards.Clear();
    }

    private void RefreshRecipePage()
    {
        // Reuse the composed grids/textures when the next page has the same
        // shape. Only its inventories and dynamic captions need refreshing.
        if (MultiPage && recipeCards.Count == Math.Min(CardsPerPage, recipes.Count - page)
            && recipeCards.Select((card, index) => CardLayoutKey(card.recipes[0]) == CardLayoutKey(recipes[page + index])).All(same => same))
        {
            for (int i = 0; i < recipeCards.Count; i++)
            {
                var card = recipeCards[i];
                card.ingredientOptions?.TryClose();
                card.allRecipes = new() { recipes[page + i] };
                card.ApplyCategories();
                card.ingredientVariantIndex = card.toolVariantIndex = 0;
                card.FillPage();
                StoreCardRecipe(page + i, card.recipes[0]);
            }
            SingleComposer.GetDynamicText("recipe-counter").SetNewText($"{page / CardsPerPage + 1} / {RecipePaging.Count(recipes.Count, CardsPerPage)}", true);
            return;
        }
        SingleComposer?.Dispose(); Compose(); FillPage();
    }

    private static string CardLayoutKey(RecipeDisplayEntry entry) =>
        $"{entry.Category}|{entry.FuelProcessCategory}|{HasSecondaryDisplay(entry)}";

    private void ComposeRecipePage()
    {
        int rows = Math.Min(CardsPerPage, recipes.Count - page);
        var root = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        double pageWidth = ThermalLayout ? rows * 310 - 10 : 620;
        var bg = ElementBounds.Fixed(0, 12, pageWidth, ThermalLayout ? 410 : 85 + rows * 170);
        dialogHitBounds = bg;
        BuildCategoryTabs();
        int footer = ThermalLayout ? 365 : 45 + rows * 170;
        previousHitBounds = ElementBounds.Fixed(pageWidth < 400 ? 24 : pageWidth / 2 - 150, footer, 60, 36);
        nextHitBounds = ElementBounds.Fixed(pageWidth < 400 ? pageWidth - 110 : pageWidth / 2 + 70, footer, 50, 36);
        closeHitBounds = ElementBounds.Fixed(pageWidth - 50, footer, 40, 36);
        autofillHitBounds = null;
        var composer = capi.Gui.CreateCompo($"xrb-recipe-page-{++composeRevision}", root)
            .AddShadedDialogBG(bg, true)
            .AddRecipeDisplayGrid(categoryTabs, categories.Count, ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8),
                "recipe-category-tabs", (slot, _) => { detailView = false; SelectCategory(slot); }, true, category)
            .AddButton("<", Previous, previousHitBounds)
            .AddDynamicText($"{page / CardsPerPage + 1} / {RecipePaging.Count(recipes.Count, CardsPerPage)}",
                CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(pageWidth < 400 ? 86 : pageWidth / 2 - 65, footer + 8, pageWidth < 400 ? 86 : 130, 24), "recipe-counter")
            .AddButton(">", Next, nextHitBounds).AddButton("×", Close, closeHitBounds);
        if (ThermalLayout)
            for (int i = 1; i < rows; i++)
                composer.AddDynamicCustomDraw(ElementBounds.Fixed(i * 310 - 8, 48, 6, 310), (context, _, bounds) => {
                    context.SetSourceRGBA(0.90, 0.82, 0.67, 0.65);
                    context.Rectangle(0, 0, bounds.InnerWidth, bounds.InnerHeight); context.Fill();
                }, "thermal-divider-" + i);
        SingleComposer = composer.Compose();
    }

    private void BuildRecipeCards()
    {
        ClearRecipeCards();
        page = page / CardsPerPage * CardsPerPage;
        int count = Math.Min(CardsPerPage, recipes.Count - page);
        for (int i = 0; i < count; i++)
        {
            recipeCards.Add(new RecipeOverlayDialog(capi, target, mode, new() { recipes[page + i] }, this, i));
            StoreCardRecipe(page + i, recipeCards[i].recipes[0]);
        }
    }

    private void StoreCardRecipe(int index, RecipeDisplayEntry enriched)
    {
        var original = recipes[index];
        int allIndex = allRecipes.FindIndex(entry => ReferenceEquals(entry, original));
        recipes[index] = enriched;
        if (allIndex >= 0) allRecipes[allIndex] = enriched;
    }

    private void ComposeCompactCard()
    {
        if (ThermalLayout) { ComposeThermalCard(); return; }
        if (StationCategory == "Smithing") { ComposeCompactSmithing(); return; }
        int count = Math.Min(cardOwner!.CardsPerPage, cardOwner.recipes.Count - cardOwner.page);
        double y = -20 + (cardIndex - (count - 1) / 2.0) * 170;
        var root = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, y);
        dialogHitBounds = ElementBounds.Fixed(0, 0, 620, 165);
        autofillHitBounds = IsCraftingCategory ? ElementBounds.Fixed(490, 60, 40, 36) : null;
        var composer = capi.Gui.CreateCompo($"xrb-recipe-card-{Guid.NewGuid():N}", root)
            // The parent paints one continuous background. Each row is transparent;
            // independently generated dialog textures create visible seams.
            .AddDynamicCustomDraw(dialogHitBounds, (_, _, _) => { }, "card-layout")
            .AddRecipeDisplayGrid(inputs, 3, ElementBounds.Fixed(55, 8, 146.4, 146.4), "recipe-inputs", OnInputClicked)
            .AddDynamicText("=", CairoFont.WhiteMediumText(), ElementBounds.Fixed(260, 64, 40, 30), "recipe-arrow")
            .AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(395, 60, 48.8, 48.8), "recipe-output", OnOutputClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(230, 116, 200, 28), "recipe-temperature");
        if (HasSecondaryDisplay(recipes[page]))
            composer.AddRecipeDisplayGrid(secondaryOutput, 1, ElementBounds.Fixed(445, 110, 48.8, 48.8), "recipe-secondary", (slot, button) => {
                if (secondaryOutput[slot].Itemstack is { } stack) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
            });
        if (autofillHitBounds is not null) composer.AddButton("+", AutoFillCraftingRecipe, autofillHitBounds,
            CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center));
        SingleComposer = composer.Compose();
    }

    private static bool HasSecondaryDisplay(RecipeDisplayEntry recipe) => recipe.SecondaryOutput is not null
        || recipe.SecondaryAlternatives?.Count > 0 || recipe.Frames?.Any(HasSecondaryDisplay) == true;
}
