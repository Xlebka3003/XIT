using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VsRecipeBrowser;

internal sealed class BarrelLiquidElement : GuiElement
{
    private LoadedTexture texture;
    private string state = "";
    public BarrelLiquidElement(ICoreClientAPI api, ElementBounds bounds) : base(api, bounds) => texture = new LoadedTexture(api);
    public override void ComposeElements(Context context, ImageSurface surface) => SetLiquid(null);
    public void SetLiquid(ItemStack? stack)
    {
        string next = stack?.Collectible.Code.ToString() ?? "empty";
        if (next == state) return;
        state = next;
        using var image = new ImageSurface(Format.Argb32, 34, 130);
        using var draw = new Context(image);
        draw.SetSourceRGBA(0.09, 0.07, 0.05, 1);
        draw.Rectangle(0, 0, 34, 130); draw.Fill();
        draw.SetSourceRGBA(0.62, 0.52, 0.38, 1);
        draw.Rectangle(1, 1, 32, 128); draw.LineWidth = 2; draw.Stroke();
        if (stack is not null)
        {
            string path = stack.Collectible.Code.Path;
            // Liquid items can render a container in the inventory. Sample the
            // actual liquid texture, not that container's random model color.
            int? liquidColor = GetLiquidColor(stack);
            if (liquidColor is int rgb)
                draw.SetSourceRGBA((rgb & 255) / 255d, ((rgb >> 8) & 255) / 255d, ((rgb >> 16) & 255) / 255d, 1);
            else if (path.Contains("tannin")) draw.SetSourceRGBA(0.40, 0.23, 0.08, 1);
            else if (path.Contains("lime")) draw.SetSourceRGBA(0.80, 0.83, 0.72, 1);
            else if (path.Contains("water")) draw.SetSourceRGBA(0.23, 0.50, 0.67, 1);
            else if (path.Contains("slush") || path.Contains("slop") || path.Contains("ice")) draw.SetSourceRGBA(0.72, 0.83, 0.88, 1);
            else if (path.Contains("milk")) draw.SetSourceRGBA(0.92, 0.91, 0.81, 1);
            else if (path.Contains("honey")) draw.SetSourceRGBA(0.93, 0.66, 0.13, 1);
            else if (path.Contains("borax")) draw.SetSourceRGBA(0.73, 0.79, 0.78, 1);
            else
            {
                int color = stack.Collectible.GetRandomColor(api, stack);
                draw.SetSourceRGBA((color & 255) / 255d, ((color >> 8) & 255) / 255d, ((color >> 16) & 255) / 255d, 1);
            }
            draw.Rectangle(4, 5, 26, 121); draw.Fill();
            draw.SetSourceRGBA(1, 1, 1, 0.4);
            draw.LineWidth = 1;
            for (int y = 22; y < 130; y += 21) { draw.MoveTo(23, y); draw.LineTo(30, y); draw.Stroke(); }
        }
        image.Flush(); generateTexture(image, ref texture, true);
    }
    public override void RenderInteractiveElements(float dt) => api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId,
        Bounds.renderX, Bounds.renderY, Bounds.OuterWidth, Bounds.OuterHeight, 80, ColorUtil.WhiteArgbVec);
    public override void Dispose() { texture.Dispose(); base.Dispose(); }

    private int? GetLiquidColor(ItemStack stack)
    {
        string? texturePath = stack.Collectible.Attributes?["waterTightContainerProps"]["texture"]["base"].AsString();
        if (string.IsNullOrEmpty(texturePath)) return null;
        foreach (var variant in stack.Collectible.Variant)
            texturePath = texturePath.Replace("{" + variant.Key + "}", variant.Value);
        try
        {
            var location = new AssetLocation(texturePath);
            if (!texturePath.Contains(':')) location.Domain = stack.Collectible.Code.Domain;
            if (api.BlockTextureAtlas.GetOrInsertTexture(location, out int textureId, out _))
                return api.BlockTextureAtlas.GetAverageColor(textureId);
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[XRB] Could not read liquid texture {0}: {1}", texturePath, exception.Message);
        }
        return null;
    }
}
