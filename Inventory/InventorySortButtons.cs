using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VsRecipeBrowser;

internal sealed class InventorySortButtons : IDisposable
{
    internal static InventorySortButtons? Instance { get; private set; }
    private readonly ICoreClientAPI api;
    private readonly Dictionary<GuiDialog, SortButtonDialog> buttons = new();
    private readonly long tickId;
    internal InventorySortButtons(ICoreClientAPI api)
    {
        this.api = api;
        Instance = this;
        tickId = api.Event.RegisterGameTickListener(Update, 50);
    }

    private GuiComposer? Composer(GuiDialog gui) => IsPlayer(gui)
        ? LinkedInventoryController.Instance?.GetPlayerInventoryComposer(gui, out _) : gui.SingleComposer;

    private static bool IsPlayer(GuiDialog gui) => gui.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory";
    private static bool Supported(GuiDialog gui) => IsPlayer(gui)
        || (gui is GuiDialogBlockEntityInventory block && block.Inventory is { } inv
            && (inv.ClassName.Contains("chest", StringComparison.OrdinalIgnoreCase)
                || inv.ClassName.Contains("storagevessel", StringComparison.OrdinalIgnoreCase)
                || inv.ClassName.Contains("basket", StringComparison.OrdinalIgnoreCase)));

    private void Update(float _)
    {
        if (api.World?.Player is null) return;
        var open = api.OpenedGuis.OfType<GuiDialog>().Where(g => InventorySorter.Enabled && g.IsOpened() && Supported(g)).ToArray();
        foreach (var gui in buttons.Keys.Where(g => !open.Contains(g)).ToArray())
        { buttons[gui].TryClose(); buttons[gui].Dispose(); buttons.Remove(gui); }
        foreach (var gui in open)
        {
            var composer = Composer(gui);
            if (composer is null) continue;
            if (!buttons.TryGetValue(gui, out var button))
            {
                button = new SortButtonDialog(api, () => InventorySorter.Sort(api,
                    (gui as GuiDialogBlockEntityInventory)?.Inventory), () => gui.IsOpened() ? Composer(gui)?.Bounds : null);
                buttons.Add(gui, button);
            }
            button.Position(composer.Bounds);
            if (!button.IsOpened()) button.TryOpen();
        }
    }

    internal bool SortHovered()
    {
        if (!InventorySorter.Enabled) return false;
        // Never sort an underlying inventory through a recipe/settings overlay.
        if (api.OpenedGuis.OfType<GuiDialog>().Any(gui => gui.IsOpened()
            && gui is BrowserFilterDialog)) return false;
        if (api.OpenedGuis.OfType<GuiDialog>().Any(gui => gui.IsOpened()
            && gui is RecipeOverlayDialog && gui.SingleComposer?.Bounds.PointInside(api.Input.MouseX, api.Input.MouseY) == true)) return false;
        // No world-wide sorting: choose only the actual window under the mouse.
        foreach (var gui in api.OpenedGuis.OfType<GuiDialog>().Where(g => g.IsOpened() && Supported(g)).Reverse().ToArray())
            if (Composer(gui)?.Bounds.PointInside(api.Input.MouseX, api.Input.MouseY) == true)
                return InventorySorter.Sort(api, (gui as GuiDialogBlockEntityInventory)?.Inventory);
        return false;
    }

    internal bool HandleSlotMouseDown(MouseEvent args)
    {
        var mapping = api.Input.GetHotKeyByCode("xrb-sort-window")?.CurrentMapping;
        if (mapping is null || mapping.KeyCode != KeyCombination.MouseStart + (int)args.Button
            || mapping.Ctrl || mapping.Shift || mapping.Alt || mapping.OnKeyUp) return false;
        return SortHovered();
    }

    public void Dispose()
    {
        if (ReferenceEquals(Instance, this)) Instance = null;
        api.Event.UnregisterGameTickListener(tickId);
        foreach (var button in buttons.Values) { button.TryClose(); button.Dispose(); }
        buttons.Clear();
    }
}

internal sealed class SortButtonDialog : GuiDialog
{
    private readonly Func<bool> sort;
    private readonly ElementBounds root;
    private readonly Func<ElementBounds?> ownerBounds;
    internal SortButtonDialog(ICoreClientAPI api, Func<bool> sort, Func<ElementBounds?> ownerBounds) : base(api)
    {
        this.sort = sort;
        this.ownerBounds = ownerBounds;
        root = ElementBounds.Fixed(0, 0, 20, 20);
        var cell = ElementBounds.Fixed(0, 0, 20, 20);
        SingleComposer = api.Gui.CreateCompo("xrb-sort-" + Guid.NewGuid().ToString("N"), root)
            .AddDynamicCustomDraw(cell, DrawIcon, "sort-icon")
            .AddHoverText(BrowserText.Choose("Сортировать это окно", "Sort this inventory"),
                CairoFont.WhiteSmallText(), 180, cell, "sort-tooltip").Compose();
    }
    internal void Position(ElementBounds window)
    {
        window.CalcWorldBounds();
        double scale = Math.Max(0.01, RuntimeEnv.GUIScale);
        root.fixedX = (window.renderX + window.OuterWidth) / scale - 80;
        root.fixedY = window.renderY / scale + 5;
        root.MarkDirtyRecursive(); root.CalcWorldBounds();
    }
    public override double DrawOrder => 0.3;
    public override void OnRenderGUI(float deltaTime)
    {
        if (ownerBounds() is not { } window) return;
        Position(window); base.OnRenderGUI(deltaTime);
    }
    public override string ToggleKeyCombinationCode => null!;
    public override bool CaptureAllInputs() => false;
    public override bool PrefersUngrabbedMouse => true;
    public override void OnMouseDown(MouseEvent args)
    {
        if (ownerBounds() is not { } window) return;
        Position(window);
        if (!root.PointInside(args.X, args.Y)) return;
        if (args.Button == EnumMouseButton.Left) { sort(); args.Handled = true; }
    }
    public override void OnMouseUp(MouseEvent args)
    {
        if (root.PointInside(args.X, args.Y)) args.Handled = true;
    }
    private static void DrawIcon(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        double s = RuntimeEnv.GUIScale;
        ctx.Save(); ctx.Scale(s, s);
        ctx.SetSourceRGBA(1, 1, 1, 1); ctx.LineWidth = 1.7;
        for (int i = 0; i < 3; i++) { ctx.MoveTo(4, 5 + i * 5); ctx.LineTo(10 - i, 5 + i * 5); }
        ctx.MoveTo(14, 4); ctx.LineTo(14, 16);
        ctx.MoveTo(11, 13); ctx.LineTo(14, 16); ctx.LineTo(17, 13);
        ctx.Stroke(); ctx.Restore();
    }
}
