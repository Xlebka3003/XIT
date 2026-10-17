using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VsRecipeBrowser;

internal sealed class FavoriteRecipePreviewDialog : GuiDialog
{
    private readonly ElementBounds root;
    internal FavoriteRecipePreviewDialog(ICoreClientAPI api, ItemStack target, RecipeOverlayDialog.RecipeDisplayEntry? recipe) : base(api)
    {
        root = ElementBounds.Fixed(0, 0, 312, 214);
        var bg = ElementBounds.Fixed(0, 0, 312, 214);
        var composer = api.Gui.CreateCompo("xrb-favorite-preview", root).AddShadedDialogBG(bg, true)
            .AddStaticText(target.GetName(), CairoFont.WhiteSmallText(), ElementBounds.Fixed(12, 9, 288, 30));
        if (recipe is null)
        {
            composer.AddStaticText(BrowserText.Choose("Рецепт не найден", "No recipe found"),
                CairoFont.WhiteSmallText(), ElementBounds.Fixed(12, 65, 285, 40));
        }
        else
        {
            var inputs = new BrowserInventory(9, api);
            for (int i = 0; i < Math.Min(9, recipe.Inputs.Count); i++) inputs[i].Itemstack = recipe.Inputs[i]?.Clone();
            var output = new BrowserInventory(1, api);
            output[0].Itemstack = recipe.Output.Clone();
            composer.AddRecipeDisplayGrid(inputs, 3, ElementBounds.Fixed(12, 49, 146.4, 146.4), "preview-inputs", (_, _) => { })
                .AddStaticText("→", CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center),
                    ElementBounds.Fixed(165, 101, 48, 40))
                .AddRecipeDisplayGrid(output, 1, ElementBounds.Fixed(235, 98, 48.8, 48.8), "preview-output", (_, _) => { });
            if (recipe.SecondaryOutput is { } secondary)
            {
                var extra = new BrowserInventory(1, api);
                extra[0].Itemstack = secondary.Clone();
                composer.AddRecipeDisplayGrid(extra, 1, ElementBounds.Fixed(235, 151, 48.8, 48.8), "preview-secondary", (_, _) => { });
            }
        }
        SingleComposer = composer.Compose();
        Position();
    }

    internal void Position()
    {
        double scale = Math.Max(0.01, RuntimeEnv.GUIScale);
        root.fixedX = Math.Clamp(capi.Input.MouseX / scale + 18, 0, Math.Max(0, capi.Render.FrameWidth / scale - 312));
        root.fixedY = Math.Clamp(capi.Input.MouseY / scale + 18, 0, Math.Max(0, capi.Render.FrameHeight / scale - 214));
        root.MarkDirtyRecursive(); root.CalcWorldBounds();
    }
    public override double DrawOrder => 9;
    public override string ToggleKeyCombinationCode => null!;
    public override bool CaptureAllInputs() => false;
    // A hover preview is read-only and must never steal an inventory click.
    public override void OnMouseDown(MouseEvent args) { }
    public override void OnMouseUp(MouseEvent args) { }
    public override void OnMouseMove(MouseEvent args) { }
}
