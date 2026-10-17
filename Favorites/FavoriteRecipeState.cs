using Vintagestory.API.Common;

namespace VsRecipeBrowser;

internal sealed class FavoriteRecipeState
{
    public string TargetData { get; set; } = "";
    public string DisplayData { get; set; } = "";
    public string Mode { get; set; } = "Recipes";
    public string Category { get; set; } = "";
    public string RecipeKey { get; set; } = "";
    public string FrameKey { get; set; } = "";
    public Dictionary<int, string> Ingredients { get; set; } = new();
    public string? Fuel { get; set; }
    public string? Anvil { get; set; }

    internal static string Encode(ItemStack stack) => Convert.ToBase64String(stack.ToBytes());
    internal static ItemStack? Decode(string? data, IWorldAccessor world)
    {
        if (string.IsNullOrEmpty(data)) return null;
        try
        {
            var stack = new ItemStack(Convert.FromBase64String(data));
            return stack.ResolveBlockOrItem(world) ? stack : null;
        }
        catch { return null; }
    }
}
