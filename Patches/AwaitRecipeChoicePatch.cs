using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace VsRecipeBrowser;

[HarmonyPatch(typeof(InventoryCraftingGrid), nameof(InventoryCraftingGrid.ActivateSlot))]
internal static class AwaitRecipeChoicePatch
{
    [HarmonyPrefix]
    private static bool Prefix(InventoryCraftingGrid __instance, int __0, ref object? __result)
    {
        if (__instance.Api.Side != EnumAppSide.Client || __0 != 9 || RecipeChoiceClient.Instance?.Pending != true) return true;
        __result = null;
        return false;
    }
}
