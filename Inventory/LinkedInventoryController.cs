using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VsRecipeBrowser;

internal sealed class LinkedInventoryController : IDisposable
{
    private const string PlayerInventoryTypeName = "Vintagestory.Client.NoObf.GuiDialogInventory";
    private const double Gap = 6;
    private const double CreativeHotbarClearance = 105;

    private ICoreClientAPI? capi;
    private readonly List<GuiDialog> activeContainers = new();
    private GuiDialogBlockEntity? constructedContainer;
    private GuiDialog? playerInventory;
    private bool inventoryOpenedByMod;
    private bool closeRequestedByMod;
    private bool sessionPositioned;
    private bool worldInteractionOverrideActive;
    private bool previousMouseWorldInteractAnyway;
    private long tickId;
    private RecipeBrowserConfig settings = new();
    private InventoryWindowPositions positions = new();
    private GuiComposer? trackedInventoryComposer;
    private (double X, double Y)? lastInventoryPosition;
    private readonly Dictionary<GuiDialog, (GuiComposer? Composer, double Width, double Height)> layoutSizes = new();
    private (int Width, int Height, float Scale) viewport;
    private readonly Dictionary<GuiComposer, SavedDialogState> savedDialogStates = new();
    private readonly HashSet<GuiComposer> loggedComposers = new();

    internal static LinkedInventoryController? Instance { get; private set; }

    public void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        Instance = this;
        ReloadSettings();
        positions = api.LoadModConfig<InventoryWindowPositions>("xrb-window-positions.json") ?? new();
        tickId = api.Event.RegisterGameTickListener(OnClientTick, 50);
        api.Event.LeaveWorld += ResetState;
        api.Logger.Notification("[Combined Container Inventory] Client integration enabled.");
    }

    internal void ReloadSettings()
    {
        if (capi is null) return;
        var next = capi.LoadModConfig<RecipeBrowserConfig>("vsrecipebrowser.json") ?? new();
        if (settings.CenterContainerWindows && !next.CenterContainerWindows) RestoreDialogStates();
        settings = next;
        sessionPositioned = false;
    }

    internal void InvalidateLayout() => sessionPositioned = false;

    private void OnClientTick(float deltaTime)
    {
        if (capi?.World?.Player is null) return;
        TrackInventoryPosition();
        if (settings.CenterContainerWindows)
        {
            var currentViewport = (capi.Render.FrameWidth, capi.Render.FrameHeight, RuntimeEnv.GUIScale);
            if (viewport != currentViewport) { viewport = currentViewport; sessionPositioned = false; }
            foreach (var dialog in activeContainers)
            {
                var composer = dialog.SingleComposer;
                var size = (composer, composer?.Bounds.OuterWidth ?? 0, composer?.Bounds.OuterHeight ?? 0);
                if (!layoutSizes.TryGetValue(dialog, out var old) || old != size)
                { layoutSizes[dialog] = size; sessionPositioned = false; }
            }
            foreach (var old in layoutSizes.Keys.Where(d => !activeContainers.Contains(d)).ToArray()) layoutSizes.Remove(old);
        }

        List<GuiDialog> containers = capi.Gui.OpenedGuis
            .OfType<GuiDialog>()
            .Where(IsSupportedContainer)
            .Distinct()
            .ToList();

        if (constructedContainer?.IsOpened() == true
            && IsSupportedContainer(constructedContainer)
            && !containers.Contains(constructedContainer))
        {
            containers.Add(constructedContainer);
        }

        // Some UI mods temporarily remove HUD dialogs from OpenedGuis while keeping
        // the underlying vanilla dialog open. Keep the current session alive until
        // the container dialog itself reports that it has actually closed.
        foreach (GuiDialog active in activeContainers)
        {
            if (active.IsOpened() && IsSupportedContainer(active) && !containers.Contains(active))
            {
                containers.Add(active);
            }
        }

        bool containersChanged = containers.Count != activeContainers.Count
            || containers.Any(container => !activeContainers.Contains(container));

        if (containersChanged)
        {
            if (containers.Count == 0)
            {
                CloseOwnedInventory();
                activeContainers.Clear();
                playerInventory = FindPlayerInventory();
                if (IsPlayerInventoryOpen()) EnableWorldInteractionMode();
                else RestoreWorldInteractionMode();
                return;
            }

            activeContainers.Clear();
            activeContainers.AddRange(containers);
            sessionPositioned = false;
            BeginCombinedSession();
        }

        playerInventory ??= FindPlayerInventory();
        if (containers.Count > 0 || IsPlayerInventoryOpen()) EnableWorldInteractionMode();
        else RestoreWorldInteractionMode();

        if (settings.CenterContainerWindows && !sessionPositioned && activeContainers.Count > 0)
        {
            playerInventory ??= FindPlayerInventory();
            if (playerInventory is not null && playerInventory.IsOpened())
            {
                sessionPositioned = PositionContainerGrid(activeContainers, playerInventory);
            }
        }
    }

    internal void OnInventoryOpened(GuiDialog dialog)
    {
        if (dialog.GetType().FullName == PlayerInventoryTypeName && dialog.IsOpened()) TrackInventoryPosition(dialog);
    }

    private void TrackInventoryPosition(GuiDialog? openedDialog = null)
    {
        if (capi is null || !settings.RememberInventoryPosition) return;
        var dialog = openedDialog ?? FindPlayerInventory();
        if (dialog?.IsOpened() != true) { trackedInventoryComposer = null; lastInventoryPosition = null; return; }
        var composer = GetPlayerInventoryComposer(dialog, out _);
        if (composer is null) return;
        var bounds = composer.Bounds;
        double scale = Math.Max(0.01, RuntimeEnv.GUIScale);
        bounds.CalcWorldBounds();
        if (!ReferenceEquals(trackedInventoryComposer, composer))
        {
            trackedInventoryComposer = composer;
            dialog.OnClosed -= OnPlayerInventoryClosed;
            dialog.OnClosed += OnPlayerInventoryClosed;
            sessionPositioned = false;
            EnsureManualPositioning(composer);
            if (positions.Windows.TryGetValue(composer.DialogName, out var saved))
                SetManualPosition(composer, bounds, Math.Clamp(saved.X * scale, 0, Math.Max(0, capi.Render.FrameWidth - bounds.OuterWidth)),
                    Math.Clamp(saved.Y * scale, 0, Math.Max(0, capi.Render.FrameHeight - bounds.OuterHeight)));
            lastInventoryPosition = null;
        }
        var current = (bounds.renderX / scale, bounds.renderY / scale);
        if (lastInventoryPosition is { } previous && (Math.Abs(current.Item1 - previous.X) > 0.5 || Math.Abs(current.Item2 - previous.Y) > 0.5))
            sessionPositioned = false;
        lastInventoryPosition = current;
        if (capi.Input.MouseButton.Left) return;
        if (!positions.Windows.TryGetValue(composer.DialogName, out var old)
            || Math.Abs(old.X - current.Item1) > 0.5 || Math.Abs(old.Y - current.Item2) > 0.5)
        {
            positions.Windows[composer.DialogName] = new InventoryWindowPosition { X = current.Item1, Y = current.Item2 };
            capi.StoreModConfig(positions, "xrb-window-positions.json");
        }
    }

    private bool PositionAroundInventory(IReadOnlyList<GuiDialog> containers, GuiComposer inventory)
    {
        if (capi is null || capi.Input.MouseButton.Left) return false;
        inventory.Bounds.CalcWorldBounds();
        var anchor = new WindowRect(inventory.Bounds.renderX, inventory.Bounds.renderY, inventory.Bounds.OuterWidth, inventory.Bounds.OuterHeight);
        var occupied = new List<WindowRect> { anchor };
        foreach (var container in containers)
        {
            var composer = container.SingleComposer;
            if (composer is null) continue;
            EnsureManualPositioning(composer); composer.Bounds.CalcWorldBounds();
            var placement = AnchoredWindowLayout.Place(anchor, occupied, composer.Bounds.OuterWidth, composer.Bounds.OuterHeight,
                capi.Render.FrameWidth, capi.Render.FrameHeight, Gap * RuntimeEnv.GUIScale, settings.ContainerSide);
            SetManualPosition(composer, composer.Bounds, placement.X, placement.Y);
            occupied.Add(placement);
        }
        return true;
    }

    internal void ResetWindowPositions()
    {
        if (capi is null) return;
        positions.Windows.Clear(); capi.StoreModConfig(positions, "xrb-window-positions.json");
        var dialog = FindPlayerInventory();
        var composer = dialog is null ? null : GetPlayerInventoryComposer(dialog, out _);
        if (composer is not null)
            SetManualPosition(composer, composer.Bounds, Math.Max(0, (capi.Render.FrameWidth - composer.Bounds.OuterWidth) / 2),
                Math.Max(0, (capi.Render.FrameHeight - composer.Bounds.OuterHeight) / 2));
        sessionPositioned = false; trackedInventoryComposer = null; lastInventoryPosition = null;
    }

    private void EnableWorldInteractionMode()
    {
        if (capi is null) return;
        if (worldInteractionOverrideActive)
        {
            // Vanilla may reset this transient input flag while GUI state is
            // updated. Reassert it on every client tick of the container session.
            capi.Input.MouseWorldInteractAnyway = true;
            return;
        }

        previousMouseWorldInteractAnyway = capi.Input.MouseWorldInteractAnyway;
        capi.Input.MouseWorldInteractAnyway = true;
        worldInteractionOverrideActive = true;
    }

    private void RestoreWorldInteractionMode()
    {
        if (capi is null || !worldInteractionOverrideActive) return;
        capi.Input.MouseWorldInteractAnyway = previousMouseWorldInteractAnyway;
        worldInteractionOverrideActive = false;
    }

    private void BeginCombinedSession()
    {
        playerInventory = FindPlayerInventory();
        if (playerInventory is null)
        {
            capi?.Logger.Warning("[Combined Container Inventory] Vanilla player inventory dialog was not found.");
            return;
        }

        // Subscribe even when the player opened the inventory manually before
        // the container. Otherwise closing that already-open inventory never
        // ends the combined session and leaves every container dialog open.
        playerInventory.OnClosed -= OnPlayerInventoryClosed;
        playerInventory.OnClosed += OnPlayerInventoryClosed;

        if (settings.AutoOpenInventory && !playerInventory.IsOpened())
        {
            inventoryOpenedByMod = playerInventory.TryOpen();
        }
        if (settings.CenterContainerWindows && playerInventory.IsOpened() && activeContainers.Count > 0)
        {
            sessionPositioned = PositionContainerGrid(activeContainers, playerInventory);
        }
    }

    private GuiDialog? FindPlayerInventory()
    {
        if (capi is null) return null;

        return capi.Gui.LoadedGuis.FirstOrDefault(dialog =>
            string.Equals(dialog.GetType().FullName, PlayerInventoryTypeName, StringComparison.Ordinal));
    }

    private bool IsPlayerInventoryOpen()
    {
        if (capi is null) return false;
        return capi.Gui.OpenedGuis.OfType<GuiDialog>().Any(dialog =>
            dialog.IsOpened()
            && string.Equals(dialog.GetType().FullName, PlayerInventoryTypeName, StringComparison.Ordinal));
    }

    private bool IsSupportedContainer(GuiDialog dialog)
    {
        if (capi is null) return false;

        string dialogType = dialog.GetType().FullName ?? string.Empty;
        if (dialogType is "Vintagestory.GameContent.GuiDialogBlockEntityFirepit"
            or "Vintagestory.GameContent.GuiDialogBarrel"
            or "Vintagestory.GameContent.GuiDialogBlockEntityQuern"
            or "Vintagestory.GameContent.GuiDialogCreatureContents")
        {
            return true;
        }

        if (dialog is not GuiDialogBlockEntity blockEntityDialog) return false;

        string inventoryClass = (dialog as GuiDialogBlockEntityInventory)?.Inventory?.ClassName ?? string.Empty;
        string path = capi.World.BlockAccessor.GetBlock(blockEntityDialog.BlockEntityPosition)?.Code?.Path ?? string.Empty;
        if ((dialog as GuiDialogBlockEntityInventory)?.Inventory is not null
            || capi.World.BlockAccessor.GetBlockEntity(blockEntityDialog.BlockEntityPosition) is IBlockEntityContainer) return true;
        return inventoryClass.Contains("chest", StringComparison.OrdinalIgnoreCase)
            || inventoryClass.Contains("storagevessel", StringComparison.OrdinalIgnoreCase)
            || inventoryClass.Contains("basket", StringComparison.OrdinalIgnoreCase)
            || path.Contains("chest", StringComparison.OrdinalIgnoreCase)
            || path.Contains("storagevessel", StringComparison.OrdinalIgnoreCase)
            || path.Contains("stationarybasket", StringComparison.OrdinalIgnoreCase);
    }

    internal void OnContainerConstructed(GuiDialogBlockEntityInventory dialog)
    {
        if (capi is null || !settings.CenterContainerWindows || !IsSupportedContainer(dialog)) return;

        constructedContainer = dialog;
        GuiComposer? composer = dialog.SingleComposer;
        if (composer is null) return;

        EnsureManualPositioning(composer);

        ElementBounds bounds = composer.Bounds;
        bounds.CalcWorldBounds();
        double scale = Math.Max(0.01, RuntimeEnv.GUIScale);
        SetManualPosition(
            composer,
            bounds,
            (capi.Render.FrameWidth - bounds.OuterWidth) / 2,
            Math.Max(0, (capi.Render.FrameHeight - bounds.OuterHeight) / 4));

        capi.Logger.Notification(
            "[Combined Container Inventory] Captured container constructor: type={0}, inventory={1}, composer={2}",
            dialog.GetType().FullName,
            dialog.Inventory?.ClassName,
            composer.DialogName);
    }

    internal void OnContainerOpened(GuiDialogBlockEntity dialog, bool opened)
    {
        if (!opened || capi is null || !IsSupportedContainer(dialog)) return;

        constructedContainer = dialog;
        if (!activeContainers.Contains(dialog))
        {
            activeContainers.Add(dialog);
            sessionPositioned = false;
            BeginCombinedSession();
        }
    }

    private bool PositionContainerGrid(IReadOnlyList<GuiDialog> containers, GuiDialog inventory)
    {
        if (capi is null || containers.Count == 0) return false;

        GuiComposer? inventoryComposer = GetPlayerInventoryComposer(inventory, out bool creativeInventory);
        if (inventoryComposer is null) return false;
        if (settings.RememberInventoryPosition || settings.ContainerSide != ContainerPlacementSide.Automatic)
            return PositionAroundInventory(containers, inventoryComposer);

        List<(GuiComposer Composer, ElementBounds Bounds)> containerLayouts = new();
        foreach (GuiDialog container in containers)
        {
            GuiComposer? composer = container.SingleComposer;
            if (composer is null) continue;

            EnsureManualPositioning(composer);
            composer.Bounds.CalcWorldBounds();
            containerLayouts.Add((composer, composer.Bounds));
        }

        if (containerLayouts.Count == 0) return false;

        EnsureManualPositioning(inventoryComposer);

        ElementBounds lower = inventoryComposer.Bounds;
        lower.CalcWorldBounds();

        double scale = Math.Max(0.01, RuntimeEnv.GUIScale);
        double gapPixels = Gap * scale;
        double edgePadding = 8 * scale;
        double availableWidth = Math.Max(1, capi.Render.FrameWidth - 2 * edgePadding);

        List<List<(GuiComposer Composer, ElementBounds Bounds)>> rows = new();
        List<(GuiComposer Composer, ElementBounds Bounds)> currentRow = new();
        double currentRowWidth = 0;
        foreach ((GuiComposer composer, ElementBounds bounds) in containerLayouts)
        {
            double requiredWidth = bounds.OuterWidth + (currentRow.Count == 0 ? 0 : gapPixels);
            if (currentRow.Count > 0 && currentRowWidth + requiredWidth > availableWidth)
            {
                rows.Add(currentRow);
                currentRow = new();
                currentRowWidth = 0;
                requiredWidth = bounds.OuterWidth;
            }

            currentRow.Add((composer, bounds));
            currentRowWidth += requiredWidth;
        }

        if (currentRow.Count > 0) rows.Add(currentRow);

        double containersHeight = rows.Sum(row => row.Max(layout => layout.Bounds.OuterHeight))
            + gapPixels * Math.Max(0, rows.Count - 1);
        double totalHeight = containersHeight + gapPixels + lower.OuterHeight;
        double top = (capi.Render.FrameHeight - totalHeight) / 2;
        double lowerTop = top + containersHeight + gapPixels;

        if (creativeInventory)
        {
            double hotbarClearance = CreativeHotbarClearance * scale;
            lowerTop = capi.Render.FrameHeight - lower.OuterHeight - hotbarClearance;
            top = lowerTop - gapPixels - containersHeight;

            if (top < 0)
            {
                top = 0;
                lowerTop = containersHeight + gapPixels;
            }
        }

        double rowTop = top;
        foreach (List<(GuiComposer Composer, ElementBounds Bounds)> row in rows)
        {
            double rowHeight = row.Max(layout => layout.Bounds.OuterHeight);
            double[] rawX = new double[row.Count];
            rawX[0] = -row[0].Bounds.OuterWidth / 2;
            double rawLeft = rawX[0];
            double rawRight = rawX[0] + row[0].Bounds.OuterWidth;

            for (int i = 1; i < row.Count; i++)
            {
                double width = row[i].Bounds.OuterWidth;
                if (i % 2 == 1)
                {
                    rawX[i] = rawRight + gapPixels;
                    rawRight = rawX[i] + width;
                }
                else
                {
                    rawX[i] = rawLeft - gapPixels - width;
                    rawLeft = rawX[i];
                }
            }

            double rowShift = capi.Render.FrameWidth / 2.0 - (rawLeft + rawRight) / 2.0;

            for (int i = 0; i < row.Count; i++)
            {
                (GuiComposer composer, ElementBounds bounds) = row[i];
                double x = rowShift + rawX[i];
                SetManualPosition(composer, bounds, x, rowTop + (rowHeight - bounds.OuterHeight) / 2);
            }

            rowTop += rowHeight + gapPixels;
        }

        SetManualPosition(
            inventoryComposer,
            lower,
            (capi.Render.FrameWidth - lower.OuterWidth) / 2,
            lowerTop);

        return true;
    }

    internal GuiComposer? GetPlayerInventoryComposer(GuiDialog inventory, out bool creative)
    {
        creative = capi?.World.Player.WorldData.CurrentGameMode == EnumGameMode.Creative;
        Type type = inventory.GetType();

        GuiComposer? creativeComposer = AccessTools.Field(type, "creativeInvDialog")?.GetValue(inventory) as GuiComposer;
        GuiComposer? survivalComposer = AccessTools.Field(type, "survivalInvDialog")?.GetValue(inventory) as GuiComposer;

        if (creativeComposer is not null && (creative || survivalComposer is null))
        {
            creative = true;
            return creativeComposer;
        }

        if (survivalComposer is not null)
        {
            creative = false;
            return survivalComposer;
        }

        return inventory.SingleComposer;
    }

    private void EnsureManualPositioning(GuiComposer composer)
    {
        if (capi is null) return;

        GuiElementDialogTitleBar? titleBar = FindTitleBar(composer);
        if (!savedDialogStates.ContainsKey(composer))
        {
            Vec2i? oldPosition = capi.Gui.GetDialogPosition(composer.DialogName);
            savedDialogStates[composer] = new SavedDialogState(
                titleBar?.Movable == true,
                oldPosition is null ? null : new Vec2i(oldPosition.X, oldPosition.Y));
        }

        if (titleBar?.Movable == false)
        {
            SetTitleBarMode(titleBar, "manual");
        }

        if (titleBar is not null)
        {
            AccessTools.Field(typeof(GuiElementDialogTitleBar), "firstFrameRendered")?.SetValue(titleBar, true);
            AccessTools.Method(typeof(GuiElementDialogTitleBar), "SetSelectedIndex")?.Invoke(titleBar, new object[] { 1 });
        }

        if (loggedComposers.Add(composer))
        {
            capi.Logger.Notification(
                "[Combined Container Inventory] Manual composer: {0}, titlebar={1}, movable={2}",
                composer.DialogName,
                titleBar is not null,
                titleBar?.Movable == true);
        }
    }

    private void SetManualPosition(GuiComposer composer, ElementBounds bounds, double pixelX, double pixelY)
    {
        if (capi is null) return;

        double scale = Math.Max(0.01, RuntimeEnv.GUIScale);
        double x = pixelX / scale;
        double y = pixelY / scale;

        bounds.Alignment = EnumDialogArea.None;
        bounds.fixedX = x;
        bounds.fixedY = y;
        bounds.fixedOffsetX = 0;
        bounds.fixedOffsetY = 0;
        bounds.absMarginX = 0;
        bounds.absMarginY = 0;
        bounds.MarkDirtyRecursive();
        bounds.CalcWorldBounds();

        capi.Gui.SetDialogPosition(
            composer.DialogName,
            new Vec2i((int)Math.Round(x), (int)Math.Round(y)));
    }

    private static GuiElementDialogTitleBar? FindTitleBar(GuiComposer composer)
    {
        for (int i = 0; i <= composer.CurrentElementKey; i++)
        {
            if (composer.GetElement("element-" + i) is GuiElementDialogTitleBar titleBar)
            {
                return titleBar;
            }
        }

        return null;
    }

    private static void SetTitleBarMode(GuiElementDialogTitleBar titleBar, string mode)
    {
        MethodInfo? method = AccessTools.Method(typeof(GuiElementDialogTitleBar), "SetUpMovableState");
        method?.Invoke(titleBar, new object?[] { mode });
    }

    private void RestoreDialogStates()
    {
        if (capi is null) return;

        foreach ((GuiComposer composer, SavedDialogState state) in savedDialogStates)
        {
            if (settings.RememberInventoryPosition && ReferenceEquals(composer, trackedInventoryComposer)) continue;
            GuiElementDialogTitleBar? titleBar = FindTitleBar(composer);
            if (!state.WasMovable)
            {
                if (titleBar?.Movable == true) SetTitleBarMode(titleBar, "auto");
                capi.Gui.SetDialogPosition(composer.DialogName, null!);
                continue;
            }

            if (titleBar?.Movable == false) SetTitleBarMode(titleBar, "manual");
            if (state.Position is not null)
            {
                composer.Bounds.Alignment = EnumDialogArea.None;
                composer.Bounds.fixedOffsetX = 0;
                composer.Bounds.fixedOffsetY = 0;
                composer.Bounds.fixedX = state.Position.X;
                composer.Bounds.fixedY = state.Position.Y;
                composer.Bounds.MarkDirtyRecursive();
                composer.Bounds.CalcWorldBounds();
                capi.Gui.SetDialogPosition(composer.DialogName, state.Position);
            }
        }

        savedDialogStates.Clear();
        loggedComposers.Clear();
    }

    private void OnPlayerInventoryClosed()
    {
        if (settings.RememberInventoryPosition && capi is not null && trackedInventoryComposer is { } composer)
        {
            composer.Bounds.CalcWorldBounds();
            double scale = Math.Max(0.01, RuntimeEnv.GUIScale);
            positions.Windows[composer.DialogName] = new InventoryWindowPosition { X = composer.Bounds.renderX / scale, Y = composer.Bounds.renderY / scale };
            capi.StoreModConfig(positions, "xrb-window-positions.json");
        }
        if (!closeRequestedByMod)
        {
            inventoryOpenedByMod = false;

            foreach (GuiDialog container in activeContainers.ToArray())
            {
                if (container.IsOpened()) container.TryClose();
            }
        }
    }

    private void CloseOwnedInventory()
    {
        savedDialogStates.Clear();
        loggedComposers.Clear();
        sessionPositioned = false;

        if (playerInventory is not null && inventoryOpenedByMod && playerInventory.IsOpened())
        {
            closeRequestedByMod = true;
            inventoryOpenedByMod = false;
            playerInventory.TryClose();
            closeRequestedByMod = false;
        }
        else
        {
            inventoryOpenedByMod = false;
        }
    }

    private void ResetState()
    {
        if (playerInventory is not null) playerInventory.OnClosed -= OnPlayerInventoryClosed;
        RestoreWorldInteractionMode();
        layoutSizes.Clear();
        savedDialogStates.Clear();
        loggedComposers.Clear();
        activeContainers.Clear();
        constructedContainer = null;
        playerInventory = null;
        inventoryOpenedByMod = false;
        closeRequestedByMod = false;
        sessionPositioned = false;
    }

    public void Dispose()
    {
        if (capi is not null) capi.Event.UnregisterGameTickListener(tickId);
        Instance = null;
        if (capi is not null)
        {
            capi.Event.LeaveWorld -= ResetState;
        }

        RestoreWorldInteractionMode();

        if (playerInventory is not null)
        {
            playerInventory.OnClosed -= OnPlayerInventoryClosed;
        }

        ResetState();
    }

    private sealed record SavedDialogState(bool WasMovable, Vec2i? Position);
}

[HarmonyPatch(typeof(GuiDialog), nameof(GuiDialog.TryOpen), new Type[] { })]
internal static class InventoryRememberedOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(GuiDialog __instance, bool __result)
    {
        if (__result) LinkedInventoryController.Instance?.OnInventoryOpened(__instance);
    }
}

[HarmonyPatch(
    typeof(GuiDialogBlockEntityInventory),
    MethodType.Constructor,
    new Type[]
    {
        typeof(string),
        typeof(InventoryBase),
        typeof(BlockPos),
        typeof(int),
        typeof(int),
        typeof(ICoreClientAPI)
    })]
internal static class ContainerDialogConstructorPatch
{
    [HarmonyPostfix]
    private static void Postfix(GuiDialogBlockEntityInventory __instance)
    {
        LinkedInventoryController.Instance?.OnContainerConstructed(__instance);
    }
}

[HarmonyPatch]
internal static class ContainerDialogOpenPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        HashSet<MethodBase> methods = new();
        foreach (string typeName in new[]
        {
            "Vintagestory.API.Client.GuiDialogBlockEntityInventory"
        })
        {
            Type? type = AccessTools.TypeByName(typeName);
            MethodInfo? method = type is null ? null : AccessTools.Method(type, nameof(GuiDialog.TryOpen));
            if (method is not null) methods.Add(method);
        }

        return methods;
    }

    [HarmonyPostfix]
    private static void Postfix(GuiDialog __instance, bool __result)
    {
        if (__instance is GuiDialogBlockEntity blockEntityDialog)
        {
            LinkedInventoryController.Instance?.OnContainerOpened(blockEntityDialog, __result);
        }
    }
}
