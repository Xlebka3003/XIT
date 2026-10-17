using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VsRecipeBrowser;

internal static class BrowserSettings
{
    internal static RecipeBrowserConfig Current { get; set; } = new();
}

public sealed class BlockAutoRefill : ModSystem
{
    internal static BlockAutoRefill? Instance;
    private ICoreClientAPI? api;
    private Harmony? harmony;
    private long tickId;
    private (ItemSlot Slot, ItemStack Template, long Expires)? pending;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api; Instance = this;
        harmony = new Harmony("xrb.block-refill");
        api.Event.LevelFinalize += InstallHooks;
        api.Event.LeaveWorld += Forget;
        tickId = api.Event.RegisterGameTickListener(Tick, 100);
    }
    private void Forget() => pending = null;
    private void InstallHooks()
    {
        if (api is null || harmony is null) return;
        harmony.UnpatchAll("xrb.block-refill");
        var signature = new[] { typeof(IWorldAccessor), typeof(IPlayer), typeof(ItemStack), typeof(BlockSelection), typeof(string).MakeByRefType() };
        var methods = api.World.Blocks.Where(block => block is not null).Select(block => block.GetType().GetMethod("TryPlaceBlock", signature))
            .Where(method => method is not null).Distinct().ToList();
        foreach (var method in methods)
            harmony.Patch(method!, prefix: new HarmonyMethod(typeof(BlockAutoRefill), nameof(BeforePlace)),
                postfix: new HarmonyMethod(typeof(BlockAutoRefill), nameof(AfterPlace)));
        api.Logger.Notification("[XRB Auto Refill] Placement hooks: {0}", methods.Count);
    }
    private sealed record Placement(ItemSlot Slot, ItemStack Template);
    private static void BeforePlace(IWorldAccessor __0, IPlayer __1, ItemStack __2, out Placement? __state)
    {
        __state = null;
        var self = Instance;
        if (self?.api is null || __0.Side != EnumAppSide.Client || __1 != self.api.World.Player
            || !BrowserSettings.Current.AutoRefillBlocksEnabled || __2?.Class != EnumItemClass.Block || __2.StackSize != 1) return;
        var active = __1.InventoryManager.ActiveHotbarSlot;
        if (ReferenceEquals(active?.Itemstack, __2)) __state = new Placement(active!, __2.Clone());
    }
    private static void AfterPlace(IPlayer __1, bool __result, Placement? __state)
    {
        if (!__result || __state is null || Instance?.api is null) return;
        if (ReferenceEquals(__1.InventoryManager.ActiveHotbarSlot, __state.Slot))
            Instance.pending = (__state.Slot, __state.Template, Instance.api.ElapsedMilliseconds + 1200);
    }
    private void Tick(float _)
    {
        if (api?.World?.Player is null || pending is not { } request) return;
        if (!BrowserSettings.Current.AutoRefillBlocksEnabled || api.ElapsedMilliseconds > request.Expires
            || !ReferenceEquals(request.Slot, api.World.Player.InventoryManager.ActiveHotbarSlot)) { pending = null; return; }
        if (!request.Slot.Empty) return; // Placement consumes the stack after TryPlaceBlock returns.
        pending = null; // One ordinary transfer, never retry optimistic client moves.
        var manager = api.World.Player.InventoryManager;
        if (manager.MouseItemSlot?.Empty == false || api.OpenedGuis.OfType<GuiDialog>().Any(gui => gui.IsOpened()
            && (gui is GuiDialogBlockEntity || gui.GetType().Name == "GuiDialogInventory"))) return;
        foreach (var inventory in manager.Inventories.Values.Where(inv => inv.ClassName == "backpack" && !inv.TakeLocked))
        foreach (var source in inventory)
        {
            if (source.Itemstack is not { } stack || !SameRefillBlock(request.Template, stack)) continue;
            if (!request.Slot.CanHold(source) || request.Slot.Inventory.PutLocked) continue;
            var operation = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, stack.StackSize);
            var packet = manager.TryTransferTo(source, request.Slot, ref operation);
            if (packet is not null) api.Network.SendPacketClient(packet);
            return;
        }
    }
    internal static bool SameRefillBlock(ItemStack template, ItemStack candidate) => template.Class == EnumItemClass.Block
        && candidate.Class == EnumItemClass.Block && FavoriteIdentity.Key(template) == FavoriteIdentity.Key(candidate);
    public override void Dispose()
    {
        pending = null; harmony?.UnpatchAll("xrb.block-refill");
        if (api is not null) { api.Event.UnregisterGameTickListener(tickId); api.Event.LevelFinalize -= InstallHooks; api.Event.LeaveWorld -= Forget; }
        if (ReferenceEquals(Instance, this)) Instance = null;
        base.Dispose();
    }
}
