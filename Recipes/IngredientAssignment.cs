namespace VsRecipeBrowser;

/// <summary>Assign stack-compatible groups to recipe cells, without moving items.</summary>
internal static class IngredientAssignment
{
    public static int[]? Solve(int[] needed, int[][] candidates, int[] capacity, Func<int[], bool>? valid = null)
    {
        var left = (int[])capacity.Clone();
        var selected = Enumerable.Repeat(-1, needed.Length).ToArray();
        int visits = 0;
        bool Search(int depth)
        {
            if (++visits > 50000) return false;
            if (depth == needed.Length) return valid?.Invoke(selected) ?? true;
            int next = -1, fewest = int.MaxValue;
            for (int slot = 0; slot < needed.Length; slot++)
            {
                if (selected[slot] >= 0) continue;
                int count = candidates[slot].Count(group => left[group] >= needed[slot]);
                if (count == 0) return false;
                if (count < fewest) { next = slot; fewest = count; }
            }
            foreach (int group in candidates[next])
            {
                if (left[group] < needed[next]) continue;
                selected[next] = group; left[group] -= needed[next];
                if (Search(depth + 1)) return true;
                left[group] += needed[next]; selected[next] = -1;
            }
            return false;
        }
        return Search(0) ? selected : null;
    }
}
