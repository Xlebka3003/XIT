using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace XrbRecipeChoice;

public sealed class RecipeChoiceServer : ModSystem
{
    private ICoreServerAPI? api;
    private IServerNetworkChannel? channel;
    private Harmony? harmony;
    private readonly Dictionary<string, long> lastRequests = new();
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        channel = api.Network.RegisterChannel(VsRecipeBrowser.XrbProtocol.ChoiceChannel)
            .RegisterMessageType<ChoiceRequest>().RegisterMessageType<ChoiceReply>();
        channel.SetMessageHandler<ChoiceRequest>(OnRequest);
        harmony = new Harmony("xrb.recipechoice.server");
        harmony.CreateClassProcessor(typeof(SelectedRecipePatch)).Patch();
        api.Event.PlayerDisconnect += player => lastRequests.Remove(player.PlayerUID);
    }
    private void OnRequest(IServerPlayer player, ChoiceRequest request)
    {
        try { ProcessRequest(player, request); }
        catch (Exception exception)
        {
            api?.Logger.Error("[XRB Recipe Choice] Request failed: {0}", exception);
            channel?.SendPacket(new ChoiceReply { Protocol = VsRecipeBrowser.XrbProtocol.Version, Revision = request.Revision }, player);
        }
    }
    private void ProcessRequest(IServerPlayer player, ChoiceRequest request)
    {
        if (api is null || channel is null) return;
        if (!VsRecipeBrowser.XrbProtocol.Compatible(request.Protocol))
        {
            channel.SendPacket(new ChoiceReply { Protocol = VsRecipeBrowser.XrbProtocol.Version, Revision = request.Revision }, player);
            return;
        }
        long now = Environment.TickCount64;
        if (lastRequests.TryGetValue(player.PlayerUID, out long last) && now - last < 100) return;
        lastRequests[player.PlayerUID] = now;
        var grid = player.InventoryManager.Inventories.Values.OfType<InventoryCraftingGrid>().FirstOrDefault();
        if (grid is null || grid.TakeLocked || grid.PutLocked) return;
        var reply = new ChoiceReply { Protocol = VsRecipeBrowser.XrbProtocol.Version, Snapshot = ChoiceSelection.Signature(grid), Revision = request.Revision };
        if (reply.Snapshot != request.Snapshot) { channel.SendPacket(reply, player); return; }
        var slots = ChoiceSelection.Slots(grid);
        var first = slots.FirstOrDefault(slot => !slot.Empty)?.Itemstack;
        var candidates = first is null ? new List<GridRecipe>() : api.World.FastSearchRecipesByIngredient
            .Where(pair => pair.Key.SatisfiesAsIngredient(first, false)).SelectMany(pair => pair.Value)
            .OfType<GridRecipe>().Distinct().Where(recipe => recipe.Enabled && recipe.Matches(player, api.World, slots, 3)).ToList();
        if (request.RecipeId >= 0)
        {
            var chosen = candidates.FirstOrDefault(recipe => api.World.GridRecipes.IndexOf(recipe) + 1 == request.RecipeId);
            if (chosen is not null)
            {
                ChoiceSelection.Select(grid, chosen);
                grid.MarkSlotDirty(9);
                reply.Accepted = true;
            }
        }
        reply.Selected = grid.MatchingRecipe is { } matching ? api.World.GridRecipes.IndexOf(matching) + 1 : -1;
        if (request.RecipeId >= 0) api.Logger.Notification("[XRB Recipe Choice] Player={0}, requested={1}, selected={2}, accepted={3}",
            player.PlayerUID, request.RecipeId, reply.Selected, reply.Accepted);
        var seenOutputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in candidates.OrderByDescending(recipe => ReferenceEquals(recipe, grid.MatchingRecipe)))
        {
            var output = new DummySlot();
            recipe.GenerateOutputStack(slots, output);
            if (output.Itemstack is not null && seenOutputs.Add(ChoiceSelection.OutputKey(output.Itemstack)))
                reply.Options.Add(new ChoiceOption { RecipeId = api.World.GridRecipes.IndexOf(recipe) + 1,
                    RecipeKey = ChoiceSelection.RecipeKey(recipe), Stack = output.Itemstack.ToBytes() });
            if (reply.Options.Count >= 100) break;
        }
        channel.SendPacket(reply, player);
    }
    public override void Dispose() => harmony?.UnpatchAll("xrb.recipechoice.server");
}
