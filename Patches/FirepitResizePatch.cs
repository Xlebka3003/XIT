using HarmonyLib;
using System.Reflection;

namespace VsRecipeBrowser;

[HarmonyPatch]
internal static class FirepitResizePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        typeof(Vintagestory.GameContent.GuiDialogBlockEntityFirepit), "SetupDialog");
    [HarmonyPostfix]
    private static void RefreshLinkedLayout()
    {
        // Recompose after inserting a pot/crucible, without reopening the firepit.
        LinkedInventoryController.Instance?.InvalidateLayout();
    }
}
