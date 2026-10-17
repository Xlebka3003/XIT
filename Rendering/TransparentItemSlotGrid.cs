using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VsRecipeBrowser;

internal sealed class TransparentItemSlotGrid : GuiElementItemSlotGrid
{
    private readonly bool ignoreEmptySlots;
    public bool SuppressDescription { get; set; }

    public TransparentItemSlotGrid(ICoreClientAPI api, IInventory inventory, Action<object> sendPacket, int columns, ElementBounds bounds, bool ignoreEmptySlots)
        : base(api, inventory, sendPacket, columns, null, bounds)
    {
        this.ignoreEmptySlots = ignoreEmptySlots;
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseMove(api, args);
        if (SuppressDescription && hoverSlotId >= 0)
            api.World.Player.InventoryManager.CurrentHoveredSlot = null!;
        if (ignoreEmptySlots && hoverSlotId >= 0 && inventory?[hoverSlotId]?.Itemstack is null)
        {
            hoverSlotId = -1;
            if (api.World.Player.InventoryManager.CurrentHoveredSlot?.Itemstack is null)
                api.World.Player.InventoryManager.CurrentHoveredSlot = null!;
        }
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        if (ignoreEmptySlots && (hoverSlotId < 0 || inventory?[hoverSlotId]?.Itemstack is null)) return;
        if (inventory is BrowserInventory browserInventory && hoverSlotId >= 0)
        {
            browserInventory.TriggerClick(hoverSlotId, args.Button);
            args.Handled = true;
            return;
        }
        base.OnMouseDown(api, args);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale;
        double itemSize = GuiElementPassiveItemSlot.unscaledItemSize * RuntimeEnv.GUIScale;
        double offset = slotSize / 2;
        int index = 0;

        foreach (var pair in renderedSlots)
        {
            if (index >= SlotBounds.Length) break;
            ElementBounds bounds = SlotBounds[index];
            ItemSlot slot = pair.Value;

            if (slot.Itemstack is not null && (highlightSlotId == pair.Key || hoverSlotId == pair.Key))
            {
                api.Render.Render2DTexturePremultipliedAlpha(
                    HighlightSlotTexture.TextureId,
                    (int)(bounds.renderX - 2), (int)(bounds.renderY - 2),
                    bounds.OuterWidthInt + 4, bounds.OuterHeightInt + 4,
                    0, ColorUtil.WhiteArgbVec
                );
            }

            if (slot.Itemstack is not null)
            {
                api.Render.PushScissor(scissorBounds[index], true);
                api.Render.RenderItemstackToGui(slot, bounds.renderX + offset, bounds.renderY + offset, 90, (float)itemSize, ColorUtil.WhiteArgb, deltaTime);
                api.Render.PopScissor();

                if (slotQuantityTextures[index].TextureId != 0)
                    api.Render.Render2DTexturePremultipliedAlpha(slotQuantityTextures[index].TextureId, bounds);
            }
            index++;
        }
    }
}

internal sealed class NavigationItemSlotGrid : GuiElementItemSlotGrid
{
    private bool consumedClick;

    public NavigationItemSlotGrid(ICoreClientAPI api, BrowserInventory inventory, int columns, ElementBounds bounds)
        : base(api, inventory, _ => { }, columns, null, bounds)
    {
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        if (inventory is BrowserInventory browserInventory && hoverSlotId >= 0 && browserInventory[hoverSlotId].Itemstack is not null)
        {
            consumedClick = true;
            browserInventory.TriggerClick(hoverSlotId, args.Button);
            args.Handled = true;
        }
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        if (!consumedClick) return;
        consumedClick = false;
        args.Handled = true;
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        // GuiComposer may dispatch both mouse-up methods. Neither may reach
        // GuiElementItemSlotGrid after our custom navigation mouse-down.
        consumedClick = false;
        args.Handled = true;
    }
}

internal static class TransparentItemSlotGridComposerExtensions
{
    public static GuiComposer AddTransparentItemSlotGrid(this GuiComposer composer, IInventory inventory, int columns, ElementBounds bounds, string key, bool ignoreEmptySlots = false)
    {
        if (!composer.Composed)
        {
            composer.AddInteractiveElement(new TransparentItemSlotGrid(composer.Api, inventory, _ => { }, columns, bounds, ignoreEmptySlots), key);
            GuiElementItemSlotGridBase.UpdateLastSlotGridFlag(composer);
        }
        return composer;
    }


    public static GuiComposer AddNavigationItemSlotGrid(this GuiComposer composer, BrowserInventory inventory, int columns, ElementBounds bounds, string key)
    {
        if (!composer.Composed)
        {
            composer.AddInteractiveElement(new NavigationItemSlotGrid(composer.Api, inventory, columns, bounds), key);
            GuiElementItemSlotGridBase.UpdateLastSlotGridFlag(composer);
        }
        return composer;
    }
}
