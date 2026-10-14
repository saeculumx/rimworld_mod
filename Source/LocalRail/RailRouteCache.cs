namespace LocalRail;

// Topology only: permissions and hazards are always checked again for each passenger.
public sealed class RailRouteCache
{
    private readonly Dictionary<(RailCell, RailCell), List<RailCell>> paths = new();
    private int retainedCells;
    public int Searches { get; private set; }
    public int Hits { get; private set; }
    public int Count => paths.Count;
    public int RetainedCells => retainedCells;
    public void Invalidate() { paths.Clear(); retainedCells = 0; }
    public IReadOnlyList<RailCell> Get(IReadOnlySet<RailCell> graph, RailCell start, RailCell end)
    {
        var key = (start, end);
        if (paths.TryGetValue(key, out var cached)) { Hits++; return cached; }
        Searches++;
        var route = RailGraph.FindPath(graph, start, end);
        int cells = route?.Count ?? 0;
        // Bound memory as well as entry count. Oversized routes can be used without retention.
        if (cells > 32768) return route;
        if (paths.Count >= 128 || retainedCells + cells > 32768) Invalidate();
        paths[key] = route; retainedCells += cells;
        return route;
    }
}

public sealed class RailPlanningBudget
{
    private int tick = -1, used;
    public int Peak { get; private set; }
    public bool TryTake(int now)
    {
        if (tick != now) { tick = now; used = 0; }
        if (used >= 2) return false;
        used++; Peak = Math.Max(Peak, used); return true;
    }
}

public static class RailAnimation
{
    public static float Progress(int now, int startedAt, float frameFraction, int duration = 3) =>
        Math.Clamp((now - startedAt + frameFraction) / Math.Max(1,duration), 0, 1);
}
