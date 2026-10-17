namespace VsRecipeBrowser;

internal sealed class RememberedContainer
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Dimension { get; set; }
    public string BlockCode { get; set; } = "";
    public HashSet<string> Items { get; set; } = new(StringComparer.Ordinal);
    public long SeenAt { get; set; }
    internal string Key => $"{Dimension}:{X}:{Y}:{Z}";
}
internal sealed class RememberedContainers
{
    public List<RememberedContainer> Entries { get; set; } = new();
    internal bool Remember(RememberedContainer snapshot)
    {
        var old = Entries.FirstOrDefault(entry => entry.Key == snapshot.Key);
        if (old is not null && old.BlockCode == snapshot.BlockCode && old.Items.SetEquals(snapshot.Items)) return false;
        Entries.RemoveAll(entry => entry.Key == snapshot.Key);
        Entries.Add(snapshot);
        Entries = Entries.OrderByDescending(entry => entry.SeenAt).Take(512).ToList();
        return true;
    }
    internal IEnumerable<RememberedContainer> Find(string identity, int x, int y, int z, int dimension, int radius) =>
        Entries.Where(entry => entry.Dimension == dimension && entry.Items.Contains(identity)
            && (long)(entry.X - x) * (entry.X - x) + (long)(entry.Y - y) * (entry.Y - y)
                + (long)(entry.Z - z) * (entry.Z - z) <= radius * radius)
            .OrderByDescending(entry => entry.SeenAt).Take(128);
}
