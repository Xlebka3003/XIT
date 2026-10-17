namespace VsRecipeBrowser;

internal static class IngredientCycle
{
    internal static int Next(IReadOnlyList<string> identities, string current, int direction)
    {
        if (identities.Count == 0) return -1;
        int index = -1;
        for (int i = 0; i < identities.Count; i++) if (identities[i] == current) { index = i; break; }
        if (index < 0) return direction > 0 ? 0 : identities.Count - 1;
        return (index + (direction > 0 ? 1 : -1) + identities.Count) % identities.Count;
    }
}
