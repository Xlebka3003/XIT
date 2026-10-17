using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VsRecipeBrowser;

internal sealed class IngredientOptionsDialog : GuiDialog
{
    private readonly List<ItemStack> options;
    private readonly Action<ItemStack> selected;
    private int page;
    public IngredientOptionsDialog(ICoreClientAPI api, List<ItemStack> options, Action<ItemStack> selected) : base(api)
    { this.options = options; this.selected = selected; Compose(); }
    private void Compose()
    {
        SingleComposer?.Dispose();
        var shown = options.Skip(page * 25).Take(25).ToList();
        var inventory = new BrowserInventory(shown.Count, capi);
        for (int i = 0; i < shown.Count; i++) inventory[i].Itemstack = shown[i].Clone();
        int columns = Math.Min(5, shown.Count), rows = (shown.Count + columns - 1) / columns;
        double width = Math.Max(245, columns * 48.8), height = rows * 48.8 + 105;
        var root = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        SingleComposer = capi.Gui.CreateCompo("xrb-ingredient-options", root)
            .AddShadedDialogBG(ElementBounds.Fixed(0, 0, width + 24, height), true)
            .AddStaticText(BrowserText.Choose("Допустимые варианты", "Accepted ingredients"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(12, 8, width, 28))
            .AddRecipeDisplayGrid(inventory, columns, ElementBounds.Fixed(12, 42, columns * 48.8, rows * 48.8), "options",
                (slot, button) => { if (button == EnumMouseButton.Left) { selected(shown[slot].Clone()); TryClose(); } })
            .AddButton("<", () => { page = (page - 1 + (options.Count + 24) / 25) % ((options.Count + 24) / 25); Compose(); return true; }, ElementBounds.Fixed(12, height - 46, 45, 32))
            .AddStaticText($"{page + 1} / {(options.Count + 24) / 25}", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), ElementBounds.Fixed(65, height - 40, width - 110, 25))
            .AddButton(">", () => { page = (page + 1) % ((options.Count + 24) / 25); Compose(); return true; }, ElementBounds.Fixed(width - 45, height - 46, 45, 32))
            .AddButton("×", () => { TryClose(); return true; }, ElementBounds.Fixed(width - 20, 5, 32, 30)).Compose();
    }
    public override double DrawOrder => 11;
    public override string ToggleKeyCombinationCode => null!;
    public override bool CaptureAllInputs() => true;
    public override void OnMouseDown(MouseEvent args)
    {
        if (SingleComposer.GetElement("options") is RecipeDisplayGridElement grid && grid.TryClick(args.X, args.Y, args.Button)) { args.Handled = true; return; }
        base.OnMouseDown(args);
    }
    public override void OnKeyDown(KeyEvent args)
    { if (args.KeyCode is (int)GlKeys.Escape or (int)GlKeys.E) { TryClose(); args.Handled = true; } else base.OnKeyDown(args); }
}
