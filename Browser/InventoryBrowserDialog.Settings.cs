using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

internal sealed partial class InventoryBrowserDialog
{
    // Settings reuse the existing configuration so toggles do not reset favorites or keybindings.
    private bool ToggleFilters()
    {
        filtersVisible = !filtersVisible;
        if (filtersVisible) ComposeFiltersPanel(); else DisposeFiltersPanel();
        return true;
    }

    private int settingsSection;
    private void ComposeFiltersPanel()
    {
        DisposeFiltersPanel();
        const double navigationWidth = 185, contentWidth = 420, gap = 12;
        double screenHeight = capi.Render.FrameHeight / Math.Max(0.5f, RuntimeEnv.GUIScale);
        double row = Math.Clamp((screenHeight - 150) / 18 - 4, 19, 30);
        double contentX = navigationWidth + gap;
        var bounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        var background = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        background.BothSizing = ElementSizing.FitToChildren;
        var composer = capi.Gui.CreateCompo("vsrecipebrowser-filters", bounds).AddStaticCustomDraw(background, DrawFilterPanelBackground);
        var font = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Left);
        string[] sections = {
            BrowserText.Choose("Фильтры предметов", "Item filters"),
            BrowserText.Choose("Инвентарь", "Inventory"),
            BrowserText.Choose("Контейнеры", "Containers"),
            BrowserText.Choose("Поиск и подсветка", "Search and highlights"),
            BrowserText.Choose("Интерфейс", "Interface")
        };
        var title = ElementBounds.Fixed(contentX, 0, contentWidth, 26);
        background.WithChildren(title);
        composer.AddStaticText(sections[settingsSection], font, title);
        for (int section = 0; section < sections.Length; section++)
        {
            int selected = section;
            var button = ElementBounds.Fixed(0, section * (row + 6), navigationWidth, row);
            background.WithChildren(button);
            composer.AddButton((section == settingsSection ? "> " : "") + sections[section], () => {
                settingsSection = selected; ComposeFiltersPanel(); return true;
            }, button, font, EnumButtonStyle.Normal, "settings-section-" + section);
        }
        int index = 0;
        void Button(string label, Func<bool> action, string key)
        {
            var button = ElementBounds.Fixed(contentX, 32 + index++ * (row + 4), contentWidth, row);
            background.WithChildren(button);
            composer.AddButton(label, () => action(), button, font, EnumButtonStyle.Normal, key);
        }
        void Toggle(string ru, string en, bool value, Action action, string key) =>
            Button(FilterLabel(BrowserText.Choose(ru, en), value), () => {
                action(); SaveConfig(); LinkedInventoryController.Instance?.ReloadSettings();
                InventorySorter.ReloadSettings(capi); ComposeFiltersPanel(); return true;
            }, key);
        if (settingsSection == 0)
        {
            var groups = new[] { BrowserItemGroup.Weapons, BrowserItemGroup.Tools, BrowserItemGroup.Armor, BrowserItemGroup.Food,
                BrowserItemGroup.Blocks, BrowserItemGroup.Ores, BrowserItemGroup.Materials, BrowserItemGroup.Mechanisms,
                BrowserItemGroup.Animals, BrowserItemGroup.Items, BrowserItemGroup.Plants, BrowserItemGroup.Decorations,
                BrowserItemGroup.Liquids, BrowserItemGroup.Technical };
            string[] ru = { "Оружие", "Инструменты", "Броня и одежда", "Еда", "Блоки", "Руды", "Материалы", "Механизмы", "Животные", "Прочие предметы", "Растения", "Декор и трофеи", "Жидкость", "Техническое" };
            string[] en = { "Weapons", "Tools", "Armor and clothing", "Food", "Blocks", "Ores", "Materials", "Mechanisms", "Animals", "Other items", "Plants", "Decor and trophies", "Liquids", "Technical" };
            for (int i = 0; i < groups.Length; i++) { var group = groups[i]; Button(FilterLabel(BrowserText.Choose(ru[i], en[i]), IsGroupEnabled(group)), () => ToggleGroup(group), "filter-" + group); }
            Button(FilterLabel(BrowserText.Choose("Искать среди скрытых", "Search hidden items"), config.SearchHiddenItems), ToggleSearchHidden, "filter-search-hidden");
            Button(FilterLabel(BrowserText.Choose("Предметы без рецепта", "Items without recipes"), config.ShowWithoutRecipes), ToggleWithoutRecipes, "without-recipes");
            Button(BrowserText.Choose("Сбросить фильтры", "Reset filters"), ResetFilters, "filter-reset");
        }
        else if (settingsSection == 1)
        {
            Button(FilterLabel(BrowserText.Choose("Сортировка", "Inventory sorting"), config.SortingEnabled), ToggleSorting, "sorting-enabled");
            Toggle("Автопополнение блоков", "Auto-refill blocks", config.AutoRefillBlocksEnabled, () => config.AutoRefillBlocksEnabled = !config.AutoRefillBlocksEnabled, "block-auto-refill");
            Toggle("Защита от случайного выброса", "Prevent accidental drops", config.PreventAccidentalDrops, () => config.PreventAccidentalDrops = !config.PreventAccidentalDrops, "prevent-accidental-drops");
        }
        else if (settingsSection == 2)
        {
            Toggle("Запоминать положение инвентаря", "Remember inventory position", config.RememberInventoryPosition, () => config.RememberInventoryPosition = !config.RememberInventoryPosition, "remember-inventory-position");
            Button(FilterLabel(BrowserText.Choose("Открывать инвентарь", "Auto-open inventory"), config.AutoOpenInventory), () => ToggleLinkedOption(true), "linked-auto-open");
            Button(FilterLabel(BrowserText.Choose("Размещать контейнеры рядом", "Arrange containers nearby"), config.CenterContainerWindows), () => ToggleLinkedOption(false), "linked-center");
            Button(BrowserText.Choose("Сторона контейнеров: ", "Container side: ") + (config.ContainerSide switch {
                ContainerPlacementSide.Left => BrowserText.Choose("Слева", "Left"),
                ContainerPlacementSide.Right => BrowserText.Choose("Справа", "Right"),
                _ => BrowserText.Choose("Авто", "Auto")
            }), () => {
                config.ContainerSide = (ContainerPlacementSide)(((int)config.ContainerSide + 1) % 3);
                SaveConfig(); LinkedInventoryController.Instance?.ReloadSettings(); ComposeFiltersPanel(); return true;
            }, "container-side");
            Button(BrowserText.Choose("Сбросить положение окон", "Reset window positions"), () => { LinkedInventoryController.Instance?.ResetWindowPositions(); return true; }, "reset-window-positions");
        }
        else if (settingsSection == 3)
        {
            Toggle("Поиск предметов", "Find items", config.ItemSearchEnabled, () => config.ItemSearchEnabled = !config.ItemSearchEnabled, "item-search");
            Toggle("Контуры хранилищ", "Container world outlines", config.WorldSearchHighlightEnabled, () => config.WorldSearchHighlightEnabled = !config.WorldSearchHighlightEnabled, "world-highlights");
            Toggle("Подсветка ячеек", "Matching slot highlights", config.SlotSearchHighlightEnabled, () => config.SlotSearchHighlightEnabled = !config.SlotSearchHighlightEnabled, "slot-highlights");
            Toggle("Закрывать инвентарь при поиске", "Close inventory on search", config.CloseInventoryOnSearch, () => config.CloseInventoryOnSearch = !config.CloseInventoryOnSearch, "close-on-search");
            Button(BrowserText.Choose("Радиус поиска: ", "Item search radius: ") + Math.Clamp(config.ItemSearchRadius, 4, 24), () => {
                int radius = Math.Clamp(config.ItemSearchRadius, 4, 24);
                config.ItemSearchRadius = radius >= 24 ? 4 : Math.Min(24, radius + 4);
                SaveConfig(); ComposeFiltersPanel(); return true;
            }, "item-search-radius");
        }
        else
        {
            Button(FilterLabel(BrowserText.Choose("Показывать избранное", "Show favorites panel"), config.ShowFavoritesPanel), () => {
                config.ShowFavoritesPanel = !config.ShowFavoritesPanel; SaveConfig();
                CloseFavoritePreview(); ComposeFavoritesPanel(); ComposeFiltersPanel(); return true;
            }, "show-favorites");
            Button(BrowserText.Choose("Список предметов: ", "Item list: ") + (config.ItemListOnLeft
                ? BrowserText.Choose("Слева", "Left") : BrowserText.Choose("Справа", "Right")), () => {
                config.ItemListOnLeft = !config.ItemListOnLeft; SaveConfig(); CloseFavoritePreview();
                capi.Event.EnqueueMainThreadTask(() => {
                    if (disposed) return;
                    SingleComposer?.Dispose(); Compose(); ComposeFiltersPanel();
                }, "xit-panel-side"); return true;
            }, "item-list-side");
            Toggle("Рецепт при наведении на избранное", "Favorite recipe hover preview", config.FavoritePreviewEnabled, () => config.FavoritePreviewEnabled = !config.FavoritePreviewEnabled, "favorite-preview");
            Button(BrowserText.Choose("Прозрачность фона: ", "Background opacity: ") + config.DimPercent + "%", () => {
                OnDimSliderChanged(((Math.Clamp(config.DimPercent, 0, 100) / 20 + 1) % 6) * 20);
                ComposeFiltersPanel(); return true;
            }, "background-opacity");
        }
        composer.Compose();
        filterDialog = new BrowserFilterDialog(capi, composer, () => filtersVisible = false);
        filtersVisible = true;
        filtersComposerCreated = true;
        filterDialog.TryOpen();
    }

    private static string FilterLabel(string name, bool enabled) => (enabled ? BrowserText.Choose("Вкл. — ", "On — ") : BrowserText.Choose("Выкл. — ", "Off — ")) + name;

    private bool ToggleLinkedOption(bool autoOpen)
    {
        if (autoOpen) config.AutoOpenInventory = !config.AutoOpenInventory;
        else config.CenterContainerWindows = !config.CenterContainerWindows;
        SaveConfig();
        LinkedInventoryController.Instance?.ReloadSettings();
        ComposeFiltersPanel();
        return true;
    }

    private bool ToggleSorting()
    {
        config.SortingEnabled = !config.SortingEnabled;
        SaveConfig();
        InventorySorter.ReloadSettings(capi);
        ComposeFiltersPanel();
        return true;
    }

    private bool ToggleWithoutRecipes()
    {
        config.ShowWithoutRecipes = !config.ShowWithoutRecipes;
        SaveConfig();
        page = 0;
        QueueBackgroundFilter();
        ComposeFiltersPanel();
        return true;
    }

    private bool ToggleGroup(BrowserItemGroup group)
    {
        SetGroupEnabled(group, !IsGroupEnabled(group));
        SaveConfig();
        page = 0;
        QueueBackgroundFilter();
        ComposeFiltersPanel();
        RefreshFilterButton();
        return true;
    }

    private bool ToggleSearchHidden()
    {
        config.SearchHiddenItems = !config.SearchHiddenItems;
        SaveConfig();
        page = 0;
        QueueBackgroundFilter();
        ComposeFiltersPanel();
        return true;
    }

    private bool ResetFilters()
    {
        config.EnableAllFilters();
        SaveConfig();
        page = 0;
        QueueBackgroundFilter();
        ComposeFiltersPanel();
        RefreshFilterButton();
        return true;
    }

    private void DisposeFiltersPanel()
    {
        if (filterDialog?.IsOpened() == true) filterDialog.TryClose();
        filterDialog?.Dispose();
        filterDialog = null;
        filtersComposerCreated = false;
    }
    public void CloseFilters() { filtersVisible = false; DisposeFiltersPanel(); }
    public override void OnGuiClosed() { CloseFilters(); CloseFavoritePreview(); base.OnGuiClosed(); }

    private void RefreshFilterButton() => SingleComposer?.GetButton("browser-filter")?.SetActive(false);
    private bool AnyFilterDisabled() => !config.ShowBlocks || !config.ShowItems || !config.ShowTools || !config.ShowFood || !config.ShowMaterials || !config.ShowMechanisms || !config.ShowTechnical || !config.ShowLiquids;

    private bool IsGroupEnabled(BrowserItemGroup group) => group switch
    {
        BrowserItemGroup.Blocks => config.ShowBlocks,
        BrowserItemGroup.Items => config.ShowItems,
        BrowserItemGroup.Tools => config.ShowTools,
        BrowserItemGroup.Food => config.ShowFood,
        BrowserItemGroup.Materials => config.ShowMaterials,
        BrowserItemGroup.Mechanisms => config.ShowMechanisms,
        BrowserItemGroup.Armor => config.ShowArmor,
        BrowserItemGroup.Weapons => config.ShowWeapons,
        BrowserItemGroup.Plants => config.ShowPlants,
        BrowserItemGroup.Decorations => config.ShowDecorations,
        BrowserItemGroup.Ores => config.ShowOres,
        BrowserItemGroup.Animals => config.ShowAnimals,
        BrowserItemGroup.Technical => config.ShowTechnical,
        BrowserItemGroup.Liquids => config.ShowLiquids,
        _ => true
    };

    private void SetGroupEnabled(BrowserItemGroup group, bool enabled)
    {
        switch (group)
        {
            case BrowserItemGroup.Blocks: config.ShowBlocks = enabled; break;
            case BrowserItemGroup.Items: config.ShowItems = enabled; break;
            case BrowserItemGroup.Tools: config.ShowTools = enabled; break;
            case BrowserItemGroup.Food: config.ShowFood = enabled; break;
            case BrowserItemGroup.Materials: config.ShowMaterials = enabled; break;
            case BrowserItemGroup.Mechanisms: config.ShowMechanisms = enabled; break;
            case BrowserItemGroup.Armor: config.ShowArmor = enabled; break;
            case BrowserItemGroup.Weapons: config.ShowWeapons = enabled; break;
            case BrowserItemGroup.Plants: config.ShowPlants = enabled; break;
            case BrowserItemGroup.Decorations: config.ShowDecorations = enabled; break;
            case BrowserItemGroup.Ores: config.ShowOres = enabled; break;
            case BrowserItemGroup.Animals: config.ShowAnimals = enabled; break;
            case BrowserItemGroup.Technical: config.ShowTechnical = enabled; break;
            case BrowserItemGroup.Liquids: config.ShowLiquids = enabled; break;
        }
    }
}
