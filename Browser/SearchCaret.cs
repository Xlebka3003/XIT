namespace VsRecipeBrowser;

internal static class SearchCaret
{
    internal static int AfterInsertion(int oldLength, int oldCaret, int? selectionStart, int newLength)
    {
        int start = selectionStart.HasValue ? Math.Min(oldCaret, selectionStart.Value) : oldCaret;
        int selected = selectionStart.HasValue ? Math.Abs(oldCaret - selectionStart.Value) : 0;
        int inserted = Math.Max(0, newLength - (oldLength - selected));
        return Math.Clamp(start + inserted, 0, newLength);
    }
}
