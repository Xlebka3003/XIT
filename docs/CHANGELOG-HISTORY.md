# Historical development notes

These are archived notes, not current installation instructions. In particular,
references to separate server/client packages and older IDs are obsolete.
See the current README for supported installation.

# Xlebka's Inventory Toolkit (XIT) 3.0.5

3.0.5 packages the MIT License (Copyright (c) 2026 Xlebka). No gameplay changes from 3.0.4.

Interface settings now include Show favorites panel and Item list: Left/Right. Favorites appear on the opposite side and remain saved while hidden. Containers settings include Container side: Auto/Left/Right when Arrange containers nearby is enabled. Side preferences retain the inventory as an anchor, prefer non-overlapping on-screen positions, and fall back when the requested side has insufficient space. New controls have Russian and English labels; preferences persist and old configurations retain the previous default layout.

The container-search hotkey takes priority over chat only when a hovered item can be searched. Without a target, with search disabled, or while editing XIT search/chat, the handler does not consume the key. Rebinding remains supported, and world reloads preserve the existing mapping without duplicate hotkey registration.

Recipes, crafting autofill, inventory sorting, container search and hotbar refill. Client and server functionality are now included in one archive. Author: Xlebka.

Install only xit-3.0.5.zip locally for all single-player features. In multiplayer, install the same archive on the server to enable live container search and crafting-result selection. Server installation is optional: clients without server support retain recipe browsing, inventory tools and labelled last-known container search. Players without XIT are not required to install it by this mod's metadata.

Remove old XIT/XRB and XIT Server archives before updating, including xitserver-1.0.0.zip (ID xrbserver). Do not run the old server addon alongside the merged mod. Fully restart the game/server. Technical ID is xit and the assembly is xit.dll. Configuration filenames and protocol-v1 channels retain legacy names for compatibility. Future incompatible wire protocols must use new channel names rather than mod-version equality checks.

The following notes describe feature history; earlier separate-server installation instructions are superseded by the installation instructions above.

Container slot highlights now expire with the eight-second search session and stop during pending/no-hit searches. Clearing a search drops its target identity. Chat no longer reports container counts; the last-known-content warning and search-limit warning remain.

U hides exact duplicate heating entries already present in the flame tab when the searched item is only fuel. Actual ingredient uses and different quantities/temperatures/results remain. The flame icon is shifted another four pixels right. Release numbers remain 3.0.0 / 1.0.0.

Final release packaging retains the latest unique-result selector and per-frame button positioning fixes. Russian and English UI text follows the game's language; English is the fallback for other locales. This replaces the earlier 3.0.0 package; fully restart the game after updating. Author: Xlebka.

Crafting-result selection lists only distinct item/class/quantity/attribute results, keeping the active recipe as representative. Identical results hide the button; underlying recipes and server validation remain unchanged. Client filtering also handles older protocol-v1 servers. Choice arrows follow live inventory output bounds every rendered frame and before mouse events.

Release based on 2.10.13, without the changes introduced in 2.10.14. Author: Xlebka. The known container-close issue immediately after entering a world remains unresolved.

XIT is Universal (ID xit), with side-gated client and server systems. Wire protocol v1 channels and explicit protocol fields gate live container search and crafting-result selection. Without compatible server support, search uses labelled remembered contents and result selection stays disabled; the browser and inventory tools remain usable.

Migration: remove the separate server addon (xrbserver) and older xrb archives. Install the single current XIT archive locally, and optionally on the dedicated server. Keep old versions outside Mods.

Cementation has an andesite coffin tab and a dedicated layout: 16 iron, 40 charcoal/coke inside, 160 coal-based heating fuel (80+80 outside), and 16 blister-steel ingots. Duration is 160 in-game hours, converted to days using the world calendar. Carbon and heating fuels cycle separately. Ordinary steel remains the separate native anvil refinement. Automatic crucible routes no longer include steel/blister steel or vanilla ingots excluded from the handbook, such as platinum. Alternative inputs are indexed for U lookup.

Smithing input amounts derive from resolved recipe voxel counts and native IAnvilWorkable.VoxelCountForHandbook, respecting explicit minimum quantities. Chain correctly needs two ingots. The separate quantity caption reads the full ingredient rather than the single-icon presentation. Multi-recipe U pages now use the dedicated ingot/anvil/animated-hammer/result layout, matching R.

Crafting ingredient display fills cloned liquid containers from native requiresContent/requiresLitres recipe attributes, including 1 L water in dough buckets, bowls and jugs. Alternative lists and output-dependent grid examples use the same preparation. Ingredient cache keys now distinguish liquid requirements. No real inventory stacks or registered recipes are modified.

Multi-recipe melting pages use the thermal crucible layout, including temperature-compatible fuel. Tool molds no longer generate separate 200/900-unit bulk melting frames; their actual metal costs remain on casting recipes. Reducible multi-ingot batches display one ingot with proportionally scaled components and unchanged vessels. Indivisible custom batches remain intact rather than inventing fractional items.

Ingot recipe lookup also finds crucible melting routes producing that same metal. Alloy component mixtures are the initial melting frame, before remelting finished ingots; casting remains separate. Native alloy ratio examples use complete 100-unit ingot batches.

Snowball thawing, including meteorite/snow/bee variants, shares a paired-result page and fuel-use card. Cooking displays native ingredient slot counts and liquid portion volumes (chrome tanning: three 1 L sulfate slots plus scraped hide); wax processing remains a separate leather stage. Matching multi-recipe page layouts reuse composers, grids and textures; enriched ingredient alternatives persist when revisiting cards. Different layouts still rebuild safely.

Thermal dividers are wider and taller; the flame-tab icon is shifted right. Rendered-fat cooking displays one page with native-validated single-slot batches of 1–6, keeping output quantities paired. Stone-filled snowballs share one material-switching page; ordinary snowballs and bee bombs remain separate.

Thermal cards have thin dividers. U on combustible fuels adds a flame tab showing compatible firepit, cooking-pot and crucible uses, filtered by burning temperature. The searched fuel is pinned in its fuel slot. Sticks are offered for processes within their native 700°C limit. Cooking-pot fuel examples include native-validated meal recipes with filled-pot outputs, not only fixed CooksInto items. The shared thermal catalog is cached per world.

Cooking pot and firepit recipes use their dedicated ingredient/vessel/fuel layouts even on multi-recipe pages. Thermal cards are arranged side-by-side (one to three depending on screen width) on one shared background. Cooking cards show four ingredient slots above the vessel; firepit cards show a single input, arrow, result and fuel below. Fuel temperature captions have more room.

Remembered container search is now automatic: use the server when available; otherwise record opened containers and show explicitly labelled last-known results. The manual memory setting was removed. After a server search timeout, use memory for 30 seconds before retrying server search on the next request. Saved inventory positions apply on opening rather than waiting for a client tick; Reset window positions is the last button in Containers.

Disabled favorite recipe previews now restore standard item descriptions. The favorite-panel footer background remains without its old Dim button. "Prevent accidental drops" blocks mouse-held stack drops while the cursor is inside an open player/container inventory window, including slot gaps. Ordinary slot transfers, explicit drops outside inventory windows and the normal drop key are unchanged.

Inventory positions persist in xrb-window-positions.json. With "Remember inventory position" enabled, container arrangement treats the inventory as a fixed anchor, with screen-bounded placement of container windows. When too many windows are open to fit on screen, overlap may be unavoidable; the inventory is never shifted by the layout. Settings include a window-position reset. Hopper/container inventory dialogs are supported. Sorting icons synchronize with their owner every rendered frame. Background opacity redraws both XRB panels (the old bottom-left Dim control was removed). Settings labels no longer assume the default search key; the Recipes/Crafting settings section was removed.

Block auto-refill: after placing the last block in the active hotbar slot, move a matching stack from the personal backpack through the normal inventory transfer packet. No refill on dropping items or manually clearing slots; no chest sourcing or tool replacement. Block identity and material attributes must match. Active-slot changes cancel pending refills. Auto-refill is client-side and configurable.

The gear menu now has sections for item filters, recipes/crafting, inventory, containers, search/highlights and interface. Individual switches control auto-refill, sorting, R/U hotkeys, + autofill, Shift maximum fill, result selection, T search, container memory, world outlines, slot highlighting, closing inventory on search and favorite previews. Existing preferences are retained.

Container search slot highlighting is drawn above 3D icons without depth occlusion and clipped to each slot. Native item model clipping is preserved; GUI depth/scissor state is restored after rendering.

T closes the player inventory and recipe window after starting a search. Multiblock storage outlines include their linked parts. Matching slots in open container windows receive a light-turquoise tint and border based on live contents; the last search target remains active until another search or world change.

Brick materials and refractory tiers, and shingle materials are grouped as paired recipe frames. Raw and fired stages remain separate; ingredient amounts, tier compositions and firing temperatures stay attached to their actual results.

Without compatible XRB Server support, T falls back to client memory of containers you opened. Results are labelled "last known contents" and may be outdated. Reopening a container refreshes its snapshot, including empty containers. Up to 512 snapshots persist separately for each world/player. Server-backed search remains authoritative when available; a timeout also falls back to explicitly labelled memory.

Search results use cached light-turquoise edge meshes (no face fill), following container selection bounds. The outline is uploaded once per search and blinks for eight seconds.

Nearby item search: hover an item and press T (rebindable in Controls). Accessible containers within 16 blocks blink for 8 seconds. XRB Server checks current contents, land claims and vanilla reinforcement locks. Without compatible server support, client memory is used and labelled as last-known contents. No automatic container opening or chunk loading is used. Item material attributes are preserved; damage and temperature are ignored. Search is bounded to 128 hits, 4096 block entities and 8192 inventory slots. Radius is configurable (4–24) in XRB Settings or through ItemSearchRadius in vsrecipebrowser.json. Custom protection systems may require a dedicated integration.

Search fields receive mouse-release events outside the browser panels, preventing stuck text dragging and a caret that follows pointer position. Crafting ingredient roles no longer exclude registered material alternatives solely because their code prefix or item/block class differs; selecting one still requires a matching source recipe and switches the corresponding complete route. This includes vine/reed rope recipes, without inventing unregistered bamboo recipes.

Alternative arrows render in a final overlay pass after every visible recipe card's item models, with depth testing disabled for that pass. This prevents block geometry and later-rendered neighbouring cells from obscuring the badges. Badge placement and interaction remain unchanged.

Fixed crafting-result selection crashes on malformed game-generated bucket contents JSON using a typed binary fallback which preserves contents and amounts. Ingredient wheel selection uses stable full display identities including quantity and the manually pinned choice, with circular forward/backward navigation and deterministic option ordering.

Nested item-stack attributes use typed identity even when their JSON parses, because the game's JSON representation omits contained stack quantities. Cassava and sunflower grain variants now join the other flour/dough variants. The test runner reports failures to the console instead of leaving unhandled-exception Windows dialogs.

Shift autofill now considers remaining durability for breakable reusable ingredients, including the final craft which breaks the tool. Tools with different remaining durability are not pooled during planning; selected moves must retain the planned durability suitability. Recipes without durability loss or without break-on-zero behavior are not capped by this rule. Native full-grid validation and original game returned-item behavior remain unchanged.

Tagged reusable-tool lookup no longer relies on a fixed list of vanilla tool-name prefixes. Candidate buckets are derived from actual registered collectibles accepted by the ingredient. U expands matching routes through a catalog-wide index, including registered variants outside the queried tool's code bucket. Tool slot roles use their tag conditions. Fixed-code, material, attribute and wear restrictions remain native.

U retains registered variants of the same crafting route with other tagged reusable tools. Exact tool ingredients are not broadened: alternatives come from actual game recipes with matching output, layout, material inputs, tag conditions, consumption and durability restrictions. This addresses U filtering out all but the queried copper saw in expanded recipes. Ingredient diagnostics remain enabled for verification.

First ingredient-library stage: lookup/display and crafting transfer planning share IngredientRules. Shift fill uses current consume properties instead of relying on the legacy IsTool flag. Native whole-grid validation remains mandatory. Saw ingredient diagnostics log loaded conditions, native/shared candidate matching and final alternatives under [XRB Ingredient Audit], once per cached slot with bounded representative rules. This is a diagnostic build; it does not claim the reported saw issue has been resolved in a live client.

Ingredient alternatives use two rounded clockwise arrows, drawn at the current GUI scale. Hover them for Choose ingredient; click to select a replacement. Tagged tool matching derives the condition from the original code and tags rather than an Exact handbook example. Ingredient cache keys retain complete tag conditions rather than only their hash.

Recipe cards now render transparently over one continuous page background, removing seams between rows without changing slot or button positions.

Removed the unrequested Details button from recipe cards. Secondary-result cells are composed only when a recipe actually has a secondary output, including grouped frames.

Recipe lists display up to three compact recipe cards per page. The mouse wheel over the background/result changes pages; over an ingredient it retains per-cell selection. Favorites retain the selected card's recipe context. Fixed-result recipes with identical layouts combine material/tool permutations into slot alternatives, without separate display frames; original game recipe references remain for validating correlated ingredients and autofill. Different outputs, quantities, tool costs and process conditions remain distinct where required.

Chisel-and-mortar brick thickening groups matching brick materials while keeping courses, slabs and stairs distinct. Chisel metal recycling groups metal inputs and their matching bits into one page, retaining source quantities, tool restrictions and complete ingredient/result pairs. R remains fixed to the requested product.

Favorites added with F while hovering an ingredient/result in a recipe save the current station, recipe identity, complete frame and displayed ingredients, fuel and anvil. Left-click or R on that favorite restores its saved recipe; right-click/U still searches uses. Hover previews use the saved recipe too. Existing item-only favorites remain compatible; remove and re-add them from the desired recipe to attach a bookmark. Removed or unavailable recipes fall back to ordinary recipe browsing.

Tool alternatives also recognize the game's client-side non-consuming durability metadata, since IsTool is not serialized. Tagged ingredients expose accepted alternatives even when the legacy tool flag is absent. Wooden crate materials, anvil assembly materials and anvil-to-ingot recycling are grouped while retaining complete source recipes and fixed R results.

R keeps the requested result fixed instead of including unrelated helmets or other output-family members. Ingredient options are collected across compatible recipe frames, checked against their original ingredients, and selecting an option switches to an accepting frame. Processing input cells now use the same alternatives picker as crafting cells.

Removed the "Variant X / Y" label beneath recipe results to avoid confusing ingredient alternatives with separate recipes. Automatic cycling, Shift pause, ingredient options and the actual page counter are unchanged.

Recipe pages are now grouped by station/process and output family, rather than every layout, quantity or ingredient permutation. Complete variants retain their original ingredients, result, amount, temperature, mold and byproduct; switching variants updates the station display together. Casting pages group molds/products by the actual molten metal. Melting retains every discovered feedstock while keeping the preferred display source.

Directional equivalents remain searchable, while R does not broaden the requested product into other materials, colours or equipment styles. Reorientation/self-transform recipes remain separate from manufacture, and autofill stays within the currently selected result. Wildcard display examples are validated rather than treated as concrete ingredients or evidence of self-crafting.

The offline grouping audit examines all installed survival recipe JSON files, retains every source crafting definition, and separately lists cooking definitions without a direct fixed output. This is not a claim that every mod recipe or every in-game UI has been visually tested. Runtime grouping counts are logged as [XRB Recipe Grouping].

Fixed sorting/favorite identities for items containing empty nested inventory slots: invalid game-generated JSON now falls back to a stable typed binary representation. Recipe variants compare attribute contents instead of object hash codes, and shapeless routes normalize ingredient ordering. Exact processing duplicate checks preserve temperatures, methods and secondary outputs. Existing valid favorite identities are retained.

Recipe ingredient variants cycle automatically, including while the mouse is over the recipe. Hold either Shift key to pause ingredient and fuel cycling. Independent unnamed wildcard cells use randomized cycle offsets; named materials and output-dependent variants remain constrained by the original recipes. A manual ingredient choice stays pinned until you change the page or cycle the whole recipe.

Crafting layout grouping now uses actual cell positions, normalized minimum quantities, and durability costs only for tools, avoiding duplicate routes caused by irrelevant ingredient defaults.

Click the small alternatives badge in the top-right corner of a recipe cell to inspect and select accepted ingredients. Crafting cells also support individual mouse-wheel selection, with source-recipe constraints preserved. The picker closes with Escape or E, or when the recipe window closes. Fuel, smithing tools/anvils and pressing ingredients also expose an alternatives picker.

Autofill now allocates compatible ingredient groups with bounded backtracking and prioritizes constrained cells. It can mix accepted materials across cells, but never combines different item types into one stack. The final arrangement is checked against the game's recipe before transfers begin.

Crafting alternatives are grouped by fixed result and compatible layout instead of enumerating every material/tool combination. Output-dependent materials retain their own result. Different layouts, quantities, trait requirements and tool costs remain separate routes.

Original game recipes remain the authority: named material constraints are retained, and autofill checks the assembled grid against the selected source recipe before moving items. The red/blue fireclay mixing recipe is retained alongside its single-colour alternatives.

NEI-style inventory side panel for Vintage Story 1.22.

Linked Inventories and Recipe Choice are now included. Remove the separate Linked Inventories and XRB Recipe Choice mods before installing XRB 2.5.0. The one-stick conflict test mod is not included and should be removed after testing.

The browser, sorting and autofill work with XRB installed only on the client.
To enable conflicting-recipe selection in multiplayer, install XRB Server on the server.
In singleplayer install both the client XRB and XRB Server archives locally.
XRB does not require every player to install it and can still join servers without it; only recipe selection is unavailable there.

The gear menu provides independent switches for automatically opening the player inventory and centering container windows. Hiding the recipe browser with Ctrl+O does not disable these features.

## Current features

- Integrated conflicting-recipe selector: a two-arrow button beside the inventory crafting result opens matching outputs. The server validates the selection and vanilla crafting consumes ingredients. No separate Recipe Choice archive is needed.

- Russian and English interface text follows the game language (English is the fallback for other languages).
- Backspace returns to the previous recipe view (without interfering with search text editing).
- Autofill considers material variants on the current grouped recipe page.
- Click the sort icon beside the title-bar menu, or press the middle mouse button over the player inventory or an open storage container. Only the selected inventory is sorted; items never move between containers. Both sorting and Backspace recipe history can be rebound in Controls. Hotbar, equipment, equipped bags and the crafting grid are excluded. Put down any cursor-held item first.
- Inventory sorting can be disabled in the gear menu (hides its title-bar buttons and disables the shortcut). Sort order: full blocks, slabs/tiles, stairs/roofs, fences/doors, furniture/decor, stations/mechanisms, materials, equipment, food/plants, other items. Families and materials stay together, with larger matching stacks first.

- Opens automatically to the left of the normal player inventory.
- Shows every registered item/block and its creative variants.
- Search field is placed under the item grid and supports Ctrl+A.
- Supports JEI-style `@modid` and `@mod name` filtering by the source mod.
- Includes persistent item-category filters and optional searching through hidden categories.
- Shows the source mod in item tooltips.
- Recipe autofill uses items from the player inventory and all currently open containers, including multiple chests. Transfers use vanilla client/server inventory packets; XRB is not required on the server.
- Hover an item and press **F** to add/remove it from the favorites panel on the left.
- Hover a favorite to see a compact recipe preview instead of its item description.
- Hover an item and press **R** for its recipe or **U** for its uses; both bindings are configurable in Controls.
- R/U open an in-inventory recipe overlay with inputs, output, method, and recipe paging.
- Provides adjustable backgrounds for the favorites and item panels.
- Can be hidden with the on-screen button or the configurable Ctrl+O hotkey.
- Clicking an entry opens its existing vanilla handbook page and recipes.
- Includes items registered by other mods when they provide handbook pages.
- Starts indexing immediately after entering a world, prioritizes favorites, and caches lightweight variant data.
- Reuses the index until the game version, language, or enabled mod list changes.
- Creates full item stacks only for the visible page and favorites.

## Build

The project defaults to the local Windows install at
`C:\Users\Xlebka\AppData\Roaming\Vintagestory`. Override it when needed:

```powershell
dotnet build -c Release -p:VintageStoryPath="D:\Games\Vintagestory"
```

Package the output DLL and `modinfo.json` at the root of a zip, then put the zip
in the Vintage Story data `Mods` folder.

Container layout refreshes when a firepit expands for a pot/crucible. Multiple open containers are supported.
