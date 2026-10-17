namespace VsRecipeBrowser;

internal static class RecipePaging
{
    internal static int Count(int recipes, int size) => Math.Max(1, (recipes + size - 1) / size);
    internal static int Start(int recipes, int current, int size, int direction)
    {
        int count = Count(recipes, size);
        return ((current / size + direction + count) % count) * size;
    }
}
