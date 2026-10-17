using HarmonyLib;
using Vintagestory.API.Client;

namespace VsRecipeBrowser;

internal static class AccidentalDropGuard
{
    internal static ICoreClientAPI? Api;
    internal static bool InsideInventoryWindow(int x, int y)
    {
        if (Api is null || !BrowserSettings.Current.PreventAccidentalDrops) return false;
        foreach (var dialog in Api.OpenedGuis.OfType<GuiDialog>().Where(dialog => dialog.IsOpened()))
        {
            var composer = dialog.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory"
                ? LinkedInventoryController.Instance?.GetPlayerInventoryComposer(dialog, out _)
                : dialog is GuiDialogBlockEntity ? dialog.SingleComposer : null;
            if (composer is null) continue;
            composer.Bounds.CalcWorldBounds();
            if (composer.Bounds.PointInside(x, y)) return true;
        }
        return false;
    }
    internal static void Install(Harmony harmony, Type managerType)
    {
        var method = AccessTools.Method(managerType, "DropMouseSlotItems", new[] { typeof(bool) });
        if (method is null) { Api?.Logger.Warning("[XRB] Mouse-slot drop protection target not found."); return; }
        method = AccessTools.DeclaredMethod(method.DeclaringType, method.Name, new[] { typeof(bool) });
        if (Harmony.GetPatchInfo(method)?.Owners.Contains(harmony.Id) == true) return;
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(AccidentalDropGuard), nameof(BeforeDrop)));
    }
    private static bool BeforeDrop(object __instance, ref bool __result)
    {
        if (Api?.World?.Player is null || !ReferenceEquals(__instance, Api.World.Player.InventoryManager)
            || !InsideInventoryWindow(Api.Input.MouseX, Api.Input.MouseY)) return true;
        __result = false;
        return false;
    }
}
