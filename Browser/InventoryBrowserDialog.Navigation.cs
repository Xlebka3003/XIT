using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

internal sealed partial class InventoryBrowserDialog
{
    // Keep normal inventory windows alive while navigating recipes.
    private bool OpenHandbook(ItemStack stack)
    {
        try
        {
            ModSystemSurvivalHandbook handbookSystem = capi.ModLoader.GetModSystem<ModSystemSurvivalHandbook>();
            FieldInfo? dialogField = typeof(ModSystemSurvivalHandbook).GetField("dialog", BindingFlags.Instance | BindingFlags.NonPublic);
            if (dialogField?.GetValue(handbookSystem) is not GuiDialogHandbook handbook) return false;
            GuiDialog? playerInventory = capi.OpenedGuis.OfType<GuiDialog>()
                .FirstOrDefault(dialog => dialog.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory");
            playerInventory?.TryClose();
            var pageForStack = new GuiHandbookItemStackPage(capi, stack.Clone());
            string pageCode = pageForStack.PageCode;
            pageForStack.Dispose();
            if (!handbook.IsOpened()) handbook.TryOpen();
            return handbook.OpenDetailPageFor(pageCode);
        }
        catch (Exception exception)
        {
            capi.Logger.Error("[Recipe Browser] Could not open handbook page for {0}: {1}", stack.Collectible.Code, exception);
            return false;
        }
    }

    public bool OpenHoveredRecipe() => OpenHoveredRecipeView(RecipeViewMode.Recipes);

    public bool OpenHoveredUses() => OpenHoveredRecipeView(RecipeViewMode.Uses);
    public bool BackRecipe() => SingleComposer?.GetTextInput("browser-search")?.HasFocus != true
        && recipeOverlay?.IsOpened() == true && recipeOverlay.Back();

    private bool OpenHoveredRecipeView(RecipeViewMode mode)
    {
        if (!IsOpened()) return false;
        if (recipeOverlay?.IsOpened() == true)
        {
            // A modal recipe dialog is not sent mouse-move events while the
            // pointer is over another HUD element. Refresh all hover states at
            // the exact moment R/U is pressed, using the real cursor position.
            // This clears a stale recipe/browser slot and lets HudHotbar claim
            // CurrentHoveredSlot without requiring the inventory to be reopened.
            recipeOverlay.OnMouseMove(new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));
        }
        // Category icons are controls, not recipe ingredients. Without this
        // guard CurrentHoveredSlot leaked the firepit tab icon into U/R and a
        // replacement dialog was opened for the icon itself.
        if (recipeOverlay?.IsOpened() == true && recipeOverlay.IsCategoryTabHovered()) return true;
        ItemStack? hovered = recipeOverlay?.IsOpened() == true
            ? recipeOverlay.GetHoveredRecipeStack() ?? GetHoveredBrowserPanelStack() ?? GetHotbarStackAtCursor()
            : GetHoveredItemStack();
        if (hovered is null) return false;

        if (mode == RecipeViewMode.Recipes && favoritesComposerCreated
            && GetStackAtCursor(Composers["vsrecipebrowser-favorites"], "favorite-items", favoriteInventory, FavoriteColumns) is { } favorite
            && OpenSavedFavorite(favorite)) return true;
        return OpenRecipeView(hovered, mode);
    }

    private bool OpenRecipeView(ItemStack stack, RecipeViewMode mode)
    {
        if (recipeOverlay?.IsOpened() == true)
        {
            recipeOverlay.Navigate(stack, mode);
            return true;
        }

        // Check and collect before constructing any GUI. A missing R/U result
        // therefore cannot allocate or dispose textures belonging to the
        // currently visible recipe dialog.
        RecipeOverlayDialog? replacement = RecipeOverlayDialog.CreateIfAvailable(capi, stack, mode);
        if (replacement is null) return true;

        if (recipeOverlay is not null)
        {
            recipeOverlay.OnClosed -= RestoreHiddenInventoryDialogs;
            if (recipeOverlay.IsOpened()) recipeOverlay.TryClose();
            recipeOverlay.Dispose();
        }
        recipeOverlay = replacement;
        recipeOverlay.OnClosed += RestoreHiddenInventoryDialogs;
        bool opened = replacement.TryOpen();
        // Keep the real inventory open: Linked Inventories otherwise closes
        // every linked container when it receives the inventory close event.
        return opened;
    }

    private void HideVanillaInventoryDialogs()
    {
        GuiDialog[] dialogs = capi.OpenedGuis.OfType<GuiDialog>().ToArray();
        foreach (GuiDialog dialog in dialogs)
        {
            Type type = dialog.GetType();
            string name = type.FullName ?? type.Name;
            if (name.StartsWith("VsRecipeBrowser.", StringComparison.Ordinal)
                || name != "Vintagestory.Client.NoObf.GuiDialogInventory") continue;
            if (dialog.TryClose()) hiddenInventoryDialogs.Add(dialog);
        }
    }

    private void RestoreHiddenInventoryDialogs()
    {
        foreach (GuiDialog dialog in hiddenInventoryDialogs.ToArray()) dialog.TryOpen();
        hiddenInventoryDialogs.Clear();
    }

    private bool OpenHoveredHandbook()
    {
        if (!IsOpened()) return false;
        ItemStack? hovered = GetHoveredItemStack();
        return hovered is not null && OpenHandbook(hovered);
    }

    internal ItemStack? GetSearchTarget()
    {
        if (!IsOpened() || SearchHasFocus || FiltersOpen) return null;
        if (recipeOverlay?.IsOpened() == true)
        {
            recipeOverlay.OnMouseMove(new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));
            if (recipeOverlay.IsCategoryTabHovered()) return null;
            return recipeOverlay.GetHoveredRecipeStack() ?? GetHoveredItemStack();
        }
        return GetHoveredItemStack();
    }

    private ItemStack? GetHoveredItemStack()
    {
        return GetHoveredBrowserPanelStack()
            ?? GetHotbarStackAtCursor()
            ?? capi.World.Player.InventoryManager.CurrentHoveredSlot?.Itemstack;
    }

    private ItemStack? GetHotbarStackAtCursor()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        FieldInfo? elementsField = typeof(GuiComposer).GetField("interactiveElements", flags);
        FieldInfo? boundsField = typeof(GuiElementItemSlotGrid).GetField("SlotBounds", flags);
        FieldInfo? inventoryField = typeof(GuiElementItemSlotGrid).GetField("inventory", flags);
        if (elementsField is null || boundsField is null || inventoryField is null) return null;

        int mouseX = capi.Input.MouseX;
        int mouseY = capi.Input.MouseY;
        foreach (GuiDialog dialog in capi.OpenedGuis.OfType<GuiDialog>())
        {
            string name = dialog.GetType().FullName ?? dialog.GetType().Name;
            if (!name.Contains("Hotbar", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (GuiComposer composer in dialog.Composers.Values)
            {
                if (elementsField.GetValue(composer) is not Dictionary<string, GuiElement> elements) continue;
                foreach (GuiElementItemSlotGrid grid in elements.Values.OfType<GuiElementItemSlotGrid>())
                {
                    if (boundsField.GetValue(grid) is not ElementBounds[] slotBounds
                        || inventoryField.GetValue(grid) is not IInventory hotbarInventory) continue;
                    for (int slot = 0; slot < slotBounds.Length && slot < hotbarInventory.Count; slot++)
                    {
                        if (slotBounds[slot]?.PointInside(mouseX, mouseY) == true)
                            return hotbarInventory[slot].Itemstack;
                    }
                }
            }
        }
        return null;
    }

    private ItemStack? GetHoveredBrowserPanelStack() =>
        GetStackAtCursor(SingleComposer, "browser-items", inventory, Columns)
        ?? (favoritesComposerCreated ? GetStackAtCursor(Composers["vsrecipebrowser-favorites"], "favorite-items", favoriteInventory, FavoriteColumns) : null);
}
