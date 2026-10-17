# Source guide

## Entry point and responsibilities

`InventoryBrowserSystem.cs` wires client services, hotkeys and window lifecycle.
All namespaces retain their existing names so this reorganization does not change
saved recipe identities, configuration keys, Harmony targets or network contracts.

| Folder | Responsibility |
| --- | --- |
| `Browser/` | Item index, browser window, search, settings and navigation |
| `Recipes/` | Recipe lookup, grouping, alternatives, processing and recipe UI |
| `Favorites/` | Stable item identities, recipe bookmarks and hover previews |
| `Inventory/` | Sorting, block refill and inventory/container placement |
| `Search/` | Live server search, remembered client contents and highlights |
| `CraftingChoice/` | Client picker, server validation and shared wire contracts |
| `Interface/` | Interface controls and overlays |
| `Rendering/` | Read-only display grids and process illustrations |
| `Patches/` | Small Harmony integration points with the native GUI/inventories |
| `Shared/` | Configuration, language selection and protocol constants |

## Recipe dialog files

`RecipeOverlayDialog` remains one class, split into `partial` files. This is a
source organization change, not a rewrite of its state model or public API.

- Main file: shared dialog state, construction and disposal.
- `Catalog`: world-scoped recipe indexes and cache invalidation.
- `Grouping` and `Variants`: presentation grouping and material families.
- `Ingredients`: per-cell alternatives and source-frame selection.
- `Resolution`: preview stack resolution, smithing amounts and display caches.
- `ProcessCatalog`, `ProcessSources`, `CookingAndAlloys`: processing adapters.
- `Rendering` and `Layouts`: interaction, compact cards and station layouts.
- `Presentation`: synchronized cell contents, fuel, tools and animations.
- `Navigation`: page/category navigation, history and favorite restoration.
- `Autofill`: bridge to the inventory transfer planner.
- `Models`: display entry data and correlated source frames.

The browser window is likewise split into settings, search, navigation and
favorites files. Avoid adding unrelated responsibilities to the lifecycle files.

## Invariants to preserve

- Display grouping must retain native source recipes and their constraints.
- Autofill validates a complete native grid before transferring real items.
- Preview stacks are clones; rendering must not mutate player inventory stacks.
- A server validates selected crafting results. The client cannot authorize them.
- Unopened container contents require server support; client snapshots are stale
  by definition and must remain labelled as last-known contents.
- Cached recipe/ingredient data must be invalidated when worlds change.
- Keep GUI and inventory mutations on the client thread.
- New settings must have backwards-compatible defaults and RU/EN labels.

## Compatibility names

The public ID is `xit` and the assembly is `xit.dll`. Older internal namespaces
(`VsRecipeBrowser`, `XrbRecipeChoice`), config filenames, hotkey codes and protocol-v1
channel names are intentionally retained. Renaming those is a separate migration,
not cosmetic cleanup. New incompatible wire formats require new channel names.

The main project contains all code it needs. Sibling `XrbServer` and
`XrbRecipeChoice` projects are legacy compatibility/test fixtures, not required
parts of the current release. Do not ship their old archives alongside XIT.

## Verification

Build the main project, then run the regression harness:

```powershell
dotnet build work/VsRecipeBrowser/VsRecipeBrowser.csproj -c Release
dotnet run --project work/TestFavorites/TestFavorites.csproj -c Release
```

The existing test harness still expects this workstation's game/fixture paths.
It covers identities, recipe grouping, alternatives, liquid previews, transfer
planning, protocol compatibility and window layouts. It does not replace in-game
visual checks. The known chest-closing issue during initial world loading is not
fixed by this refactor.
