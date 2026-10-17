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
    // Populate display inventories from the selected source frame.
    private void FillPage()
    {
        if (MultiPage) { BuildRecipeCards(); return; }
        ingredientGridSlots.Clear();
        ingredientOptionCache.Clear();
        displayedProcessTemperature = null;
        selectedIngredients.Clear();
        for (int slot = 0; slot < slotCycleOffsets.Length; slot++) slotCycleOffsets[slot] = Random.Shared.Next(100000);
        ingredientOptions?.TryClose();
        for (int i = 0; i < inputs.Count; i++)
        {
            inputs[i].Itemstack = null;
            inputs[i].MarkDirty();
        }
        firepitInput[0].Itemstack = null;
        firepitInput[0].MarkDirty();
        firepitFuel[0].Itemstack = null;
        firepitFuel[0].MarkDirty();
        smithingMetal[0].Itemstack = null;
        smithingMetal[0].MarkDirty();
        smithingHammer[0].Itemstack = null;
        smithingHammer[0].MarkDirty();
        output[0].Itemstack = null;
        output[0].MarkDirty();

        if (recipes.Count == 0)
        {
            currentPageHasAnimatedVariants = false;
            SingleComposer.GetDynamicText("recipe-temperature").SetNewText("", true);
            SingleComposer.GetDynamicText("recipe-counter").SetNewText("0 / 0", true);
            return;
        }

        RecipeDisplayEntry recipe = EnsureCurrentPageAlternatives(recipes[page]);
        if (recipe.Frames is { Count: > 0 } initialFrames)
            initialFrames[0] = EnsureCurrentPageAlternatives(initialFrames[0]);
        recipes[page] = recipe;
        currentPageHasAnimatedVariants = recipe.Frames is { Count: > 1 } || recipe.InputAlternatives?.Any(list => list.Count > 1) == true
            || recipe.OutputAlternatives is { Count: > 1 };
        // Dedicated station icons are views of the same ingredients, not a separate
        // source. Variant selection must retain even single-option ingredients.
        for (int i = 0; i < Math.Min(inputs.Count, recipe.Inputs.Count); i++)
        {
            inputs[i].Itemstack = recipe.Inputs[i]?.Clone();
            inputs[i].MarkDirty();
        }
        if (IsCrucibleCategory)
        {
            for (int i = 0; i < Math.Min(inputs.Count, recipe.Inputs.Count); i++) inputs[i].Itemstack = recipe.Inputs[i]?.Clone();
            UpdateCrucibleSlots();
            firepitInput[0].Itemstack = StationCategory == "Cooking"
                ? CategoryIcon("Cooking") : FindCrucible(capi);
            firepitInput[0].MarkDirty();
            firepitFuels = DisplayedFuels(recipe.Temperature ?? 0);
            firepitFuelIndex = 0;
            lastFuelChangeMs = capi.ElapsedMilliseconds;
            SetDisplayedFirepitFuel();
        }
        else if (IsFirepitCategory)
        {
            firepitInput[0].Itemstack = recipe.Inputs.ElementAtOrDefault(1)?.Clone();
            firepitInput[0].MarkDirty();
            firepitFuels = DisplayedFuels(recipe.Temperature ?? 0);
            if (firepitFuels.Count == 0 && recipe.Inputs.ElementAtOrDefault(4) is { } fallbackFuel)
                firepitFuels.Add(fallbackFuel.Clone());
            firepitFuelIndex = 0;
            lastFuelChangeMs = capi.ElapsedMilliseconds;
            SetDisplayedFirepitFuel();
        }
        else if (IsGrindingCategory || IsBakingCategory || IsDryingCategory)
        {
            firepitInput[0].Itemstack = recipe.Inputs.FirstOrDefault(stack => stack is not null)?.Clone();
            firepitInput[0].MarkDirty();
        }
        else if (IsSmithingCategory || IsCastingCategory)
        {
            smithingMetal[0].Itemstack = recipe.Inputs.ElementAtOrDefault(0)?.Clone();
            smithingMetal[0].MarkDirty();
            smithingHammer[0].Itemstack = recipe.Inputs.ElementAtOrDefault(1)?.Clone();
            smithingHammer[0].MarkDirty();
        }
        else for (int i = 0; i < Math.Min(inputs.Count, recipe.Inputs.Count); i++)
        {
            inputs[i].Itemstack = recipe.Inputs[i]?.Clone();
            inputs[i].MarkDirty();
        }
        if (IsPressingCategory) { firepitInput[0].Itemstack = inputs[0].Itemstack?.Clone(); firepitInput[0].MarkDirty(); }
        if (IsBarrelCategory) UpdateBarrelSlots();
        if (IsPitKilnCategory) UpdatePitSlots();
        ingredientVariantIndex = 0;
        toolVariantIndex = 0;
        lastIngredientVariantChangeMs = capi.ElapsedMilliseconds;
        secondaryOutput[0].Itemstack = recipe.SecondaryOutput?.Clone();
        secondaryOutput[0].MarkDirty();
        output[0].Itemstack = recipe.Output.Clone();
        output[0].MarkDirty();
        if (IsFirepitCategory || IsCrucibleCategory)
        {
            string outputName = recipe.Output.GetName();
            string titleText = BrowserText.Choose("Создаст ", "Produces ") + $"{recipe.Output.StackSize}x {outputName}";
            if (IsCrucibleCategory && recipe.Output.Collectible is BlockSmeltedContainer filled)
            {
                var content = filled.GetContents(capi.World, recipe.Output);
                titleText = BrowserText.Choose("Расплав: ", "Molten metal: ") + content.Key?.GetName()
                    + " · " + content.Value + BrowserText.Choose(" единиц", " units");
            }
            SingleComposer.GetDynamicText("firepit-title").SetNewText("", true);
        }
        string temperatureText = StationCategory is "Heating" or "Casting" or "Alloying" or "Baking" or "Melting" or "Cooking" && recipe.Temperature is int degrees
            ? $"{degrees}°C"
            : IsBarrelCategory || IsPitKilnCategory || IsDryingCategory ? recipe.Method : "";
        if (recipe.Category == "Casting")
        {
            var crucible = recipe.Inputs.FirstOrDefault(stack => stack?.Collectible is BlockSmeltedContainer);
            int units = crucible?.Collectible is BlockSmeltedContainer filled
                ? filled.GetContents(capi.World, crucible).Value : 0;
            temperatureText = units > 0 ? units + BrowserText.Choose(" единиц", " units") : "";
        }
        if (IsFirepitCategory || IsCrucibleCategory)
        {
            SingleComposer.GetDynamicText("recipe-temperature").SetNewText("", true);
            (SingleComposer.GetElement("firepit-symbols") as FirepitProgressElement)?.SetTemperature(temperatureText);
        }
        else SingleComposer.GetDynamicText("recipe-temperature").SetNewText(temperatureText, true);
        if (IsFirepitCategory || IsCrucibleCategory)
            ConfigureOptionsGrid("firepit-fuel", _ => firepitFuels, (_, selected) =>
            {
                firepitFuelIndex = firepitFuels.FindIndex(stack => stack.Equals(capi.World, selected));
                lastFuelChangeMs = capi.ElapsedMilliseconds;
                SetDisplayedFirepitFuel();
            });
        if (IsSmithingCategory)
        {
            ConfigureOptionsGrid("smithing-anvil", _ => smithingAnvils, (_, selected) =>
            {
                smithingAnvilIndex = smithingAnvils.FindIndex(stack => stack.Equals(capi.World, selected));
                smithingAnvil[0].Itemstack = selected.Clone(); smithingAnvil[0].MarkDirty();
            });
            ConfigureInputOptions("smithing-metal", _ => 0);
            ConfigureInputOptions("smithing-hammer", _ => 1);
        }
        if (IsPressingCategory) ConfigureInputOptions("pressing-input", _ => 0);
        if (IsGrindingCategory) ConfigureInputOptions("grinding-input", _ => 0);
        if (IsBakingCategory || IsDryingCategory) ConfigureInputOptions("baking-input", _ => 0);
        if (IsFirepitCategory) ConfigureInputOptions("firepit-input", _ => 1);
        if (IsCastingCategory)
        {
            ConfigureInputOptions("casting-crucible", _ => 0);
            ConfigureInputOptions("casting-mold", _ => 1);
        }
        if (IsPitKilnCategory)
        {
            ConfigureInputOptions("pit-input", _ => 0);
            ConfigureInputOptions("pit-materials", slot => slot + 1);
        }
        if (IsBarrelCategory) ConfigureInputOptions("barrel-input", _ => CurrentDisplayFrame().Inputs.FindIndex(stack => stack is not null
            && !(stack.Collectible.Attributes?["waterTightContainerProps"]["itemsPerLitre"].AsDouble(0) > 0)));
        if (IsCrucibleCategory) ConfigureInputOptions("crucible-inputs", slot => CurrentDisplayFrame().Inputs
            .Select((stack, index) => (stack, index)).Where(pair => pair.stack is not null
                && !pair.stack.Collectible.Code.Path.StartsWith("crucible-") && !pair.stack.Collectible.Code.Path.Contains("mold"))
            .Select(pair => pair.index).Skip(slot).DefaultIfEmpty(-1).First());
        ConfigureInputOptions("recipe-inputs", slot => slot);
        if (!compactCard) SingleComposer.GetDynamicText("recipe-counter").SetNewText($"{page + 1} / {recipes.Count}", true);
        if (IsSmithingCategory) UpdateSmithingDetails();
        if (IsCementationCategory)
        {
            UpdateCementationSlots();
            ConfigureInputOptions("cementation-iron", _ => 0);
            ConfigureInputOptions("cementation-carbon", _ => 1);
            ConfigureInputOptions("cementation-fuel", _ => 2);
        }
    }

    // Keep displayed ingredients, tools, fuel and outputs synchronized.
    private RecipeDisplayEntry EnsureCurrentPageAlternatives(RecipeDisplayEntry recipe)
    {
        // Output-dependent frames retain their own ingredient sets. Within a
        // fixed result, alternatives live in slots rather than a Cartesian list.
        if (recipe.Frames is { Count: > 0 }) return recipe;
        if (recipe.InputAlternatives is not null || recipe.SourceGridRecipes is not { Count: > 0 }) return recipe;

        var timing = Stopwatch.StartNew();
        int slotCount = Math.Max(9, recipe.Inputs.Count);
        var alternatives = Enumerable.Range(0, slotCount).Select(_ => new List<ItemStack>()).ToList();
        foreach (GridRecipe grid in recipe.SourceGridRecipes)
        {
            List<List<ItemStack>> expanded = BuildGridIngredientAlternatives(capi, grid, variantsOnly: true);
            for (int slot = 0; slot < Math.Min(alternatives.Count, expanded.Count); slot++)
                alternatives[slot].AddRange(expanded[slot].Select(stack => stack.Clone()));
        }
        for (int slot = 0; slot < alternatives.Count; slot++)
        {
            if (slot < recipe.Inputs.Count && recipe.Inputs[slot] is { } fallback) alternatives[slot].Add(fallback.Clone());
            alternatives[slot] = alternatives[slot]
                .Where(stack => stack.Collectible?.Code is not null)
                .GroupBy(stack => $"{stack.Class}:{stack.Collectible.Code}:{stack.StackSize}", StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
        }

        RecipeDisplayEntry enriched = recipe with { InputAlternatives = alternatives };
        int categoryIndex = recipes.FindIndex(entry => ReferenceEquals(entry, recipe));
        if (categoryIndex >= 0) recipes[categoryIndex] = enriched;
        int allIndex = allRecipes.FindIndex(entry => ReferenceEquals(entry, recipe));
        if (allIndex >= 0) allRecipes[allIndex] = enriched;
        LogSlowOperation(capi, target, mode, timing.ElapsedMilliseconds,
            $"page variants={timing.ElapsedMilliseconds}ms, source recipes={recipe.SourceGridRecipes.Count}");
        return enriched;
    }

    private void SetDisplayedIngredientVariants(RecipeDisplayEntry recipe)
    {
        if (recipe.Frames is { Count: > 0 })
        {
            int frameIndex = ingredientVariantIndex % recipe.Frames.Count;
            var frame = EnsureCurrentPageAlternatives(recipe.Frames[frameIndex]);
            recipe.Frames[frameIndex] = frame;
            for (int i = 0; i < inputs.Count; i++)
            {
                ItemStack? selected = frame.Inputs.ElementAtOrDefault(i);
                // Tools are interchangeable inside this exact recipe frame;
                // boards, nails and other materials retain its original pairing.
                if (frame.InputAlternatives?.ElementAtOrDefault(i) is { Count: > 1 } variants)
                {
                    if (IsToolSlot(frame, i)) selected = variants[toolVariantIndex % variants.Count];
                    else
                        selected = variants[IngredientCycleIndex(frame, i, ingredientVariantIndex, variants.Count)];
                }
                if (IsCrucibleCategory && frame.InputAlternatives?.ElementAtOrDefault(i) is { Count: > 1 } cookingVariants)
                    selected = cookingVariants[ingredientVariantIndex % cookingVariants.Count];
                inputs[i].Itemstack = selected?.Clone();
                inputs[i].MarkDirty();
            }
            ApplyGridVariantConstraints(frame);
            output[0].Itemstack = frame.Output.Clone();
            output[0].MarkDirty();
            secondaryOutput[0].Itemstack = frame.SecondaryOutput?.Clone();
            secondaryOutput[0].MarkDirty();
            UpdateFramePresentation(frame);
            if (IsCrucibleCategory) UpdateCrucibleSlots();
            if (IsSmithingCategory)
            {
                smithingMetal[0].Itemstack = inputs[0].Itemstack?.Clone();
                smithingHammer[0].Itemstack = inputs[1].Itemstack?.Clone();
                smithingMetal[0].MarkDirty(); smithingHammer[0].MarkDirty();
                UpdateSmithingDetails();
            }
            if (IsBarrelCategory || IsPitKilnCategory)
            {
                if (IsBarrelCategory) UpdateBarrelSlots(); else UpdatePitSlots();
                SingleComposer.GetDynamicText("recipe-temperature").SetNewText(frame.Method, true);
            }
            return;
        }
        if (recipe.SecondaryAlternatives is { Count: > 0 })
        {
            secondaryOutput[0].Itemstack = recipe.SecondaryAlternatives[ingredientVariantIndex % recipe.SecondaryAlternatives.Count].Clone();
            secondaryOutput[0].MarkDirty();
        }
        if ((IsSmithingCategory || IsCastingCategory) && recipe.InputAlternatives is not null)
        {
            List<ItemStack> metals = recipe.InputAlternatives.ElementAtOrDefault(0) ?? new();
            List<ItemStack> hammers = recipe.InputAlternatives.ElementAtOrDefault(1) ?? new();
            if (metals.Count > 0)
            {
                smithingMetal[0].Itemstack = metals[ingredientVariantIndex % metals.Count].Clone();
                smithingMetal[0].MarkDirty();
            }
            if (hammers.Count > 0)
            {
                smithingHammer[0].Itemstack = hammers[toolVariantIndex % hammers.Count].Clone();
                smithingHammer[0].MarkDirty();
            }
        }
        if (recipe.InputAlternatives is not null)
        {
            for (int slot = 0; slot < Math.Min(inputs.Count, recipe.InputAlternatives.Count); slot++)
            {
                List<ItemStack> variants = recipe.InputAlternatives[slot];
                if (variants.Count <= 1) continue;
                inputs[slot].Itemstack = variants[IsToolSlot(recipe, slot) ? toolVariantIndex % variants.Count
                    : IngredientCycleIndex(recipe, slot, ingredientVariantIndex, variants.Count)].Clone();
                inputs[slot].MarkDirty();
            }
        }
        if (recipe.OutputAlternatives is { Count: > 1 })
        {
            output[0].Itemstack = recipe.OutputAlternatives[ingredientVariantIndex % recipe.OutputAlternatives.Count].Clone();
            output[0].MarkDirty();
        }
        ApplyGridVariantConstraints(recipe);
        if (IsCementationCategory) UpdateCementationSlots();
        if (IsSmithingCategory || IsCastingCategory)
        {
            smithingMetal[0].Itemstack = inputs[0].Itemstack?.Clone(); smithingMetal[0].MarkDirty();
            smithingHammer[0].Itemstack = inputs[1].Itemstack?.Clone(); smithingHammer[0].MarkDirty();
        }
        if (IsCastingCategory && smithingMetal[0].Itemstack is { Collectible: BlockSmeltedContainer filled } molten)
        {
            int units = filled.GetContents(capi.World, molten).Value;
            SingleComposer.GetDynamicText("recipe-temperature").SetNewText(units + BrowserText.Choose(" единиц", " units"), true);
        }
        if (IsPressingCategory) { firepitInput[0].Itemstack = inputs[0].Itemstack?.Clone(); firepitInput[0].MarkDirty(); }
        if (IsBarrelCategory) UpdateBarrelSlots();
        if (IsCrucibleCategory) UpdateCrucibleSlots();
        if (IsSmithingCategory) UpdateSmithingDetails();
    }

    private int? displayedProcessTemperature;
    private void UpdateFramePresentation(RecipeDisplayEntry frame)
    {
        if (IsPressingCategory || IsGrindingCategory || IsBakingCategory || IsDryingCategory)
        {
            firepitInput[0].Itemstack = inputs[0].Itemstack?.Clone(); firepitInput[0].MarkDirty();
        }
        if (IsCastingCategory)
        {
            smithingMetal[0].Itemstack = inputs[0].Itemstack?.Clone(); smithingMetal[0].MarkDirty();
            smithingHammer[0].Itemstack = inputs[1].Itemstack?.Clone(); smithingHammer[0].MarkDirty();
        }
        int? requiredTemperature = StationCategory == "Cooking"
            ? Math.Max(100, Enumerable.Range(0, inputs.Count).Select(index => inputs[index].Itemstack?.Collectible.CombustibleProps?.MeltingPoint ?? 0).Max())
            : frame.Temperature;
        string text = requiredTemperature is int temperature ? $"{temperature}°C" : "";
        if (IsBarrelCategory || IsPitKilnCategory || IsDryingCategory) text = frame.Method;
        if (IsCastingCategory)
        {
            var molten = smithingMetal[0].Itemstack;
            int units = molten?.Collectible is BlockSmeltedContainer filled ? filled.GetContents(capi.World, molten).Value : 0;
            text = units + BrowserText.Choose(" единиц", " units");
        }
        if (IsFirepitCategory || IsCrucibleCategory)
        {
            if (IsFirepitCategory) { firepitInput[0].Itemstack = inputs[1].Itemstack?.Clone(); firepitInput[0].MarkDirty(); }
            if (displayedProcessTemperature != requiredTemperature)
            {
                displayedProcessTemperature = requiredTemperature;
                firepitFuels = DisplayedFuels(requiredTemperature ?? 0);
                firepitFuelIndex = 0;
                SetDisplayedFirepitFuel();
            }
            (SingleComposer.GetElement("firepit-symbols") as FirepitProgressElement)?.SetTemperature(text);
            SingleComposer.GetDynamicText("firepit-title").SetNewText("", true);
            text = "";
        }
        SingleComposer.GetDynamicText("recipe-temperature").SetNewText(text, true);
    }

    private int IngredientCycleIndex(RecipeDisplayEntry recipe, int slot, int cycle, int count)
    {
        // Only an unnamed wildcard admits independent choices in each cell.
        // Named materials and output-dependent frames retain synchronization.
        bool independent = recipe.Category == "Crafting"
            && recipe.SourceGridRecipes?.Any(grid => IsIndependentGridSlot(grid, slot)) == true;
        return (int)(((long)cycle + (independent ? slotCycleOffsets[slot] : 0)) % count);
    }

    private void ApplyGridVariantConstraints(RecipeDisplayEntry recipe)
    {
        foreach (var choice in selectedIngredients)
        {
            inputs[choice.Key].Itemstack = choice.Value.Clone();
            inputs[choice.Key].MarkDirty();
        }
        if (recipe.Category != "Crafting" || recipe.SourceGridRecipes is not { Count: > 0 } sources) return;
        CraftingRecipeIngredient? At(GridRecipe grid, int slot)
        {
            int width = Math.Clamp(grid.Width, 1, 3), height = Math.Clamp(grid.Height, 1, 3);
            int x = slot % 3 - (3 - width) / 2, y = slot / 3 - (3 - height) / 2;
            return x < 0 || y < 0 || x >= width || y >= height ? null
                : grid.GetElementInGrid(y, x, grid.ResolvedIngredients!, width);
        }
        bool Accepts(GridRecipe grid) => Enumerable.Range(0, 9).All(slot =>
            At(grid, slot) is { } ingredient
                ? inputs[slot].Itemstack is { } stack && IngredientAcceptsDisplay(ingredient, stack)
                : inputs[slot].Empty);
        if (sources.Any(Accepts)) return;
        // Correlated source recipes are not a licence to mix arbitrary slots.
        // Fall back to one real recipe, retaining any already compatible tool.
        var compatible = sources.Where(grid => selectedIngredients.All(choice => At(grid, choice.Key) is { } ingredient
            && IngredientAcceptsDisplay(ingredient, choice.Value))).ToList();
        var selected = compatible.Count > 0 ? compatible[ingredientVariantIndex % compatible.Count]
            : sources[ingredientVariantIndex % sources.Count];
        for (int slot = 0; slot < 9; slot++)
        {
            var ingredient = At(selected, slot);
            if (ingredient is not null && inputs[slot].Itemstack is { } current
                && IngredientAcceptsDisplay(ingredient, current)) continue;
            inputs[slot].Itemstack = ingredient is null ? null : ResolveIngredientDisplayStack(capi, ingredient)?.Clone();
            inputs[slot].MarkDirty();
        }
    }

    private void UpdateSmithingDetails()
    {
        if (smithingMetal[0].Itemstack is not { } metal) return;
        SingleComposer.GetDynamicText("smithing-quantity").SetNewText(Math.Max(1, inputs[0].Itemstack?.StackSize ?? metal.StackSize).ToString(), true);
        metal.StackSize = 1; // Quantity lives beside the ingot, not on its icon.
        smithingMetal[0].MarkDirty();
        string key = metal.Collectible.Code.ToString();
        if (smithingAnvilMetal != key)
        {
            smithingAnvilMetal = key;
            int required = metal.Collectible is IAnvilWorkable workable ? workable.GetRequiredAnvilTier(metal) : 1;
            smithingAnvils = capi.World.Blocks.OfType<BlockAnvil>()
                .Where(anvil => anvil.MetalTier >= required && anvil.Code is not null)
                .OrderBy(anvil => anvil.MetalTier).ThenBy(anvil => anvil.Code.ToString(), StringComparer.Ordinal)
                .Select(anvil => new ItemStack(anvil)).ToList();
            smithingAnvilIndex = 0;
        }
        if (smithingAnvils.Count > 0) smithingAnvilIndex = toolVariantIndex % smithingAnvils.Count;
        smithingAnvil[0].Itemstack = smithingAnvils.ElementAtOrDefault(smithingAnvilIndex)?.Clone();
        smithingAnvil[0].MarkDirty();
    }

    private static bool IsIndependentVariantSlot(RecipeDisplayEntry frame, int slot)
        => frame.SourceGridRecipes?.FirstOrDefault() is { } grid && IsIndependentGridSlot(grid, slot);

    private static bool IsIndependentGridSlot(GridRecipe grid, int slot)
    {
        if (grid.ResolvedIngredients is null) return false;
        int width = Math.Clamp(grid.Width, 1, 3), height = Math.Clamp(grid.Height, 1, 3);
        int x = slot % 3 - (3 - width) / 2, y = slot / 3 - (3 - height) / 2;
        if (x < 0 || x >= width || y < 0 || y >= height) return false;
        var ingredient = grid.GetElementInGrid(y, x, grid.ResolvedIngredients, width);
        return ingredient?.Code?.Path.Contains('*') == true && string.IsNullOrEmpty(ingredient.Name);
    }

    private static bool IsToolSlot(RecipeDisplayEntry frame, int slot)
    {
        if (frame.Category == "Smithing") return slot == 1;
        var grid = frame.SourceGridRecipes?.FirstOrDefault();
        if (grid?.ResolvedIngredients is null) return frame.Inputs.ElementAtOrDefault(slot)?.Collectible.Tool is not null;
        int width = Math.Clamp(grid.Width, 1, 3), height = Math.Clamp(grid.Height, 1, 3);
        int x = slot % 3 - (3 - width) / 2, y = slot / 3 - (3 - height) / 2;
        return x >= 0 && x < width && y >= 0 && y < height
            && grid.GetElementInGrid(y, x, grid.ResolvedIngredients, width) is { } ingredient && IsReusableToolIngredient(ingredient);
    }

    private static bool IsReusableToolIngredient(CraftingRecipeIngredient ingredient) =>
        IngredientRules.IsReusable(ingredient);

    private void UpdateCrucibleSlots()
    {
        var metals = Enumerable.Range(0, inputs.Count).Select(i => inputs[i].Itemstack)
            .Where(stack => stack is not null && !stack.Collectible.Code.Path.StartsWith("crucible-")
                && !stack.Collectible.Code.Path.Contains("mold")).Take(4).ToArray();
        for (int i = 0; i < 4; i++) { crucibleInputs[i].Itemstack = metals.ElementAtOrDefault(i)?.Clone(); crucibleInputs[i].MarkDirty(); }
    }

    private void UpdateBarrelSlots()
    {
        bool Liquid(ItemStack stack) => stack.Collectible.Attributes?["waterTightContainerProps"]["itemsPerLitre"].AsDouble(0) > 0;
        var stacks = Enumerable.Range(0, inputs.Count).Select(i => inputs[i].Itemstack).OfType<ItemStack>().ToList();
        var liquid = stacks.FirstOrDefault(Liquid);
        barrelLiquid = liquid?.Clone();
        firepitInput[0].Itemstack = stacks.FirstOrDefault(stack => !Liquid(stack))?.Clone();
        firepitInput[0].MarkDirty();
        (SingleComposer.GetElement("barrel-liquid") as BarrelLiquidElement)?.SetLiquid(liquid);
        double perLitre = liquid?.Collectible.Attributes?["waterTightContainerProps"]["itemsPerLitre"].AsDouble(0) ?? 0;
        string tooltip = liquid is null ? BrowserText.Choose("Без жидкости", "No liquid")
            : liquid.GetName() + "\n" + (perLitre > 0 ? (liquid.StackSize / perLitre).ToString("0.##") + BrowserText.Choose(" л", " L") : "");
        (SingleComposer.GetElement("barrel-liquid-tooltip") as GuiElementHoverText)?.SetNewText(tooltip);
    }

    private void UpdatePitSlots()
    {
        firepitInput[0].Itemstack = inputs[0].Itemstack?.Clone(); firepitInput[0].MarkDirty();
        for (int i = 0; i < 3; i++) { pitMaterials[i].Itemstack = inputs[i + 1].Itemstack?.Clone(); pitMaterials[i].MarkDirty(); }
    }

    private List<ItemStack> DisplayedFuels(int temperature)
    {
        if (recipes[page].Category != "Fuel") return FindFirepitFuels(capi, temperature);
        var fuel = target.Clone(); fuel.StackSize = 1;
        return FuelFits(fuel, temperature) ? new() { fuel } : new();
    }

    internal static bool FuelFits(ItemStack fuel, int requiredTemperature) =>
        fuel.Collectible.CombustibleProps is { BurnDuration: > 0, BurnTemperature: > 0 } properties
        && properties.BurnTemperature >= requiredTemperature;

    private void SetDisplayedFirepitFuel()
    {
        ItemStack? fuel = firepitFuels.Count == 0 ? null : firepitFuels[firepitFuelIndex];
        firepitFuel[0].Itemstack = fuel?.Clone();
        firepitFuel[0].MarkDirty();
        int temperature = fuel?.Collectible?.CombustibleProps?.BurnTemperature ?? 0;
        SingleComposer.GetDynamicText("firepit-fuel-temperature").SetNewText(temperature > 0 ? $"{temperature}°C" : "", true);
    }
}
