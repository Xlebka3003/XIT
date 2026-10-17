using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

internal sealed partial class InventoryBrowserDialog
{
    // Hiding the panel removes its composer, not the saved favorites.
    private void ComposeFavoritesPanel()
    {
        if (favoritesComposerCreated)
        {
            Composers["vsrecipebrowser-favorites"].Dispose();
            Composers.Remove("vsrecipebrowser-favorites");
            favoritesComposerCreated = false;
        }
        if (!config.ShowFavoritesPanel) return;
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(config.ItemListOnLeft ? EnumDialogArea.RightTop : EnumDialogArea.LeftTop)
            .WithFixedOffset(config.ItemListOnLeft ? -8 : 8, 8);
        ElementBounds gridBounds = ElementBounds.Fixed(0, 0, favoriteGridWidth, favoriteGridHeight);
        ElementBounds footerBounds = ElementBounds.Fixed(0, bottomControlY, favoriteGridWidth, 44);
        ElementBounds sizingBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        sizingBounds.BothSizing = ElementSizing.FitToChildren;
        sizingBounds.WithChildren(gridBounds, footerBounds);

        GuiComposer composer = capi.Gui.CreateCompo("vsrecipebrowser-favorites", dialogBounds)
            .AddDynamicCustomDraw(sizingBounds, DrawPanelBackground, "favorites-background")
            .AddTransparentItemSlotGrid(favoriteInventory, FavoriteColumns, gridBounds, "favorite-items", ignoreEmptySlots: true)
            .Compose();
        Composers["vsrecipebrowser-favorites"] = composer;
        if (composer.GetElement("favorite-items") is TransparentItemSlotGrid favoriteGrid)
            favoriteGrid.SuppressDescription = config.FavoritePreviewEnabled;
        favoritesComposerCreated = true;
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (favoritesComposerCreated && Composers["vsrecipebrowser-favorites"].GetElement("favorite-items") is TransparentItemSlotGrid grid
            && grid.SuppressDescription != config.FavoritePreviewEnabled)
        {
            grid.SuppressDescription = config.FavoritePreviewEnabled;
            grid.OnMouseMove(capi, new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));
        }
        base.OnRenderGUI(deltaTime);
        ItemStack? hovered = !favoritesComposerCreated || !config.FavoritePreviewEnabled || FiltersOpen || RecipeOverlayOpen ? null
            : GetStackAtCursor(Composers["vsrecipebrowser-favorites"], "favorite-items", favoriteInventory, FavoriteColumns);
        string? key = hovered is null ? null : FavoriteKey(hovered);
        if (key != previewKey)
        {
            CloseFavoritePreview();
            previewKey = key;
            previewHoverSince = capi.ElapsedMilliseconds;
        }
        if (hovered is null) return;
        capi.World.Player.InventoryManager.CurrentHoveredSlot = null!;
        if (favoritePreview is null && capi.ElapsedMilliseconds - previewHoverSince >= 350)
        {
            config.FavoriteRecipes.TryGetValue(FavoriteKey(hovered), out var savedRecipe);
            favoritePreview = new FavoriteRecipePreviewDialog(capi, hovered, RecipeOverlayDialog.FavoritePreview(capi, hovered, savedRecipe));
            favoritePreview.TryOpen();
        }
        favoritePreview?.Position();
    }

    private void CloseFavoritePreview()
    {
        favoritePreview?.TryClose();
        favoritePreview?.Dispose();
        favoritePreview = null;
        previewKey = null;
    }

    // Saved favorites can restore a recipe context as well as an item.
    private void OnFavoriteSlotClicked(int slotId, EnumMouseButton button)
    {
        ItemStack? stack = favoriteInventory[slotId].Itemstack;
        if (stack is null) return;
        if (button == EnumMouseButton.Left && OpenSavedFavorite(stack)) return;
        OpenRecipeView(stack, button == EnumMouseButton.Right ? RecipeViewMode.Uses : RecipeViewMode.Recipes);
    }

    private bool OpenSavedFavorite(ItemStack display)
    {
        if (!config.FavoriteRecipes.TryGetValue(FavoriteKey(display), out var saved)
            || FavoriteRecipeState.Decode(saved.TargetData, capi.World) is not { } target
            || !Enum.TryParse<RecipeViewMode>(saved.Mode, out var savedMode)) return false;
        OpenRecipeView(target, savedMode);
        return recipeOverlay?.RestoreFavoriteRecipe(saved) == true;
    }

    // Favorites preserve item attributes and optional recipe bookmarks.
    public bool ToggleHoveredFavorite()
    {
        if (!IsOpened()) return false;
        if (recipeOverlay?.IsOpened() == true)
            recipeOverlay.OnMouseMove(new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));
        ItemStack? hovered = recipeOverlay?.IsOpened() == true
            ? recipeOverlay.GetHoveredRecipeStack() ?? GetHoveredItemStack() : GetHoveredItemStack();
        if (hovered is null) return false;

        string key = FavoriteKey(hovered);
        string legacyKey = $"{hovered.Class}:{hovered.Collectible.Code}";
        if (favorites.Contains(legacyKey)) key = legacyKey;
        if (favorites.Add(key))
        {
            favoriteOrder.Add(key);
            if (recipeOverlay?.IsOpened() == true && recipeOverlay.CaptureFavoriteRecipe(hovered) is { } saved)
                config.FavoriteRecipes[key] = saved;
        }
        else
        {
            favorites.Remove(key);
            favoriteOrder.Remove(key);
            config.FavoriteRecipes.Remove(key);
        }
        config.Favorites = new List<string>(favoriteOrder);
        SaveConfig();
        FillFavoriteSlots();
        return true;
    }

    // Legacy item-only favorites remain readable.
    private void FillFavoriteSlots()
    {
        Dictionary<string, BrowserIndexEntry> entriesByKey = browserIndex.Entries
            .GroupBy(entry => entry.FavoriteKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        // Preserve old code-only favorites. Their original attributes were not
        // stored, so users must re-add a legacy favorite to select its material.
        foreach (var entry in browserIndex.Entries)
            entriesByKey.TryAdd($"{entry.ItemClass}:{entry.Code}", entry);
        List<ItemStack> stacks = favoriteOrder
            .Select(key => config.FavoriteRecipes.TryGetValue(key, out var saved)
                ? FavoriteRecipeState.Decode(saved.DisplayData, capi.World)
                    ?? (entriesByKey.TryGetValue(key, out var fallback) ? fallback.CreateStack(capi.World) : null)
                : entriesByKey.TryGetValue(key, out var entry) ? entry.CreateStack(capi.World) : null)
            .Where(stack => stack is not null)
            .Select(stack => stack!)
            .Take(favoriteInventory.Count)
            .ToList();
        for (int slot = 0; slot < favoriteInventory.Count; slot++)
        {
            favoriteInventory[slot].Itemstack = slot < stacks.Count ? stacks[slot].Clone() : null;
            favoriteInventory[slot].MarkDirty();
        }
    }

    private static string FavoriteKey(ItemStack stack) => FavoriteIdentity.Key(stack);
}
