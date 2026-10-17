using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VsRecipeBrowser;

internal sealed class BrowserIndex : IDisposable
{
    private const string CacheFile = "vsrecipebrowser-index.json";
    private readonly ICoreClientAPI capi;
    private readonly HashSet<string> favoriteCodes;
    private readonly Dictionary<string, string> modNames;
    private IEnumerator<BrowserIndexEntry>? enumerator;
    private long tickId;
    private long lastNotifyMs;
    private bool disposed;

    public List<BrowserIndexEntry> Entries { get; private set; } = new();
    public bool IsComplete { get; private set; }
    public event Action? Updated;

    public BrowserIndex(ICoreClientAPI capi, IEnumerable<string> favoriteKeys)
    {
        this.capi = capi;
        favoriteCodes = favoriteKeys
            .Select(key => key[(key.IndexOf(':') + 1)..].Split('|')[0])
            .ToHashSet(StringComparer.Ordinal);
        modNames = capi.ModLoader.Mods
            .GroupBy(mod => mod.Info.ModID, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Info.Name ?? group.Key, StringComparer.OrdinalIgnoreCase);
    }

    public void Start()
    {
        string fingerprint = BuildFingerprint();
        BrowserIndexCache? cache = capi.LoadModConfig<BrowserIndexCache>(CacheFile);
        if (cache?.Fingerprint == fingerprint && cache.Entries.Count > 0)
        {
            Entries = cache.Entries;
            IsComplete = true;
            Updated?.Invoke();
            capi.Logger.Notification("[Recipe Browser] Loaded {0} cached item variants", Entries.Count);
            return;
        }

        Entries.Clear();
        enumerator = EnumerateEntries().GetEnumerator();
        tickId = capi.Event.RegisterGameTickListener(IndexNextBatch, 20);
    }

    private IEnumerable<BrowserIndexEntry> EnumerateEntries()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectibleObject[] collectibles = capi.World.Collectibles
            .Where(collectible => collectible?.Code is not null)
            .OrderByDescending(collectible => favoriteCodes.Contains(collectible.Code.ToString()))
            .ToArray();

        foreach (CollectibleObject collectible in collectibles)
        {
            foreach (ItemStack stack in GetStacksSafe(collectible))
            {
                BrowserIndexEntry? entry = CreateEntrySafe(stack, seen);
                if (entry is not null) yield return entry;
            }
        }
    }

    private ItemStack[] GetStacksSafe(CollectibleObject collectible)
    {
        try
        {
            return collectible.CreativeInventoryStacks is { Length: > 0 }
                ? collectible.CreativeInventoryStacks.Where(entry => entry is not null)
                    .SelectMany(entry => entry.Stacks ?? Array.Empty<JsonItemStack>())
                    .Where(stack => stack?.ResolvedItemstack?.Collectible?.Code is not null)
                    .Select(stack => stack.ResolvedItemstack!)
                    .ToArray()
                : new[] { new ItemStack(collectible) };
        }
        catch (Exception exception)
        {
            capi.Logger.Warning("[Recipe Browser] Skipping malformed collectible {0}: {1}", collectible.Code, exception.Message);
            return Array.Empty<ItemStack>();
        }
    }

    private BrowserIndexEntry? CreateEntrySafe(ItemStack stack, HashSet<string> seen)
    {
        try
        {
            if (stack?.Collectible?.Code is null) return null;
            stack = DirectionalCatalog.Normalize(capi.World, stack);
            if (stack.Collectible.Code.Domain == "game" && (stack.Collectible.Code.Path.StartsWith("fruitpresstop-")
                || stack.Collectible.Code.Path == "fruitpress-we")) return null;
            string variantKey = $"{stack.Class}:{stack.Collectible.Code}:{stack.Attributes.ToJsonToken()}";
            if (!seen.Add(variantKey)) return null;
            string name;
            try { name = stack.GetName() ?? stack.Collectible.Code.ToString(); }
            catch { name = stack.Collectible.Code.ToString(); }
            string modId = stack.Collectible.Code.Domain;
            string modName = modId == "game" ? "Vanilla" : modNames.TryGetValue(modId, out string? loadedName) ? loadedName : modId;
            bool hasRecipe;
            try { hasRecipe = RecipeOverlayDialog.HasObtainingRecipe(capi, stack); }
            catch (Exception exception)
            {
                // Fail open for malformed third-party recipes: never erase the
                // whole item entry just because availability cannot be checked.
                hasRecipe = true;
                capi.Logger.Warning("[XRB] Recipe availability for {0}: {1}", stack.Collectible.Code, exception.Message);
            }
            return new BrowserIndexEntry
            {
                Code = stack.Collectible.Code.ToString(),
                ItemClass = stack.Class,
                DisplayName = name,
                SearchIndex = $"{name} {stack.Collectible.Code}".ToLowerInvariant(),
                ModId = modId.ToLowerInvariant(),
                ModName = modName.ToLowerInvariant(),
                FavoriteKey = FavoriteIdentity.Key(stack),
                Group = Classify(stack),
                HasRecipe = hasRecipe,
                StackData = Convert.ToBase64String(stack.ToBytes())
            };
        }
        catch { return null; }
    }

    private void IndexNextBatch(float _)
    {
        if (disposed || enumerator is null || IsComplete) return;
        var stopwatch = Stopwatch.StartNew();
        int added = 0;
        while (added < 160 && stopwatch.ElapsedMilliseconds < 4)
        {
            if (!enumerator.MoveNext())
            {
                FinishIndex();
                return;
            }
            Entries.Add(enumerator.Current);
            added++;
        }

        long now = capi.ElapsedMilliseconds;
        if (now - lastNotifyMs >= 250)
        {
            lastNotifyMs = now;
            Updated?.Invoke();
        }
    }

    private void FinishIndex()
    {
        enumerator?.Dispose();
        enumerator = null;
        if (tickId != 0) capi.Event.UnregisterGameTickListener(tickId);
        tickId = 0;
        BrowserIndexEntry[] snapshot = Entries.ToArray();
        Task.Run(() => snapshot.OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList())
            .ContinueWith(task => capi.Event.EnqueueMainThreadTask(() =>
            {
                if (disposed || task.IsFaulted || task.IsCanceled) return;
                Entries = task.Result;
                IsComplete = true;
                capi.StoreModConfig(new BrowserIndexCache { Fingerprint = BuildFingerprint(), Entries = Entries }, CacheFile);
                Updated?.Invoke();
                capi.Logger.Notification("[Recipe Browser] Indexed and cached {0} item variants", Entries.Count);
            }, "vsrecipebrowser-save-index"));
    }

    private string BuildFingerprint()
    {
        string mods = string.Join("|", capi.ModLoader.Mods
            .Select(mod => $"{mod.Info.ModID}@{mod.Info.Version}")
            .OrderBy(value => value, StringComparer.Ordinal));
        string raw = $"cache-v20-tagged-tool-index|{GameVersion.ShortGameVersion}|{Lang.CurrentLocale}|{mods}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public void Dispose()
    {
        disposed = true;
        if (tickId != 0) capi.Event.UnregisterGameTickListener(tickId);
        enumerator?.Dispose();
    }

    private static BrowserItemGroup Classify(ItemStack stack)
    {
        string path = stack.Collectible.Code.Path.ToLowerInvariant();
        if (stack.Collectible is Vintagestory.GameContent.ItemCreature
            || path.StartsWith("fish-") || path.StartsWith("butterfly")) return BrowserItemGroup.Animals;
        if (path.StartsWith("crystalizedore-") || path.StartsWith("crystallizedore-")
            || stack.Collectible.Variant.ContainsKey("ore")) return BrowserItemGroup.Ores;
        if (path.StartsWith("creature-") && !path.StartsWith("creatureinventory-")) return BrowserItemGroup.Animals;
        if (path.StartsWith("tusk-")) return BrowserItemGroup.Decorations;
        if (path.StartsWith("ore-") || path.StartsWith("nugget-") || path.StartsWith("crushed-")
            || path.StartsWith("looseores-") || path.StartsWith("coal-") || path.StartsWith("rawquartz-")) return BrowserItemGroup.Ores;
        if (stack.Class == EnumItemClass.Block && (path.StartsWith("crystal-") || path.StartsWith("symbols-")
            || path.StartsWith("symbol-") || path.StartsWith("caveart-"))) return BrowserItemGroup.Decorations;
        if (stack.Class == EnumItemClass.Block && (path.Contains("slab") || path.Contains("stairs")
            || path.StartsWith("beam-"))) return BrowserItemGroup.Blocks;
        if (path.StartsWith("ontree-")) return BrowserItemGroup.Technical;
        if (path.StartsWith("antler-") || path.StartsWith("antlers-")) return BrowserItemGroup.Decorations;
        if (path.StartsWith("ontree-") || path.StartsWith("antlers-") || path.StartsWith("antler-")
            || path.StartsWith("berrybush-") || path.StartsWith("fruitingbush-"))
        {
            if (stack.Collectible.CreativeInventoryStacks is not { Length: > 0 }) return BrowserItemGroup.Technical;
        }
        if (path.StartsWith("armor-") || path.StartsWith("clothes-")) return BrowserItemGroup.Armor;
        if (ContainsAny(path, "sword", "spear", "bow-", "arrow-", "shield", "club", "blade-falx")) return BrowserItemGroup.Weapons;
        if (ContainsAny(path, "bush", "sapling", "flower", "fern", "tallgrass", "plant-", "crop-", "seed-")) return BrowserItemGroup.Plants;
        if (stack.Class == EnumItemClass.Block && stack.Collectible.Variant.ContainsKey("flow"))
        {
            bool source = stack.Collectible.Variant["flow"] == "still"
                && (!stack.Collectible.Variant.ContainsKey("height") || stack.Collectible.Variant["height"] == "7");
            return source ? BrowserItemGroup.Blocks : BrowserItemGroup.Liquids;
        }
        if (ContainsAny(path, "pile", "slag", "groundstorage", "multiblock", "creativeblock", "placeholder", "dummy", "placed-")) return BrowserItemGroup.Technical;
        if (stack.Collectible.Tool is not null || ContainsAny(path, "pickaxe", "hammer", "chisel", "saw", "axe", "shovel", "knife", "sickle", "shears", "cleaver", "wrench", "bow", "spear")) return BrowserItemGroup.Tools;
        if (ContainsAny(path, "bread", "meat", "berry", "fruit", "vegetable", "grain", "meal", "pie", "cheese", "milk", "egg", "mushroom", "honey", "porridge", "jam", "food")) return BrowserItemGroup.Food;
        if (ContainsAny(path, "gear", "axle", "clutch", "transmission", "pulverizer", "quern", "helvehammer", "windmill", "rotor", "mechanical", "toggle", "lever")) return BrowserItemGroup.Mechanisms;
        if (ContainsAny(path, "ingot", "nugget", "ore-", "crushed", "plate-", "metalbit", "metalchain", "nails", "board", "plank", "stick", "leather", "hide", "cloth", "linen", "fiber", "resin", "charcoal", "coal", "quartz", "gem")) return BrowserItemGroup.Materials;
        return stack.Class == EnumItemClass.Block ? BrowserItemGroup.Blocks : BrowserItemGroup.Items;
    }

    private static bool ContainsAny(string value, params string[] fragments) => fragments.Any(value.Contains);
}

internal sealed class BrowserIndexEntry
{
    public string Code { get; set; } = "";
    public EnumItemClass ItemClass { get; set; }
    public string DisplayName { get; set; } = "";
    public string SearchIndex { get; set; } = "";
    public string ModId { get; set; } = "";
    public string ModName { get; set; } = "";
    public string FavoriteKey { get; set; } = "";
    public BrowserItemGroup Group { get; set; } = BrowserItemGroup.Items;
    public string StackData { get; set; } = "";
    public bool HasRecipe { get; set; }

    public ItemStack? CreateStack(IWorldAccessor world)
    {
        try
        {
            var stack = new ItemStack(Convert.FromBase64String(StackData));
            return stack.ResolveBlockOrItem(world) ? stack : null;
        }
        catch { return null; }
    }
}

internal enum BrowserItemGroup { Blocks, Items, Tools, Food, Materials, Mechanisms, Technical, Liquids, Armor, Weapons, Plants, Decorations, Ores, Animals }

internal sealed class BrowserIndexCache
{
    public string Fingerprint { get; set; } = "";
    public List<BrowserIndexEntry> Entries { get; set; } = new();
}
