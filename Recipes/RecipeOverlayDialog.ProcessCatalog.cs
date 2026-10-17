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
    // Build processing routes from registered collectible properties.
    private static void AppendCollectibleProcesses(ICoreClientAPI api, ItemStack target, RecipeViewMode mode, List<RecipeDisplayEntry> result)
    {
        if (!ReferenceEquals(processCatalogWorld, api.World) || processCatalog is null)
        {
            processCatalogWorld = api.World;
            processCatalog = BuildProcessCatalog(api);
            BuildProcessIndexes();
        }
        string? key = mode == RecipeViewMode.Recipes && CeramicOutputGroup(target.Collectible.Code.Path) is not null
            ? LogicalOutputGroup(target) : RecipeStackKey(target);
        Dictionary<string, List<RecipeDisplayEntry>> index = mode == RecipeViewMode.Recipes ? processesByOutput : processesByInput;
        if (key is not null && index.TryGetValue(key, out List<RecipeDisplayEntry>? entries))
        {
            if (mode == RecipeViewMode.Uses && target.Collectible is BlockSmeltedContainer)
            {
                int available = ((BlockSmeltedContainer)target.Collectible).GetContents(api.World, target).Value;
                result.AddRange(entries.Where(entry => entry.Inputs.Where(stack => stack?.Collectible is BlockSmeltedContainer)
                    .All(stack => ((BlockSmeltedContainer)stack!.Collectible).GetContents(api.World, stack).Value <= available)));
            }
            else result.AddRange(entries);
        }
    }

    private static void BuildProcessIndexes()
    {
        processesByOutput.Clear();
        processesByInput.Clear();
        foreach (RecipeDisplayEntry entry in processCatalog ?? Enumerable.Empty<RecipeDisplayEntry>())
        {
            string? outputKey = RecipeStackKey(entry.Output);
            if (outputKey is not null) AddProcessIndex(processesByOutput, outputKey, entry);
            if (entry.Category == "Cementation") AddProcessIndex(processesByOutput, "Item:game:ingot-steel", entry);
            if (entry.Category == "Melting" && entry.Output.Collectible is BlockSmeltedContainer filled
                && processCatalogWorld is not null && RecipeStackKey(filled.GetContents(processCatalogWorld, entry.Output).Key) is { } ingotKey)
                AddProcessIndex(processesByOutput, ingotKey, entry);
            if (LogicalOutputGroup(entry.Output) is { } logicalKey) AddProcessIndex(processesByOutput, logicalKey, entry);
            string? secondaryKey = RecipeStackKey(entry.SecondaryOutput);
            if (secondaryKey is not null) AddProcessIndex(processesByOutput, secondaryKey, entry);
            foreach (ItemStack input in entry.Inputs.OfType<ItemStack>()
                .Concat(entry.InputAlternatives?.SelectMany(options => options) ?? Enumerable.Empty<ItemStack>())
                .GroupBy(RecipeStackKey).Select(group => group.First()))
            {
                string? inputKey = RecipeStackKey(input);
                if (inputKey is not null) AddProcessIndex(processesByInput, inputKey, entry);
            }
        }
    }

    private static void AddProcessIndex(Dictionary<string, List<RecipeDisplayEntry>> index, string key, RecipeDisplayEntry entry)
    {
        if (!index.TryGetValue(key, out List<RecipeDisplayEntry>? entries)) index[key] = entries = new();
        entries.Add(entry);
    }

    private static bool CouldHaveCollectibleUse(ItemStack target)
    {
        CollectibleObject? collectible = target.Collectible;
        if (collectible is null) return false;
        if (collectible.TransitionableProps?.Any(transition => transition.Type is not EnumTransitionType.Perish and not EnumTransitionType.None) == true)
            return true;
        if (collectible.GrindingProps?.GroundStack is not null || collectible.CrushingProps?.CrushedStack is not null)
            return true;
        try
        {
            return collectible.GetCombustibleProperties(recipeCatalogWorld!, target, null)?.SmeltedStack is not null;
        }
        catch { return false; }
    }

    private static List<RecipeDisplayEntry> BuildProcessCatalog(ICoreClientAPI api)
    {
        var catalog = new List<RecipeDisplayEntry>();
        var castingFeedstocks = new Dictionary<string, (ItemStack Stack, CombustibleProperties Heat)>(StringComparer.Ordinal);
        preferredCastingFeedstocks.Clear();
        List<CollectibleObject> toolMolds = api.World.Collectibles
            .Where(value => value?.Code?.Path.StartsWith("toolmold-blue-fired-", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        foreach (CollectibleObject collectible in api.World.Collectibles.Where(value => value?.Code is not null))
        {
            if (IsHiddenVanillaIngot(collectible)) continue;
            // Baking variants are real per-code collectibles. Do not rely on
            // handbook grouping here: bread-*-{state} may expose only one
            // representative grain and would omit e.g. amaranth.
            bool collectibleHasBaking = AppendBakingProcess(api, catalog, new ItemStack(collectible));
            if (collectible.Code.Path.StartsWith("hide-oiled-"))
                AppendTransitionProcesses(catalog, new ItemStack(collectible), collectible.TransitionableProps);
            List<ItemStack> handbookStacks;
            try { handbookStacks = collectible.GetHandBookStacks(api) ?? new List<ItemStack>(); }
            catch { handbookStacks = new List<ItemStack> { new(collectible) }; }
            if (handbookStacks.Count == 0) handbookStacks.Add(new ItemStack(collectible));

            foreach (ItemStack? variant in handbookStacks)
            {
                if (variant?.Collectible?.Code is null) continue;
                try
                {
                    AppendPressingProcess(api, catalog, variant);
                    // Handbook groups can return stacks belonging to other
                    // collectibles (including other cooking states). Always
                    // use the actual variant's processing properties.
                    CollectibleObject variantCollectible = variant.Collectible;
                    AppendTransitionProcesses(catalog, variant, variantCollectible.TransitionableProps);

                    CombustibleProperties? heat = variantCollectible.GetCombustibleProperties(api.World, variant, null);
                    bool variantHasBaking = ReferenceEquals(variantCollectible, collectible)
                        ? collectibleHasBaking : AppendBakingProcess(api, catalog, variant);
                    if (!variantHasBaking && heat?.SmeltedStack?.ResolvedItemstack is { Collectible.Code: not null } heatedOutput
                        && !IsUnavailableCrucibleOutput(heatedOutput))
                    {
                        ItemStack input = variant.Clone();
                        input.StackSize = Math.Max(1, heat.SmeltedRatio);
                        if (heat.RequiresContainer && IsCastIngot(heatedOutput))
                        {
                            string key = RecipeStackKey(heatedOutput) ?? heatedOutput.Collectible.Code.ToString();
                            // Keep every legal feedstock, not just the preferred
                            // icon. One melting page can then show all sources.
                            castingFeedstocks[key + "|" + DisplayStackIdentity(variant)] = (variant.Clone(), heat);
                        }
                        else
                        {
                            bool isBloom = heatedOutput.Collectible is ItemIronBloom;
                            bool isGlassmaking = heat.MeltingPoint >= 1200
                                && heatedOutput.Collectible.Code.Path.StartsWith("glass-", StringComparison.OrdinalIgnoreCase);
                            string category = heat.SmeltingType == EnumSmeltType.Fire ? "Pit firing"
                                : isBloom || isGlassmaking || (!heat.RequiresContainer && heat.SmeltingType == EnumSmeltType.Smelt)
                                ? "Bloomery"
                                : "Heating";
                            if (category == "Pit firing") catalog.AddRange(PitKilnEntries(api, input, heatedOutput));
                            else if (category == "Heating")
                            {
                                ItemStack? fuel = FindFirepitFuel(api, heat.MeltingPoint);
                                catalog.Add(new RecipeDisplayEntry(
                                    category,
                                    heat.SmeltingType.ToString(),
                                    BuildFirepitInputs(input, fuel),
                                    heatedOutput.Clone(),
                                    heat.MeltingPoint));
                            }
                            else
                            {
                                catalog.Add(ProcessEntry(category, $"{heat.SmeltingType} · {heat.MeltingPoint}°C", input, heatedOutput));
                            }
                        }
                    }

                    ItemStack? ground = variantCollectible.GrindingProps?.GroundStack?.ResolvedItemstack;
                    if (ground?.Collectible?.Code is not null) catalog.Add(ProcessEntry("Grinding", "Quern", variant, ground));
                    ItemStack? crushed = variantCollectible.CrushingProps?.CrushedStack?.ResolvedItemstack;
                    if (crushed?.Collectible?.Code is not null) catalog.Add(ProcessEntry("Crushing", $"Hardness tier {variantCollectible.CrushingProps!.HardnessTier}", variant, crushed));
                }
                catch (Exception exception)
                {
                    api.Logger.Warning("[Recipe Browser] Skipped invalid handbook process variant {0}: {1}", variant.Collectible.Code, exception.Message);
                }
            }
        }
        foreach ((ItemStack feedstock, CombustibleProperties heat) in castingFeedstocks.Values)
        {
            ItemStack? ingot = heat.SmeltedStack?.ResolvedItemstack?.Clone();
            ItemStack? crucible = FindCrucible(api);
            ItemStack? mold = FindIngotMold(api);
            if (ingot?.Collectible?.Code is null || crucible is null || mold is null) continue;
            string ingotKey = RecipeStackKey(ingot) ?? ingot.Collectible.Code.ToString();
            if (!preferredCastingFeedstocks.TryGetValue(ingotKey, out var preferred)
                || CastingFeedstockRank(feedstock, ingot) < CastingFeedstockRank(preferred, ingot))
                preferredCastingFeedstocks[ingotKey] = feedstock.Clone();
            ItemStack input = feedstock.Clone();
            input.StackSize = Math.Max(1, heat.SmeltedRatio);
            catalog.Add(new RecipeDisplayEntry(
                "Casting", $"Crucible → ingot mold · {heat.MeltingPoint}°C",
                new List<ItemStack?> { input, crucible, mold }, ingot, heat.MeltingPoint));
            AppendToolMoldCastingRecipes(api, catalog, toolMolds, input, ingot, heat, crucible);
        }
        var splitCatalog = catalog.SelectMany(entry => entry.Category == "Casting" ? SplitCastingEntry(api, entry) : new[] { entry }).ToList();
        foreach (var alloy in api.ModLoader.GetModSystem<RecipeRegistrySystem>().MetalAlloys.Where(recipe => recipe.Enabled))
        {
            var ingot = alloy.Output?.ResolvedItemstack;
            var crucible = FindCrucible(api);
            var mold = FindIngotMold(api);
            if (ingot is null || crucible is null || mold is null) continue;
            var quantities = FindValidAlloyNuggetQuantities(alloy.Ingredients);
            var ingredients = alloy.Ingredients.Select((ingredient, i) => ingredient.ResolvedItemStack is { } metal
                ? FindPreferredCastingFeedstock(api, metal, quantities[i]) : null).ToList();
            ingredients.Add(crucible);
            ingredients.Add(mold);
            var result = ingot.Clone();
            result.StackSize = Math.Max(1, quantities.Sum() / 20);
            int temperature = GetMeltingPoint(api, ingot) ?? 1000;
            splitCatalog.AddRange(SplitCastingEntry(api, new RecipeDisplayEntry("Alloying", "Alloy", ingredients, result, temperature)));
            var toolEntries = new List<RecipeDisplayEntry>();
            AppendToolMoldCastingRecipes(api, toolEntries, toolMolds, ingot,
                ingot, new CombustibleProperties { SmeltedRatio = 20, MeltingPoint = temperature }, crucible);
            foreach (var toolEntry in toolEntries)
                splitCatalog.AddRange(SplitCastingEntry(api, toolEntry).Where(entry => entry.Category == "Casting"));
        }
        if (CementationRecipe(api.World) is { } cementation) splitCatalog.Add(cementation);
        return splitCatalog
            .GroupBy(RecipeIdentity, StringComparer.Ordinal).Select(group => group.First()).ToList();
    }

    private static bool IsHiddenVanillaIngot(CollectibleObject collectible) => collectible.Code?.Domain == "game"
        && collectible.Code.Path.StartsWith("ingot-", StringComparison.Ordinal)
        && collectible.Attributes?["handbook"]["exclude"].AsBool(false) == true;

    private static bool IsUnavailableCrucibleOutput(ItemStack output) => IsHiddenVanillaIngot(output.Collectible)
        || output.Collectible.Code.Domain == "game" && output.Collectible.Code.Path is "ingot-steel" or "ingot-blistersteel";

    private static RecipeDisplayEntry? CementationRecipe(IWorldAccessor world)
    {
        ItemStack? Stack(string code, int quantity) => world.GetItem(new AssetLocation("game", code)) is { } item ? new ItemStack(item, quantity) : null;
        var iron = Stack("ingot-iron", 16);
        var steel = Stack("ingot-blistersteel", 16);
        var carbons = new[] { Stack("charcoal", 40), Stack("coke", 40) }.OfType<ItemStack>().ToList();
        var fuels = world.Collectibles.OfType<ItemCoal>().Where(item => item.Code is not null && item.CombustibleProps?.BurnDuration > 0)
            .OrderBy(item => item.Code.ToString(), StringComparer.Ordinal).Select(item => new ItemStack(item, 160)).ToList();
        if (iron is null || steel is null || carbons.Count == 0 || fuels.Count == 0) return null;
        double hoursPerDay = world.Calendar?.HoursPerDay ?? 24;
        string duration = (160 / Math.Max(1, hoursPerDay)).ToString("0.##") + BrowserText.Choose(" игровых дней · 160 часов нагрева", " in-game days · 160 hours of heating");
        return new RecipeDisplayEntry("Cementation", duration, new() { iron, carbons[0], fuels[0] }, steel,
            InputAlternatives: new() { new() { iron.Clone() }, carbons, fuels });
    }

    private static IEnumerable<RecipeDisplayEntry> SplitCastingEntry(ICoreClientAPI api, RecipeDisplayEntry entry)
    {
        if (IsCastIngot(entry.Output) && entry.Output.StackSize > 1)
        {
            entry = SingleIngotBatch(entry) ?? entry;
        }
        ItemStack? metal = entry.Output.Collectible.Code.Path.StartsWith("ingot-") ? entry.Output.Clone() : null;
        if (metal is null)
            foreach (var input in entry.Inputs.Where(stack => stack is not null))
            {
                var heat = input!.Collectible.GetCombustibleProperties(api.World, input, null);
                if (heat?.SmeltedStack?.ResolvedItemstack is { } smelted && smelted.Collectible.Code.Path.StartsWith("ingot-"))
                { metal = smelted.Clone(); break; }
            }
        var block = api.World.GetBlock(new AssetLocation("game:crucible-blue-smelted")) as BlockSmeltedContainer;
        if (metal is null || block is null) return new[] { entry };
        int units = Math.Max(1, entry.Output.StackSize) * 100;
        Match amount = Regex.Match(entry.Method, @"(\d+) units");
        if (amount.Success) units = int.Parse(amount.Groups[1].Value);
        metal.StackSize = 1;
        var molten = new ItemStack(block);
        block.SetContents(molten, metal, units);
        molten.Collectible.SetTemperature(api.World, molten, (entry.Temperature ?? 1000) + 50);
        var mold = entry.Inputs.FirstOrDefault(stack => stack?.Collectible.Code.Path.Contains("mold") == true);
        var smeltingInputs = entry.Inputs.Where(stack => stack?.Collectible.Code.Path.Contains("mold") != true).ToList();
        var casting = new RecipeDisplayEntry("Casting", $"{units} units", new List<ItemStack?> { molten.Clone(), mold?.Clone() }, entry.Output.Clone(), entry.Temperature);
        // Tool molds consume larger amounts, but are not separate bulk smelting
        // recipes. Their real 200/900-unit costs remain on the casting route.
        return IsCastIngot(entry.Output)
            ? new[] { new RecipeDisplayEntry("Melting", $"{units} units", smeltingInputs, molten, entry.Temperature), casting }
            : new[] { casting };
    }

    private static RecipeDisplayEntry? SingleIngotBatch(RecipeDisplayEntry entry)
    {
        int count = entry.Output.StackSize;
        if (!IsCastIngot(entry.Output) || count <= 1) return entry;
        bool Vessel(ItemStack stack) => stack.Collectible.Code.Path.StartsWith("crucible-") || stack.Collectible.Code.Path.Contains("mold");
        if (entry.Inputs.OfType<ItemStack>().Any(stack => !Vessel(stack) && stack.StackSize % count != 0)) return null;
        var output = entry.Output.Clone(); output.StackSize = 1;
        var inputs = entry.Inputs.Select(stack => {
            var copy = stack?.Clone();
            if (copy is not null && !Vessel(copy)) copy.StackSize /= count;
            return copy;
        }).ToList();
        return entry with { Inputs = inputs, Output = output,
            Method = Regex.Replace(entry.Method, @"\d+ units", "100 units") };
    }

    private static IEnumerable<RecipeDisplayEntry> PitKilnEntries(ICoreClientAPI api, ItemStack input, ItemStack result)
    {
        var kiln = api.World.GetBlock(new AssetLocation("game:pitkiln"));
        var models = kiln?.Attributes?["modelConfigs"].AsObject<Dictionary<string, PitModel>>();
        var mats = kiln?.Attributes?["buildMats"].AsObject<Dictionary<string, PitMaterial[]>>();
        if (models is null || mats is null) yield break;
        var model = models.FirstOrDefault(pair => pair.Key != "*" && Regex.IsMatch(input.Collectible.Code.Path,
                "^" + Regex.Escape(pair.Key).Replace("\\*", ".*") + "$", RegexOptions.CultureInvariant)).Value
            ?? models.GetValueOrDefault("*");
        if (model is null || !mats.TryGetValue("fuel", out var fuels)) yield break;
        ItemStack? Resolve(PitMaterial material, int stages)
        {
            var json = new JsonItemStack { Code = new AssetLocation(input.Collectible.Code.Domain, material.Code),
                Type = material.Type == "block" ? EnumItemClass.Block : EnumItemClass.Item,
                StackSize = material.Quantity * stages };
            json.Resolve(api.World, "XRB pit kiln material");
            return json.ResolvedItemstack?.Clone();
        }
        int Count(string code) => model.BuildMatCodes.Count(value => value == code);
        var grass = mats.GetValueOrDefault("grass")?.FirstOrDefault();
        var sticks = mats.GetValueOrDefault("sticks")?.FirstOrDefault();
        if (grass is null || sticks is null) yield break;
        foreach (var fuel in fuels)
        {
            var fuelStack = Resolve(fuel, Count("fuel"));
            if (fuelStack is null) continue;
            yield return new RecipeDisplayEntry("Pit firing", fuel.BurnTimeHours + BrowserText.Choose(" ч · Поджечь", " h · Ignite"),
                new List<ItemStack?> { input.Clone(), Resolve(grass, Count("grass")), Resolve(sticks, Count("sticks")), fuelStack }, result.Clone());
        }
    }

    private sealed class PitModel { public string[] BuildMatCodes { get; set; } = Array.Empty<string>(); }
    private sealed class PitMaterial
    {
        public string Code { get; set; } = "";
        public string Type { get; set; } = "item";
        public int Quantity { get; set; } = 1;
        public double BurnTimeHours { get; set; }
    }
}
