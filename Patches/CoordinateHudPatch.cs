using HarmonyLib;
using Vintagestory.API.Client;
using System.Reflection;

namespace VsRecipeBrowser;

[HarmonyPatch]
internal static class CoordinateHudPatch
{
    internal static ICoreClientAPI? Api;
    private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Vintagestory.Client.NoObf.HudElementCoordinates"), "Every250ms");
    [HarmonyPostfix]
    private static void FixPosition(GuiDialog __instance)
    {
        if (Api is not { } api || !__instance.IsOpened() || __instance.SingleComposer is null) return;
        var own = __instance.SingleComposer.Bounds;
        var excluded = api.OpenedGuis.OfType<GuiDialog>().Where(dialog => dialog.GetType().Namespace == "VsRecipeBrowser")
            .SelectMany(dialog => dialog.Composers.Values).Select(composer => composer.Bounds).ToHashSet();
        var anchor = api.Gui.GetDialogBoundsInArea(EnumDialogArea.RightTop).FirstOrDefault(bounds => bounds != own && !excluded.Contains(bounds));
        own.absOffsetX = -26 * Vintagestory.API.Config.RuntimeEnv.GUIScale;
        own.absOffsetY = GuiStyle.DialogToScreenPadding + 120 * Vintagestory.API.Config.RuntimeEnv.GUIScale
            + (anchor is null ? 0 : anchor.absY + anchor.OuterHeight);
    }
}
