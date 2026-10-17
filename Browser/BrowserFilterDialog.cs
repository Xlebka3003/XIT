using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VsRecipeBrowser;

internal sealed class BrowserFilterDialog : GuiDialog
{
    private readonly Action closed;
    public BrowserFilterDialog(ICoreClientAPI api, GuiComposer composer, Action closed) : base(api)
    { SingleComposer = composer; this.closed = closed; }
    public override double DrawOrder => 10;
    public override string ToggleKeyCombinationCode => null!;
    public override bool PrefersUngrabbedMouse => true;
    public override bool CaptureAllInputs() => true;
    public override void OnKeyDown(KeyEvent args)
    {
        if (args.KeyCode == (int)GlKeys.Escape) { TryClose(); args.Handled = true; return; }
        if (args.KeyCode == (int)GlKeys.E)
        {
            TryClose();
            foreach (var inventory in capi.OpenedGuis.OfType<GuiDialog>()
                .Where(gui => gui.IsOpened() && gui.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory").ToArray())
                inventory.TryClose();
            args.Handled = true;
            return;
        }
        base.OnKeyDown(args);
    }
    public override void OnGuiClosed() { base.OnGuiClosed(); closed(); }
}
