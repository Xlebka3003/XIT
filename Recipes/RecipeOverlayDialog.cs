using System.Text.RegularExpressions;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

internal enum RecipeViewMode { Recipes, Uses }

// Lifecycle and shared dialog state; implementation is organized under Recipes/.
internal sealed partial class RecipeOverlayDialog : GuiDialog
{
    private readonly BrowserInventory inputs;
    private readonly BrowserInventory crucibleInputs;
    private readonly BrowserInventory pitMaterials;
    private readonly BrowserInventory firepitInput;
    private readonly BrowserInventory firepitFuel;
    private readonly BrowserInventory smithingMetal;
    private readonly BrowserInventory smithingHammer;
    private readonly BrowserInventory smithingAnvil;
    private List<ItemStack> smithingAnvils = new();
    private int smithingAnvilIndex;
    private string smithingAnvilMetal = "";
    private readonly BrowserInventory output;
    private readonly BrowserInventory secondaryOutput;
    private ItemStack? barrelLiquid;
    private BrowserInventory categoryTabs;
    private List<RecipeDisplayEntry> recipes;
    private List<RecipeDisplayEntry> allRecipes;
    private List<string> categories = new();
    private int category;
    private ItemStack target;
    private RecipeViewMode mode;
    private sealed record ViewState(ItemStack Stack, RecipeViewMode Mode, string Category, int Page, int Variant, int Fuel, int Tool);
    private readonly Stack<ViewState> history = new();
    private int page;
    private bool compactCard;
    private bool detailView;
    private RecipeOverlayDialog? cardOwner;
    private readonly List<RecipeOverlayDialog> recipeCards = new();
    private int cardIndex;
    private string StationCategory => recipes is { Count: > 0 } ? recipes[page].FuelProcessCategory ?? recipes[page].Category
        : categories.Count > 0 ? categories[category] : "";
    private bool ThermalLayout => categories.Count > 0 && categories[category] is "Heating" or "Cooking" or "Melting" or "Fuel";
    private int CardsPerPage => detailView || compactCard ? 1 : ThermalLayout
        ? Math.Clamp((int)((capi.Render.FrameWidth / Math.Max(0.5f, RuntimeEnv.GUIScale) - 100) / 310), 1, 3) : Math.Clamp(
        (int)((capi.Render.FrameHeight / Math.Max(0.5f, RuntimeEnv.GUIScale) - 160) / 170), 1, 3);
    private bool MultiPage => !compactCard && !detailView && recipes.Count > 1;
    private int composeRevision;
    private List<ItemStack> firepitFuels = new();
    private int firepitFuelIndex;
    private long lastFuelChangeMs;
    private int ingredientVariantIndex;
    private int toolVariantIndex;
    private IngredientOptionsDialog? ingredientOptions;
    private readonly Dictionary<int, ItemStack> selectedIngredients = new();
    private readonly Dictionary<string, System.Func<int, int>> ingredientGridSlots = new();
    private long lastIngredientVariantChangeMs;
    private bool currentPageHasAnimatedVariants;
    private readonly int[] slotCycleOffsets = Enumerable.Range(0, 9).Select(slot => slot).ToArray();
    private readonly Dictionary<string, List<RecipeDisplayEntry>> navigationCache = new(StringComparer.Ordinal);
    private ElementBounds? dialogHitBounds;
    private ElementBounds? previousHitBounds;
    private ElementBounds? nextHitBounds;
    private ElementBounds? closeHitBounds;
    private ElementBounds? autofillHitBounds;

    private RecipeOverlayDialog(ICoreClientAPI api, ItemStack target, RecipeViewMode mode, List<RecipeDisplayEntry> collectedRecipes, RecipeOverlayDialog? owner = null, int index = 0) : base(api)
    {
        cardOwner = owner; compactCard = owner is not null; cardIndex = index;
        this.target = target.Clone();
        this.mode = mode;
        inputs = new BrowserInventory(9, api) { SlotClicked = OnInputClicked };
        firepitInput = new BrowserInventory(1, api) { SlotClicked = OnFirepitInputClicked };
        firepitFuel = new BrowserInventory(1, api) { SlotClicked = OnFirepitFuelClicked };
        smithingMetal = new BrowserInventory(1, api) { SlotClicked = OnSmithingMetalClicked };
        smithingHammer = new BrowserInventory(1, api) { SlotClicked = OnSmithingHammerClicked };
        smithingAnvil = new BrowserInventory(1, api);
        crucibleInputs = new BrowserInventory(4, api);
        pitMaterials = new BrowserInventory(3, api);
        output = new BrowserInventory(1, api) { SlotClicked = OnOutputClicked };
        secondaryOutput = new BrowserInventory(1, api);
        categoryTabs = new BrowserInventory(1, api);
        allRecipes = collectedRecipes;
        recipes = new();
        if (!compactCard) RecipeTransferSystem.ResultReceived += OnRecipeTransferResult;
        ApplyCategories();
        Compose();
        FillPage();
    }

    public static RecipeOverlayDialog? CreateIfAvailable(ICoreClientAPI api, ItemStack target, RecipeViewMode mode)
    {
        var total = Stopwatch.StartNew();
        long collectStarted = total.ElapsedMilliseconds;
        List<RecipeDisplayEntry> collected = CollectRecipes(api, target, mode);
        long collectMs = total.ElapsedMilliseconds - collectStarted;
        if (collected.Count == 0)
        {
            LogSlowOperation(api, target, mode, total.ElapsedMilliseconds, $"collect={collectMs}ms, no recipes");
            return null;
        }
        long dialogStarted = total.ElapsedMilliseconds;
        var dialog = new RecipeOverlayDialog(api, target, mode, collected);
        long dialogMs = total.ElapsedMilliseconds - dialogStarted;
        LogSlowOperation(api, target, mode, total.ElapsedMilliseconds,
            $"collect={collectMs}ms, dialog={dialogMs}ms, recipes={collected.Count}");
        return dialog;
    }

    private static void LogSlowOperation(ICoreClientAPI api, ItemStack target, RecipeViewMode mode, long totalMs, string details)
    {
        if (totalMs < 50) return;
        api.Logger.Warning("[Recipe Browser Performance] {0} {1}:{2} total={3}ms ({4})",
            mode, target.Class, target.Collectible?.Code, totalMs, details);
    }

    public override void Dispose()
    {
        ClearRecipeCards();
        ingredientOptions?.TryClose(); ingredientOptions?.Dispose();
        RecipeTransferSystem.ResultReceived -= OnRecipeTransferResult;
        base.Dispose();
    }

    public override bool TryClose()
    {
        foreach (var card in recipeCards) { card.ingredientOptions?.TryClose(); }
        ingredientOptions?.TryClose();
        return base.TryClose();
    }

    private void NotifyAutoFill(string message) =>
        capi.TriggerIngameError(this, "xei-autofill", message);


}
