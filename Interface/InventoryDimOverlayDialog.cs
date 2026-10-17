using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VsRecipeBrowser;

internal sealed class InventoryDimOverlayDialog : GuiDialog
{
    private int dimPercent;

    public InventoryDimOverlayDialog(ICoreClientAPI api) : base(api)
    {
        dimPercent = Math.Clamp(api.LoadModConfig<RecipeBrowserConfig>("vsrecipebrowser.json")?.DimPercent ?? 48, 0, 85);
        ComposeOverlay();
    }

    private void ComposeOverlay()
    {
        double scale = Math.Max(0.5f, RuntimeEnv.GUIScale);
        double width = capi.Render.FrameWidth / scale;
        double height = capi.Render.FrameHeight / scale;
        ElementBounds dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, width, height);
        ElementBounds overlayBounds = ElementBounds.Fixed(0, 0, width, height);

        SingleComposer = capi.Gui.CreateCompo("vsrecipebrowser-screen-dimmer", dialogBounds)
            .AddStaticCustomDraw(overlayBounds, (context, _, bounds) =>
            {
                context.SetSourceRGBA(0, 0, 0, dimPercent / 100.0);
                context.Rectangle(0, 0, bounds.OuterWidth, bounds.OuterHeight);
                context.Fill();
            })
            .Compose();
    }

    public void SetOpacity(int percent)
    {
        dimPercent = Math.Clamp(percent, 0, 85);
        bool wasOpen = IsOpened();
        if (wasOpen) TryClose();
        SingleComposer?.Dispose();
        ComposeOverlay();
        if (wasOpen) TryOpen();
    }

    public override double DrawOrder => -0.1;
    public override string ToggleKeyCombinationCode => null!;
    public override bool PrefersUngrabbedMouse => true;
    public override bool CaptureAllInputs() => false;
    public override bool ShouldReceiveMouseEvents() => false;
}
