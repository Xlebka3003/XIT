namespace VsRecipeBrowser;

internal enum ContainerPlacementSide { Automatic, Left, Right }

internal readonly record struct WindowRect(double X, double Y, double Width, double Height)
{
    internal double Right => X + Width;
    internal double Bottom => Y + Height;
    internal double Overlap(WindowRect other) => Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X))
        * Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
}
internal static class AnchoredWindowLayout
{
    internal static WindowRect Place(WindowRect anchor, IReadOnlyList<WindowRect> occupied, double width, double height,
        double screenWidth, double screenHeight, double gap, ContainerPlacementSide side = ContainerPlacementSide.Automatic)
    {
        var candidates = new List<WindowRect>();
        void Add(double x, double y) => candidates.Add(new WindowRect(Math.Clamp(x, 0, Math.Max(0, screenWidth - width)),
            Math.Clamp(y, 0, Math.Max(0, screenHeight - height)), width, height));
        foreach (var rectangle in occupied)
        {
            Add(rectangle.X + (rectangle.Width - width) / 2, rectangle.Y - height - gap);
            Add(rectangle.Right + gap, rectangle.Y);
            Add(rectangle.X - width - gap, rectangle.Y);
            Add(rectangle.X + (rectangle.Width - width) / 2, rectangle.Bottom + gap);
        }
        Add(0, 0); Add(screenWidth - width, 0); Add(0, screenHeight - height); Add(screenWidth - width, screenHeight - height);
        // Avoid overlap first; a preferred side must not force windows off-screen.
        return candidates.OrderBy(rectangle => occupied.Sum(rectangle.Overlap))
            .ThenBy(rectangle => side switch {
                ContainerPlacementSide.Left => Math.Max(0, rectangle.Right + gap - anchor.X),
                ContainerPlacementSide.Right => Math.Max(0, anchor.Right + gap - rectangle.X),
                _ => 0
            })
            .ThenBy(rectangle => Math.Pow(rectangle.X + width / 2 - anchor.X - anchor.Width / 2, 2)
                + Math.Pow(rectangle.Y + height / 2 - anchor.Y - anchor.Height / 2, 2)).First();
    }
}

internal sealed class InventoryWindowPositions
{
    public Dictionary<string, InventoryWindowPosition> Windows { get; set; } = new();
}
internal sealed class InventoryWindowPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}
