using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VsRecipeBrowser;

[ProtoContract]
public sealed class RecipeTransferRequest
{
    [ProtoMember(1)] public int RecipeId { get; set; }
    [ProtoMember(2)] public bool Maximize { get; set; }
    [ProtoMember(3)] public int[] RecipeIds { get; set; } = Array.Empty<int>();
    [ProtoMember(4)] public string[] RecipeNames { get; set; } = Array.Empty<string>();
}

[ProtoContract]
public sealed class RecipeTransferResult
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = "";
}

public sealed class RecipeTransferSystem : ModSystem
{
    private static ICoreClientAPI? clientApi;
    private static readonly List<ReturnOrigin> returnOrigins = new();
    private static bool returning;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public static event Action<RecipeTransferResult>? ResultReceived;

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        returnOrigins.Clear();
    }


    public static bool Request(IEnumerable<GridRecipe> recipes, bool maximize)
    {
        if (clientApi is null) return false;
        var api = clientApi;
        var manager = api.World.Player.InventoryManager;
        var crafting = FindInventories(manager, "craftinggrid").FirstOrDefault(inv => inv.Count >= 9);
        if (crafting is null)
        {
            ResultReceived?.Invoke(new RecipeTransferResult { Message = BrowserText.Choose("Сетка крафта не найдена.", "Crafting grid not found.") });
            return true;
        }
        // Snapshot every actually open container, not all inventories retained
        // by the manager (which can also include closed containers).
        var openedContainers = FindOpenContainers(api)
            .Where(inv => inv is not null && inv != crafting && !inv.TakeLocked
                && !IsInventoryKind(inv, "creative")
                && !IsInventoryKind(inv, "character")
                && !IsInventoryKind(inv, "mouse"))
            .ToArray();
        var sourceInventories = FindInventories(manager, "hotbar").Concat(FindInventories(manager, "backpack"))
            .Concat(openedContainers)
            .Distinct().ToArray();
        List<ItemSlot> GetSources() => sourceInventories.SelectMany(inv => Enumerable.Range(0, inv.Count).Select(i => inv[i]))
            .OfType<ItemSlot>()
            .Where(slot => !slot.Empty && slot.CanTake()).Distinct().ToList();
        var sources = GetSources();
        var candidates = recipes.Distinct().ToArray();
        if (Enumerable.Range(0, 9).Any(i => !crafting[i].Empty))
        {
            var available = sources.Concat(Enumerable.Range(0, 9).Select(i => crafting[i]).OfType<ItemSlot>()
                .Where(slot => !slot.Empty && slot.CanTake())).Distinct().ToList();
            if (!candidates.Any(candidate => TryPlan(api.World, candidate, crafting, available, 1, out _, out _)))
            {
                ResultReceived?.Invoke(new RecipeTransferResult { Message = BrowserText.Choose("Не хватает ингредиентов выбранного рецепта. Сетка сохранена.", "Missing ingredients for the selected recipe. Grid unchanged.") });
                return true;
            }
            if (!TryClearGrid(api, crafting))
            {
                ResultReceived?.Invoke(new RecipeTransferResult { Message = BrowserText.Choose("Не удалось вернуть остатки: проверьте свободное место и доступ к сундукам.", "Unable to return leftovers: check free space and container access.") });
                return true;
            }
            sources = GetSources();
        }
        api.Logger.Notification("[XRB Autofill] Open containers: {0}; sources: {1}",
            string.Join(", ", openedContainers.Select(inv => $"{inv.InventoryID} ({inv.Count} slots)")),
            string.Join(", ", sources.Select(slot => $"{slot.Inventory.InventoryID}:{slot.Itemstack?.Collectible.Code} x{slot.StackSize}")));
        string missing = "";
        List<PlannedMove>? bestPlan = null;
        int bestSets = 0;
        foreach (var candidate in candidates)
        {
            int upper = maximize ? MaximumSets(candidate, crafting, sources) : 1;
            if (upper == 0) { TryPlan(api.World, candidate, crafting, sources, 1, out _, out missing); continue; }
            for (int sets = upper; sets > bestSets; sets--)
            {
                if (!TryPlan(api.World, candidate, crafting, sources, sets, out var candidatePlan, out missing)) continue;
                bestPlan = candidatePlan;
                bestSets = sets;
                break;
            }
            if (!maximize && bestPlan is not null) break;
        }
        if (bestPlan is not null)
        {
            foreach (var move in bestPlan)
            {
                // An external source is valid only while its container remains
                // open. Use vanilla transfer packets for every source inventory.
                if (move.Source.Inventory.TakeLocked
                    || (!IsInventoryKind(move.Source.Inventory, "hotbar")
                        && !IsInventoryKind(move.Source.Inventory, "backpack")
                        && !FindOpenContainers(api).Contains(move.Source.Inventory)))
                {
                    ResultReceived?.Invoke(new RecipeTransferResult { Message = BrowserText.Choose("Хранилище закрыто или недоступно. Перенос прерван.", "Container closed or unavailable. Transfer interrupted.") });
                    return true;
                }
                ItemStack original = move.Source.Itemstack.Clone();
                var op = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, move.Quantity);
                op.ActingPlayer = api.World.Player;
                object packet = manager.TryTransferTo(move.Source, move.Target, ref op);
                if (packet is not null) api.Network.SendPacketClient(packet);
                if (packet is null || op.MovedQuantity != move.Quantity)
                {
                    ResultReceived?.Invoke(new RecipeTransferResult { Message = BrowserText.Choose("Перенос прерван. Проверьте сетку крафта.", "Transfer interrupted. Check the crafting grid.") });
                    return true;
                }
                returnOrigins.Add(new ReturnOrigin(move.Target, move.Source, original, move.Quantity));
            }
            ResultReceived?.Invoke(new RecipeTransferResult { Success = true, Message = BrowserText.Choose("Разложено комплектов: ", "Recipe sets placed: ") + bestSets });
            return true;
        }
        ResultReceived?.Invoke(new RecipeTransferResult { Message = BrowserText.Choose("Не хватает: ", "Missing: ") + missing });
        return true;
    }


    private static IEnumerable<IInventory> FindInventories(IPlayerInventoryManager manager, string kind)
    {
        return manager.Inventories
            .Where(pair => pair.Value is not null && (InventoryNameMatches(pair.Key, kind) || IsInventoryKind(pair.Value, kind)))
            .Select(pair => pair.Value)
            .Distinct();
    }

    private static bool TryClearGrid(ICoreClientAPI api, IInventory crafting)
    {
        var manager = api.World.Player.InventoryManager;
        var open = FindOpenContainers(api).ToHashSet();
        var personal = FindInventories(manager, "hotbar").Concat(FindInventories(manager, "backpack"))
            .Distinct().SelectMany(inv => Enumerable.Range(0, inv.Count).Select(i => inv[i])).OfType<ItemSlot>().ToArray();
        var planned = new List<PlannedMove>();
        var simulated = new Dictionary<ItemSlot, ItemStack?>();
        bool Accessible(ItemSlot slot) => !slot.Inventory.PutLocked && (IsInventoryKind(slot.Inventory, "hotbar")
            || IsInventoryKind(slot.Inventory, "backpack") || open.Contains(slot.Inventory));
        foreach (var source in Enumerable.Range(0, 9).Select(i => crafting[i]).OfType<ItemSlot>())
        {
            if (source.Itemstack is not { } stack) continue;
            if (!source.CanTake()) return false;
            int remaining = stack.StackSize;
            var origins = returnOrigins.Where(origin => origin.Crafting == source && MatchesReturnItem(api.World, stack, origin.Original)).ToArray();
            var destinations = origins.Select(origin => origin.Source).Concat(personal).Distinct();
            foreach (var target in destinations)
            {
                if (remaining == 0) break;
                if (!Accessible(target) || !target.CanHold(source)) continue;
                if (!simulated.TryGetValue(target, out var current)) current = target.Itemstack?.Clone();
                if (current is not null && !current.Equals(api.World, stack, GlobalConstants.IgnoredStackAttributes)) continue;
                int capacity = Math.Max(0, Math.Min(target.MaxSlotStackSize, stack.Collectible.MaxStackSize) - (current?.StackSize ?? 0));
                var origin = origins.FirstOrDefault(value => value.Source == target);
                int limit = origin is null ? remaining : Math.Min(remaining, origins.Where(value => value.Source == target).Sum(value => value.Quantity));
                int quantity = Math.Min(limit, capacity);
                if (quantity <= 0) continue;
                var placed = current ?? stack.Clone();
                placed.StackSize = (current?.StackSize ?? 0) + quantity;
                simulated[target] = placed;
                planned.Add(new PlannedMove(source, target, quantity));
                remaining -= quantity;
            }
            if (remaining != 0) return false; // no moves have happened yet
        }
        foreach (var move in planned)
        {
            var op = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, move.Quantity)
                { ActingPlayer = api.World.Player };
            object packet = manager.TryTransferTo(move.Source, move.Target, ref op);
            if (packet is not null) api.Network.SendPacketClient(packet);
            if (packet is null || op.MovedQuantity != move.Quantity) return false;
        }
        returnOrigins.Clear();
        return true;
    }

    internal static void ReturnCraftingRemainders()
    {
        if (clientApi is not { } api || returning || returnOrigins.Count == 0) return;
        returning = true;
        try
        {
            var manager = api.World.Player.InventoryManager;
            var open = FindOpenContainers(api).ToHashSet();
            foreach (var origin in returnOrigins)
            {
                var stack = origin.Crafting.Itemstack;
                if (stack is null || !MatchesReturnItem(api.World, stack, origin.Original)) continue;
                var inventory = origin.Source.Inventory;
                bool available = IsInventoryKind(inventory, "hotbar") || IsInventoryKind(inventory, "backpack") || open.Contains(inventory);
                if (!available || inventory.PutLocked || origin.Crafting.Inventory.TakeLocked) continue;
                // Only move actual remaining items, never restore consumed items
                // or overwrite a slot occupied since the original transfer.
                if (!origin.Source.Empty && !origin.Source.Itemstack.Equals(api.World, stack, GlobalConstants.IgnoredStackAttributes)) continue;
                var op = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, 0,
                    EnumMergePriority.AutoMerge, Math.Min(origin.Quantity, stack.StackSize));
                op.ActingPlayer = api.World.Player;
                object packet = manager.TryTransferTo(origin.Crafting, origin.Source, ref op);
                if (packet is not null) api.Network.SendPacketClient(packet);
            }
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[XRB] Remainder return interrupted: {0}", exception.Message);
        }
        finally
        {
            // Any leftovers are left to vanilla inventory-close handling.
            returnOrigins.Clear();
            returning = false;
        }
    }

    public override void Dispose()
    {
        returnOrigins.Clear();
        clientApi = null;
        base.Dispose();
    }

    private sealed record ReturnOrigin(ItemSlot Crafting, ItemSlot Source, ItemStack Original, int Quantity);

    private static bool MatchesReturnItem(IWorldAccessor world, ItemStack current, ItemStack original)
    {
        // Crafting changes tool durability; preserve the remaining tool's
        // actual attributes while comparing its identity to the borrowed one.
        var ignored = GlobalConstants.IgnoredStackAttributes.Concat(new[] { "durability" }).Distinct().ToArray();
        return current.Equals(world, original, ignored);
    }

    private static IEnumerable<IInventory> FindOpenContainers(ICoreClientAPI api)
    {
        var manager = api.World.Player.InventoryManager;
        var found = new HashSet<IInventory>(manager.OpenedInventories);
        // Some block inventories are registered outside OpenedInventories.
        // Their own open-player state or a live block-entity dialog provides
        // evidence that they are currently open; never include closed caches.
        foreach (var inventory in manager.Inventories.Values)
            if (inventory.HasOpened(api.World.Player)) found.Add(inventory);
        foreach (var dialog in api.OpenedGuis.OfType<GuiDialogBlockEntity>())
        {
            if (!dialog.IsOpened()) continue;
            for (Type? type = dialog.GetType(); type is not null; type = type.BaseType)
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (typeof(IInventory).IsAssignableFrom(field.FieldType)
                    && field.GetValue(dialog) is IInventory inventory) found.Add(inventory);
            }
        }
        return found;
    }

    private static bool InventoryNameMatches(string? name, string kind)
    {
        name ??= "";
        return name.Equals(kind, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-" + kind, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInventoryKind(IInventory? inventory, string kind)
    {
        if (inventory is null) return false;
        string className = inventory.ClassName ?? "";
        string inventoryId = inventory.InventoryID ?? "";
        return className.Equals(kind, StringComparison.OrdinalIgnoreCase)
            || inventoryId.Equals(kind, StringComparison.OrdinalIgnoreCase)
            || inventoryId.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryPlan(IWorldAccessor world, GridRecipe recipe, IInventory crafting,
        List<ItemSlot> sources, int sets, out List<PlannedMove>? plan, out string missing)
    {
        var remaining = sources.ToDictionary(slot => slot, slot => slot.StackSize);
        var result = new List<PlannedMove>();
        int width = Math.Clamp(recipe.Width, 1, 3);
        int height = Math.Clamp(recipe.Height, 1, 3);
        int offsetX = (3 - width) / 2;
        int offsetY = (3 - height) / 2;

        var groups = new List<List<ItemSlot>>();
        foreach (var source in sources)
        {
            var group = groups.FirstOrDefault(items => items[0].Itemstack.Equals(world, source.Itemstack, GlobalConstants.IgnoredStackAttributes)
                && items[0].Itemstack.Collectible.GetRemainingDurability(items[0].Itemstack)
                    == source.Itemstack.Collectible.GetRemainingDurability(source.Itemstack));
            if (group is null) groups.Add(new List<ItemSlot> { source }); else group.Add(source);
        }
        var cells = new List<(int Slot, CraftingRecipeIngredient Ingredient, int Needed)>();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var ingredient = recipe.GetElementInGrid(y, x, recipe.ResolvedIngredients!, width);
            if (ingredient is not null) cells.Add(((y + offsetY) * 3 + x + offsetX, ingredient,
                IngredientRules.RequiredQuantity(ingredient, sets)));
        }
        var candidates = cells.Select(cell => Enumerable.Range(0, groups.Count).Where(group =>
            IngredientRules.Accepts(cell.Ingredient, groups[group][0].Itemstack)
            && IngredientRules.AvailableUses(cell.Ingredient, groups[group][0].Itemstack) >= sets
            && cell.Needed <= Math.Min(groups[group][0].Itemstack.Collectible.MaxStackSize, crafting[cell.Slot].MaxSlotStackSize)).ToArray()).ToArray();
        bool Valid(int[] chosen)
        {
            if (clientApi is null) return true;
            var preview = Enumerable.Range(0, 9).Select(_ => (ItemSlot)new DummySlot()).ToArray();
            for (int i = 0; i < cells.Count; i++)
            {
                preview[cells[i].Slot].Itemstack = groups[chosen[i]][0].Itemstack.Clone();
                preview[cells[i].Slot].Itemstack.StackSize = cells[i].Needed;
            }
            return recipe.Matches(clientApi.World.Player, world, preview, 3);
        }
        int[]? assignment = IngredientAssignment.Solve(cells.Select(cell => cell.Needed).ToArray(), candidates,
            groups.Select(group => group.Sum(source => source.StackSize)).ToArray(), Valid);
        if (assignment is null)
        {
            plan = null;
            missing = string.Join(", ", cells.Select(cell => $"{cell.Ingredient.Code} x{cell.Needed}"));
            return false;
        }
        var selectedBySlot = cells.Select((cell, index) => (cell.Slot, Source: groups[assignment[index]][0])).ToDictionary(cell => cell.Slot, cell => cell.Source);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            CraftingRecipeIngredient? ingredient = recipe.GetElementInGrid(y, x, recipe.ResolvedIngredients!, width);
            if (ingredient is null) continue;
            int needed = IngredientRules.RequiredQuantity(ingredient, sets);
            int targetIndex = (y + offsetY) * 3 + x + offsetX;
            ItemSlot target = crafting[targetIndex];
            ItemSlot? representative = selectedBySlot[targetIndex];
            if (representative?.Itemstack is not { } selected)
            {
                plan = null;
                missing = $"{ingredient.Code} x{needed}";
                return false;
            }

            foreach (ItemSlot source in sources.Where(slot => remaining[slot] > 0
                && slot.Itemstack is { } stack
                && IngredientRules.AvailableUses(ingredient, stack) >= sets
                && stack.Collectible.GetRemainingDurability(stack) == selected.Collectible.GetRemainingDurability(selected)
                && stack.Equals(world, selected, GlobalConstants.IgnoredStackAttributes)))
            {
                int quantity = Math.Min(needed, remaining[source]);
                if (quantity <= 0) continue;
                result.Add(new PlannedMove(source, target, quantity));
                remaining[source] -= quantity;
                needed -= quantity;
                if (needed == 0) break;
            }
            if (needed > 0)
            {
                plan = null;
                missing = $"{ingredient.Code} x{needed}";
                return false;
            }
        }
        // Validate the assembled material combination, not just each ingredient
        // independently. Named variants and recipe-specific rules remain vanilla.
        if (clientApi is not null)
        {
            var preview = Enumerable.Range(0, 9).Select(_ => (ItemSlot)new DummySlot()).ToArray();
            foreach (var move in result)
            {
                int index = Enumerable.Range(0, 9).First(i => ReferenceEquals(crafting[i], move.Target));
                if (preview[index].Empty)
                {
                    preview[index].Itemstack = move.Source.Itemstack.Clone();
                    preview[index].Itemstack.StackSize = move.Quantity;
                }
                else preview[index].Itemstack.StackSize += move.Quantity;
            }
            if (!recipe.Matches(clientApi.World.Player, world, preview, 3))
            {
                plan = null;
                missing = BrowserText.Choose("Несовместимое сочетание ингредиентов", "Incompatible ingredient combination");
                return false;
            }
        }
        plan = result;
        missing = "";
        return true;
    }

    private sealed record PlannedMove(ItemSlot Source, ItemSlot Target, int Quantity);

    private static int MaximumSets(GridRecipe recipe, IInventory crafting, List<ItemSlot> sources)
    {
        if (recipe.ResolvedIngredients is null) return 0;
        int upper = int.MaxValue;
        bool consumable = false;
        int width = Math.Clamp(recipe.Width, 1, 3), height = Math.Clamp(recipe.Height, 1, 3);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var ingredient = recipe.GetElementInGrid(y, x, recipe.ResolvedIngredients, width);
            if (ingredient is null) continue;
            if (IngredientRules.IsReusable(ingredient))
            {
                int uses = sources.Where(slot => slot.Itemstack is { } stack && IngredientRules.Accepts(ingredient, stack))
                    .Select(slot => IngredientRules.AvailableUses(ingredient, slot.Itemstack!)).DefaultIfEmpty(0).Max();
                upper = Math.Min(upper, uses);
                continue;
            }
            consumable = true;
            int quantity = Math.Max(1, ingredient.Quantity);
            var matching = sources.Where(slot => slot.Itemstack is { } stack && IngredientRules.Accepts(ingredient, stack)).ToArray();
            long available = matching.Sum(slot => (long)slot.StackSize);
            int slotIndex = (y + (3 - height) / 2) * 3 + x + (3 - width) / 2;
            int capacity = matching.Select(slot => Math.Min(slot.Itemstack!.Collectible.MaxStackSize, crafting[slotIndex]!.MaxSlotStackSize)).DefaultIfEmpty(0).Max();
            upper = Math.Min(upper, (int)Math.Min(available / quantity, capacity / quantity));
        }
        return consumable ? upper : Math.Min(upper, 1);
    }
}
