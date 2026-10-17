using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using XrbRecipeChoice;

namespace VsRecipeBrowser;

internal sealed class ChoiceButtonDialog : GuiDialog
{
    private readonly ElementBounds root = ElementBounds.Fixed(0, 0, 28, 28);
    private readonly Func<bool> open;
    private readonly Func<ElementBounds?> ownerBounds;
    internal ChoiceButtonDialog(ICoreClientAPI api, Func<bool> open, Func<ElementBounds?> ownerBounds) : base(api)
    {
        this.open = open;
        this.ownerBounds = ownerBounds;
        var cell = ElementBounds.Fixed(0, 0, 28, 28);
        SingleComposer = api.Gui.CreateCompo("xrb-recipe-choice-button", root)
            .AddDynamicCustomDraw(cell, DrawArrows, "choice-arrows")
            .AddHoverText(BrowserText.Choose("Выбрать результат крафта", "Choose crafting result"),
                CairoFont.WhiteSmallText(), 220, cell).Compose();
    }
    internal void Position(ElementBounds output)
    {
        output.CalcWorldBounds();
        double scale = Math.Max(.01, RuntimeEnv.GUIScale);
        root.fixedX = (output.renderX + output.OuterWidth) / scale + 8;
        root.fixedY = output.renderY / scale + (output.OuterHeight / scale - 28) / 2;
        root.MarkDirtyRecursive(); root.CalcWorldBounds();
    }
    public override double DrawOrder => .31;
    public override string ToggleKeyCombinationCode => null!;
    public override bool CaptureAllInputs() => false;
    public override void OnRenderGUI(float deltaTime)
    {
        if (ownerBounds() is not { } output) return;
        Position(output); base.OnRenderGUI(deltaTime);
    }
    public override void OnMouseDown(MouseEvent args)
    {
        if (ownerBounds() is not { } output) return;
        Position(output);
        if (args.Button == EnumMouseButton.Left && root.PointInside(args.X, args.Y)) { open(); args.Handled = true; }
    }
    public override void OnMouseUp(MouseEvent args)
    {
        if (ownerBounds() is not { } output) return;
        Position(output);
        if (root.PointInside(args.X, args.Y)) args.Handled = true;
    }
    private static void DrawArrows(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        ctx.Save(); ctx.Scale(RuntimeEnv.GUIScale, RuntimeEnv.GUIScale);
        ctx.SetSourceRGBA(1, 1, 1, 1); ctx.LineWidth = 2;
        ctx.MoveTo(4, 8); ctx.LineTo(24, 8); ctx.MoveTo(20, 4); ctx.LineTo(24, 8); ctx.LineTo(20, 12);
        ctx.MoveTo(24, 20); ctx.LineTo(4, 20); ctx.MoveTo(8, 16); ctx.LineTo(4, 20); ctx.LineTo(8, 24);
        ctx.Stroke(); ctx.Restore();
    }
}

internal sealed class ChoicePickerDialog : GuiDialog
{
    internal ChoicePickerDialog(ICoreClientAPI api, List<ChoiceOption> options, Action<int> selected) : base(api)
    {
        var inventory = new BrowserInventory(options.Count, api);
        for (int i = 0; i < options.Count; i++)
        {
            var stack = new ItemStack(options[i].Stack);
            if (stack.ResolveBlockOrItem(api.World)) inventory[i].Itemstack = stack;
        }
        int columns = Math.Min(5, options.Count), rows = (options.Count + columns - 1) / columns;
        var root = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        var bg = ElementBounds.Fixed(0, 0, columns * 48.8 + 24, rows * 48.8 + 58);
        SingleComposer = api.Gui.CreateCompo("xrb-recipe-choice-picker", root).AddShadedDialogBG(bg, true)
            .AddStaticText(BrowserText.Choose("Выберите результат", "Choose a result"), CairoFont.WhiteSmallText(),
                ElementBounds.Fixed(12, 8, bg.fixedWidth - 24, 28))
            .AddRecipeDisplayGrid(inventory, columns, ElementBounds.Fixed(12, 44, columns * 48.8, rows * 48.8), "choice-results",
                (slot, button) => { if (button == EnumMouseButton.Left) selected(options[slot].RecipeId); }).Compose();
    }
    public override double DrawOrder => 11;
    public override string ToggleKeyCombinationCode => null!;
    public override bool CaptureAllInputs() => true;
    public override void OnMouseDown(MouseEvent args)
    {
        if (args.Button == EnumMouseButton.Left
            && SingleComposer.GetElement("choice-results") is RecipeDisplayGridElement grid
            && grid.TryClick(args.X, args.Y, args.Button))
        {
            args.Handled = true;
            return;
        }
        base.OnMouseDown(args);
    }
    public override void OnMouseUp(MouseEvent args) => args.Handled = true;
    public override void OnKeyDown(KeyEvent args)
    { if (args.KeyCode is (int)GlKeys.Escape or (int)GlKeys.E) { TryClose(); args.Handled = true; } else base.OnKeyDown(args); }
}
