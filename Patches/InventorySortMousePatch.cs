using HarmonyLib;
using Vintagestory.API.Client;

namespace VsRecipeBrowser;

// Survival/server inventories can consume middle-click at the slot grid
// before the GUI hotkey handler runs. Intercept the mapped mouse button at
// the same point, without changing the vanilla/server sorting operations.
[HarmonyPatch(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.OnMouseDown),
    new[] { typeof(ICoreClientAPI), typeof(MouseEvent) })]
internal static class InventorySortMousePatch
{
    [HarmonyPrefix]
    private static bool Prefix(MouseEvent __1)
    {
        if (InventorySortButtons.Instance?.HandleSlotMouseDown(__1) != true) return true;
        __1.Handled = true;
        return false;
    }
}
