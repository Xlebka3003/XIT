using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;
using XrbRecipeChoice;

namespace VsRecipeBrowser;

public sealed class RecipeChoiceClient : ModSystem
{
    internal static RecipeChoiceClient? Instance;
    private ICoreClientAPI? api;
    private IClientNetworkChannel? channel;
    private long tickId;
    private string snapshot = "";
    private int revision;
    private long sentAt;
    internal bool Pending { get; private set; }
    private bool awaitingReply;
    private ChoiceReply? reply;
    private ChoiceButtonDialog? button;
    private ChoicePickerDialog? picker;
    private bool queried;
    private bool protocolRejected;
    private string worldKey = "";
    private readonly Dictionary<string, GridRecipe> confirmedRecipes = new(StringComparer.Ordinal);
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api; Instance = this;
        channel = api.Network.RegisterChannel(XrbProtocol.ChoiceChannel)
            .RegisterMessageType<ChoiceRequest>().RegisterMessageType<ChoiceReply>();
        channel.SetMessageHandler<ChoiceReply>(response => api.Event.EnqueueMainThreadTask(() => OnReply(response), "xrb-choice-reply"));
        tickId = api.Event.RegisterGameTickListener(Tick, 150);
    }
    private InventoryCraftingGrid? Grid() => api?.World?.Player?.InventoryManager.Inventories.Values.OfType<InventoryCraftingGrid>().FirstOrDefault();
    private GuiDialog? InventoryDialog() => api?.OpenedGuis.OfType<GuiDialog>()
        .FirstOrDefault(g => g.IsOpened() && g.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory");
    private void Tick(float _)
    {
        string currentWorld = api?.World?.SavegameIdentifier?.ToString() ?? "";
        if (currentWorld != worldKey)
        {
            worldKey = currentWorld; protocolRejected = false; confirmedRecipes.Clear();
            CloseUi(); snapshot = ""; queried = false; reply = null; Pending = false; awaitingReply = false;
        }
        var grid = Grid(); var dialog = InventoryDialog();
        if (protocolRejected || !BrowserSettings.Current.RecipeChoiceEnabled || api is null || grid is null || dialog is null || channel?.Connected != true)
        { CloseUi(); snapshot = ""; queried = false; Pending = false; awaitingReply = false; return; }
        string current = ChoiceSelection.Signature(grid);
        if (awaitingReply && api.ElapsedMilliseconds - sentAt > 5000)
        {
            Pending = false; awaitingReply = false; queried = false;
            api.TriggerIngameError(this, "xrb-choice-timeout", BrowserText.Choose("Сервер не подтвердил выбор рецепта.", "The server did not confirm the recipe selection."));
        }
        if (current != snapshot)
        { snapshot = current; queried = false; reply = null; Pending = false; awaitingReply = false; CloseUi(); revision++; }
        if (!queried && !awaitingReply)
        {
            queried = true; Send(-1);
        }
        if (reply?.Options.Count > 1 && !Pending && OutputBounds(dialog, grid) is { } output)
        {
            button ??= new ChoiceButtonDialog(api, OpenPicker, () => InventoryDialog() is { } owner && Grid() is { } currentGrid ? OutputBounds(owner, currentGrid) : null);
            button.Position(output);
            if (!button.IsOpened()) button.TryOpen();
        }
        else button?.TryClose();
    }
    private void Send(int recipeId)
    {
        if (channel is null || api is null) return;
        Pending = recipeId >= 0; awaitingReply = true; sentAt = api.ElapsedMilliseconds;
        if (recipeId >= 0) api.Logger.Notification("[XRB Recipe Choice] Selecting recipe {0}, revision {1}", recipeId, revision + 1);
        channel.SendPacket(new ChoiceRequest { Protocol = XrbProtocol.Version, Snapshot = snapshot, RecipeId = recipeId, Revision = ++revision });
    }
    private void OnReply(ChoiceReply response)
    {
        var grid = Grid();
        if (api is null || grid is null || response.Revision != revision) return;
        if (!XrbProtocol.Compatible(response.Protocol))
        { protocolRejected = true; Pending = false; awaitingReply = false; reply = null; CloseUi(); return; }
        Pending = false; awaitingReply = false;
        api.Logger.Notification("[XRB Recipe Choice] Reply revision={0}, selected={1}, accepted={2}, options={3}",
            response.Revision, response.Selected, response.Accepted, response.Options.Count);
        if (response.Snapshot.Length == 0) { reply = null; return; } // Server error: do not spam repeated requests.
        if (response.Snapshot != snapshot || ChoiceSelection.Signature(grid) != snapshot) { queried = false; return; }
        // Older protocol-v1 servers can still send one option per recipe.
        // Keep the currently selected representative of each distinct result.
        var seenOutputs = new HashSet<string>(StringComparer.Ordinal);
        response.Options = response.Options.OrderByDescending(option => option.RecipeId == response.Selected).Where(option => {
            try
            {
            var stack = new ItemStack(option.Stack);
            return stack.ResolveBlockOrItem(api.World) && seenOutputs.Add(ChoiceSelection.OutputKey(stack));
            }
            catch { return false; }
        }).ToList();
        reply = response;
        if (response.Selected >= 0)
        {
            var option = response.Options.FirstOrDefault(option => option.RecipeId == response.Selected);
            if (option is null) return;
            var expected = new ItemStack(option.Stack);
            if (!expected.ResolveBlockOrItem(api.World)) return;
            var slots = ChoiceSelection.Slots(grid);
            bool ConfirmedOutput(GridRecipe recipe)
            {
                if (!recipe.Enabled || !recipe.Matches(api.World.Player, api.World, slots, 3)) return false;
                var generated = new DummySlot();
                recipe.GenerateOutputStack(slots, generated);
                return generated.Itemstack is { } result && result.StackSize == expected.StackSize
                    && result.Equals(api.World, expected, Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes);
            }
            GridRecipe? chosen = confirmedRecipes.TryGetValue(option.RecipeKey, out var cached) && ConfirmedOutput(cached) ? cached : null;
            // Grid recipe registry order is synchronized by the game. Verify
            // the output too, rather than trusting a numeric index alone.
            var indexed = api.World.GridRecipes.ElementAtOrDefault(response.Selected - 1);
            if (chosen is null && indexed is not null && ConfirmedOutput(indexed)) chosen = indexed;
            // Some mods reorder client recipes or drop source-only metadata.
            // Resolve by the server-confirmed output, never by client preference.
            chosen ??= api.World.GridRecipes.FirstOrDefault(ConfirmedOutput);
            if (chosen is not null)
            {
                confirmedRecipes[option.RecipeKey] = chosen;
                ChoiceSelection.Select(grid, chosen);
                api.Logger.Notification("[XRB Recipe Choice] Client restored confirmed output {0}", expected.Collectible.Code);
            }
            else api.Logger.Warning("[XRB Recipe Choice] No local recipe for confirmed output {0}", expected.Collectible.Code);
        }
    }
    private bool OpenPicker()
    {
        if (api is null || reply is null || reply.Options.Count <= 1 || Pending) return true;
        picker?.TryClose(); picker?.Dispose();
        picker = new ChoicePickerDialog(api, reply!.Options, recipeId =>
        {
            if (Grid() is not { } current || ChoiceSelection.Signature(current) != snapshot) return;
            picker?.TryClose();
            Send(recipeId);
        });
        picker.TryOpen();
        return true;
    }
    private static ElementBounds? OutputBounds(GuiDialog dialog, InventoryCraftingGrid grid)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        foreach (var composer in dialog.Composers.Values)
        {
            if (typeof(GuiComposer).GetField("interactiveElements", flags)?.GetValue(composer) is not Dictionary<string, GuiElement> elements) continue;
            foreach (var slots in elements.Values.OfType<GuiElementItemSlotGridBase>())
            {
                var rendered = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "renderedSlots")?.GetValue(slots);
                var bounds = AccessTools.Field(typeof(GuiElementItemSlotGridBase), "SlotBounds")?.GetValue(slots) as ElementBounds[];
                if (rendered is not IEnumerable<KeyValuePair<int, ItemSlot>> dictionary || bounds is null) continue;
                int index = 0;
                foreach (var entry in dictionary)
                {
                    if (ReferenceEquals(entry.Value, grid[9]) && index < bounds.Length) return bounds[index];
                    index++;
                }
            }
        }
        return null;
    }
    private void CloseUi()
    { button?.TryClose(); button?.Dispose(); button = null; picker?.TryClose(); picker?.Dispose(); picker = null; }
    public override void Dispose()
    { if (api is not null) api.Event.UnregisterGameTickListener(tickId); CloseUi(); if (Instance == this) Instance = null; }
}
