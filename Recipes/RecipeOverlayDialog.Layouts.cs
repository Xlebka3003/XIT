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
    // Station layouts only describe display cells; they do not move inventory items.
    private bool IsCastingCategory => !compactCard && categories.Count > 0 && categories[category] == "Casting";
    private bool IsPressingCategory => !compactCard && categories.Count > 0 && categories[category] == "Pressing";
    private bool IsBarrelCategory => !compactCard && categories.Count > 0 && categories[category] == "Barrel";
    private bool IsPitKilnCategory => !compactCard && categories.Count > 0 && categories[category] == "Pit firing";

    private void ComposePitKiln()
    {
        var bounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        var bg = ElementBounds.Fixed(0, 12, 330, 335);
        dialogHitBounds = bg;
        previousHitBounds = ElementBounds.Fixed(24, 278, 62, 40);
        nextHitBounds = ElementBounds.Fixed(210, 278, 62, 40);
        closeHitBounds = ElementBounds.Fixed(280, 278, 40, 40);
        var composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", bounds).AddShadedDialogBG(bg, true);
        BuildCategoryTabs();
        composer.AddRecipeDisplayGrid(categoryTabs, categories.Count, ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8),
            "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        SingleComposer = composer
            .AddRecipeDisplayGrid(firepitInput, 1, ElementBounds.Fixed(35, 90, 48.8, 48.8), "pit-input", OnFirepitInputClicked)
            .AddDynamicText("→", CairoFont.WhiteMediumText(), ElementBounds.Fixed(145, 99, 45, 30), "recipe-arrow")
            .AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(245, 90, 48.8, 48.8), "recipe-output", OnOutputClicked)
            .AddDynamicText(BrowserText.Choose("Солома · палки · топливо", "Dry grass · sticks · fuel"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(25, 155, 280, 28), "pit-label")
            .AddRecipeDisplayGrid(pitMaterials, 3, ElementBounds.Fixed(87, 187, 146.4, 48.8), "pit-materials", (slot, button) => {
                if (pitMaterials[slot].Itemstack is { } stack) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
            })
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(20, 242, 290, 28), "recipe-temperature")
            .AddButton("<", Previous, previousHitBounds)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(92, 288, 108, 24), "recipe-counter")
            .AddButton(">", Next, nextHitBounds).AddButton("×", Close, closeHitBounds).Compose();
    }

    private void ComposeBarrel()
    {
        var bounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        var bg = ElementBounds.Fixed(0, 12, 330, 330);
        dialogHitBounds = bg;
        previousHitBounds = ElementBounds.Fixed(24, 273, 62, 40);
        nextHitBounds = ElementBounds.Fixed(210, 273, 62, 40);
        closeHitBounds = ElementBounds.Fixed(280, 273, 40, 40);
        var composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", bounds).AddShadedDialogBG(bg, true);
        BuildCategoryTabs();
        composer.AddRecipeDisplayGrid(categoryTabs, categories.Count, ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8),
            "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        var liquidBounds = ElementBounds.Fixed(145, 82, 34, 130);
        composer.AddInteractiveElement(new BarrelLiquidElement(capi, liquidBounds), "barrel-liquid");
        SingleComposer = composer
            .AddRecipeDisplayGrid(firepitInput, 1, ElementBounds.Fixed(35, 122, 48.8, 48.8), "barrel-input", OnFirepitInputClicked)
            .AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(247, 122, 48.8, 48.8), "recipe-output", OnOutputClicked)
            .AddHoverText("", CairoFont.WhiteSmallText(), 260, liquidBounds, "barrel-liquid-tooltip")
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(20, 230, 290, 28), "recipe-temperature")
            .AddButton("<", Previous, previousHitBounds)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(92, 283, 108, 24), "recipe-counter")
            .AddButton(">", Next, nextHitBounds).AddButton("×", Close, closeHitBounds).Compose();
    }
    private bool IsBakingCategory => !compactCard && categories.Count > 0 && categories[category] == "Baking";
    private bool IsDryingCategory => !compactCard && categories.Count > 0 && categories[category] == "Drying";

    private void ComposeBaking()
    {
        bool drying = IsDryingCategory;
        var bounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        var bg = ElementBounds.Fixed(0, 12, 300, drying ? 285 : 245);
        dialogHitBounds = bg;
        previousHitBounds = ElementBounds.Fixed(24, drying ? 226 : 186, 62, 40);
        nextHitBounds = ElementBounds.Fixed(184, drying ? 226 : 186, 62, 40);
        closeHitBounds = ElementBounds.Fixed(250, drying ? 226 : 186, 40, 40);
        var composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", bounds).AddShadedDialogBG(bg, true);
        BuildCategoryTabs();
        composer.AddRecipeDisplayGrid(categoryTabs, categories.Count, ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8),
            "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        if (drying) composer.AddDynamicText(BrowserText.Choose("Разместить в мире для сушки", "Place in the world to dry"),
            CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(10, 146, 280, 28), "drying-note");
        SingleComposer = composer
            .AddRecipeDisplayGrid(firepitInput, 1, ElementBounds.Fixed(30, 82, 48.8, 48.8), "baking-input", OnFirepitInputClicked)
            .AddDynamicText(drying ? "→" : "=", CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(110, 91, 60, 30), "recipe-arrow")
            .AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(204, 82, 48.8, 48.8), "recipe-output", OnOutputClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(80, drying ? 182 : 146, 140, 26), "recipe-temperature")
            .AddButton("<", Previous, previousHitBounds)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(90, drying ? 236 : 196, 86, 24), "recipe-counter")
            .AddButton(">", Next, nextHitBounds).AddButton("×", Close, closeHitBounds).Compose();
    }

    private void ComposePressing()
    {
        var bounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        var bg = ElementBounds.Fixed(0, 12, 300, 310);
        dialogHitBounds = bg;
        previousHitBounds = ElementBounds.Fixed(24, 254, 62, 40);
        nextHitBounds = ElementBounds.Fixed(184, 254, 62, 40);
        closeHitBounds = ElementBounds.Fixed(250, 254, 40, 40);
        var composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", bounds).AddShadedDialogBG(bg, true);
        BuildCategoryTabs();
        composer.AddRecipeDisplayGrid(categoryTabs, categories.Count, ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8),
            "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        composer.AddRecipeDisplayGrid(firepitInput, 1, ElementBounds.Fixed(35, 125, 48.8, 48.8), "pressing-input", OnFirepitInputClicked)
            .AddDynamicText("=", CairoFont.WhiteMediumText(), ElementBounds.Fixed(118, 132, 45, 32), "recipe-arrow");
        bool hasPulp = recipes[page].SecondaryOutput is not null;
        if (hasPulp) composer.AddRecipeDisplayGrid(secondaryOutput, 1, ElementBounds.Fixed(205, 87, 48.8, 48.8),
            "recipe-secondary", (slot, button) => {
                if (secondaryOutput[slot].Itemstack is { } stack) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
            });
        SingleComposer = composer.AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(205, hasPulp ? 159 : 125, 48.8, 48.8), "recipe-output", OnOutputClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(25, 217, 250, 26), "recipe-temperature")
            .AddButton("<", Previous, previousHitBounds)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(90, 264, 86, 24), "recipe-counter")
            .AddButton(">", Next, nextHitBounds).AddButton("×", Close, closeHitBounds).Compose();
    }
    private bool IsCrucibleCategory => (!compactCard || ThermalLayout) && StationCategory is "Melting" or "Cooking";

    private void ComposeCasting()
    {
        var bounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        var bg = ElementBounds.Fixed(0, 12, 440, 325);
        dialogHitBounds = bg;
        previousHitBounds = ElementBounds.Fixed(35, 268, 62, 40);
        nextHitBounds = ElementBounds.Fixed(265, 268, 62, 40);
        closeHitBounds = ElementBounds.Fixed(375, 268, 40, 40);
        var composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", bounds).AddShadedDialogBG(bg, true);
        BuildCategoryTabs();
        composer.AddRecipeDisplayGrid(categoryTabs, categories.Count, ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8),
            "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        composer.AddRecipeDisplayGrid(smithingMetal, 1, ElementBounds.Fixed(45, 80, 48.8, 48.8), "casting-crucible", OnSmithingMetalClicked)
            .AddRecipeDisplayGrid(smithingHammer, 1, ElementBounds.Fixed(125, 170, 48.8, 48.8), "casting-mold", OnSmithingHammerClicked)
            .AddInteractiveElement(new CastingPourElement(capi, ElementBounds.Fixed(108, 94, 70, 100)), "casting-pour");
        SingleComposer = composer
            .AddDynamicText("=", CairoFont.WhiteMediumText(), ElementBounds.Fixed(244, 176, 60, 32), "recipe-arrow")
            .AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(335, 170, 48.8, 48.8), "recipe-output", OnOutputClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(100, 225, 160, 30), "recipe-temperature")
            .AddButton("<", Previous, previousHitBounds)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(115, 278, 130, 24), "recipe-counter")
            .AddButton(">", Next, nextHitBounds)
            .AddButton("×", Close, closeHitBounds).Compose();
        ((RecipeDisplayGridElement)SingleComposer.GetElement("casting-crucible")).AnimatePouring = true;
    }
    private bool IsFirepitCategory => StationCategory == "Heating";
    private bool IsGrindingCategory => !compactCard && categories.Count > 0 && categories[category] == "Grinding";
    private bool IsSmithingCategory => categories.Count > 0 && categories[category] == "Smithing";
    private bool IsCraftingCategory => categories.Count > 0 && categories[category] == "Crafting";

    private bool IsCementationCategory => StationCategory == "Cementation";

    private void ComposeCementation()
    {
        var root = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        dialogHitBounds = ElementBounds.Fixed(0, 12, 420, 305);
        var composer = capi.Gui.CreateCompo($"xrb-cementation-{++composeRevision}", root).AddShadedDialogBG(dialogHitBounds, true);
        BuildCategoryTabs();
        composer.AddRecipeDisplayGrid(categoryTabs, categories.Count, ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8), "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        SingleComposer = composer
            .AddRecipeDisplayGrid(smithingMetal, 1, ElementBounds.Fixed(42, 75, 48.8, 48.8), "cementation-iron", (slot, button) => OnInputClicked(0, button))
            .AddRecipeDisplayGrid(smithingHammer, 1, ElementBounds.Fixed(106, 75, 48.8, 48.8), "cementation-carbon", (slot, button) => OnInputClicked(1, button))
            .AddRecipeDisplayGrid(firepitFuel, 1, ElementBounds.Fixed(74, 157, 48.8, 48.8), "cementation-fuel", (slot, button) => OnInputClicked(2, button))
            .AddDynamicText("=", CairoFont.WhiteMediumText(), ElementBounds.Fixed(200, 117, 40, 30), "recipe-arrow")
            .AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(285, 110, 48.8, 48.8), "recipe-output", OnOutputClicked)
            .AddDynamicText(BrowserText.Choose("Внутри саркофага", "Inside the coffin"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(40, 49, 230, 24), "cementation-charge-label")
            .AddDynamicText(BrowserText.Choose("Топливо: 80 + 80", "Fuel: 80 + 80"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(140, 169, 250, 24), "cementation-fuel-label")
            .AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(28, 215, 350, 28), "recipe-temperature")
            .AddDynamicText(BrowserText.Choose("Нужна собранная цементационная печь", "Requires a complete cementation furnace"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(28, 244, 350, 28), "cementation-structure-label")
            .AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 0, 1, 1), "recipe-counter")
            .AddButton("×", Close, closeHitBounds = ElementBounds.Fixed(354, 273, 40, 30)).Compose();
    }

    private void UpdateCementationSlots()
    {
        smithingMetal[0].Itemstack = inputs[0].Itemstack?.Clone(); smithingMetal[0].MarkDirty();
        smithingHammer[0].Itemstack = inputs[1].Itemstack?.Clone(); smithingHammer[0].MarkDirty();
        firepitFuel[0].Itemstack = inputs[2].Itemstack?.Clone(); firepitFuel[0].MarkDirty();
        SingleComposer.GetDynamicText("recipe-temperature").SetNewText(CurrentDisplayFrame().Method, true);
    }

    private void ComposeSmithing()
    {
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        ElementBounds bg = ElementBounds.Fixed(0, 12, 380, 245);
        dialogHitBounds = bg;
        ElementBounds metalSlot = ElementBounds.Fixed(65, 70, 48.8, 48.8);
        ElementBounds plus = ElementBounds.Fixed(128, 99, 28, 30);
        ElementBounds hammerSlot = ElementBounds.Fixed(172, 96, 48.8, 48.8);
        ElementBounds arrow = ElementBounds.Fixed(237, 105, 35, 30);
        ElementBounds outputSlot = ElementBounds.Fixed(291, 96, 48.8, 48.8);
        ElementBounds previous = ElementBounds.Fixed(42, 186, 62, 40);
        ElementBounds counter = ElementBounds.Fixed(112, 196, 86, 24);
        ElementBounds next = ElementBounds.Fixed(206, 186, 62, 40);
        ElementBounds close = ElementBounds.Fixed(314, 186, 40, 40);
        previousHitBounds = previous;
        nextHitBounds = next;
        closeHitBounds = close;

        GuiComposer composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", dialogBounds)
            .AddShadedDialogBG(bg, true);
        if (categories.Count > 0)
        {
            BuildCategoryTabs();
            composer.AddRecipeDisplayGrid(categoryTabs, categories.Count,
                ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8),
                "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        }
        SingleComposer = composer
            .AddRecipeDisplayGrid(smithingMetal, 1, metalSlot, "smithing-metal", OnSmithingMetalClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right),
                ElementBounds.Fixed(15, 82, 40, 26), "smithing-quantity")
            .AddRecipeDisplayGrid(smithingAnvil, 1, ElementBounds.Fixed(65, 126, 48.8, 48.8), "smithing-anvil", (slot, button) =>
            {
                if (smithingAnvil[slot].Itemstack is { } stack)
                    Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
            })
            .AddDynamicText("+", CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center), plus, "smithing-plus")
            .AddRecipeDisplayGrid(smithingHammer, 1, hammerSlot, "smithing-hammer", OnSmithingHammerClicked)
            .AddDynamicText("=", CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center), arrow, "recipe-arrow")
            .AddRecipeDisplayGrid(output, 1, outputSlot, "recipe-output", OnOutputClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 0, 1, 1), "recipe-temperature")
            .AddButton("<", Previous, previous)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), counter, "recipe-counter")
            .AddButton(">", Next, next)
            .AddButton("×", Close, close)
            .Compose();
        ((RecipeDisplayGridElement)SingleComposer.GetElement("smithing-hammer")).AnimateHammer = true;
    }

    private void ComposeCompactSmithing()
    {
        int count = Math.Min(cardOwner!.CardsPerPage, cardOwner.recipes.Count - cardOwner.page);
        var root = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedOffset(0, -20 + (cardIndex - (count - 1) / 2.0) * 170);
        dialogHitBounds = ElementBounds.Fixed(0, 0, 620, 165);
        autofillHitBounds = null;
        SingleComposer = capi.Gui.CreateCompo($"xrb-smithing-card-{Guid.NewGuid():N}", root)
            .AddDynamicCustomDraw(dialogHitBounds, (_, _, _) => { }, "card-layout")
            .AddRecipeDisplayGrid(smithingMetal, 1, ElementBounds.Fixed(140, 15, 48.8, 48.8), "smithing-metal", OnSmithingMetalClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right), ElementBounds.Fixed(85, 27, 45, 26), "smithing-quantity")
            .AddRecipeDisplayGrid(smithingAnvil, 1, ElementBounds.Fixed(140, 85, 48.8, 48.8), "smithing-anvil", (slot, button) => {
                if (smithingAnvil[slot].Itemstack is { } stack) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
            })
            .AddDynamicText("+", CairoFont.WhiteMediumText(), ElementBounds.Fixed(235, 58, 30, 30), "smithing-plus")
            .AddRecipeDisplayGrid(smithingHammer, 1, ElementBounds.Fixed(285, 55, 48.8, 48.8), "smithing-hammer", OnSmithingHammerClicked)
            .AddDynamicText("=", CairoFont.WhiteMediumText(), ElementBounds.Fixed(365, 65, 35, 30), "recipe-arrow")
            .AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(430, 55, 48.8, 48.8), "recipe-output", OnOutputClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 0, 1, 1), "recipe-temperature")
            .Compose();
        ((RecipeDisplayGridElement)SingleComposer.GetElement("smithing-hammer")).AnimateHammer = true;
    }

    private void ComposeGrinding()
    {
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        ElementBounds bg = ElementBounds.Fixed(0, 12, 300, 245);
        dialogHitBounds = bg;
        ElementBounds inputSlot = ElementBounds.Fixed(30, 82, 48.8, 48.8);
        ElementBounds arrow = ElementBounds.Fixed(105, 91, 70, 30);
        ElementBounds outputSlot = ElementBounds.Fixed(204, 82, 48.8, 48.8);
        ElementBounds previous = ElementBounds.Fixed(24, 186, 62, 40);
        ElementBounds counter = ElementBounds.Fixed(90, 196, 86, 24);
        ElementBounds next = ElementBounds.Fixed(184, 186, 62, 40);
        ElementBounds close = ElementBounds.Fixed(250, 186, 40, 40);
        previousHitBounds = previous;
        nextHitBounds = next;
        closeHitBounds = close;

        GuiComposer composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", dialogBounds)
            .AddShadedDialogBG(bg, true);
        if (categories.Count > 0)
        {
            BuildCategoryTabs();
            composer.AddRecipeDisplayGrid(categoryTabs, categories.Count,
                ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8),
                "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        }

        SingleComposer = composer
            .AddRecipeDisplayGrid(firepitInput, 1, inputSlot, "grinding-input", OnFirepitInputClicked)
            .AddDynamicText("→", CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center), arrow, "recipe-arrow")
            .AddRecipeDisplayGrid(output, 1, outputSlot, "recipe-output", OnOutputClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 0, 1, 1), "recipe-temperature")
            .AddButton("<", Previous, previous)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), counter, "recipe-counter")
            .AddButton(">", Next, next)
            .AddButton("×", Close, close)
            .Compose();
    }

    private void ComposeThermalCard()
    {
        int count = Math.Min(cardOwner!.CardsPerPage, cardOwner.recipes.Count - cardOwner.page);
        bool cooking = StationCategory is "Cooking" or "Melting";
        double rowY = cooking ? 127 : 75;
        double fuelY = rowY + 110;
        var root = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedOffset((cardIndex - (count - 1) / 2.0) * 310, -20);
        dialogHitBounds = ElementBounds.Fixed(0, 0, 300, 300);
        autofillHitBounds = null;
        var composer = capi.Gui.CreateCompo($"xrb-thermal-card-{Guid.NewGuid():N}", root)
            .AddDynamicCustomDraw(dialogHitBounds, (_, _, _) => { }, "card-layout")
            .AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(20, 32, 260, 24), "firepit-title");
        if (cooking)
            composer.AddRecipeDisplayGrid(crucibleInputs, 4, ElementBounds.Fixed(30, 65, 195.2, 48.8), "crucible-inputs", (slot, button) => {
                if (crucibleInputs[slot].Itemstack is { } stack) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
            });
        SingleComposer = composer
            .AddRecipeDisplayGrid(firepitInput, 1, ElementBounds.Fixed(30, rowY, 48.8, 48.8), "firepit-input", OnFirepitInputClicked)
            .AddFirepitProgress(ElementBounds.Fixed(30, rowY, 225, 145), "firepit-symbols")
            .AddRecipeDisplayGrid(firepitFuel, 1, ElementBounds.Fixed(30, fuelY, 48.8, 48.8), "firepit-fuel", OnFirepitFuelClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(99, rowY + 8, 80, 28), "recipe-temperature")
            .AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(88, fuelY + 10, 170, 28), "firepit-fuel-temperature")
            .AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(204, rowY, 48.8, 48.8), "recipe-output", OnOutputClicked)
            .Compose();
    }

    private void ComposeFirepit()
    {
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        ElementBounds bg = ElementBounds.Fixed(0, 12, 300, 365);
        dialogHitBounds = bg;
        ElementBounds inputSlot = ElementBounds.Fixed(30, 108, 48.8, 48.8);
        ElementBounds outputSlot = ElementBounds.Fixed(204, 108, 48.8, 48.8);
        ElementBounds symbols = ElementBounds.Fixed(30, 108, 225, 145);
        ElementBounds process = ElementBounds.Fixed(99, 116, 80, 28);
        ElementBounds fuelSlot = ElementBounds.Fixed(30, 218, 48.8, 48.8);
        ElementBounds fuelTemperature = ElementBounds.Fixed(88, 228, 170, 28);
        ElementBounds title = ElementBounds.Fixed(20, 54, 260, 42);
        ElementBounds previous = ElementBounds.Fixed(24, 306, 62, 40);
        ElementBounds counter = ElementBounds.Fixed(90, 316, 86, 24);
        ElementBounds next = ElementBounds.Fixed(184, 306, 62, 40);
        ElementBounds close = ElementBounds.Fixed(250, 306, 40, 40);
        previousHitBounds = previous;
        nextHitBounds = next;
        closeHitBounds = close;

        GuiComposer composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", dialogBounds)
            .AddShadedDialogBG(bg, true);

        if (categories.Count > 0)
        {
            BuildCategoryTabs();
            ElementBounds tabBounds = ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8);
            composer.AddRecipeDisplayGrid(categoryTabs, categories.Count, tabBounds, "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        }

        SingleComposer = composer
            .AddDynamicText("", CairoFont.WhiteSmallText(), title, "firepit-title")
            .AddRecipeDisplayGrid(firepitInput, 1, inputSlot, "firepit-input", OnFirepitInputClicked)
            .AddFirepitProgress(symbols, "firepit-symbols")
            .AddRecipeDisplayGrid(firepitFuel, 1, fuelSlot, "firepit-fuel", OnFirepitFuelClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), process, "recipe-temperature")
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), fuelTemperature, "firepit-fuel-temperature")
            .AddRecipeDisplayGrid(output, 1, outputSlot, "recipe-output", OnOutputClicked)
            .AddButton("<", Previous, previous)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), counter, "recipe-counter")
            .AddButton(">", Next, next)
            .AddButton("×", Close, close)
            .Compose();
    }

    private void ComposeCrucible()
    {
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle).WithFixedOffset(0, -20);
        ElementBounds bg = ElementBounds.Fixed(0, 12, 300, 425);
        dialogHitBounds = bg;
        ElementBounds inputSlot = ElementBounds.Fixed(30, 158, 48.8, 48.8);
        ElementBounds outputSlot = ElementBounds.Fixed(204, 158, 48.8, 48.8);
        ElementBounds symbols = ElementBounds.Fixed(30, 158, 225, 145);
        ElementBounds process = ElementBounds.Fixed(99, 166, 80, 28);
        ElementBounds fuelSlot = ElementBounds.Fixed(30, 268, 48.8, 48.8);
        ElementBounds fuelTemperature = ElementBounds.Fixed(88, 278, 170, 28);
        ElementBounds title = ElementBounds.Fixed(20, 54, 260, 42);
        ElementBounds previous = ElementBounds.Fixed(24, 366, 62, 40);
        ElementBounds counter = ElementBounds.Fixed(90, 376, 86, 24);
        ElementBounds next = ElementBounds.Fixed(184, 366, 62, 40);
        ElementBounds close = ElementBounds.Fixed(250, 366, 40, 40);
        previousHitBounds = previous;
        nextHitBounds = next;
        closeHitBounds = close;

        GuiComposer composer = capi.Gui.CreateCompo($"vsrecipebrowser-recipe-overlay-{++composeRevision}", dialogBounds)
            .AddShadedDialogBG(bg, true);

        if (categories.Count > 0)
        {
            BuildCategoryTabs();
            ElementBounds tabBounds = ElementBounds.Fixed(18, 0, categories.Count * 48.8, 48.8);
            composer.AddRecipeDisplayGrid(categoryTabs, categories.Count, tabBounds, "recipe-category-tabs", (slot, _) => SelectCategory(slot), true, category);
        }

        SingleComposer = composer
            .AddDynamicText("", CairoFont.WhiteSmallText(), title, "firepit-title")
            .AddRecipeDisplayGrid(crucibleInputs, 4, ElementBounds.Fixed(30, 105, 195.2, 48.8), "crucible-inputs", (slot, button) => {
                if (crucibleInputs[slot].Itemstack is { } stack) Load(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes, true);
            })
            .AddRecipeDisplayGrid(firepitInput, 1, inputSlot, "firepit-input", OnFirepitInputClicked)
            .AddFirepitProgress(symbols, "firepit-symbols")
            .AddRecipeDisplayGrid(firepitFuel, 1, fuelSlot, "firepit-fuel", OnFirepitFuelClicked)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), process, "recipe-temperature")
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), fuelTemperature, "firepit-fuel-temperature")
            .AddRecipeDisplayGrid(output, 1, outputSlot, "recipe-output", OnOutputClicked)
            .AddButton("<", Previous, previous)
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), counter, "recipe-counter")
            .AddButton(">", Next, next)
            .AddButton("×", Close, close)
            .Compose();
    }
}
