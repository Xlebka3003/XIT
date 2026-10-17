# Xlebka’s Inventory Toolkit — XIT 3.0.6

A JEI-inspired toolkit for Vintage Story: recipe browsing, crafting autofill,
favorites, inventory sorting, container search and automatic hotbar block refill.
Features can be enabled or disabled in the gear menu. English and Russian are supported.

## Installation

Install `xit-3.0.6.zip` in the game's data `Mods` folder and restart the game.

- Singleplayer: one archive includes both client and server functionality.
- Multiplayer: client features work without server installation. Installing the
  same mod on the server enables live container search and crafting-result selection.
  Without compatible server support, search uses labelled last-known contents.
- Remove older XIT/XRB, XIT Server, Linked Inventories and XRB Recipe Choice
  archives before updating. Do not load old and new IDs together.

The current mod ID is `xit` and the assembly is `xit.dll`.

## Controls

| Control | Action |
| --- | --- |
| R / Left-click | Show recipes |
| U / Right-click | Show uses |
| Mouse wheel | Browse pages; over an ingredient, select alternatives |
| Shift | Pause automatic ingredient cycling |
| Backspace | Return to the previous recipe and page |
| Middle mouse button | Sort the inventory/container under the cursor |
| + / Shift + + | Fill ingredients for one craft / maximum available crafts |
| F | Toggle favorites |
| T | Find containers holding the hovered item; otherwise leave chat available |
| Ctrl + O | Show/hide the browser interface |
| Two-arrow button | Choose between different matching crafting results |

Keyboard shortcuts can be rebound in the game's Controls settings.

## Interface settings

Hide the favorites panel independently, move the item list to either side, or
choose Auto/Left/Right container placement. Inventory positions are saved.
Hidden favorites are retained. Container placement prefers free on-screen space
and may fall back to another side when necessary.

## Development

The 3.0.6 update reorganizes source files and adds brief maintenance comments.
It intentionally does not change recipes, gameplay, save formats or network channels.
See [the source guide](docs/ARCHITECTURE.md) for folders, responsibilities and invariants.
Earlier notes are preserved in [historical notes](docs/CHANGELOG-HISTORY.md).

Build with the .NET 10 SDK and a Vintage Story 1.22 installation:

```powershell
dotnet build VsRecipeBrowser.csproj -c Release
# Use a non-default game installation:
dotnet build VsRecipeBrowser.csproj -c Release -p:VintageStoryPath="D:\Games\Vintagestory"
```

The project is self-contained; the old separate Recipe Choice source folder is
not needed. Game libraries are referenced from your installation, not redistributed.

## Known limitation

Containers may close during the first few seconds of world loading. This
source-only reorganization does not claim to fix that issue.

## License

MIT License. Copyright (c) 2026 Xlebka. See [LICENSE](LICENSE).
