using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace XrbRecipeChoice;

internal static class ChoiceSelection
{
    internal static string OutputKey(ItemStack stack) => $"{stack.Class}:{stack.Collectible.Code}:{stack.StackSize}:{StableAttributes.Key(stack.Attributes)}";
    private sealed record Selection(GridRecipe Recipe, string Layout);
    // Selections belong to individual grids and must not keep closed inventories alive.
    private static readonly ConditionalWeakTable<InventoryCraftingGrid, Selection> selected = new();
    private static readonly MethodInfo found = AccessTools.Method(typeof(InventoryCraftingGrid), "FoundMatch");
    internal static readonly MethodInfo Refresh = AccessTools.Method(typeof(InventoryCraftingGrid), "FindMatchingRecipe");
    internal static string RecipeKey(GridRecipe recipe)
    {
        var definition = JToken.FromObject(new
        {
            recipe.Width, recipe.Height, recipe.Shapeless, recipe.RequiresTrait,
            Inputs = recipe.ResolvedIngredients?.Select(ingredient => ingredient is null ? null : new
            {
                ingredient.Type, Code = ingredient.Code?.ToString(), ingredient.Quantity,
                ingredient.IsTool, ingredient.ToolDurabilityCost, Tags = TagKey(ingredient),
                Attributes = ingredient.ResolvedItemStack?.Attributes.ToJsonToken()
            }).ToArray(),
            Output = recipe.Output?.Code?.ToString(), Quantity = recipe.Output?.Quantity,
            OutputAttributes = recipe.Output?.ResolvedItemStack?.Attributes.ToJsonToken()
        });
        string canonical = Sort(definition).ToString(Newtonsoft.Json.Formatting.None);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    private static string TagKey(CraftingRecipeIngredient ingredient)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        ingredient.Tags.ToBytes(writer);
        writer.Flush();
        return Convert.ToBase64String(stream.ToArray());
    }
    internal static ItemSlot[] Slots(IInventory grid) => Enumerable.Range(0, 9).Select(i => grid[i]!).ToArray();
    internal static string Signature(IInventory grid, bool quantities = true) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", Slots(grid).Select(slot =>
            slot.Empty ? "-" : $"{slot.Itemstack.Class}:{slot.Itemstack.Collectible.Code}:{Attributes(slot.Itemstack, quantities)}:{(quantities ? slot.StackSize : 0)}")))));

    private static string Attributes(ItemStack stack, bool exact)
    {
        var attributes = stack.Attributes.Clone();
        if (!exact)
        {
            foreach (string key in Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes) attributes.RemoveAttribute(key);
            attributes.RemoveAttribute("durability");
        }
        return StableAttributes.Key(attributes);
    }
    private static JToken Sort(JToken token) => token switch
    {
        JObject obj => new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Sort(p.Value)))),
        JArray array => new JArray(array.Select(Sort)),
        _ => token.DeepClone()
    };

    internal static void Select(InventoryCraftingGrid grid, GridRecipe recipe)
    {
        selected.Remove(grid);
        selected.Add(grid, new Selection(recipe, Signature(grid, false)));
        Refresh.Invoke(grid, null);
    }
    internal static bool Apply(InventoryCraftingGrid grid)
    {
        if (!selected.TryGetValue(grid, out var state)) return false;
        if (state.Layout != Signature(grid, false) || !state.Recipe.Enabled
            || !state.Recipe.Matches(grid.Player, grid.Api.World, Slots(grid), 3))
        { selected.Remove(grid); return false; }
        // Vanilla generates the output and tracks the exact recipe to consume.
        found.Invoke(grid, new object[] { state.Recipe });
        return true;
    }
}

[HarmonyPatch]
internal static class SelectedRecipePatch
{
    private static MethodBase TargetMethod() => ChoiceSelection.Refresh;
    [HarmonyPrefix]
    private static bool Prefix(InventoryCraftingGrid __instance) => !ChoiceSelection.Apply(__instance);
}
