using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VsRecipeBrowser;

/// <summary>JEI-style display cells: no inventory slot activation at all.</summary>
internal sealed class RecipeDisplayGridElement : GuiElement
{
    private readonly BrowserInventory inventory;
    private readonly int columns;
    private readonly Action<int, EnumMouseButton> clicked;
    private readonly bool tabStyle;
    private readonly int activeSlot;
    private LoadedTexture background;
    private LoadedTexture hover;
    private readonly Dictionary<string, LoadedTexture> quantityTextures = new();
    public int HoveredSlotId { get; private set; } = -1;
    public bool AnimatePouring { get; set; }
    public bool AnimateHammer { get; set; }
    public System.Func<int, int>? AlternativeCount { get; set; }
    public Action<int>? ShowAlternatives { get; set; }
    private LoadedTexture? alternativesBadge;
    private LoadedTexture? alternativesHint;
    private LoadedTexture? fuelTabIcon;

    public RecipeDisplayGridElement(ICoreClientAPI api, BrowserInventory inventory, int columns, ElementBounds bounds, Action<int, EnumMouseButton> clicked, bool tabStyle = false, int activeSlot = -1)
        : base(api, bounds)
    {
        this.inventory = inventory;
        this.columns = columns;
        this.clicked = clicked;
        this.tabStyle = tabStyle;
        this.activeSlot = activeSlot;
        background = new LoadedTexture(api);
        hover = new LoadedTexture(api);
    }

    public override void ComposeElements(Context context, ImageSurface surface)
    {
        int rows = (inventory.Count + columns - 1) / columns;
        int width = Math.Max(1, (int)Math.Ceiling(columns * GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale));
        int height = Math.Max(1, (int)Math.Ceiling(rows * GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale));
        using (var bgSurface = new ImageSurface(Format.Argb32, width, height))
        using (var ctx = new Context(bgSurface))
        {
            double pitch = GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale;
            for (int i = 0; i < inventory.Count; i++)
            {
                double x = i % columns * pitch + 2;
                double y = i / columns * pitch + 2;
                bool active = tabStyle && i == activeSlot;
                if (tabStyle)
                    ctx.SetSourceRGBA(active ? 0.29 : 0.20, active ? 0.24 : 0.16, active ? 0.18 : 0.12, 0.97);
                else
                    ctx.SetSourceRGBA(0.72, 0.61, 0.48, 0.92);
                ctx.Rectangle(x, y, pitch - 4, pitch - 4);
                ctx.Fill();
                EmbossRoundRectangleElement(ctx, x, y, pitch - 4, pitch - 4, false, 2, 1);
                if (tabStyle)
                {
                    ctx.LineWidth = active ? 2.4 : 1.6;
                    ctx.SetSourceRGBA(active ? 0.58 : 0.38, active ? 0.48 : 0.31, active ? 0.35 : 0.22, active ? 0.92 : 0.82);
                    ctx.Rectangle(x + 1, y + 1, pitch - 6, pitch - 6);
                    ctx.Stroke();
                }
            }
            bgSurface.Flush();
            generateTexture(bgSurface, ref background, true);
        }

        int hoverSize = Math.Max(1, (int)Math.Ceiling(GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale));
        using var hoverSurface = new ImageSurface(Format.Argb32, hoverSize, hoverSize);
        using var hoverContext = new Context(hoverSurface);
        if (tabStyle) hoverContext.SetSourceRGBA(0.95, 0.72, 0.38, 0.30);
        else hoverContext.SetSourceRGBA(0.15, 0.85, 1, 0.36);
        hoverContext.Rectangle(2, 2, hoverSize - 4, hoverSize - 4);
        hoverContext.Fill();
        hoverSurface.Flush();
        generateTexture(hoverSurface, ref hover, true);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        api.Render.Render2DTexturePremultipliedAlpha(background.TextureId, Bounds.renderX, Bounds.renderY, Bounds.OuterWidth, Bounds.OuterHeight, 80, ColorUtil.WhiteArgbVec);
        double pitch = GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale;
        double itemSize = GuiElementPassiveItemSlot.unscaledItemSize * RuntimeEnv.GUIScale;
        for (int i = 0; i < inventory.Count; i++)
        {
            ItemSlot slot = inventory[i];
            if (slot.Itemstack is null) continue;
            double x = Bounds.renderX + i % columns * pitch;
            double y = Bounds.renderY + i / columns * pitch;
            if (i == HoveredSlotId)
                api.Render.Render2DTexturePremultipliedAlpha(hover.TextureId, x, y, pitch, pitch, 0, ColorUtil.WhiteArgbVec);

            if (tabStyle && slot.Itemstack.Attributes.GetBool("xrb-flame-tab"))
            {
                if (fuelTabIcon is null)
                {
                    fuelTabIcon = new LoadedTexture(api);
                    using var surface = new ImageSurface(Format.Argb32, 48, 48);
                    using var context = new Context(surface);
                    context.Translate(13, 5); context.Scale(0.16, 0.16); context.SetSourceRGBA(1, 0.65, 0.1, 1);
                    api.Gui.Icons.DrawFlame(context, 0, false, false);
                    surface.Flush(); generateTexture(surface, ref fuelTabIcon, true);
                }
                api.Render.Render2DTexturePremultipliedAlpha(fuelTabIcon.TextureId, x, y, pitch, pitch, 100, ColorUtil.WhiteArgbVec);
                continue;
            }

            bool oversizedModel = IsOversizedModel(slot.Itemstack);
            if (AnimatePouring || AnimateHammer)
            {
                api.Render.GlPushMatrix();
                api.Render.GlTranslate(x + pitch / 2, y + pitch / 2, 0);
                float phase = (api.ElapsedMilliseconds % 3000) / 3000f;
                float angle = AnimateHammer ? 12f * (float)Math.Sin(api.ElapsedMilliseconds / 700f * Math.PI * 2)
                    : 22f * (float)Math.Sin(Math.PI * phase);
                api.Render.GlRotate(angle, 0, 0, 1);
                api.Render.GlTranslate(-x - pitch / 2, -y - pitch / 2, 0);
            }
            if (IsLongTool(slot.Itemstack) || oversizedModel)
            {
                double unscaledPitch = GuiElementPassiveItemSlot.unscaledSlotSize;
                ElementBounds clip = ElementBounds.Fixed(
                    i % columns * unscaledPitch + 2,
                    i / columns * unscaledPitch + 2,
                    unscaledPitch - 4,
                    unscaledPitch - 4
                ).WithParent(Bounds);
                clip.CalcWorldBounds();
                api.Render.PushScissor(clip, true);
                float renderSize = (float)(itemSize * (oversizedModel ? 0.68 : 1));
                api.Render.RenderItemstackToGui(slot, x + pitch / 2, y + pitch / 2, 100, renderSize, ColorUtil.WhiteArgb, deltaTime);
                api.Render.PopScissor();
            }
            else
            {
                api.Render.RenderItemstackToGui(slot, x + pitch / 2, y + pitch / 2, 100, (float)itemSize, ColorUtil.WhiteArgb, deltaTime);
            }
            if (AnimatePouring || AnimateHammer) api.Render.GlPopMatrix();
            int quantity = slot.Itemstack.StackSize;
            double itemsPerLitre = slot.Itemstack.Collectible.Attributes?["waterTightContainerProps"]["itemsPerLitre"].AsDouble(0) ?? 0;
            string quantityLabel = itemsPerLitre > 0 ? (quantity / itemsPerLitre).ToString("0.##") + BrowserText.Choose(" л", " L") : quantity.ToString();
            if (quantity > 1 && !RendersOwnQuantity(slot.Itemstack))
            {
                if (!quantityTextures.TryGetValue(quantityLabel, out LoadedTexture? texture))
                {
                    texture = api.Gui.TextTexture.GenTextTexture(quantityLabel, CairoFont.WhiteSmallText(), new TextBackground());
                    quantityTextures[quantityLabel] = texture;
                }
                api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, x + pitch - texture.Width - 3, y + pitch - texture.Height - 2, texture.Width, texture.Height, 110, ColorUtil.WhiteArgbVec);
            }
        }
    }

    internal void RenderAlternativesOverlay()
    {
        if (AlternativeCount is null) return;
        double pitch = GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale;
        api.Render.GLDisableDepthTest();
        try
        {
        for (int i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].Itemstack is not null && AlternativeCount?.Invoke(i) > 1)
            {
                EnsureAlternativesBadge();
                double x = Bounds.renderX + i % columns * pitch;
                double y = Bounds.renderY + i / columns * pitch;
                api.Render.Render2DTexturePremultipliedAlpha(alternativesBadge.TextureId,
                    x + pitch - alternativesBadge.Width - 3, y + 2, alternativesBadge.Width, alternativesBadge.Height, 120, ColorUtil.WhiteArgbVec);
            }
        }
        if (HoveredSlotId >= 0 && AlternativeCount?.Invoke(HoveredSlotId) > 1)
        {
            double badgeX = Bounds.renderX + (HoveredSlotId % columns + 1) * pitch - 20 * RuntimeEnv.GUIScale;
            double badgeY = Bounds.renderY + HoveredSlotId / columns * pitch;
            if (api.Input.MouseX >= badgeX && api.Input.MouseY <= badgeY + 20 * RuntimeEnv.GUIScale)
            {
                alternativesHint ??= api.Gui.TextTexture.GenTextTexture(BrowserText.Choose("Выбрать замену", "Choose ingredient"),
                    CairoFont.WhiteSmallText(), new TextBackground());
                api.Render.Render2DTexturePremultipliedAlpha(alternativesHint.TextureId, api.Input.MouseX + 12,
                    api.Input.MouseY + 18, alternativesHint.Width, alternativesHint.Height, 300, ColorUtil.WhiteArgbVec);
            }
        }
        }
        finally { api.Render.GLEnableDepthTest(); }
    }

    private void EnsureAlternativesBadge()
    {
        if (alternativesBadge is not null) return;
        int size = Math.Max(1, (int)Math.Round(18 * RuntimeEnv.GUIScale));
        using var surface = new ImageSurface(Format.Argb32, size, size);
        using var context = new Context(surface);
        context.Scale(size / 18.0, size / 18.0);
        // Cairo's positive arcs travel clockwise in screen coordinates.
        void DrawArrows(double red, double green, double blue, double width)
        {
            context.SetSourceRGBA(red, green, blue, 1);
            context.LineWidth = width; context.LineCap = LineCap.Round; context.LineJoin = LineJoin.Round;
            for (int arrow = 0; arrow < 2; arrow++)
            {
                double start = (-135 + arrow * 180) * Math.PI / 180;
                double end = (-15 + arrow * 180) * Math.PI / 180;
                context.Arc(9, 9, 5.5, start, end); context.Stroke();
                double x = 9 + 5.5 * Math.Cos(end), y = 9 + 5.5 * Math.Sin(end);
                double tx = -Math.Sin(end), ty = Math.Cos(end);
                context.MoveTo(x - 3 * tx + 2 * ty, y - 3 * ty - 2 * tx);
                context.LineTo(x, y);
                context.LineTo(x - 3 * tx - 2 * ty, y - 3 * ty + 2 * tx);
                context.Stroke();
            }
        }
        DrawArrows(0.20, 0.16, 0.11, 3.8);
        DrawArrows(1, 0.96, 0.84, 2);
        surface.Flush();
        alternativesBadge = new LoadedTexture(api);
        generateTexture(surface, ref alternativesBadge, true);
    }

    private static bool IsLongTool(ItemStack stack)
    {
        string path = stack.Collectible?.Code?.Path ?? "";
        string[] families =
        {
            "axe-", "pickaxe-", "shovel-", "hoe-", "scythe-", "spear-",
            "saw-", "knife-", "chisel-", "hammer-", "prospectingpick-",
            "cleaver-", "sword-", "bow-"
        };
        return families.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RendersOwnQuantity(ItemStack stack)
    {
        string typeName = stack.Collectible?.GetType().Name ?? "";
        string path = stack.Collectible?.Code?.Path ?? "";
        return typeName.Contains("Liquid", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("portion", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOversizedModel(ItemStack stack)
    {
        string path = stack.Collectible?.Code?.Path ?? "";
        return path.StartsWith("angledgears-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("largegear-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("woodengear-", StringComparison.OrdinalIgnoreCase);
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        int previous = HoveredSlotId;
        HoveredSlotId = SlotAt(args.X, args.Y);
        if (HoveredSlotId >= 0 && inventory[HoveredSlotId].Itemstack is not null)
            api.World.Player.InventoryManager.CurrentHoveredSlot = inventory[HoveredSlotId];
        else
        {
            HoveredSlotId = -1;
            if (previous >= 0 && ReferenceEquals(api.World.Player.InventoryManager.CurrentHoveredSlot?.Inventory, inventory))
                api.World.Player.InventoryManager.CurrentHoveredSlot = null!;
        }
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        TryClick(args.X, args.Y, args.Button);
        args.Handled = true;
    }

    public bool TryClick(int mouseX, int mouseY, EnumMouseButton button)
    {
        int slot = SlotAt(mouseX, mouseY);
        if (slot < 0 || inventory[slot].Itemstack is null) return false;
        double pitch = GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale;
        if (button == EnumMouseButton.Left && AlternativeCount?.Invoke(slot) > 1
            && mouseX >= Bounds.renderX + (slot % columns + 1) * pitch - 20 * RuntimeEnv.GUIScale
            && mouseY <= Bounds.renderY + slot / columns * pitch + 20 * RuntimeEnv.GUIScale)
        {
            ShowAlternatives?.Invoke(slot);
            return true;
        }
        clicked(slot, button);
        return true;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args) => args.Handled = true;
    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args) => args.Handled = true;

    private int SlotAt(int mouseX, int mouseY)
    {
        if (!Bounds.PointInside(mouseX, mouseY)) return -1;
        double pitch = GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale;
        int column = (int)((mouseX - Bounds.renderX) / pitch);
        int row = (int)((mouseY - Bounds.renderY) / pitch);
        int slot = row * columns + column;
        return column >= 0 && column < columns && slot >= 0 && slot < inventory.Count ? slot : -1;
    }

    public override void Dispose()
    {
        background.Dispose();
        hover.Dispose();
        alternativesBadge?.Dispose();
        alternativesHint?.Dispose();
        fuelTabIcon?.Dispose();
        foreach (LoadedTexture texture in quantityTextures.Values) texture.Dispose();
        quantityTextures.Clear();
        base.Dispose();
    }
}

internal static class RecipeDisplayGridComposerExtensions
{
    public static GuiComposer AddRecipeDisplayGrid(this GuiComposer composer, BrowserInventory inventory, int columns, ElementBounds bounds, string key, Action<int, EnumMouseButton> clicked, bool tabStyle = false, int activeSlot = -1)
    {
        if (!composer.Composed) composer.AddInteractiveElement(new RecipeDisplayGridElement(composer.Api, inventory, columns, bounds, clicked, tabStyle, activeSlot), key);
        return composer;
    }
}
