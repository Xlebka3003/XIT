using HarmonyLib;
using Vintagestory.API.Client;
using System.Reflection;

namespace VsRecipeBrowser;

[HarmonyPatch]
internal static class CraftingRemainderPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GuiDialog), nameof(GuiDialog.TryClose));
        Type? inventory = AccessTools.TypeByName("Vintagestory.Client.NoObf.GuiDialogInventory");
        MethodInfo? close = inventory is null ? null : AccessTools.DeclaredMethod(inventory, "TryClose");
        if (close is not null) yield return close;
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void BeforeClose(GuiDialog __instance)
    {
        if (__instance.IsOpened() && __instance.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory")
            RecipeTransferSystem.ReturnCraftingRemainders();
    }
}
