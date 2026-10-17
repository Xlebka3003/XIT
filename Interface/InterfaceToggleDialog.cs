using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VsRecipeBrowser;

internal sealed class InterfaceToggleDialog : GuiDialog
{
    private const double SlotPitch = 48.8;
    private readonly Action toggle;

    public InterfaceToggleDialog(ICoreClientAPI api, Action toggle) : base(api)
    {
        this.toggle = toggle;
        double logicalScreenHeight = api.Render.FrameHeight / Math.Max(0.5f, RuntimeEnv.GUIScale);
        int rows = Math.Max(4, (int)Math.Floor((logicalScreenHeight - 170) / SlotPitch));
        double searchY = rows * SlotPitch + 94;
        double inset = GuiStyle.ElementToDialogPadding;
        ElementBounds dialogBounds = ElementBounds.Fixed(EnumDialogArea.RightTop, -8 - inset, 8 + inset + searchY, 48, 44);
        ElementBounds buttonBounds = ElementBounds.Fixed(0, 0, 48, 44);
        SingleComposer = api.Gui.CreateCompo("vsrecipebrowser-interface-toggle", dialogBounds)
            .AddButton("", ToggleInterface, buttonBounds, CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), EnumButtonStyle.Normal, "browser-show")
            .AddDynamicCustomDraw(buttonBounds, InterfaceIcons.DrawClose, "browser-show-icon")
            .Compose();
    }

    public override double DrawOrder => 0.26;
    public override string ToggleKeyCombinationCode => null!;
    public override bool PrefersUngrabbedMouse => true;
    public override bool CaptureAllInputs() => false;

    private bool ToggleInterface()
    {
        toggle();
        return true;
    }
}
