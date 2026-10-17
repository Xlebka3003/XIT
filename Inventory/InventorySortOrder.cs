using Vintagestory.API.Common;

namespace VsRecipeBrowser;

internal static class InventorySortOrder
{
    private static readonly HashSet<string> Materials = new(
        ("oak birch maple pine acacia kapok baldcypress larch redwood ebony walnut purpleheart aged veryaged " +
         "andesite chalk chert conglomerate limestone claystone granite sandstone shale basalt peridotite phyllite slate bauxite suevite whitemarble greenmarble redmarble travertine kimberlite " +
         "copper tinbronze bismuthbronze blackbronze iron meteoriciron steel gold silver electrum brass tin lead zinc flint obsidian " +
         "blue brown gray black green orange pink purple red white yellow plain").Split(' '), StringComparer.OrdinalIgnoreCase);

    internal static int Group(ItemStack stack)
    {
        string path = stack.Collectible.Code.Path.ToLowerInvariant();
        bool Has(params string[] names) => names.Any(path.Contains);
        // Specific types must precede the generic Block fallback.
        if (Has("ore-", "crystalizedore", "nugget-", "ingot-", "metalbit-", "gem-", "rock-", "stone-", "clay-", "plank-", "firewood", "fat-", "hide-", "leather", "linen", "flaxfiber", "flaxtwine", "nails", "metalchain")) return 6;
        if (stack.Collectible.Tool is not null || Has("armor-", "clothes-", "shield", "bow-", "spear-", "sword", "arrow-", "club-")) return 7;
        if (stack.Collectible.NutritionProps is not null || Has("flower-", "sapling-", "seed-", "mushroom-", "bush-", "grass-", "fern-", "reed-", "plant-", "fruit-", "vegetable-")) return 8;
        if (Has("fence", "palisade", "door", "trapdoor", "gate-")) return 3;
        if (Has("cabinet", "bookshelf", "chair", "table-", "bed-", "sign", "crate", "chest", "storagevessel", "basket", "carpet", "rug-", "painting", "torch", "lantern", "oillamp", "sculpture")) return 4;
        if (Has("gear", "axle", "clutch", "brake", "transmission", "rotor", "sail", "pulverizer", "quern", "grindstone", "anvil", "forge", "bloomery", "oven", "firepit", "fruitpress", "barrel")) return 5;
        if (Has("slab", "plate", "tile")) return 1;
        if (Has("stairs", "roof", "shingle")) return 2;
        if (Has("water", "lava", "liquid", "portion", "dye-", "slush")) return 9;
        return stack.Class == EnumItemClass.Block ? 0 : 9;
    }

    internal static string Family(ItemStack stack) =>
        stack.Collectible.Code.Domain + ":" + string.Join("-", stack.Collectible.Code.Path.Split('-')
            .Select(part => Materials.Contains(part) ? "{material}" : part));

    internal static string Material(ItemStack stack)
    {
        string attributed = stack.Attributes.GetString("material") ?? stack.Attributes.GetString("metal")
            ?? stack.Attributes.GetString("wood") ?? "";
        if (attributed.Length > 0) return attributed;
        return string.Join("-", stack.Collectible.Code.Path.Split('-').Where(Materials.Contains));
    }
}
