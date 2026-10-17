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
    // Material families affect presentation only; native recipes remain authoritative.
    private static List<List<ItemStack>> BuildGridIngredientAlternatives(ICoreClientAPI api, GridRecipe grid, bool variantsOnly = false)
    {
        var alternatives = Enumerable.Range(0, 9).Select(_ => new List<ItemStack>()).ToList();
        if (grid.ResolvedIngredients is not { Length: > 0 }) return alternatives;
        int width = Math.Clamp(grid.Width, 1, 3);
        int height = Math.Clamp(grid.Height, 1, 3);
        int offsetX = (3 - width) / 2;
        int offsetY = (3 - height) / 2;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            CraftingRecipeIngredient? ingredient = grid.GetElementInGrid(y, x, grid.ResolvedIngredients, width);
            if (ingredient is null) continue;
            List<ItemStack> matches = !variantsOnly || IsInterchangeableIngredient(ingredient)
                ? FindIngredientAlternatives(api, ingredient)
                : ResolveIngredientDisplayStack(api, ingredient) is { } resolved
                    ? new List<ItemStack> { resolved }
                    : new List<ItemStack>();
            alternatives[(y + offsetY) * 3 + x + offsetX] = matches;
        }
        return alternatives;
    }

    private static readonly string[] InterchangeableToolPrefixes =
    {
        "hammer-", "chisel-", "saw-", "knife-", "axe-", "pickaxe-", "prospectingpick-",
        "shovel-", "shears-", "hoe-", "scythe-"
    };

    private static bool HasInterchangeableToolIngredient(GridRecipe grid) =>
        grid.ResolvedIngredients?.Any(ingredient => ingredient is not null && IsInterchangeableToolIngredient(ingredient)) == true;

    private static bool HasInterchangeableIngredient(GridRecipe grid) =>
        grid.ResolvedIngredients?.Any(ingredient => ingredient is not null && IsInterchangeableIngredient(ingredient)) == true;

    private static bool IsInterchangeableIngredient(CraftingRecipeIngredient ingredient)
    {
        string path = ingredient.Code?.Path ?? "";
        return path.Contains('*') || path.Contains('{') || !ingredient.Tags.IsEmpty || IsInterchangeableToolIngredient(ingredient);
    }

    private static bool IsInterchangeableToolIngredient(CraftingRecipeIngredient ingredient)
    {
        string path = ingredient.Code?.Path ?? "";
        if (path.Contains('*') && InterchangeableToolPrefixes.Any(path.StartsWith)) return true;
        // Tag-based recipes (for example clutch) expose an empty Code and use
        // IsTool + Tags to accept hammer/chisel/saw variants.
        return IsReusableToolIngredient(ingredient) && !ingredient.Tags.IsEmpty;
    }

    private static List<ItemStack> FindIngredientAlternatives(ICoreClientAPI api, IRecipeIngredientBase ingredient)
    {
        if (!ReferenceEquals(ingredientCacheWorld, api.World))
        {
            ingredientCacheWorld = api.World;
            ingredientDisplayCache.Clear();
            ingredientAlternativesCache.Clear();
        }
        if (ingredientAlternativesCache.TryGetValue(ingredient, out List<ItemStack>? cached))
            return cached.Select(stack => stack.Clone()).ToList();
        string groupKey = IngredientGroupKey(ingredient);
        if (ingredientGroupAlternativesCache.TryGetValue(groupKey, out List<ItemStack>? groupedCached))
        {
            ingredientAlternativesCache[ingredient] = groupedCached;
            return groupedCached.Select(stack => stack.Clone()).ToList();
        }

        var matches = new List<ItemStack>();
        foreach (CollectibleObject collectible in api.World.Collectibles.Where(value => value?.Code is not null))
        {
            if (collectible.ItemClass != ingredient.Type) continue;
            int quantity = ingredient is IRecipeIngredient sized ? Math.Max(1, sized.Quantity) : Math.Max(1, ingredient.ResolvedItemStack?.StackSize ?? 1);
            ItemStack candidate = PrepareDisplayIngredient(api.World, ingredient, new ItemStack(collectible, quantity));
            try
            {
                if (IngredientAcceptsDisplay(ingredient, candidate)) matches.Add(candidate);
            }
            catch { }
        }
        List<ItemStack> result = matches
                .GroupBy(stack => $"{stack.Class}:{stack.Collectible.Code}:{stack.StackSize}:{FavoriteIdentity.AttributesKey(stack.Attributes)}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        ingredientAlternativesCache[ingredient] = result.Select(stack => stack.Clone()).ToList();
        ingredientGroupAlternativesCache[groupKey] = result.Select(stack => stack.Clone()).ToList();
        return result;
    }

    private static string IngredientGroupKey(IRecipeIngredientBase ingredient)
    {
        string allowed = ingredient is CraftingRecipeIngredient crafting
            ? string.Join(',', crafting.AllowedVariants ?? Array.Empty<string>())
            : "";
        string skipped = ingredient is CraftingRecipeIngredient craftingSkipped
            ? string.Join(',', craftingSkipped.SkipVariants ?? Array.Empty<string>())
            : "";
        bool isTool = ingredient is CraftingRecipeIngredient tool && IsReusableToolIngredient(tool);
        int quantity = ingredient is IRecipeIngredient recipeIngredient ? recipeIngredient.Quantity : 1;
        string attributes = ingredient.ResolvedItemStack is { } resolved ? FavoriteIdentity.AttributesKey(resolved.Attributes) : "";
        using var tagBytes = new MemoryStream();
        using (var writer = new BinaryWriter(tagBytes, System.Text.Encoding.UTF8, true)) ingredient.Tags.ToBytes(writer);
        return $"{ingredient.Type}|{ingredient.Code}|{Convert.ToBase64String(tagBytes.ToArray())}|{allowed}|{skipped}|{isTool}|{quantity}|{attributes}|{(ingredient as CraftingRecipeIngredient)?.RecipeAttributes}";
    }

    private static bool IngredientAcceptsDisplay(IRecipeIngredientBase ingredient, ItemStack candidate)
        => IngredientRules.Accepts(ingredient, candidate);

    private static string? GetSynchronizedVariantGroupKey(RecipeDisplayEntry recipe)
    {
        if (recipe.Category == "Casting") return "casting:" + RecipeStackKey(recipe.Inputs.FirstOrDefault());
        if (recipe.Category == "Pressing")
            return recipe.Inputs[0]?.Collectible.Code.Path.Contains("honey") == true ? "honey-pressing" : "fruit-pressing";
        string outputPath = recipe.Output.Collectible?.Code?.Path ?? "";
        bool hasChisel = recipe.Inputs.Any(stack => stack?.Collectible.Code.Path.StartsWith("chisel-") == true);
        if (recipe.Category == "Crafting" && hasChisel && outputPath.StartsWith("metalbit-"))
            return "chisel-metal-recycling";
        if (recipe.Category == "Crafting" && hasChisel && recipe.Inputs.Any(stack => stack?.Collectible.Code.Path == "mortar"))
        {
            var parts = outputPath.Split('-');
            int colorIndex = parts[0] == "brickcourse" ? parts.Length - 1
                : parts[0] is "brickstairs" or "brickslabs" or "brickslab" ? 2 : -1;
            if (colorIndex > 0 && colorIndex < parts.Length)
            {
                parts[colorIndex] = "{brick-material}";
                return "brick-thickening:" + recipe.Output.Class + ":" + recipe.Output.Collectible.Code.Domain + ":" + string.Join('-', parts);
            }
        }
        if (recipe.Category == "Crafting" && outputPath.StartsWith("ingot-")
            && recipe.Inputs.Any(stack => stack?.Collectible.Code.Path.StartsWith("anvil-") == true))
            return "anvil-recycling-ingots";
        if (recipe.Category == "Crafting" && outputPath.StartsWith("anvil-") && ContainsVariant(outputPath, MetalVariants))
            return "anvil-metal-materials";
        bool hasHammer = recipe.Inputs.Any(stack =>
        {
            string path = stack?.Collectible?.Code?.Path ?? "";
            return path.StartsWith("hammer-", StringComparison.OrdinalIgnoreCase);
        });
        bool hasOre = recipe.Inputs.Any(stack =>
        {
            string path = stack?.Collectible?.Code?.Path ?? "";
            return path.StartsWith("ore-", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("crystalizedore-", StringComparison.OrdinalIgnoreCase);
        });
        if (hasHammer && hasOre && outputPath.StartsWith("nugget-", StringComparison.OrdinalIgnoreCase))
            return "ore-hammer-crushing";

        bool hasCrystal = recipe.Inputs.Any(stack =>
            stack?.Collectible?.Code?.Path.StartsWith("crystal-", StringComparison.OrdinalIgnoreCase) == true);
        if (hasHammer && hasCrystal && outputPath is "clearquartz" or "rosequartz" or "smokyquartz")
            return "crystal-smashing:quartz";
        if (hasHammer && hasCrystal && outputPath is "amethyst" or "ore-olivine")
            return $"crystal-smashing:{outputPath}";

        if (outputPath == "anchor-stone") return "stone-anchor";
        if (outputPath.StartsWith("diamond-stone-", StringComparison.OrdinalIgnoreCase)
            && !outputPath.EndsWith("-aged", StringComparison.OrdinalIgnoreCase))
            return "diamond-stone-tiles";
        if (outputPath.StartsWith("stonebrick-", StringComparison.OrdinalIgnoreCase)) return "stonebrick-materials";
        if (recipe.Category == "Barrel"
            && outputPath == "leather-normal-plain"
            && recipe.Inputs.Any(stack => stack?.Collectible?.Code?.Path.StartsWith("hide-prepared-", StringComparison.OrdinalIgnoreCase) == true))
            return "barrel-leather-hide-size";
        return null;
    }

    private static string? CustomOutputGroup(string? outputPath)
    {
        if (outputPath == "fat-rendered") return "rendered-fat-batches";
        if (IsStoneSnowball(outputPath)) return "stone-snowballs";
        if (CeramicOutputGroup(outputPath) is { } ceramic) return ceramic;
        if (outputPath?.StartsWith("clothes-arm-", StringComparison.Ordinal) == true)
        {
            string bracelet = outputPath["clothes-arm-".Length..];
            if (WoodVariants.Contains(bracelet) || bracelet is "bamboo" or "cattail" or "papyrus" or "tule"
                or "maplered" or "coral" or "cypress" or "feather" or "flint" or "{seed}" or "{misc}")
                return "bracelets";
        }
        if (outputPath?.StartsWith("clothes-", StringComparison.Ordinal) == true)
        {
            string normalized = NormalizeVariant(outputPath, VariantFamily.Color);
            if (normalized != outputPath) return "clothes:" + normalized;
        }
        if (outputPath?.StartsWith("book-normal-") == true) return "normal-books";
        if (outputPath?.StartsWith("taxidermy-fish-") == true) return "taxidermy-fish";
        if (outputPath?.StartsWith("hide-") == true)
        {
            string stage = Regex.Replace(outputPath, "-(small|medium|large|huge)$", "");
            return "hide-stage:" + stage;
        }
        if (outputPath?.StartsWith("daubraw-", StringComparison.OrdinalIgnoreCase) == true) return "raw-daub";
        if (outputPath is "table-whitemarble" or "table-redmarble" or "table-greenmarble") return "marble-table";
        if (outputPath is "clearquartz" or "rosequartz" or "smokyquartz") return "crystal-smashing:quartz";
        if (outputPath?.StartsWith("stonebrick-", StringComparison.OrdinalIgnoreCase) == true) return "stonebrick-materials";
        if (outputPath?.StartsWith("diamond-stone-", StringComparison.OrdinalIgnoreCase) == true
            && !outputPath.EndsWith("-aged", StringComparison.OrdinalIgnoreCase)) return "diamond-stone-tiles";
        return null;
    }

    private static string? CeramicOutputGroup(string? path)
    {
        if (path?.StartsWith("shingle-raw-", StringComparison.Ordinal) == true) return "ceramic-shingles-raw";
        if (path?.StartsWith("shingle-burned-", StringComparison.Ordinal) == true) return "ceramic-shingles-fired";
        if (path?.StartsWith("rawbrick-", StringComparison.Ordinal) == true
            || path?.StartsWith("refractorybrick-raw-", StringComparison.Ordinal) == true) return "ceramic-bricks-raw";
        if (path?.StartsWith("burnedbrick-", StringComparison.Ordinal) == true
            || path?.StartsWith("refractorybrick-fired-", StringComparison.Ordinal) == true) return "ceramic-bricks-fired";
        return null;
    }

    private static bool SameCeramicFamily(ItemStack? result, ItemStack target) => result is not null
        && result.Class == EnumItemClass.Item && target.Class == EnumItemClass.Item
        && result.Collectible.Code.Domain == target.Collectible.Code.Domain
        && CeramicOutputGroup(result.Collectible.Code.Path) is { } group
        && group == CeramicOutputGroup(target.Collectible.Code.Path);

    private static bool IsStoneSnowball(string? path) => path?.StartsWith("snowball-", StringComparison.Ordinal) == true
        && (StoneVariants.Contains(path[9..]) || path[9..] is "{stone}" or "*" or "flint" or "obsidian");
    private static bool SameStoneSnowballFamily(ItemStack? result, ItemStack target) => result is not null
        && result.Class == target.Class && result.Collectible.Code.Domain == target.Collectible.Code.Domain
        && IsStoneSnowball(result.Collectible.Code.Path) && IsStoneSnowball(target.Collectible.Code.Path);

    private static bool UsesLogOrLogSection(string outputPath)
    {
        return outputPath == "club-generic-wood"
            || outputPath.StartsWith("windmillrotor-wood-", StringComparison.OrdinalIgnoreCase)
            || outputPath.StartsWith("woodenaxle-", StringComparison.OrdinalIgnoreCase)
            || outputPath.StartsWith("angledgears-", StringComparison.OrdinalIgnoreCase)
            || outputPath.StartsWith("brake-", StringComparison.OrdinalIgnoreCase)
            || outputPath.StartsWith("clutch-", StringComparison.OrdinalIgnoreCase)
            || outputPath.StartsWith("pounder-", StringComparison.OrdinalIgnoreCase);
    }

    private static string CraftingLayoutIdentity(RecipeDisplayEntry recipe)
    {
        if (recipe.Category != "Crafting" || recipe.SourceGridRecipes?.FirstOrDefault() is not { } grid) return "";
        int width = Math.Clamp(grid.Width, 1, 3), height = Math.Clamp(grid.Height, 1, 3);
        var cells = new List<string>();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var ingredient = grid.ResolvedIngredients is null ? null : grid.GetElementInGrid(y, x, grid.ResolvedIngredients, width);
            cells.Add(ingredient is null ? "-" : $"{ingredient.Type}:{Math.Max(1, ingredient.Quantity)}:{IsReusableToolIngredient(ingredient)}:{(IsReusableToolIngredient(ingredient) ? ingredient.GetConsumeProperties().DurabilityCost : 0)}");
        }
        if (grid.Shapeless) cells.Sort(StringComparer.Ordinal);
        return $"|layout:{(grid.Shapeless ? 0 : width)}:{(grid.Shapeless ? 0 : height)}:{grid.Shapeless}:{grid.RequiresTrait}|" + string.Join(",", grid.Shapeless ? cells.Where(cell => cell != "-") : cells);
    }

    private static string VariantRecipeIdentityCore(RecipeDisplayEntry recipe)
    {
        if (recipe.Category == "Pit firing") return "Pit firing|" + RecipeStackKey(recipe.Inputs.FirstOrDefault()) + "|" + RecipeStackKey(recipe.Output);
        if (recipe.Category == "Barrel" && CustomOutputGroup(recipe.Output.Collectible.Code.Path) is { } barrelGroup
            && (barrelGroup == "taxidermy-fish" || barrelGroup.StartsWith("hide-stage:")))
            return "Barrel|" + barrelGroup;
        if (recipe.Category == "Crafting")
        {
            if (recipe.Output.Collectible.Code.Path.StartsWith("shield-", StringComparison.Ordinal))
                return "Crafting|" + RecipeStackKey(recipe.Output) + "|"
                    + recipe.Output.Attributes.GetString("deco", "none") + "|"
                    + recipe.SourceGridRecipes?.FirstOrDefault()?.RequiresTrait;
            string? custom = CustomOutputGroup(recipe.Output.Collectible.Code.Path);
            // Exact-result grouping retains attributes and output quantity in
            // each complete frame, never independently mixing slot variants.
            if (custom is "marble-table" or "raw-daub" or "normal-books" or "bracelets") return "Crafting|" + custom;
            if (custom?.StartsWith("clothes:", StringComparison.Ordinal) == true)
                return "Crafting|" + custom + "|" + recipe.SourceGridRecipes?.FirstOrDefault()?.RequiresTrait;
            if (recipe.Output.Collectible.Code.Path.Contains("slab") && recipe.SourceGridRecipes is { Count: > 0 } slabRecipes)
            {
                bool turning = slabRecipes.All(grid => grid.ResolvedIngredients?.Where(ingredient => ingredient is not null)
                    .All(ingredient => ingredient.ResolvedItemStack?.Collectible.Code.Path.Contains("slab") == true) == true);
                return "Crafting|slab|" + (turning ? "rotation|" : "manufacture|") + RecipeStackKey(recipe.Output) + "|" + FavoriteIdentity.AttributesKey(recipe.Output.Attributes);
            }
            VariantFamily outputFamily = GetVariantFamily(recipe.Output);
            if (outputFamily == VariantFamily.None)
                return $"Crafting|{RecipeStackKey(recipe.Output)}|{FavoriteIdentity.AttributesKey(recipe.Output.Attributes)}";
            return $"Crafting|{recipe.Output.Class}:{recipe.Output.Collectible.Code.Domain}:"
                + ExplicitOutputKey(recipe.Output.Collectible.Code.Path, outputFamily)
                + "|" + OutputShapeKey(recipe.Output);
        }
        if (recipe.Category == "Casting") return "Casting|" + RecipeStackKey(recipe.Inputs.FirstOrDefault());
        if (recipe.Category is "Melting" or "Casting") return RecipeIdentity(recipe);
        string? synchronizedGroup = GetSynchronizedVariantGroupKey(recipe);
        if (synchronizedGroup is not null)
            return $"{recipe.Category}|{recipe.Method}|{{{synchronizedGroup}}}";

        VariantFamily family = GetVariantFamily(recipe.Output);
        string outputPath = recipe.Output.Collectible?.Code?.Path ?? "-";
        string inputs = string.Join(',', recipe.Inputs.Select(stack => ExplicitIngredientKey(stack, outputPath, family)));
        string outputCode = ExplicitOutputKey(outputPath, family);
        string outputQuantity = outputPath == "firewood" ? "*" : recipe.Output.StackSize.ToString();
        return $"{recipe.Category}|{recipe.Method}|{inputs}|{recipe.Output.Class}:{recipe.Output.Collectible?.Code?.Domain}:{outputCode}:{outputQuantity}|{recipe.Temperature}";
    }

    private static string ExplicitOutputKey(string path, VariantFamily family)
    {
        if (path.StartsWith("fishingpole-simple-", StringComparison.OrdinalIgnoreCase)) return "fishingpole-simple-{material}";
        if (path.StartsWith("woodenfencegate-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("bamboogate-", StringComparison.OrdinalIgnoreCase)) return "simple-fencegate-{material}";
        return NormalizeVariant(path, family);
    }

    private static string ExplicitIngredientKey(ItemStack? stack, string outputPath, VariantFamily family)
    {
        if (stack?.Collectible?.Code is null) return "-";
        string path = stack.Collectible.Code.Path;
        bool weaving = path is "bamboostakes" or "cattailtops" or "papyrustops";
        if (outputPath == "firewood"
            && (path.StartsWith("log-", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("logsection-", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("flotsam-driftwood-", StringComparison.OrdinalIgnoreCase)))
            return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{firewood-source}}:1";
        if ((outputPath == "shield-crude" || outputPath.StartsWith("stationarybasket-", StringComparison.OrdinalIgnoreCase)) && weaving)
            return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{weaving}}:*";
        if (outputPath.StartsWith("bellows-crude-", StringComparison.OrdinalIgnoreCase) && path.StartsWith("hide-pelt-", StringComparison.OrdinalIgnoreCase))
            return $"{stack.Class}:{stack.Collectible.Code.Domain}:hide-pelt-*:1";
        if (outputPath.StartsWith("torch-basic-extinct-", StringComparison.OrdinalIgnoreCase) && (weaving || path == "drygrass"))
            return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{torch-fiber}}:*";
        if (UsesLogOrLogSection(outputPath)
            && (path.StartsWith("log-placed-", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("logsection-placed-", StringComparison.OrdinalIgnoreCase)))
            return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{wood-log-source}}:{stack.StackSize}";
        if ((outputPath.StartsWith("clutch-", StringComparison.OrdinalIgnoreCase)
                || outputPath.StartsWith("roughhewnfence-", StringComparison.OrdinalIgnoreCase)
                || outputPath.StartsWith("roughhewnfencegate-", StringComparison.OrdinalIgnoreCase))
            && (path.StartsWith("log-placed-", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("logsection-placed-", StringComparison.OrdinalIgnoreCase)))
            return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{rough-log}}:{stack.StackSize}";
        if (outputPath.StartsWith("fishingpole-simple-", StringComparison.OrdinalIgnoreCase))
        {
            if (path is "stick" or "bamboostakes") return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{rod}}:{stack.StackSize}";
            if (path is "rope" or "flaxtwine") return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{line}}:{stack.StackSize}";
        }
        if ((outputPath.StartsWith("woodenfencegate-", StringComparison.OrdinalIgnoreCase)
            || outputPath.StartsWith("bamboogate-", StringComparison.OrdinalIgnoreCase))
            && (path.StartsWith("plank-", StringComparison.OrdinalIgnoreCase) || path == "bamboostakes"))
            return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{gate-material}}:{stack.StackSize}";
        if (outputPath == "bowstave-long-raw" && path.StartsWith("log-placed-", StringComparison.OrdinalIgnoreCase))
            return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{longbow-wood}}:{stack.StackSize}";
        if (outputPath == "bow-long" && path is "fat-rendered" or "resin" or "beeswax")
            return $"{stack.Class}:{stack.Collectible.Code.Domain}:{{bow-coating}}:{stack.StackSize}";
        return InterchangeableIngredientKey(stack, family);
    }

    private static bool IsFirewoodSource(ItemStack? stack)
    {
        string path = stack?.Collectible?.Code?.Path ?? "";
        return path.StartsWith("log-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("logsection-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("flotsam-driftwood-", StringComparison.OrdinalIgnoreCase);
    }

    private static string OutputShapeKey(ItemStack stack)
    {
        var attributes = stack.Attributes.Clone();
        attributes.RemoveAttribute("material");
        attributes.RemoveAttribute("wood");
        return FavoriteIdentity.AttributesKey(attributes);
    }

    private static string InterchangeableIngredientKey(ItemStack? stack, VariantFamily family = VariantFamily.None)
    {
        if (stack?.Collectible?.Code is null) return "-";
        string path = stack.Collectible.Code.Path;
        string[] toolFamilies = { "saw", "knife", "hammer", "chisel", "axe", "pickaxe", "shovel", "shears", "hoe", "scythe", "prospectingpick" };
        string? toolFamily = toolFamilies.FirstOrDefault(name => path.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase));
        string code = toolFamily is not null
            ? $"{stack.Collectible.Code.Domain}:{toolFamily}-*"
            : $"{stack.Collectible.Code.Domain}:{NormalizeIngredientVariants(path, family)}";
        return $"{stack.Class}:{code}:{stack.StackSize}";
    }

    private static string NormalizeIngredientVariants(string path, VariantFamily preferredFamily)
    {
        if (preferredFamily != VariantFamily.None) path = NormalizeVariant(path, preferredFamily);
        path = NormalizeVariant(path, VariantFamily.Wood);
        path = NormalizeVariant(path, VariantFamily.Stone);
        path = NormalizeVariant(path, VariantFamily.Metal);
        path = NormalizeVariant(path, VariantFamily.Glass);
        path = NormalizeVariant(path, VariantFamily.Color);
        return path;
    }

    private static readonly HashSet<string> WoodVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "birch", "oak", "maple", "pine", "acacia", "kapok", "baldcypress",
        "larch", "redwood", "ebony", "walnut", "purpleheart", "aged", "veryaged"
    };

    private static readonly HashSet<string> StoneVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "andesite", "chalk", "chert", "conglomerate", "limestone", "claystone", "granite",
        "sandstone", "shale", "basalt", "peridotite", "phyllite", "slate", "bauxite",
        "suevite", "whitemarble", "greenmarble", "redmarble", "kimberlite"
    };

    private static readonly HashSet<string> MetalVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "copper", "tinbronze", "bismuthbronze", "blackbronze", "iron", "meteoriciron", "steel",
        "gold", "silver", "electrum", "brass", "bronze", "tin", "lead", "zinc"
    };

    private static readonly HashSet<string> ToolMaterialVariants = new(
        MetalVariants.Concat(StoneVariants).Concat(new[] { "flint", "obsidian" }),
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ColorVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "plain", "blue", "brown", "gray", "black", "green", "orange", "pink", "purple",
        "red", "white", "yellow", "redblack"
    };

    private static readonly HashSet<string> GlassVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        "quartz", "plain", "green", "blue", "violet", "red", "yellow", "brown",
        "vintage", "pink", "smoky"
    };

    private static readonly HashSet<string> GrainVariants = new(StringComparer.OrdinalIgnoreCase)
        { "amaranth", "rice", "rye", "wheat", "spelt", "einkorn", "flax", "cassava", "sunflower", "millet" };
    private enum VariantFamily { None, Wood, Stone, Metal, ToolMaterial, Color, Glass, Grain }

    private static bool IsCollapsibleWoodOutput(ItemStack? stack)
    {
        string path = stack?.Collectible?.Code?.Path ?? "";
        string[] families =
        {
            "door-solid-", "door-1x3gate-", "door-2x2gate-", "door-2x3gate-", "door-2x4gate-",
            "woodenfence-", "woodenfencegate-", "roughhewnfence-", "roughhewnfencegate-",
            "trapdoor-solid-", "trapdoor-window-", "plank-", "planks-", "plankslab-", "plankstairs-",
            "slantedroofing", "boatseat-", "ladder-wood-", "woodenpath-", "supportbeam-",
            "debarkedlog-", "logquad-", "glasspane-leaded-", "oar-crude-", "boat-raft-"
        };
        string[] attributedFamilies =
        {
            "cabinet-", "cabinetdoor-", "bookshelf", "crate", "scrollrack", "antlermount",
            "woodenaxle", "woodengear", "transmission", "clutch", "brake", "pulverizer",
            "sign-ground-", "signpost", "largegearsection-", "figurehead-"
        };
        return (families.Any(path.StartsWith) && path.Split('-').Any(WoodVariants.Contains))
            || attributedFamilies.Any(path.StartsWith);
    }

    private static VariantFamily GetVariantFamily(ItemStack? stack)
    {
        if (stack?.Collectible.Code.Path.StartsWith("shield-", StringComparison.Ordinal) == true) return VariantFamily.Wood;
        if (IsCollapsibleWoodOutput(stack)) return VariantFamily.Wood;
        string path = stack?.Collectible?.Code?.Path ?? "";
        if ((path.StartsWith("glassslab-", StringComparison.OrdinalIgnoreCase) || path.StartsWith("glass-", StringComparison.OrdinalIgnoreCase))
            && ContainsVariant(path, GlassVariants)) return VariantFamily.Glass;
        if ((path.StartsWith("bread-") || path.StartsWith("dough-") || path.StartsWith("flour-")) && ContainsVariant(path, GrainVariants)) return VariantFamily.Grain;
        string[] toolFamilies =
        {
            "axe-", "knife-generic-", "shovel-", "pickaxe-", "prospectingpick-", "saw-",
            "hammer-", "cleaver-", "hoe-", "scythe-", "spear-generic-", "blade-falx-",
            "arrow-", "helvehammer-"
        };
        if (toolFamilies.Any(path.StartsWith) && ContainsVariant(path, ToolMaterialVariants))
            return VariantFamily.ToolMaterial;
        string[] stoneFamilies = { "cobblestone-", "cobblestoneslab-", "cobblestonestairs-", "drystone-", "drystonefence-", "rockpolished-", "polishedrockslab-", "stonebrick-", "stonebricks-", "stonebrickslab-", "stonebrickstairs-", "stonecoffin", "quern-", "grindstone-" };
        if (stoneFamilies.Any(path.StartsWith) && ContainsVariant(path, StoneVariants)) return VariantFamily.Stone;
        string[] metalFamilies = { "pickaxe-", "prospectingpick-", "saw-", "hammer-", "cleaver-", "hoe-", "scythe-", "spear-generic-", "blade-falx-", "arrow-", "helvehammer-", "metalbit-", "metalblock-", "plaque-fancy-", "armor-head-", "armor-body-", "armor-legs-", "punchset-" };
        if (metalFamilies.Any(path.StartsWith) && ContainsVariant(path, MetalVariants)) return VariantFamily.Metal;
        string[] colorFamilies = { "chair-", "oillamp-", "brickcourse-", "brickstairs-", "brickslabs-", "tile-", "tileslab-", "armor-head-tailored-", "armor-body-tailored-", "armor-legs-tailored-", "clothes-", "linen-", "wool-", "carpet-", "dye-" };
        if (colorFamilies.Any(path.StartsWith) && ContainsVariant(path, ColorVariants, allowSuffix: true)) return VariantFamily.Color;
        return VariantFamily.None;
    }

    private static bool ContainsVariant(string path, HashSet<string> variants, bool allowSuffix = false)
    {
        if (path.Split('-').Any(variants.Contains)) return true;
        return allowSuffix && variants.Any(color => path.EndsWith(color, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeVariant(string? path, VariantFamily family)
    {
        if (string.IsNullOrEmpty(path)) return "-";
        HashSet<string>? variants = family switch
        {
            VariantFamily.Wood => WoodVariants,
            VariantFamily.Stone => StoneVariants,
            VariantFamily.Metal => MetalVariants,
            VariantFamily.ToolMaterial => ToolMaterialVariants,
            VariantFamily.Color => ColorVariants,
            VariantFamily.Glass => GlassVariants,
            VariantFamily.Grain => GrainVariants,
            _ => null
        };
        if (variants is null) return path;
        string[] parts = path.Split('-');
        for (int i = 0; i < parts.Length; i++)
            if (variants.Contains(parts[i])) parts[i] = $"{{{family.ToString().ToLowerInvariant()}}}";
        string normalized = string.Join('-', parts);
        if (family == VariantFamily.Color)
        {
            bool found = false;
            string? suffix;
            while ((suffix = ColorVariants.OrderByDescending(value => value.Length)
                .FirstOrDefault(color => normalized.EndsWith(color, StringComparison.OrdinalIgnoreCase))) is not null)
            {
                normalized = normalized[..^suffix.Length];
                found = true;
            }
            if (found) normalized += "{color}";
        }
        return normalized;
    }

    private static string DisplayStackIdentity(ItemStack? stack) => stack is null ? "-"
        : $"{RecipeStackKey(stack)}:{stack.StackSize}:{FavoriteIdentity.AttributesKey(stack.Attributes)}";

    private static string RecipeIdentity(RecipeDisplayEntry recipe)
    {
        var ingredients = recipe.Inputs.Select(DisplayStackIdentity);
        if (recipe.Category == "Crafting" && recipe.SourceGridRecipes?.FirstOrDefault()?.Shapeless == true)
            ingredients = ingredients.Where(identity => identity != "-").OrderBy(identity => identity, StringComparer.Ordinal);
        return $"{recipe.Category}|{recipe.Method}|{recipe.Temperature}|{string.Join(',', ingredients)}|{DisplayStackIdentity(recipe.Output)}|{DisplayStackIdentity(recipe.SecondaryOutput)}";
    }
}
