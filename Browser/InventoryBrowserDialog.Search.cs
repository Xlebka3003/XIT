using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

internal sealed partial class InventoryBrowserDialog
{
    // Filter immutable snapshots off-thread; apply only the latest generation on the GUI thread.
    private void OnSearchChanged(string value)
    {
        searchText = value ?? "";
        if (searchCallbackId != 0) capi.Event.UnregisterCallback(searchCallbackId);
        searchCallbackId = capi.Event.RegisterCallback(_ =>
        {
            searchCallbackId = 0;
            page = 0;
            QueueBackgroundFilter();
        }, 180);
    }

    private void QueueBackgroundFilter()
    {
        int generation = ++searchGeneration;
        SearchQuery query = ParseSearchQuery(searchText);
        BrowserIndexEntry[] snapshot = browserIndex.Entries.ToArray();
        HashSet<BrowserItemGroup> enabledGroups = EnabledGroups();
        bool searchAllGroups = config.SearchHiddenItems && !query.IsEmpty;
        bool showWithoutRecipes = config.ShowWithoutRecipes || searchAllGroups;

        Task.Run(() => snapshot.Where(entry => (searchAllGroups || enabledGroups.Contains(entry.Group))
                && (showWithoutRecipes || entry.HasRecipe)
                && (query.IsEmpty || MatchesSearch(entry, query))).ToList())
            .ContinueWith(task => capi.Event.EnqueueMainThreadTask(() =>
            {
                if (disposed || generation != searchGeneration || task.IsFaulted || task.IsCanceled) return;
                filteredEntries = task.Result;
                page = Math.Clamp(page, 0, Math.Max(0, (filteredEntries.Count - 1) / pageSize));
                FillVisibleSlots();
                RefreshPageLabel();
            }, "vsrecipebrowser-apply-filter"));
    }

    private void ApplyFilter()
    {
        SearchQuery query = ParseSearchQuery(searchText);
        HashSet<BrowserItemGroup> enabledGroups = EnabledGroups();
        bool searchAllGroups = config.SearchHiddenItems && !query.IsEmpty;
        filteredEntries = browserIndex.Entries.Where(entry => (searchAllGroups || enabledGroups.Contains(entry.Group))
            && (config.ShowWithoutRecipes || searchAllGroups || entry.HasRecipe)
            && (query.IsEmpty || MatchesSearch(entry, query))).ToList();
        page = Math.Clamp(page, 0, Math.Max(0, (filteredEntries.Count - 1) / pageSize));
        FillVisibleSlots();
    }

    private void FillVisibleSlots()
    {
        int first = page * pageSize;
        for (int slot = 0; slot < pageSize; slot++)
        {
            inventory[slot].Itemstack = first + slot < filteredEntries.Count
                ? filteredEntries[first + slot].CreateStack(capi.World)
                : null;
            inventory[slot].MarkDirty();
        }
    }

    private bool PreviousPage()
    {
        int pageCount = Math.Max(1, (filteredEntries.Count + pageSize - 1) / pageSize);
        page = (page - 1 + pageCount) % pageCount;
        FillVisibleSlots();
        RefreshPageLabel();
        return true;
    }

    private bool NextPage()
    {
        int pageCount = Math.Max(1, (filteredEntries.Count + pageSize - 1) / pageSize);
        page = (page + 1) % pageCount;
        FillVisibleSlots();
        RefreshPageLabel();
        return true;
    }

    private string PageLabel()
    {
        int pageCount = Math.Max(1, (filteredEntries.Count + pageSize - 1) / pageSize);
        return $"{page + 1} / {pageCount}";
    }

    private void RefreshPageLabel() => SingleComposer?.GetDynamicText("browser-page")?.SetNewText(PageLabel(), true);

    // Queries separate item terms from @mod filters.
    private static SearchQuery ParseSearchQuery(string text)
    {
        string[] tokens = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] modTerms = tokens.Where(token => token.StartsWith('@') && token.Length > 1)
            .Select(token => token[1..].ToLowerInvariant()).ToArray();
        string[] itemTerms = tokens.Where(token => !token.StartsWith('@'))
            .Select(token => token.ToLowerInvariant()).ToArray();
        return new SearchQuery(itemTerms, modTerms);
    }

    private static bool MatchesSearch(BrowserIndexEntry entry, SearchQuery query)
    {
        return query.ItemTerms.All(term => entry.SearchIndex.Contains(term, StringComparison.Ordinal))
            && query.ModTerms.All(term => entry.ModId.Contains(term, StringComparison.Ordinal)
                || entry.ModName.Contains(term, StringComparison.Ordinal));
    }

    private HashSet<BrowserItemGroup> EnabledGroups() => Enum.GetValues<BrowserItemGroup>()
        .Where(IsGroupEnabled).ToHashSet();

    // Parsed terms are immutable for background filtering.
    private sealed record SearchQuery(string[] ItemTerms, string[] ModTerms)
    {
        public bool IsEmpty => ItemTerms.Length == 0 && ModTerms.Length == 0;
    }
}
