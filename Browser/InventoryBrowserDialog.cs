using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

// Lifecycle, panel composition and input forwarding; feature code lives under Browser/.
internal sealed partial class InventoryBrowserDialog : GuiDialog
{
    private static readonly FieldInfo? SelectionStartField = typeof(GuiElementEditableTextBase)
        .GetField("selectedTextStart", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? TextDraggingField = typeof(GuiElementEditableTextBase)
        .GetField("mouseDown", BindingFlags.Instance | BindingFlags.NonPublic);
    private const int Columns = 8;
    private const int FavoriteColumns = Columns;
    private const double SlotPitch = 48.8;
    private readonly int rows;
    private readonly int pageSize;
    private readonly BrowserInventory inventory;
    private readonly BrowserInventory favoriteInventory;
    private readonly BrowserIndex browserIndex;
    private List<BrowserIndexEntry> filteredEntries = new();
    private readonly HashSet<string> favorites;
    private readonly List<string> favoriteOrder;
    private readonly RecipeBrowserConfig config;
    private readonly Action<int> onDimChanged;
    private readonly Action onToggleInterface;
    private bool favoritesComposerCreated;
    private bool filtersVisible;
    private BrowserFilterDialog? filterDialog;
    private bool filtersComposerCreated;
    private double favoriteGridHeight;
    private double favoriteGridWidth;
    private double bottomControlY;
    private bool disposed;
    private int searchGeneration;
    private string searchText = "";
    private int page;
    private long searchCallbackId;
    private RecipeOverlayDialog? recipeOverlay;
    private FavoriteRecipePreviewDialog? favoritePreview;
    private string? previewKey;
    private long previewHoverSince;
    private readonly HashSet<GuiDialog> hiddenInventoryDialogs = new();

    public bool RecipeOverlayOpen => recipeOverlay?.IsOpened() == true;
    public bool SearchHasFocus => SingleComposer?.GetTextInput("browser-search")?.HasFocus == true;
    public bool FiltersOpen => filterDialog?.IsOpened() == true;

    public InventoryBrowserDialog(ICoreClientAPI api, BrowserIndex browserIndex, Action<int> onDimChanged, Action onToggleInterface) : base(api)
    {
        this.onDimChanged = onDimChanged;
        this.onToggleInterface = onToggleInterface;
        this.browserIndex = browserIndex;
        double logicalScreenHeight = api.Render.FrameHeight / Math.Max(0.5f, RuntimeEnv.GUIScale);
        rows = Math.Max(4, (int)Math.Floor((logicalScreenHeight - 170) / SlotPitch));
        pageSize = Columns * rows;
        inventory = new BrowserInventory(pageSize, api) { SlotClicked = OnSlotClicked };
        config = api.LoadModConfig<RecipeBrowserConfig>("vsrecipebrowser.json") ?? new RecipeBrowserConfig();
        favoriteOrder = config.Favorites.Distinct(StringComparer.Ordinal).ToList();
        BrowserSettings.Current = config;
        favorites = new HashSet<string>(favoriteOrder, StringComparer.Ordinal);
        favoriteInventory = new BrowserInventory(FavoriteColumns * rows, api) { SlotClicked = OnFavoriteSlotClicked };
        ApplyFilter();
        Compose();
        FillFavoriteSlots();
        browserIndex.Updated += OnIndexUpdated;
    }

    public override double DrawOrder => 0.25;
    public override string ToggleKeyCombinationCode => null!;
    public override bool PrefersUngrabbedMouse => true;
    public override bool CaptureAllInputs() => false;

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        // The search field should receive keyboard input only after the player
        // explicitly clicks it. Otherwise opening inventory immediately routes
        // ordinary hotkeys and movement keys into the search query.
        SingleComposer?.UnfocusOwnElements();
        if (favoritesComposerCreated) Composers["vsrecipebrowser-favorites"].UnfocusOwnElements();
    }

    public override void OnKeyDown(KeyEvent args)
    {
        GuiElementTextInput? search = SingleComposer?.GetTextInput("browser-search");
        if (search?.HasFocus == true && args.CtrlPressed && args.KeyCode == (int)GlKeys.A)
        {
            search.CaretPosWithoutLineBreaks = search.TextLengthWithoutLineBreaks;
            SelectionStartField?.SetValue(search, (int?)0);
            args.Handled = true;
            return;
        }
        base.OnKeyDown(args);
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (MouseInsideModPanels(args.X, args.Y))
        {
            base.OnMouseDown(args);
            return;
        }

        IPlayerInventoryManager inventoryManager = capi.World.Player.InventoryManager;
        bool isDropClick = args.Button is EnumMouseButton.Left or EnumMouseButton.Right;
        if (isDropClick && inventoryManager.CurrentHoveredSlot is null && inventoryManager.MouseItemSlot?.Itemstack is not null)
            args.Handled = inventoryManager.DropMouseSlotItems(args.Button == EnumMouseButton.Left);
    }

    public override void OnKeyPress(KeyEvent args)
    {
        var search = SingleComposer?.GetTextInput("browser-search");
        if (search?.HasFocus == true && !char.IsControl(args.KeyChar))
        {
            int oldLength = search.TextLengthWithoutLineBreaks;
            int oldCaret = search.CaretPosWithoutLineBreaks;
            int? selection = SelectionStartField?.GetValue(search) as int?;
            search.OnKeyPress(capi, args);
            if (args.Handled)
                search.CaretPosWithoutLineBreaks = SearchCaret.AfterInsertion(oldLength, oldCaret, selection, search.TextLengthWithoutLineBreaks);
            return;
        }
        base.OnKeyPress(args);
    }

    public override void OnMouseUp(MouseEvent args)
    {
        // Release must reach the text field even outside our panels; otherwise
        // its drag-selection flag stays set and the caret follows the mouse.
        SingleComposer?.GetTextInput("browser-search")?.OnMouseUp(capi, args);
        if (MouseInsideModPanels(args.X, args.Y)) base.OnMouseUp(args);
    }

    public override void OnMouseMove(MouseEvent args)
    {
        // Another dialog can consume the mouse-up before this HUD sees it.
        // Never allow the text field to drag when the physical button is up.
        if (!capi.Input.MouseButton.Left)
            if (SingleComposer?.GetTextInput("browser-search") is { } search) TextDraggingField?.SetValue(search, false);
        base.OnMouseMove(args);
    }

    private bool MouseInsideModPanels(int x, int y)
    {
        if (SingleComposer?.Bounds?.PointInside(x, y) == true) return true;
        if (filterDialog?.IsOpened() == true && filterDialog.SingleComposer.Bounds.PointInside(x, y)) return true;
        return favoritesComposerCreated
            && Composers["vsrecipebrowser-favorites"].Bounds.PointInside(x, y);
    }

    private void OnIndexUpdated()
    {
        if (disposed) return;
        QueueBackgroundFilter();
        FillFavoriteSlots();
    }

    private void Compose()
    {
        double gridHeight = rows * SlotPitch;
        double gridWidth = Columns * SlotPitch;
        double navigationY = gridHeight + 40;
        double searchY = navigationY + 54;
        const double filterWidth = 48;
        const double filterGap = 6;
        const double hideWidth = 48;
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(config.ItemListOnLeft ? EnumDialogArea.LeftTop : EnumDialogArea.RightTop)
            .WithFixedOffset(config.ItemListOnLeft ? 8 : -8, 8);
        ElementBounds gridBounds = ElementBounds.Fixed(0, 0, gridWidth, gridHeight);
        ElementBounds searchBounds = ElementBounds.Fixed(0, searchY, gridWidth - filterWidth - hideWidth - 2 * filterGap, 44);
        ElementBounds filterBounds = ElementBounds.Fixed(gridWidth - filterWidth - hideWidth - filterGap, searchY, filterWidth, 44);
        ElementBounds hideBounds = ElementBounds.Fixed(gridWidth - hideWidth, searchY, hideWidth, 44);
        ElementBounds previousBounds = ElementBounds.Fixed(0, navigationY, 90, 42);
        ElementBounds pageBounds = ElementBounds.Fixed(98, navigationY + 10, gridWidth - 196, 24);
        ElementBounds nextBounds = ElementBounds.Fixed(gridWidth - 90, navigationY, 90, 42);
        ElementBounds backgroundBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        backgroundBounds.BothSizing = ElementSizing.FitToChildren;
        backgroundBounds.WithChildren(gridBounds, searchBounds, filterBounds, hideBounds, previousBounds, pageBounds, nextBounds);

        SingleComposer = capi.Gui.CreateCompo("vsrecipebrowser-inventory-panel", dialogBounds)
            .AddDynamicCustomDraw(backgroundBounds, DrawPanelBackground, "browser-background")
            .AddTransparentItemSlotGrid(inventory, Columns, gridBounds, "browser-items")
            .AddButton("<", PreviousPage, previousBounds)
            .AddDynamicText(PageLabel(), CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), pageBounds, "browser-page")
            .AddButton(">", NextPage, nextBounds)
            .AddTextInput(searchBounds, OnSearchChanged, CairoFont.WhiteSmallText(), "browser-search")
            .AddButton("", ToggleFilters, filterBounds, CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), EnumButtonStyle.Normal, "browser-filter")
            .AddDynamicCustomDraw(filterBounds, DrawFilterGear, "browser-filter-gear")
            .AddButton("", HideInterface, hideBounds, CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), EnumButtonStyle.Normal, "browser-hide")
            .AddDynamicCustomDraw(hideBounds, InterfaceIcons.DrawClose, "browser-hide-icon")
            .Compose();

        SingleComposer.GetTextInput("browser-search").SetPlaceHolderText(BrowserText.Choose("Поиск предмета...  @mod", "Search items...  @mod"));
        SingleComposer.GetTextInput("browser-search").SetValue(searchText);
        RefreshFilterButton();

        favoriteGridHeight = gridHeight;
        favoriteGridWidth = FavoriteColumns * SlotPitch;
        bottomControlY = searchY;
        ComposeFavoritesPanel();
    }

    private bool HideInterface()
    {
        onToggleInterface();
        return true;
    }

    private bool OnDimSliderChanged(int value)
    {
        config.DimPercent = value;
        SaveConfig();
        (SingleComposer?.GetElement("browser-background") as GuiElementCustomDraw)?.Redraw();
        if (favoritesComposerCreated)
            (Composers["vsrecipebrowser-favorites"].GetElement("favorites-background") as GuiElementCustomDraw)?.Redraw();
        onDimChanged(value);
        return true;
    }

    private void DrawPanelBackground(Cairo.Context context, Cairo.ImageSurface surface, ElementBounds bounds)
    {
        double opacity = Math.Clamp(config.DimPercent / 100.0, 0, 1);
        context.SetSourceRGBA(0.08, 0.055, 0.035, opacity);
        context.Rectangle(0, 0, bounds.OuterWidth, bounds.OuterHeight);
        context.Fill();
    }

    private void DrawFilterGear(Cairo.Context context, Cairo.ImageSurface surface, ElementBounds bounds)
    {
        double cx = bounds.InnerWidth / 2;
        double cy = bounds.InnerHeight / 2;
        double radius = Math.Min(bounds.InnerWidth, bounds.InnerHeight) * 0.29;
        context.Save();
        context.SetSourceRGBA(0.94, 0.88, 0.76, 1);
        for (int i = 0; i < 64; i++)
        {
            double angle = i * Math.PI * 2 / 64;
            double r = radius * (i % 8 is 2 or 3 or 4 or 5 ? 1 : 0.77);
            double x = cx + Math.Cos(angle) * r;
            double y = cy + Math.Sin(angle) * r;
            if (i == 0) context.MoveTo(x, y);
            else context.LineTo(x, y);
        }
        context.ClosePath();
        context.Fill();
        context.SetSourceRGBA(0.16, 0.12, 0.08, 1);
        context.Arc(cx, cy, radius * 0.36, 0, Math.PI * 2);
        context.Fill();
        context.Restore();
    }

    private void DrawFilterPanelBackground(Cairo.Context context, Cairo.ImageSurface surface, ElementBounds bounds)
    {
        context.SetSourceRGBA(0.29, 0.245, 0.185, 0.97);
        context.Rectangle(0, 0, bounds.OuterWidth, bounds.OuterHeight);
        context.Fill();
        context.SetSourceRGBA(0.62, 0.53, 0.40, 1);
        context.LineWidth = 2 * RuntimeEnv.GUIScale;
        context.Rectangle(1, 1, bounds.OuterWidth - 2, bounds.OuterHeight - 2);
        context.Stroke();
    }

    private void OnSlotClicked(int slotId, EnumMouseButton button)
    {
        ItemStack? stack = inventory[slotId].Itemstack;
        if (stack is null) return;
        OpenRecipeView(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes);
    }

    private ItemStack? GetStackAtCursor(GuiComposer composer, string gridKey, BrowserInventory sourceInventory, int columns)
    {
        GuiElementItemSlotGrid grid = composer.GetSlotGrid(gridKey);
        int mouseX = capi.Input.MouseX;
        int mouseY = capi.Input.MouseY;

        // The vanilla slot grid adds its own scaled padding. Deriving a row
        // from the outer Bounds therefore selected the cell one row below on
        // some GUI scales even though the game's highlight/tooltip was right.
        // Use the exact per-slot bounds already calculated by the grid.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        FieldInfo? slotBoundsField = typeof(GuiElementItemSlotGrid).GetField("SlotBounds", flags);
        if (slotBoundsField?.GetValue(grid) is ElementBounds[] slotBounds)
        {
            for (int slot = 0; slot < slotBounds.Length && slot < sourceInventory.Count; slot++)
                if (slotBounds[slot]?.PointInside(mouseX, mouseY) == true)
                    return sourceInventory[slot].Itemstack;
            return null;
        }

        if (!grid.Bounds.PointInside(mouseX, mouseY)) return null;

        double pitch = GuiElementPassiveItemSlot.unscaledSlotSize * RuntimeEnv.GUIScale;
        int column = (int)((mouseX - grid.Bounds.renderX) / pitch);
        int row = (int)((mouseY - grid.Bounds.renderY) / pitch);
        if (column < 0 || column >= columns || row < 0) return null;
        int slotId = row * columns + column;
        if (slotId < 0 || slotId >= sourceInventory.Count) return null;
        return sourceInventory[slotId].Itemstack;
    }

    private void SaveConfig()
    {
        capi.StoreModConfig(config, "vsrecipebrowser.json");
        BrowserSettings.Current = config;
    }

    public override void Dispose()
    {
        disposed = true;
        CloseFavoritePreview();
        recipeOverlay?.Dispose();
        RestoreHiddenInventoryDialogs();
        browserIndex.Updated -= OnIndexUpdated;
        searchGeneration++;
        if (searchCallbackId != 0) capi.Event.UnregisterCallback(searchCallbackId);
        DisposeFiltersPanel();
        base.Dispose();
    }

    public void CloseRecipeOverlay()
    {
        if (recipeOverlay?.IsOpened() == true) recipeOverlay.TryClose();
    }


}
