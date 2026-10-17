using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace VsRecipeBrowser;

internal static class DirectionalCatalog
{
    private static IWorldAccessor? cachedWorld;
    private static readonly Dictionary<string, CollectibleObject> representatives = new(StringComparer.Ordinal);

    internal static string Key(CollectibleObject collectible)
    {
        string path = collectible.Code.Path;
        if (collectible is not Block) return collectible.Code.ToString();
        bool fence = collectible.Attributes?["isFence"].AsBool(false) == true;
        bool fullCoating = collectible.GetType().Name == "BlockFullCoating";
        bool coating = collectible.Attributes?["attachas3d"].AsBool(false) == true;
        foreach (var variant in collectible.Variant)
        {
            if (variant.Key == "cover" && variant.Value is "free" or "snow" or "snowed")
            {
                path = Regex.Replace(path, "(^|-)" + Regex.Escape(variant.Value) + "(?=-|$)", "$1{direction}");
                continue;
            }
            if ((fence && variant.Key is "type" or "cover" or "state") || fullCoating)
            {
                path = Regex.Replace(path, "(^|-)" + Regex.Escape(variant.Value) + "(?=-|$)", "$1{direction}");
                continue;
            }
            if (variant.Key is not ("side" or "orientation" or "horizontalorientation" or "facing" or "position" or "rot" or "rotation")) continue;
            bool horizontal = variant.Value is "north" or "east" or "south" or "west" or "up" or "down"
                || Regex.IsMatch(variant.Value, "^[neswud]{1,6}$", RegexOptions.CultureInvariant);
            if (!horizontal && !(coating && variant.Key == "side" && variant.Value is "up" or "down")) continue;
            path = Regex.Replace(path, "(^|-)" + Regex.Escape(variant.Value) + "(?=-|$)", "$1{direction}");
        }
        return collectible.Code.Domain + ":" + path;
    }

    internal static ItemStack Normalize(IWorldAccessor world, ItemStack stack)
    {
        if (!ReferenceEquals(world, cachedWorld))
        {
            cachedWorld = world;
            representatives.Clear();
            var outputs = world.GridRecipes.Select(recipe => recipe.Output?.ResolvedItemStack?.Collectible?.Code?.ToString())
                .Where(code => code is not null).ToHashSet(StringComparer.Ordinal);
            foreach (var collectible in world.Collectibles.Where(value => value?.Code is not null)
                .OrderByDescending(value => outputs.Contains(value.Code.ToString()))
                .ThenByDescending(value => value.CreativeInventoryStacks is { Length: > 0 })
                .ThenBy(value => value.Code.ToString(), StringComparer.Ordinal))
                representatives.TryAdd(Key(collectible), collectible);
        }
        string key = Key(stack.Collectible);
        if (!key.Contains("{direction}") || !representatives.TryGetValue(key, out var representative)
            || representative.Code == stack.Collectible.Code) return stack;
        var normalized = new ItemStack(representative, stack.StackSize);
        normalized.Attributes = stack.Attributes.Clone();
        return normalized;
    }
}
