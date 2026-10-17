using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System.Reflection;
using HarmonyLib;

namespace VsRecipeBrowser;

public sealed class InventoryBrowserSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private InventoryBrowserDialog? browser;
    private InventoryDimOverlayDialog? dimOverlay;
    private InterfaceToggleDialog? interfaceToggle;
    private readonly HashSet<GuiDialog> hiddenHudDialogs = new();
    private readonly HashSet<GuiDialog> forceHiddenHudDialogs = new();
    private static readonly FieldInfo? OpenedField = typeof(GuiDialog).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic);
    private BrowserIndex? browserIndex;
    private IClientWorldAccessor? indexedWorld;
    private long tickId;
    private Harmony? harmony;
    private LinkedInventoryController? linkedInventory;
    private InventorySortButtons? sortButtons;
    private bool interfaceVisible = true;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        AccidentalDropGuard.Api = api;
        var defaults = api.LoadModConfig<RecipeBrowserConfig>("vsrecipebrowser.json") ?? new();
        if (defaults.SettingsDefaultsVersion < 2)
        {
            defaults.EnableAllFilters();
            defaults.AutoOpenInventory = defaults.CenterContainerWindows = defaults.SortingEnabled = defaults.InterfaceVisible = true;
            defaults.SettingsDefaultsVersion = 2;
            api.StoreModConfig(defaults, "vsrecipebrowser.json");
        }
        InventorySorter.ReloadSettings(api);
        BrowserSettings.Current = defaults;
        linkedInventory = new LinkedInventoryController();
        linkedInventory.StartClientSide(api);
        sortButtons = new InventorySortButtons(api);
        CoordinateHudPatch.Api = api;
        ModTooltipPatch.Initialize(api);
        harmony = new Harmony("xlebka.xei.itemmodtooltip");
        harmony.PatchAll(typeof(ModTooltipPatch).Assembly);
        api.Input.RegisterHotKey("vsrecipebrowser-favorite", BrowserText.Choose("XRB: Добавить или убрать из избранного", "XRB: Toggle hovered item favorite"), GlKeys.F, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("vsrecipebrowser-favorite", _ => browser?.ToggleHoveredFavorite() == true);
        api.Input.RegisterHotKey("vsrecipebrowser-recipe", BrowserText.Choose("XRB: Показать рецепт", "XRB: Show recipes"), GlKeys.R, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("vsrecipebrowser-recipe", _ => BrowserSettings.Current.RecipeHotkeysEnabled && browser?.OpenHoveredRecipe() == true);
        api.Input.RegisterHotKey("vsrecipebrowser-uses", BrowserText.Choose("XRB: Показать использование", "XRB: Show uses"), GlKeys.U, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("vsrecipebrowser-uses", _ => BrowserSettings.Current.RecipeHotkeysEnabled && browser?.OpenHoveredUses() == true);
        RegisterSearchHotkey(api.Input);
        api.Input.SetHotKeyHandler("xrb-find-item", _ => browser?.SearchHasFocus != true && browser?.FiltersOpen != true
            && !api.OpenedGuis.OfType<GuiDialog>().Any(gui => gui.Focused && gui.GetType().Name.Contains("Chat", StringComparison.OrdinalIgnoreCase))
            && FindAndCloseInventory(api));
        api.Input.RegisterHotKey("vsrecipebrowser-toggle-interface", BrowserText.Choose("XRB: Показать или скрыть интерфейс", "XRB: Show or hide interface"), GlKeys.O, HotkeyType.GUIOrOtherControls, false, true, false);
        api.Input.SetHotKeyHandler("vsrecipebrowser-toggle-interface", _ => ToggleInterface());
        tickId = api.Event.RegisterGameTickListener(CheckInventoryDialog, 100);
        api.Input.RegisterHotKey("xrb-back-recipe", BrowserText.Choose("XRB: Предыдущий рецепт", "XRB: Previous recipe"), GlKeys.BackSpace, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("xrb-back-recipe", _ => browser?.BackRecipe() == true);
        api.Input.RegisterHotKeyFirst("xrb-sort-window", BrowserText.Choose("XRB: Сортировать окно под курсором", "XRB: Sort inventory under cursor"),
            (GlKeys)(KeyCombination.MouseStart + (int)EnumMouseButton.Middle), HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("xrb-sort-window", _ => browser?.SearchHasFocus != true
            && browser?.FiltersOpen != true && sortButtons?.SortHovered() == true);
    }

    internal static void RegisterSearchHotkey(IInputAPI input)
    {
        const string code = "xrb-find-item";
        // Register before chat, retaining the user's mapping across world reloads.
        var mapping = input.GetHotKeyByCode(code)?.CurrentMapping?.Clone();
        input.HotKeys.Remove(code);
        input.RegisterHotKeyFirst(code, BrowserText.Choose("XIT: Найти предмет в хранилищах", "XIT: Find item in nearby containers"),
            GlKeys.T, HotkeyType.GUIOrOtherControls);
        if (mapping is not null) input.GetHotKeyByCode(code).CurrentMapping = mapping;
        // The handler returns false without a target or while typing, leaving chat available.
    }

    private bool FindAndCloseInventory(ICoreClientAPI api)
    {
        var target = browser?.IsOpened() == true ? browser.GetSearchTarget() : api.World.Player.InventoryManager.CurrentHoveredSlot?.Itemstack;
        if (NearbyItemSearchClient.Instance?.Find(target) != true) return false;
        if (!BrowserSettings.Current.CloseInventoryOnSearch) return true;
        api.Event.EnqueueMainThreadTask(() =>
        {
            browser?.CloseRecipeOverlay(); browser?.CloseFilters();
            foreach (var dialog in api.OpenedGuis.OfType<GuiDialog>().Where(gui => gui.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory").ToArray()) dialog.TryClose();
        }, "xrb-search-close-inventory");
        return true;
    }

    private bool ToggleInterface()
    {
        if (capi is null) return false;
        interfaceVisible = !interfaceVisible;
        RecipeBrowserConfig config = capi.LoadModConfig<RecipeBrowserConfig>("vsrecipebrowser.json") ?? new RecipeBrowserConfig();
        config.InterfaceVisible = interfaceVisible;
        capi.StoreModConfig(config, "vsrecipebrowser.json");
        if (!interfaceVisible)
        {
            browser?.CloseRecipeOverlay();
            if (browser?.IsOpened() == true) browser.TryClose();
            RestoreHiddenHudDialogs();
        }
        return true;
    }

    private void CheckInventoryDialog(float _)
    {
        if (capi?.World?.Player is null) return;
        if (!ReferenceEquals(indexedWorld, capi.World))
        {
            if (harmony is not null) AccidentalDropGuard.Install(harmony, capi.World.Player.InventoryManager.GetType());
            browser?.Dispose();
            browser = null;
            browserIndex?.Dispose();
            RecipeBrowserConfig config = capi.LoadModConfig<RecipeBrowserConfig>("vsrecipebrowser.json") ?? new RecipeBrowserConfig();
            interfaceVisible = config.InterfaceVisible;
            browserIndex = new BrowserIndex(capi, config.Favorites);
            indexedWorld = capi.World;
            RecipeOverlayDialog.PrepareRecipeCatalog(capi);
            browserIndex.Start();
        }
        bool handbookOpen = capi.OpenedGuis.OfType<GuiDialog>().Any(gui => gui.IsOpened()
            && (gui.GetType().FullName == "Vintagestory.GameContent.GuiDialogHandbook"
                || gui.GetType().Name.Contains("Handbook", StringComparison.OrdinalIgnoreCase)));
        if (handbookOpen)
        {
            // Let H switch directly from the inventory/recipe context to the
            // vanilla handbook instead of hiding it until the inventory closes.
            browser?.CloseRecipeOverlay();
            foreach (GuiDialog inventory in capi.OpenedGuis.OfType<GuiDialog>()
                .Where(gui => gui.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory")
                .ToArray())
            {
                try { inventory.TryClose(); }
                catch (Exception exception)
                {
                    capi.Logger.Warning("[Recipe Browser] Could not close inventory for handbook: {0}", exception.Message);
                }
            }
        }

        bool inventoryOpen = capi.OpenedGuis.Any(gui => gui.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory");
        if (!inventoryOpen) browser?.CloseFilters();

        // If the player presses the normal inventory key while a recipe is
        // displayed, the newly opened inventory wins instead of being left
        // behind the recipe overlay.
        // Inventory and recipe windows coexist. The recipe must not close
        // merely because the preserved player inventory is still open.

        bool inventoryContextOpen = inventoryOpen || browser?.RecipeOverlayOpen == true;

        if (inventoryContextOpen && interfaceVisible)
        {
            if (interfaceToggle?.IsOpened() == true) interfaceToggle.TryClose();
            dimOverlay ??= new InventoryDimOverlayDialog(capi);
            browser ??= new InventoryBrowserDialog(capi, browserIndex!, percent => dimOverlay.SetOpacity(percent), () => ToggleInterface());
            // Do not close dialogs opened by the game while the inventory is
            // visible. Many chests and crafting stations use generic dialog
            // class names, so trying to recognize an allow-list caused them to
            // open for one tick and then immediately disappear.
            // Item panels and the recipe dialog now paint their own local
            // backdrops. Never cover the center of the screen with a global
            // dimmer: opened storage windows must stay visible and clickable.
            if (dimOverlay.IsOpened()) dimOverlay.TryClose();
            if (!browser.IsOpened()) browser.TryOpen();
        }
        else
        {
            browser?.CloseRecipeOverlay();
            if (browser?.IsOpened() == true) browser.TryClose();
            if (dimOverlay?.IsOpened() == true) dimOverlay.TryClose();
            if (inventoryContextOpen)
            {
                interfaceToggle ??= new InterfaceToggleDialog(capi, () => ToggleInterface());
                if (!interfaceToggle.IsOpened()) interfaceToggle.TryOpen();
            }
            else if (interfaceToggle?.IsOpened() == true) interfaceToggle.TryClose();
            RestoreHiddenHudDialogs();
        }
    }

    private void HideOtherHudDialogs()
    {
        if (capi is null) return;
        // TryClose mutates LoadedGuis, so always iterate over a stable snapshot.
        GuiDialog[] loadedDialogs = capi.LoadedGuis.OfType<GuiDialog>().ToArray();
        foreach (GuiDialog dialog in loadedDialogs)
        {
            if (!dialog.IsOpened() || KeepVisibleWithInventory(dialog.GetType())) continue;
            try
            {
                if (dialog.TryClose())
                {
                    hiddenHudDialogs.Add(dialog);
                    capi.Logger.Debug("[Recipe Browser] Temporarily hid HUD GUI {0}", dialog.GetType().FullName);
                }
                else if (dialog.GetType().FullName == "Vintagestory.Client.NoObf.HudDialogChat" && OpenedField is not null)
                {
                    // Vanilla chat deliberately refuses TryClose(), although its
                    // rendering is still controlled by GuiDialog's opened flag.
                    OpenedField.SetValue(dialog, false);
                    forceHiddenHudDialogs.Add(dialog);
                    capi.Logger.Debug("[Recipe Browser] Temporarily force-hid vanilla chat");
                }
            }
            catch (Exception exception)
            {
                // Some interaction dialogs run entity callbacks from TryClose.
                // A broken callback must never take down the client merely
                // because the recipe browser opened beside the inventory.
                capi.Logger.Warning("[Recipe Browser] Left GUI {0} open because TryClose failed: {1}",
                    dialog.GetType().FullName, exception.Message);
            }
        }
    }

    private static bool KeepVisibleWithInventory(Type type)
    {
        string name = type.FullName ?? type.Name;
        return name == "Vintagestory.Client.NoObf.GuiDialogInventory"
            || name.StartsWith("VsRecipeBrowser.", StringComparison.Ordinal)
            || name == "Vintagestory.GameContent.GuiDialogBlockEntityQuern"
            || name == "Vintagestory.GameContent.GuiDialogBlockEntityFirepit"
            || name == "Vintagestory.GameContent.GuiDialogBarrel"
            || name == "Vintagestory.GameContent.GuiDialogHandbook"
            || name.Contains("Handbook", StringComparison.OrdinalIgnoreCase)
            || name == "Vintagestory.GameContent.GuiDialogCreatureContents"
            || name.Contains("CreatureContents", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Harvestable", StringComparison.OrdinalIgnoreCase)
            // Container windows own live inventories and GPU textures. Closing
            // and reopening the same dialog instance can restore it with a
            // black background, so keep chests/storage alongside the player UI.
            || type.Name.Contains("Inventory", StringComparison.OrdinalIgnoreCase)
            || type.Name.Contains("Container", StringComparison.OrdinalIgnoreCase)
            || type.Name.Contains("Chest", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Hotbar", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tooltip", StringComparison.OrdinalIgnoreCase);
    }

    private void RestoreHiddenHudDialogs()
    {
        foreach (GuiDialog dialog in hiddenHudDialogs.ToArray())
        {
            dialog.TryOpen();
        }
        hiddenHudDialogs.Clear();
        if (OpenedField is not null)
        {
            foreach (GuiDialog dialog in forceHiddenHudDialogs)
                OpenedField.SetValue(dialog, true);
        }
        forceHiddenHudDialogs.Clear();
    }

    public override void Dispose()
    {
        AccidentalDropGuard.Api = null;
        sortButtons?.Dispose();
        linkedInventory?.Dispose();
        if (capi is not null && tickId != 0) capi.Event.UnregisterGameTickListener(tickId);
        browser?.Dispose();
        browserIndex?.Dispose();
        dimOverlay?.Dispose();
        interfaceToggle?.Dispose();
        RecipeOverlayDialog.ClearRecipeCatalog();
        harmony?.UnpatchAll("xlebka.xei.itemmodtooltip");
        RestoreHiddenHudDialogs();
    }
}
