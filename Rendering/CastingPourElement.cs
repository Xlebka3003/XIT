using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VsRecipeBrowser;

internal sealed class CastingPourElement : GuiElement
{
    private LoadedTexture stream;
    public CastingPourElement(ICoreClientAPI api, ElementBounds bounds) : base(api, bounds) => stream = new LoadedTexture(api);
    public override void ComposeElements(Context context, ImageSurface surface)
    {
        using var image = new ImageSurface(Format.Argb32, 70, 100);
        using var draw = new Context(image);
        draw.SetSourceRGBA(1, 0.58, 0.12, 0.95);
        draw.LineWidth = 5;
        draw.MoveTo(6, 5);
        draw.CurveTo(34, 5, 42, 30, 42, 72);
        draw.Stroke();
        draw.SetSourceRGBA(1, 0.88, 0.40, 1);
        draw.LineWidth = 2;
        draw.MoveTo(6, 5);
        draw.CurveTo(34, 5, 42, 30, 42, 72);
        draw.Stroke();
        image.Flush();
        generateTexture(image, ref stream, true);
    }
    public override void RenderInteractiveElements(float deltaTime)
    {
        double phase = (api.ElapsedMilliseconds % 3000) / 3000d;
        double progress = Math.Clamp((phase - 0.12) / 0.35, 0, 1);
        if (phase > 0.88 || progress <= 0) return;
        var clip = ElementBounds.Fixed(0, 0, 70, 100 * progress).WithParent(Bounds);
        clip.CalcWorldBounds();
        api.Render.PushScissor(clip, true);
        api.Render.Render2DTexturePremultipliedAlpha(stream.TextureId, Bounds.renderX, Bounds.renderY,
            scaled(70), scaled(100), 90, ColorUtil.WhiteArgbVec);
        api.Render.PopScissor();
    }
    public override void Dispose() { stream.Dispose(); base.Dispose(); }
}
