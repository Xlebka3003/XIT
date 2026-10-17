using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VsRecipeBrowser;

internal static class InterfaceIcons
{
    internal static void DrawClose(Context context, ImageSurface surface, ElementBounds bounds)
    {
        double cx = bounds.InnerWidth / 2, cy = bounds.InnerHeight / 2;
        double radius = Math.Min(bounds.InnerWidth, bounds.InnerHeight) * 0.23;
        context.Save();
        context.SetSourceRGBA(0.97, 0.92, 0.81, 1);
        context.LineWidth = 2.4 * RuntimeEnv.GUIScale;
        context.LineCap = LineCap.Round;
        context.MoveTo(cx - radius, cy - radius); context.LineTo(cx + radius, cy + radius);
        context.MoveTo(cx + radius, cy - radius); context.LineTo(cx - radius, cy + radius);
        context.Stroke(); context.Restore();
    }
}
