using Vintagestory.API.Common;

namespace VsRecipeBrowser;

internal sealed class BrowserInventory : InventoryGeneric
{
    public Action<int, EnumMouseButton>? SlotClicked { get; set; }

    public BrowserInventory(int quantitySlots, ICoreAPI api)
        : base(quantitySlots, "vsrecipebrowser", "client", api, null)
    {
    }

    protected override ItemSlot NewSlot(int slotId) => new(this);

    public void TriggerClick(int slotId, EnumMouseButton button) => SlotClicked?.Invoke(slotId, button);

    public override object? ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        TriggerClick(slotId, op.MouseButton);
        return null;
    }
}
