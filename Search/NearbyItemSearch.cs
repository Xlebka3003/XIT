using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

[ProtoContract]
public sealed class NearbySearchRequest
{
    [ProtoMember(6)] public int Protocol { get; set; }
    [ProtoMember(1)] public int Revision { get; set; }
    [ProtoMember(2)] public string Code { get; set; } = "";
    [ProtoMember(3)] public bool IsBlock { get; set; }
    [ProtoMember(4)] public int Radius { get; set; } = 16;
    [ProtoMember(5)] public string Identity { get; set; } = "";
}
[ProtoContract]
public sealed class NearbySearchHit
{
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }
    [ProtoMember(4)] public int Dimension { get; set; }
}
[ProtoContract]
public sealed class NearbySearchReply
{
    [ProtoMember(4)] public int Protocol { get; set; }
    [ProtoMember(1)] public int Revision { get; set; }
    [ProtoMember(2)] public List<NearbySearchHit> Hits { get; set; } = new();
    [ProtoMember(3)] public bool Truncated { get; set; }
}

#if XRB_SERVER || XRB_UNIFIED
// Only the server can inspect current contents of unopened containers.
public sealed class NearbyItemSearchServer : ModSystem
{
    private ICoreServerAPI? api;
    private IServerNetworkChannel? channel;
    private readonly Dictionary<string, long> lastRequests = new();
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        channel = api.Network.RegisterChannel(XrbProtocol.NearbyChannel)
            .RegisterMessageType<NearbySearchRequest>().RegisterMessageType<NearbySearchReply>();
        channel.SetMessageHandler<NearbySearchRequest>(Search);
        api.Event.PlayerDisconnect += player => lastRequests.Remove(player.PlayerUID);
    }
    private void Search(IServerPlayer player, NearbySearchRequest request)
    {
        if (api is null || channel is null) return;
        long now = Environment.TickCount64;
        if (lastRequests.TryGetValue(player.PlayerUID, out long last) && now - last < 750) return;
        lastRequests[player.PlayerUID] = now;
        var reply = new NearbySearchReply { Revision = request.Revision, Protocol = XrbProtocol.Version };
        try
        {
            if (!XrbProtocol.Compatible(request.Protocol)) return;
            if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 256 || request.Identity.Length > 400) return;
            var code = new AssetLocation(request.Code);
            CollectibleObject? target = request.IsBlock ? api.World.GetBlock(code) : api.World.GetItem(code);
            if (target is null) return;
            int radius = Math.Clamp(request.Radius, 4, 24);
            var center = player.Entity.Pos.AsBlockPos;
            int chunkSize = Vintagestory.API.Config.GlobalConstants.ChunkSize;
            var chunks = new HashSet<IWorldChunk>();
            int checkedSlots = 0, checkedEntities = 0;
            var reinforcement = api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            for (int dx = -radius; dx <= radius + chunkSize; dx += chunkSize)
            for (int dy = -radius; dy <= radius + chunkSize; dy += chunkSize)
            for (int dz = -radius; dz <= radius + chunkSize; dz += chunkSize)
            {
                var chunk = api.World.BlockAccessor.GetChunkAtBlockPos(center.AddCopy(dx, dy, dz));
                if (chunk is null || !chunks.Add(chunk)) continue;
                foreach (var entity in chunk.BlockEntities.Values.ToArray())
                {
                    if (++checkedEntities > 4096) { reply.Truncated = true; return; }
                    var pos = entity.Pos;
                    if (pos.dimension != center.dimension || pos.DistanceSqTo(center.X, center.Y, center.Z) > radius * radius
                        || entity is not IBlockEntityContainer container
                        || api.World.Claims.TestAccess(player, pos, EnumBlockAccessFlags.Use) != EnumWorldAccessResponse.Granted
                        || reinforcement?.IsLockedForInteract(pos, player) == true
                        || container.Inventory.TakeLocked) continue;
                    bool found = false;
                    foreach (var slot in container.Inventory)
                    {
                        if (++checkedSlots > 8192) { reply.Truncated = true; break; }
                        if (slot.Itemstack is { } stack && stack.Collectible == target
                            && FavoriteIdentity.Key(stack) == request.Identity) { found = true; break; }
                    }
                    if (found) reply.Hits.Add(new NearbySearchHit { X = pos.X, Y = pos.Y, Z = pos.Z, Dimension = pos.dimension });
                    if (reply.Hits.Count >= 128 || reply.Truncated) { reply.Truncated = true; return; }
                }
            }
        }
        catch (Exception error) { api.Logger.Warning("[XRB item search] {0}", error.Message); reply.Hits.Clear(); }
        finally { channel.SendPacket(reply, player); }
    }
}
#endif

#if !XRB_SERVER
// Without a compatible server, use explicitly labelled snapshots of opened containers.
public sealed class NearbyItemSearchClient : ModSystem, IRenderer
{
    internal static NearbyItemSearchClient? Instance;
    private ICoreClientAPI? api;
    private IClientNetworkChannel? channel;
    private int revision;
    private long sentAt, expiresAt, tickId;
    private bool pending, lit;
    private List<BlockPos> hits = new();
    private RememberedContainers memory = new();
    private string memoryFile = "";
    private bool memoryDirty;
    private long savedAt;
    private readonly Dictionary<string, (BlockPos Position, IInventory Inventory)> observed = new();
    private string pendingIdentity = "";
    private int pendingRadius;
    private long serverRetryAt;
    internal static bool UsesRememberedMode(bool connected, long now, long retryAt) => !connected || now < retryAt;
    private const int HighlightId = 0x585242;
    private MeshRef? outlineMesh;
    private BlockPos? outlineOrigin;
    private LoadedTexture? slotHighlight;
    private readonly HashSet<IInventory> visibleContainers = new();
    public double RenderOrder => 0.99;
    public int RenderRange => 64;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api; Instance = this;
        channel = api.Network.RegisterChannel(XrbProtocol.NearbyChannel)
            .RegisterMessageType<NearbySearchRequest>().RegisterMessageType<NearbySearchReply>();
        channel.SetMessageHandler<NearbySearchReply>(reply => api.Event.EnqueueMainThreadTask(() => Receive(reply), "xrb-nearby-reply"));
        tickId = api.Event.RegisterGameTickListener(Tick, 250);
        api.Event.RegisterRenderer(this, EnumRenderStage.AfterFinalComposition, "xrb-container-outlines");
    }
    internal bool Find(ItemStack? stack)
    {
        if (api is null || stack is null || !BrowserSettings.Current.ItemSearchEnabled) return false;
        if (api.ElapsedMilliseconds - sentAt < 800) return true;
        ObserveContainers();
        Clear();
        sentAt = api.ElapsedMilliseconds; revision++;
        var config = api.LoadModConfig<RecipeBrowserConfig>("vsrecipebrowser.json") ?? new();
        pendingIdentity = FavoriteIdentity.Key(stack); pendingRadius = Math.Clamp(config.ItemSearchRadius, 4, 24);
        if (UsesRememberedMode(channel?.Connected == true, api.ElapsedMilliseconds, serverRetryAt)) { pending = false; FindRemembered(); return true; }
        pending = true;
        channel.SendPacket(new NearbySearchRequest { Protocol = XrbProtocol.Version, Revision = revision, Code = stack.Collectible.Code.ToString(),
            IsBlock = stack.Class == EnumItemClass.Block, Radius = pendingRadius, Identity = pendingIdentity });
        return true;
    }
    private void Receive(NearbySearchReply reply)
    {
        if (api is null || !pending || reply.Revision != revision) return;
        if (!XrbProtocol.Compatible(reply.Protocol))
        {
            pending = false; serverRetryAt = api.ElapsedMilliseconds + 30000;
            ObserveContainers(); FindRemembered(); return;
        }
        pending = false;
        serverRetryAt = 0;
        int dimension = api.World.Player.Entity.Pos.AsBlockPos.dimension;
        hits = reply.Hits.Take(128).Where(hit => hit.Dimension == dimension)
            .Select(hit => new BlockPos(hit.X, hit.Y, hit.Z, hit.Dimension)).Distinct().ToList();
        expiresAt = api.ElapsedMilliseconds + 8000;
        BuildOutlines();
        if (reply.Truncated) api.ShowChatMessage(BrowserText.Choose("XRB: достигнут лимит поиска.", "XRB: search limit reached."));
        Tick(0);
    }
    private void Tick(float _)
    {
        if (api?.World?.Player is null) return;
        ObserveContainers();
        if (pending && api.ElapsedMilliseconds - sentAt > 5000)
        {
            pending = false;
            serverRetryAt = api.ElapsedMilliseconds + 30000;
            ObserveContainers();
            FindRemembered();
        }
        if (expiresAt > 0 && api.ElapsedMilliseconds >= expiresAt) { Clear(); return; }
        if (hits.Count == 0) return;
        if (hits[0].dimension != api.World.Player.Entity.Pos.AsBlockPos.dimension) { Clear(); return; }
        bool show = api.ElapsedMilliseconds / 500 % 2 == 0;
        if (show == lit) return;
        lit = show;
        // Blink the cached edge mesh, never rebuild or upload it per frame.
    }
    private void ObserveContainers()
    {
        if (api?.World?.Player is null) return;
        string world = api.World.SavegameIdentifier?.ToString() ?? "";
        if (world.Length == 0) return;
        string file = "xrb-container-memory-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(world + ":" + api.World.Player.PlayerUID)))[..24] + ".json";
        if (file != memoryFile)
        {
            SaveMemory(); observed.Clear(); Clear(); pending = false; revision++; pendingIdentity = ""; serverRetryAt = 0;
            memoryFile = file;
            memory = api.LoadModConfig<RememberedContainers>(file) ?? new();
            memory.Entries = memory.Entries.Where(entry => entry is not null && entry.Items is not null).Take(512).ToList();
            memoryDirty = false;
        }
        var current = new Dictionary<string, (BlockPos Position, IInventory Inventory)>();
        foreach (var dialog in api.OpenedGuis.OfType<GuiDialogBlockEntity>().Where(dialog => dialog.IsOpened()).ToArray())
        {
            var pos = dialog.BlockEntityPosition;
            var inventory = (dialog as GuiDialogBlockEntityInventory)?.Inventory
                ?? (api.World.BlockAccessor.GetBlockEntity(pos) as IBlockEntityContainer)?.Inventory;
            if (inventory is null) continue;
            string key = $"{pos.dimension}:{pos.X}:{pos.Y}:{pos.Z}";
            current[key] = (pos.Copy(), inventory);
        }
        // Include the final known contents when an observed window closes.
        foreach (var pair in observed.Concat(current).GroupBy(pair => pair.Key).Select(group => group.Last()))
        {
            var pos = pair.Value.Position;
            var block = api.World.BlockAccessor.GetBlock(pos);
            if (block?.Code is null || block.Id == 0) continue;
            var identities = pair.Value.Inventory.Where(slot => slot.Itemstack is not null)
                .Take(2048).Select(slot => FavoriteIdentity.Key(slot.Itemstack!)).ToHashSet(StringComparer.Ordinal);
            if (UsesRememberedMode(channel?.Connected == true, api.ElapsedMilliseconds, serverRetryAt)) memoryDirty |= memory.Remember(new RememberedContainer { X = pos.X, Y = pos.Y, Z = pos.Z,
                Dimension = pos.dimension, BlockCode = block.Code.ToString(), Items = identities, SeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        }
        observed.Clear(); foreach (var pair in current) observed[pair.Key] = pair.Value;
        visibleContainers.Clear(); foreach (var pair in current) visibleContainers.Add(pair.Value.Inventory);
        if (memoryDirty && api.ElapsedMilliseconds - savedAt >= 10000) SaveMemory();
    }
    private void SaveMemory()
    {
        if (api is null || !memoryDirty || memoryFile.Length == 0) return;
        try { api.StoreModConfig(memory, memoryFile); memoryDirty = false; savedAt = api.ElapsedMilliseconds; }
        catch (Exception error) { api.Logger.Warning("[XRB container memory] {0}", error.Message); savedAt = api.ElapsedMilliseconds; }
    }
    private void FindRemembered()
    {
        if (api?.World?.Player is null) return;
        var center = api.World.Player.Entity.Pos.AsBlockPos;
        hits = memory.Find(pendingIdentity, center.X, center.Y, center.Z, center.dimension, pendingRadius)
            .Where(entry => api.World.BlockAccessor.GetBlock(new BlockPos(entry.X, entry.Y, entry.Z, entry.Dimension))?.Code?.ToString() == entry.BlockCode)
            .Select(entry => new BlockPos(entry.X, entry.Y, entry.Z, entry.Dimension)).ToList();
        expiresAt = api.ElapsedMilliseconds + 8000;
        BuildOutlines();
        api.ShowChatMessage(BrowserText.Choose("XRB: поиск по последнему известному содержимому. Данные могли устареть; откройте сундук для обновления.",
            "XRB: searching last known contents. Data may be outdated; open containers to refresh."));
    }
    private void Clear()
    {
        if (api?.World?.Player is not null)
            api.World.HighlightBlocks(api.World.Player, HighlightId, new(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
        hits.Clear(); lit = false; expiresAt = 0;
        pendingIdentity = "";
        outlineMesh?.Dispose(); outlineMesh = null; outlineOrigin = null;
    }
    private void BuildOutlines()
    {
        outlineMesh?.Dispose(); outlineMesh = null;
        if (api is null || hits.Count == 0) return;
        outlineOrigin = hits[0].Copy();
        var mesh = new MeshData(1024, 3072, withNormals: false, withUv: false, withRgba: true, withFlags: false);
        mesh.SetMode(EnumDrawMode.Lines);
        int color = ColorUtil.ToRgba(255, 240, 225, 120); // Light turquoise RGB 120,225,240; API uses ABGR.
        var outlinePositions = new HashSet<BlockPos>();
        foreach (var controller in hits)
        {
            outlinePositions.Add(controller);
            var block = api.World.BlockAccessor.GetBlock(controller);
            var behavior = block.BlockBehaviors.FirstOrDefault(value => value.GetType().Name == "BlockBehaviorMultiblock");
            var iterate = behavior?.GetType().GetMethod("IterateOverEach");
            if (iterate is not null)
            {
                int count = 0;
                ActionConsumable<BlockPos> collect = part => { if (++count > 64) return false; outlinePositions.Add(part.Copy()); return true; };
                iterate.Invoke(behavior, new object[] { controller, collect });
            }
        }
        foreach (var pos in outlinePositions)
        {
            var boxes = api.World.BlockAccessor.GetBlock(pos).GetSelectionBoxes(api.World.BlockAccessor, pos);
            foreach (var box in (boxes ?? new[] { new Cuboidf(0, 0, 0, 1, 1, 1) }).Take(16))
            {
                const float pad = 0.004f;
                float x = pos.X - outlineOrigin.X, y = pos.Y - outlineOrigin.Y, z = pos.Z - outlineOrigin.Z;
                int first = mesh.VerticesCount;
                for (int corner = 0; corner < 8; corner++)
                    mesh.AddVertexSkipTex(x + ((corner & 1) == 0 ? box.X1 - pad : box.X2 + pad),
                        y + ((corner & 2) == 0 ? box.Y1 - pad : box.Y2 + pad),
                        z + ((corner & 4) == 0 ? box.Z1 - pad : box.Z2 + pad), color);
                for (int corner = 0; corner < 8; corner++)
                for (int axis = 1; axis <= 4; axis *= 2)
                    if ((corner & axis) == 0) { mesh.AddIndex(first + corner); mesh.AddIndex(first + (corner | axis)); }
            }
        }
        if (mesh.VerticesCount > 0) outlineMesh = api.Render.UploadMesh(mesh);
    }
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!BrowserSettings.Current.WorldSearchHighlightEnabled || !BrowserSettings.Current.ItemSearchEnabled
            || api?.World?.Player is null || !lit || outlineMesh is null || outlineOrigin is null
            || api.ElapsedMilliseconds >= expiresAt || outlineOrigin.dimension != api.World.Player.Entity.Pos.AsBlockPos.dimension) return;
        var previous = api.Render.CurrentActiveShader;
        previous?.Stop();
        var shader = api.Render.GetEngineShader(EnumShaderProgram.Autocamera);
        shader.Use();
        api.Render.GLDisableDepthTest();
        try
        {
            var camera = api.World.Player.Entity.CameraPos;
            var model = new Matrixf().Set(api.Render.CameraMatrixOriginf)
                .Translate((float)(outlineOrigin.X - camera.X), (float)(outlineOrigin.Y - camera.Y), (float)(outlineOrigin.Z - camera.Z));
            shader.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
            shader.UniformMatrix("modelViewMatrix", model.Values);
            api.Render.BindTexture2d(0);
            api.Render.RenderMesh(outlineMesh);
        }
        finally { api.Render.GLEnableDepthTest(); shader.Stop(); previous?.Use(); }
    }
    public override void Dispose()
    {
        SaveMemory(); observed.Clear();
        visibleContainers.Clear(); slotHighlight?.Dispose(); slotHighlight = null;
        Clear(); pending = false;
        api?.Event.UnregisterGameTickListener(tickId);
        api?.Event.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);
        if (ReferenceEquals(Instance, this)) Instance = null;
        base.Dispose();
    }

    internal static bool ShouldHighlightSlots(bool waiting, string identity, int hitCount, long expiry, long now) =>
        !waiting && identity.Length > 0 && hitCount > 0 && expiry > now;

    internal void DrawMatchingSlots(GuiElementItemSlotGridBase grid)
    {
        if (!BrowserSettings.Current.SlotSearchHighlightEnabled || !BrowserSettings.Current.ItemSearchEnabled
            || api?.World?.Player is null || visibleContainers.Count == 0
            || !ShouldHighlightSlots(pending, pendingIdentity, hits.Count, expiresAt, api.ElapsedMilliseconds)) return;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(GuiElementItemSlotGridBase);
        if (type.GetField("inventory", flags)?.GetValue(grid) is not IInventory inventory || !visibleContainers.Contains(inventory)
            || type.GetField("SlotBounds", flags)?.GetValue(grid) is not ElementBounds[] bounds
            || type.GetField("availableSlots", flags)?.GetValue(grid) is not Vintagestory.API.Datastructures.OrderedDictionary<int, ItemSlot> available) return;
        // Item icons are 3D meshes: their depth can cover an otherwise later
        // 2D draw. Overlay the tint/border after them without depth testing.
        api.Render.GLDisableDepthTest();
        try
        {
        for (int i = 0; i < Math.Min(bounds.Length, inventory.Count); i++)
        {
            int id = available.GetKeyAtIndex(i);
            if (!grid.renderedSlots.ContainsKey(id)) continue;
            var slot = inventory[id];
            if (slot.Itemstack is null || FavoriteIdentity.Key(slot.Itemstack) != pendingIdentity) continue;
            if (slotHighlight is null)
            {
                slotHighlight = new LoadedTexture(api);
                using var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, 48, 48);
                using var context = new Cairo.Context(surface);
                context.SetSourceRGBA(120 / 255d, 225 / 255d, 240 / 255d, 0.22); context.Rectangle(2, 2, 44, 44); context.Fill();
                context.SetSourceRGBA(120 / 255d, 225 / 255d, 240 / 255d, 0.95); context.LineWidth = 2; context.Rectangle(2, 2, 44, 44); context.Stroke();
                surface.Flush(); api.Gui.LoadOrUpdateCairoTexture(surface, false, ref slotHighlight);
            }
            var box = bounds[i];
            api.Render.PushScissor(box, true);
            try
            {
                api.Render.Render2DTexturePremultipliedAlpha(slotHighlight.TextureId, box.renderX, box.renderY, box.OuterWidth, box.OuterHeight, 115, ColorUtil.WhiteArgbVec);
            }
            finally { api.Render.PopScissor(); }
        }
        }
        finally { api.Render.GLEnableDepthTest(); }
    }
}

[HarmonyLib.HarmonyPatch(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.RenderInteractiveElements))]
internal static class ContainerSearchSlotPatch
{
    [HarmonyLib.HarmonyPostfix]
    private static void Postfix(GuiElementItemSlotGridBase __instance) => NearbyItemSearchClient.Instance?.DrawMatchingSlots(__instance);
}
#endif
