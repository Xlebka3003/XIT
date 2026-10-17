namespace VsRecipeBrowser;

internal sealed class RecipeBrowserConfig
{
    public List<string> Favorites { get; set; } = new();
    public Dictionary<string, FavoriteRecipeState> FavoriteRecipes { get; set; } = new();
    public int DimPercent { get; set; } = 48;
    public int ItemSearchRadius { get; set; } = 16;
    public bool AutoRefillBlocksEnabled { get; set; } = true;
    public bool RecipeHotkeysEnabled { get; set; } = true;
    public bool CraftingAutofillEnabled { get; set; } = true;
    public bool ShiftAutofillEnabled { get; set; } = true;
    public bool RecipeChoiceEnabled { get; set; } = true;
    public bool ItemSearchEnabled { get; set; } = true;
    public bool WorldSearchHighlightEnabled { get; set; } = true;
    public bool SlotSearchHighlightEnabled { get; set; } = true;
    public bool CloseInventoryOnSearch { get; set; } = true;
    public bool FavoritePreviewEnabled { get; set; } = true;
    public bool ShowFavoritesPanel { get; set; } = true;
    public bool ItemListOnLeft { get; set; }
    public ContainerPlacementSide ContainerSide { get; set; }
    public bool ShowBlocks { get; set; } = true;
    public bool ShowItems { get; set; } = true;
    public bool ShowTools { get; set; } = true;
    public bool ShowArmor { get; set; } = true;
    public bool ShowWeapons { get; set; } = true;
    public bool ShowPlants { get; set; } = true;
    public bool ShowDecorations { get; set; } = true;
    public bool ShowOres { get; set; } = true;
    public bool ShowAnimals { get; set; } = true;
    public bool ShowFood { get; set; } = true;
    public bool ShowMaterials { get; set; } = true;
    public bool ShowMechanisms { get; set; } = true;
    public bool ShowTechnical { get; set; } = true;
    public bool ShowLiquids { get; set; } = true;
    public bool SearchHiddenItems { get; set; } = true;
    public int SettingsDefaultsVersion { get; set; }
    public bool InterfaceVisible { get; set; } = true;
    public bool AutoOpenInventory { get; set; } = true;
    public bool CenterContainerWindows { get; set; } = true;
    public bool RememberInventoryPosition { get; set; } = true;
    public bool PreventAccidentalDrops { get; set; } = true;
    public bool SortingEnabled { get; set; } = true;
    public bool ShowWithoutRecipes { get; set; }
    internal void EnableAllFilters()
    {
        ShowBlocks = ShowItems = ShowTools = ShowArmor = ShowWeapons = ShowPlants = true;
        ShowDecorations = ShowOres = ShowAnimals = ShowFood = ShowMaterials = true;
        ShowMechanisms = ShowTechnical = ShowLiquids = SearchHiddenItems = true;
        ShowWithoutRecipes = false;
    }
}
