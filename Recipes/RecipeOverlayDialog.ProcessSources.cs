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
    // Processing adapters retain actual temperatures, quantities and byproducts.
    private static void AppendPressingProcess(ICoreClientAPI api, List<RecipeDisplayEntry> catalog, ItemStack input)
    {
        JsonObject? properties = input.Collectible.Attributes?["juiceableProperties"];
        if (properties is null || !properties.Exists) return;
        ItemStack? Resolve(string key)
        {
            string? code = properties[key]["code"].AsString(null);
            if (string.IsNullOrWhiteSpace(code)) return null;
            foreach (var variant in input.Collectible.Variant)
                code = code.Replace("{" + variant.Key + "}", variant.Value);
            var json = new JsonItemStack { Code = new AssetLocation(input.Collectible.Code.Domain, code),
                Type = properties[key]["type"].AsString("item") == "block" ? EnumItemClass.Block : EnumItemClass.Item,
                StackSize = properties[key]["stacksize"].AsInt(1) };
            json.Resolve(api.World, "XRB juice press");
            return json.ResolvedItemstack?.Clone();
        }
        double litres = properties["litresPerItem"].AsDouble(0);
        ItemStack? liquid = Resolve("liquidStack");
        ItemStack? mash = Resolve("pressedStack");
        ItemStack? returned = Resolve("returnStack");
        if (litres <= 0 || liquid is null) return; // spent mash is not fresh fruit
        ItemStack source = input.Clone();
        source.StackSize = 1;
        double itemsPerLitre = liquid.Collectible.Attributes?["waterTightContainerProps"]["itemsPerLitre"].AsDouble(100) ?? 100;
        liquid.StackSize = Math.Max(1, (int)Math.Round(litres * itemsPerLitre));
        string label = $"{litres:0.####} L";
        catalog.Add(new RecipeDisplayEntry("Pressing", label, new List<ItemStack?> { source }, liquid,
            SecondaryOutput: mash));
        // The return stack belongs to the pressed residue, not to each raw
        // ingredient. Show this separate step instead of inventing wax ratios.
        if (returned is not null && mash is not null)
            catalog.Add(new RecipeDisplayEntry("Pressing", BrowserText.Choose("После отжима: остаток", "After pressing: residue"),
                new List<ItemStack?> { mash.Clone() }, returned));
    }

    private static bool AppendBakingProcess(ICoreClientAPI api, List<RecipeDisplayEntry> catalog, ItemStack input)
    {
        JsonObject? baking = input.Collectible?.Attributes?["bakingProperties"];
        if (baking is null || !baking.Exists) return false;
        string? resultCode = baking["resultCode"].AsString(null);
        if (string.IsNullOrWhiteSpace(resultCode)) return true;

        foreach ((string key, string value) in input.Collectible!.Variant)
            resultCode = resultCode.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
        AssetLocation location = new(input.Collectible.Code.Domain, resultCode);
        CollectibleObject? result = api.World.GetItem(location) ?? (CollectibleObject?)api.World.GetBlock(location);
        if (result?.Code is null) return true;

        ItemStack source = input.Clone();
        source.StackSize = 1;
        var inputs = new List<ItemStack?> { source };
        ItemStack? oven = FindClayOven(api);
        if (oven is not null) inputs.Add(oven);
        // bakingProperties.temp is the oven's browning threshold, not the
        // baking temperature advertised by the item/handbook (e.g. 160 vs 200).
        var heat = input.Collectible.GetCombustibleProperties(api.World, input, null);
        int temperature = heat is { SmeltingType: EnumSmeltType.Bake, MeltingPoint: > 0 }
            ? heat.MeltingPoint : baking["temp"].AsInt(0);
        catalog.Add(new RecipeDisplayEntry(
            "Baking", "Clay oven", inputs, new ItemStack(result), temperature));
        return true;
    }

    private static ItemStack? FindClayOven(ICoreClientAPI api)
    {
        CollectibleObject? oven = api.World.Collectibles.FirstOrDefault(value =>
            value?.Code?.Path.Equals("clayoven-north", StringComparison.OrdinalIgnoreCase) == true);
        oven ??= api.World.Collectibles.FirstOrDefault(value =>
            value?.Code?.Path.StartsWith("clayoven-", StringComparison.OrdinalIgnoreCase) == true);
        return oven is null ? null : new ItemStack(oven);
    }

    private static void AppendTransitionProcesses(
        List<RecipeDisplayEntry> catalog,
        ItemStack inputVariant,
        TransitionableProperties[]? transitions)
    {
        if (transitions is null) return;
        foreach (TransitionableProperties transition in transitions)
        {
            // Spoilage is a status consequence rather than a player recipe,
            // and None carries no process information.
            if (transition.Type is EnumTransitionType.Perish or EnumTransitionType.None) continue;
            ItemStack? output = transition.TransitionedStack?.ResolvedItemstack?.Clone();
            if (output?.Collectible?.Code is null) continue;

            string category = transition.Type switch
            {
                EnumTransitionType.Dry => "Drying",
                EnumTransitionType.Cure => "Curing",
                EnumTransitionType.Ripen => "Ripening",
                EnumTransitionType.Harden => "Hardening",
                EnumTransitionType.Burn => "Burning",
                EnumTransitionType.Melt => "Melting",
                EnumTransitionType.Convert => "Conversion",
                _ => "Processing"
            };
            ItemStack input = inputVariant.Clone();
            input.StackSize = 1;
            output.StackSize = Math.Max(1, output.StackSize);
            bool oiledHide = transition.Type == EnumTransitionType.Cure && input.Collectible.Code.Path.StartsWith("hide-oiled-");
            double hours = (transition.FreshHours?.avg ?? 0) + (transition.TransitionHours?.avg ?? 0);
            catalog.Add(new RecipeDisplayEntry(
                oiledHide ? "Drying" : category,
                oiledHide ? hours.ToString("0.##") + BrowserText.Choose(" ч", " h") : transition.Type.ToString(),
                new List<ItemStack?> { input },
                output));
        }
    }

    private static void AppendToolMoldCastingRecipes(
        ICoreClientAPI api,
        List<RecipeDisplayEntry> catalog,
        List<CollectibleObject> toolMolds,
        ItemStack baseInput,
        ItemStack ingotOutput,
        CombustibleProperties heat,
        ItemStack crucible)
    {
        string ingotPath = ingotOutput.Collectible.Code.Path;
        if (!ingotPath.StartsWith("ingot-", StringComparison.OrdinalIgnoreCase)) return;
        string metal = ingotPath["ingot-".Length..];

        foreach (CollectibleObject moldCollectible in toolMolds)
        {
            string moldPath = moldCollectible.Code.Path;
            const string moldPrefix = "toolmold-blue-fired-";
            string toolType = moldPath[moldPrefix.Length..];
            int requiredUnits = toolType switch
            {
                "anvil" => 900,
                "helvehammer" => 200,
                _ => 100
            };
            string outputPath = toolType switch
            {
                "anvil" => $"anvil-{metal}",
                "helvehammer" => $"helvehammerhead-{metal}",
                "lamellae" => $"metallamellae-{metal}",
                "blade-falx" => $"bladehead-falx-{metal}",
                _ => $"{toolType}head-{metal}"
            };

            CollectibleObject? outputCollectible = api.World.GetItem(new AssetLocation(ingotOutput.Collectible.Code.Domain, outputPath));
            outputCollectible ??= api.World.GetBlock(new AssetLocation(ingotOutput.Collectible.Code.Domain, outputPath));
            if (outputCollectible?.Code is null) continue;

            ItemStack castingInput = baseInput.Clone();
            castingInput.StackSize = Math.Max(1, (int)Math.Ceiling(heat.SmeltedRatio * requiredUnits / 100d));
            catalog.Add(new RecipeDisplayEntry(
                "Casting",
                $"Crucible → tool mold · {requiredUnits} units",
                new List<ItemStack?> { castingInput, crucible.Clone(), new ItemStack(moldCollectible) },
                new ItemStack(outputCollectible), heat.MeltingPoint));
        }
    }

    private static int CastingFeedstockRank(ItemStack candidate, ItemStack ingot)
    {
        string path = candidate.Collectible?.Code?.Path ?? "";
        string metal = ingot.Collectible?.Code?.Path.StartsWith("ingot-", StringComparison.OrdinalIgnoreCase) == true
            ? ingot.Collectible.Code.Path["ingot-".Length..]
            : "";
        if (path.Equals($"nugget-native{metal}", StringComparison.OrdinalIgnoreCase)) return 0;
        if (path.StartsWith("nugget-", StringComparison.OrdinalIgnoreCase)) return 1;
        if (path.Equals($"metalbit-{metal}", StringComparison.OrdinalIgnoreCase)) return 2;
        if (path.StartsWith("metalbit-", StringComparison.OrdinalIgnoreCase)) return 3;
        if (path.StartsWith("crushed-", StringComparison.OrdinalIgnoreCase)) return 4;
        if (path.StartsWith("ingot-", StringComparison.OrdinalIgnoreCase)) return 100;
        return 20;
    }

    private static ItemStack? FindPreferredCastingFeedstock(ICoreClientAPI api, ItemStack ingot, int nuggetQuantity)
    {
        string ingotKey = RecipeStackKey(ingot) ?? ingot.Collectible?.Code?.ToString() ?? "";
        if (preferredCastingFeedstocks.TryGetValue(ingotKey, out ItemStack? cached))
        {
            ItemStack result = cached.Clone();
            result.StackSize = Math.Max(1, nuggetQuantity);
            return result;
        }
        ItemStack? best = null;
        int bestRank = int.MaxValue;
        foreach (CollectibleObject collectible in api.World.Collectibles.Where(value => value?.Code is not null))
        {
            ItemStack candidate = new(collectible);
            CombustibleProperties? heat;
            try { heat = collectible.GetCombustibleProperties(api.World, candidate, null); }
            catch { continue; }
            ItemStack? melted = heat?.SmeltedStack?.ResolvedItemstack;
            if (melted is null || !heat!.RequiresContainer || !SameCollectible(melted, ingot)) continue;
            int rank = CastingFeedstockRank(candidate, ingot);
            if (rank >= bestRank) continue;
            bestRank = rank;
            best = candidate;
        }
        if (best is not null)
        {
            preferredCastingFeedstocks[ingotKey] = best.Clone();
            best.StackSize = Math.Max(1, nuggetQuantity);
        }
        return best;
    }

    private static ItemStack? FindCrucible(ICoreClientAPI api)
    {
        CollectibleObject? crucible = api.World.Collectibles.FirstOrDefault(value =>
            value?.Code?.Path.Equals("crucible-blue-fired", StringComparison.OrdinalIgnoreCase) == true);
        crucible ??= api.World.Collectibles.FirstOrDefault(value =>
            value?.Code?.Path.StartsWith("crucible-", StringComparison.OrdinalIgnoreCase) == true
            && value.Code.Path.Contains("-fired", StringComparison.OrdinalIgnoreCase));
        return crucible is null ? null : new ItemStack(crucible);
    }

    private static int? GetMeltingPoint(ICoreClientAPI api, ItemStack stack)
    {
        try { return stack.Collectible?.GetCombustibleProperties(api.World, stack, null)?.MeltingPoint; }
        catch { return null; }
    }

    private static bool IsCastIngot(ItemStack output) =>
        output.Collectible?.Code?.Path.StartsWith("ingot-", StringComparison.OrdinalIgnoreCase) == true;

    private static ItemStack? FindIngotMold(ICoreClientAPI api)
    {
        CollectibleObject? preferred = api.World.Collectibles.FirstOrDefault(value =>
            value?.Code?.Path.Equals("ingotmold-blue-fired", StringComparison.OrdinalIgnoreCase) == true);
        preferred ??= api.World.Collectibles.FirstOrDefault(value =>
            value?.Code?.Path.StartsWith("ingotmold-", StringComparison.OrdinalIgnoreCase) == true
            && value.Code.Path.Contains("-fired", StringComparison.OrdinalIgnoreCase));
        return preferred is null ? null : new ItemStack(preferred);
    }

    private static List<ItemStack?> BuildFirepitInputs(ItemStack input, ItemStack? fuel)
    {
        var slots = Enumerable.Repeat<ItemStack?>(null, 9).ToList();
        slots[1] = input.Clone();
        slots[4] = fuel?.Clone();
        return slots;
    }

    private static ItemStack? FindFirepitFuel(ICoreClientAPI api, int requiredTemperature)
    {
        string[] preferredCodes = { "firewood", "charcoal", "coal-bituminous", "coal-anthracite" };
        foreach (string path in preferredCodes)
        {
            CollectibleObject? collectible = api.World.Collectibles.FirstOrDefault(value =>
                value?.Code?.Path.Equals(path, StringComparison.OrdinalIgnoreCase) == true);
            if (collectible?.CombustibleProps is { } fuel && fuel.BurnTemperature >= requiredTemperature)
                return new ItemStack(collectible);
        }

        CollectibleObject? fallback = api.World.Collectibles.FirstOrDefault(value =>
            value?.Code is not null
            && value.CombustibleProps is { BurnDuration: > 0 } fuel
            && fuel.BurnTemperature >= requiredTemperature);
        return fallback is null ? null : new ItemStack(fallback);
    }

    private static List<ItemStack> FindFirepitFuels(ICoreClientAPI api, int requiredTemperature)
    {
        string[] preferred = { "firewood", "stick", "peatbrick", "charcoal", "coal-lignite", "coal-bituminous", "coal-anthracite" };
        return api.World.Collectibles
            .Where(value => value?.Code is not null
                && value.CombustibleProps is { BurnDuration: > 0 } fuel
                && fuel.BurnTemperature > 0 && fuel.BurnTemperature >= requiredTemperature)
            .GroupBy(value => value.Code)
            .Select(group => group.First())
            .OrderBy(value =>
            {
                int index = Array.FindIndex(preferred, path => value.Code.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(value => value.Code.ToString(), StringComparer.Ordinal)
            .Select(value => new ItemStack(value))
            .ToList();
    }
}
