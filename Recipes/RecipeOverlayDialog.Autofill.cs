using System.Text.RegularExpressions;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

internal sealed partial class RecipeOverlayDialog
{
    // The transfer planner validates the final grid before moving real items.
    private bool AutoFillCraftingRecipe()
    {
        // Offer real source recipes to the planner, not synthetic display combinations.
        if (!IsCraftingCategory || recipes.Count == 0) return true;
        if (!BrowserSettings.Current.CraftingAutofillEnabled)
        {
            NotifyAutoFill(BrowserText.Choose("Автозаполнение выключено в настройках XRB.", "Crafting autofill is disabled in XRB settings."));
            return true;
        }
        RecipeDisplayEntry entry = recipes[page];
        var selectedSources = entry.Frames is { Count: > 0 }
            ? entry.Frames[ingredientVariantIndex % entry.Frames.Count].SourceGridRecipes : entry.SourceGridRecipes;
        GridRecipe? recipe = selectedSources?
            .FirstOrDefault(grid => GridOutputMatches(grid, output[0].Itemstack ?? entry.Output))
            ?? selectedSources?.FirstOrDefault();
        if (recipe?.ResolvedIngredients is not { Length: > 0 })
        {
            NotifyAutoFill(BrowserText.Choose("Для этой объединённой страницы автозаполнение недоступно.", "Autofill is unavailable for this combined recipe page."));
            return true;
        }
        bool maximize = BrowserSettings.Current.ShiftAutofillEnabled && (capi.Input.KeyboardKeyState[(int)GlKeys.LShift]
            || capi.Input.KeyboardKeyState[(int)GlKeys.RShift]);
        IEnumerable<GridRecipe> sourceRecipes = (entry.SourceGridRecipes ?? selectedSources!)
            .Where(grid => SameCraftingRoute(grid, recipe))
            .Where(grid => ReferenceEquals(grid, recipe) || GridOutputMatches(grid, output[0].Itemstack ?? entry.Output))
            .OrderByDescending(grid => ReferenceEquals(grid, recipe))
            .Distinct();
        if (!RecipeTransferSystem.Request(sourceRecipes, maximize))
            NotifyAutoFill(BrowserText.Choose("Серверная часть XRB недоступна.", "XRB server component is unavailable."));
        return true;
    }

    private static bool SameCraftingRoute(GridRecipe candidate, GridRecipe selected)
    {
        // Sources are already limited to this grouped page. Consider every
        // material/ingredient variant, but never cross from manufacture into
        // a recipe that simply reorients the output block itself.
        return IsSelfTransformRecipe(candidate) == IsSelfTransformRecipe(selected);
    }

    private void OnRecipeTransferResult(RecipeTransferResult result)
    {
        NotifyAutoFill(result.Message);
        if (!result.Success) return;
        TryClose();
        if (capi.OpenedGuis.OfType<GuiDialog>().Any(gui => gui.IsOpened()
            && gui.GetType().FullName == "Vintagestory.Client.NoObf.GuiDialogInventory")) return;
        HotKey? inventoryHotKey = capi.Input.HotKeys.Values.FirstOrDefault(hotKey =>
            hotKey.Code.Equals("inventorydialog", StringComparison.OrdinalIgnoreCase))
            ?? capi.Input.HotKeys.Values.FirstOrDefault(hotKey =>
                hotKey.Code.Contains("inventory", StringComparison.OrdinalIgnoreCase) && hotKey.Handler is not null);
        inventoryHotKey?.Handler?.Invoke(inventoryHotKey.CurrentMapping);
    }
}
