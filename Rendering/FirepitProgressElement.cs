using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VsRecipeBrowser;

/// <summary>
/// Keeps animated firepit graphics in their own OpenGL textures. Unlike
/// GuiElementCustomDraw.Redraw(), this never touches the composer's static
/// Cairo surface, so failed recipe navigation cannot erase the dialog.
/// </summary>
internal sealed class FirepitProgressElement : GuiElement
{
    private LoadedTexture arrowDark;
    private LoadedTexture arrowColor;
    private LoadedTexture flameDark;
    private LoadedTexture flameColor;
    private LoadedTexture temperature;
    private string temperatureText = "";

    public FirepitProgressElement(ICoreClientAPI api, ElementBounds bounds) : base(api, bounds)
    {
        arrowDark = new LoadedTexture(api);
        arrowColor = new LoadedTexture(api);
        flameDark = new LoadedTexture(api);
        flameColor = new LoadedTexture(api);
        temperature = new LoadedTexture(api);
    }

    public override void ComposeElements(Context context, ImageSurface surface)
    {
        MakeTexture(100, 55, arrow: true, colored: false, ref arrowDark);
        MakeTexture(100, 55, arrow: true, colored: true, ref arrowColor);
        MakeTexture(54, 54, arrow: false, colored: false, ref flameDark);
        MakeTexture(54, 54, arrow: false, colored: true, ref flameColor);
    }

    public void SetTemperature(string text)
    {
        if (temperatureText == text) return;
        temperatureText = text;
        temperature.Dispose();
        temperature = string.IsNullOrEmpty(text)
            ? new LoadedTexture(api)
            : api.Gui.TextTexture.GenTextTexture(text, CairoFont.WhiteSmallText(), new TextBackground());
    }

    private void MakeTexture(int width, int height, bool arrow, bool colored, ref LoadedTexture texture)
    {
        using var surface = new ImageSurface(Format.Argb32, width, height);
        using var ctx = new Context(surface);
        ctx.SetSourceRGBA(0, 0, 0, 0);
        ctx.Paint();
        if (colored) ctx.SetSourceRGBA(arrow ? 0.04 : 1, arrow ? 0.62 : 0.55, 0.04, 1);
        if (arrow)
        {
            ctx.Scale(0.47, 0.47);
            api.Gui.Icons.DrawArrowRight(ctx, colored ? 0 : 2, !colored, !colored);
        }
        else
        {
            ctx.Scale(0.22, 0.22);
            api.Gui.Icons.DrawFlame(ctx, colored ? 0 : 3, !colored, !colored);
        }
        surface.Flush();
        generateTexture(surface, ref texture, true);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        double progress = (api.ElapsedMilliseconds % 2000) / 2000d;
        // The food slots are centered at x=24 and x=198 relative to this
        // element. Center the vanilla-style arrow exactly between them.
        double arrowX = Bounds.renderX + scaled(80);
        double arrowY = Bounds.renderY + scaled(7);
        double arrowW = scaled(100);
        double arrowH = scaled(55);
        // Keep the flame in the same column as the input and fuel slots.
        double flameX = Bounds.renderX + scaled(8);
        double flameY = Bounds.renderY + scaled(56);
        double flameW = scaled(54);
        double flameH = scaled(54);

        api.Render.Render2DTexturePremultipliedAlpha(arrowDark.TextureId, arrowX, arrowY, arrowW, arrowH, 50, ColorUtil.WhiteArgbVec);
        ElementBounds arrowClip = ElementBounds.Fixed(80, 7, 100 * progress, 55).WithParent(Bounds);
        arrowClip.CalcWorldBounds();
        api.Render.PushScissor(arrowClip, true);
        api.Render.Render2DTexturePremultipliedAlpha(arrowColor.TextureId, arrowX, arrowY, arrowW, arrowH, 51, ColorUtil.WhiteArgbVec);
        api.Render.PopScissor();
        if (!string.IsNullOrEmpty(temperatureText))
        {
            double textX = arrowX + (arrowW - temperature.Width) / 2 - scaled(16);
            double textY = arrowY + (arrowH - temperature.Height) / 2 - scaled(10);
            api.Render.Render2DTexturePremultipliedAlpha(temperature.TextureId, textX, textY,
                temperature.Width, temperature.Height, 52, ColorUtil.WhiteArgbVec);
        }

        api.Render.Render2DTexturePremultipliedAlpha(flameDark.TextureId, flameX, flameY, flameW, flameH, 50, ColorUtil.WhiteArgbVec);
        ElementBounds flameClip = ElementBounds.Fixed(8, 56 + 54 * (1 - progress), 54, 54 * progress).WithParent(Bounds);
        flameClip.CalcWorldBounds();
        api.Render.PushScissor(flameClip, true);
        api.Render.Render2DTexturePremultipliedAlpha(flameColor.TextureId, flameX, flameY, flameW, flameH, 51, ColorUtil.WhiteArgbVec);
        api.Render.PopScissor();
    }

    public override void Dispose()
    {
        arrowDark.Dispose();
        arrowColor.Dispose();
        flameDark.Dispose();
        flameColor.Dispose();
        temperature.Dispose();
        base.Dispose();
    }
}

internal static class FirepitProgressComposerExtensions
{
    public static GuiComposer AddFirepitProgress(this GuiComposer composer, ElementBounds bounds, string key)
    {
        if (!composer.Composed) composer.AddInteractiveElement(new FirepitProgressElement(composer.Api, bounds), key);
        return composer;
    }
}
