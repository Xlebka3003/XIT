using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VsRecipeBrowser;

internal static class InventorySorter
{
    private static bool sorting;
    private static long lastSortMs;
    internal static bool Enabled { get; private set; } = true;
    internal static void ReloadSettings(ICoreClientAPI api) =>
        Enabled = (api.LoadModConfig<RecipeBrowserConfig>("vsrecipebrowser.json") ?? new()).SortingEnabled;

    internal static bool Sort(ICoreClientAPI api, IInventory? container = null)
    {
        if (!Enabled) return false;
        if (sorting) return true;
        if (api.ElapsedMilliseconds - lastSortMs < 1000) return true;
        if (container is null && !api.OpenedGuis.OfType<GuiDialog>().Any(gui => gui.IsOpened()
            && gui.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory")) return false;
        if (container is not null && (container.TakeLocked || container.PutLocked
            || !api.OpenedGuis.OfType<GuiDialogBlockEntityInventory>().Any(gui => gui.IsOpened() && gui.Inventory == container))) return false;
        var manager = api.World.Player.InventoryManager;
        if (!manager.MouseItemSlot.Empty)
        {
            Notify(api, BrowserText.Choose("Сначала уберите предмет с курсора.", "Put down the item held by the cursor first."));
            return true;
        }
        var inventories = container is not null ? new[] { container } : manager.Inventories.Values.Where(inv => inv.ClassName == "backpack"
            && !inv.TakeLocked && !inv.PutLocked).Distinct().ToArray();
        var slots = inventories.SelectMany(inv => Enumerable.Range(0, inv.Count).Select(i => inv[i]))
            .Where(slot => container is not null || slot is ItemSlotBagContent).ToArray();
        if (slots.Length == 0) return true;
        lastSortMs = api.ElapsedMilliseconds;
        sorting = true;
        int operations = 0;
        try
        {
            // Use vanilla predictive transfers and their packets only. Do not
            // assign Itemstack/StackSize directly or touch bag attachment slots.
            for (int i = 0; i < slots.Length; i++)
            for (int j = i + 1; j < slots.Length; j++)
            {
                var target = slots[i]; var source = slots[j];
                if (source.Empty || target.Empty || !source.CanTake() || !target.CanHold(source)
                    || !source.Itemstack.Equals(api.World, target.Itemstack, GlobalConstants.IgnoredStackAttributes)) continue;
                int room = Math.Min(target.MaxSlotStackSize, target.Itemstack.Collectible.MaxStackSize) - target.StackSize;
                if (room <= 0) continue;
                if (!Transfer(source, target, Math.Min(room, source.StackSize))) return true;
            }
            // Selection sort permits filled inventories: vanilla slot flips
            // swap two stacks without requiring an empty temporary slot.
            for (int i = 0; i < slots.Length; i++)
            {
                var target = slots[i];
                int best = i;
                for (int j = i + 1; j < slots.Length; j++)
                {
                    var candidate = slots[j];
                    if (candidate.Empty || !candidate.CanTake() || !target.CanHold(candidate)
                        || (!target.Empty && (!target.CanTake() || !candidate.CanHold(target)))) continue;
                    if (Compare(candidate.Itemstack, slots[best].Itemstack) < 0) best = j;
                }
                if (best == i) continue;
                var source = slots[best];
                if (target.Empty)
                {
                    if (!Transfer(source, target, source.StackSize)) return true;
                }
                else
                {
                    var packet = target.Inventory.TryFlipItems(target.Inventory.GetSlotId(target), source);
                    if (packet is null) { Notify(api, BrowserText.Choose("Сортировка прервана: перестановка недоступна.", "Sorting stopped: slot swap unavailable.")); return true; }
                    api.Network.SendPacketClient(packet);
                    operations++;
                }
            }
            Notify(api, BrowserText.Choose("Инвентарь отсортирован.", "Inventory sorted."));
            api.Logger.Notification("[XRB Sort] Backpack slots={0}, vanilla operations={1}", slots.Length, operations);
        }
        catch (Exception exception)
        {
            api.Logger.Error("[XRB Sort] Sorting stopped: {0}", exception);
            Notify(api, BrowserText.Choose("Сортировка остановлена. Подробности в журнале.", "Sorting stopped. See the log for details."));
        }
        finally { sorting = false; }
        return true;

        bool Transfer(ItemSlot source, ItemSlot target, int quantity)
        {
            var op = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, quantity)
                { ActingPlayer = api.World.Player };
            var packet = manager.TryTransferTo(source, target, ref op);
            if (packet is not null) api.Network.SendPacketClient(packet);
            operations++;
            if (packet is not null && op.MovedQuantity == quantity) return true;
            Notify(api, BrowserText.Choose("Сортировка прервана. Проверьте инвентарь.", "Sorting interrupted. Check your inventory."));
            return false;
        }
    }

    private static int Compare(ItemStack? a, ItemStack? b)
    {
        if (a is null) return b is null ? 0 : 1;
        if (b is null) return -1;
        int category = InventorySortOrder.Group(a).CompareTo(InventorySortOrder.Group(b));
        if (category != 0) return category;
        int family = StringComparer.Ordinal.Compare(InventorySortOrder.Family(a), InventorySortOrder.Family(b));
        if (family != 0) return family;
        int material = StringComparer.Ordinal.Compare(InventorySortOrder.Material(a), InventorySortOrder.Material(b));
        if (material != 0) return material;
        int name = StringComparer.CurrentCultureIgnoreCase.Compare(a.GetName(), b.GetName());
        if (name != 0) return name;
        int variant = StringComparer.Ordinal.Compare(FavoriteIdentity.Key(a), FavoriteIdentity.Key(b));
        return variant != 0 ? variant : b.StackSize.CompareTo(a.StackSize);
    }

    private static void Notify(ICoreClientAPI api, string message) => api.TriggerIngameError(typeof(InventorySorter), "xrb-sort", message);
}
