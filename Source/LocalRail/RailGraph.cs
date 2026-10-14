namespace LocalRail;

// Pure graph/cost logic: shares its production source with the offline test runner.
public readonly record struct RailCell(int X, int Z)
{
    public IEnumerable<RailCell> Neighbors()
    {
        yield return new(X + 1, Z); yield return new(X - 1, Z);
        yield return new(X, Z + 1); yield return new(X, Z - 1);
        yield return new(X+1,Z+1); yield return new(X+1,Z-1);
        yield return new(X-1,Z+1); yield return new(X-1,Z-1);
    }
}

public static class RailGraph
{
    public static bool Connected(IReadOnlySet<RailCell> cells, RailCell a, RailCell b)
    {
        int dx = Math.Abs(a.X-b.X), dz = Math.Abs(a.Z-b.Z);
        if (!cells.Contains(a) || !cells.Contains(b) || dx > 1 || dz > 1 || dx+dz == 0) return false;
        return dx == 0 || dz == 0 || (!cells.Contains(new(a.X,b.Z)) && !cells.Contains(new(b.X,a.Z)));
    }
    public static int EdgeDuration(RailCell a, RailCell b) => a.X != b.X && a.Z != b.Z ? 4 : 3;
    public static List<RailCell> FindPath(IReadOnlySet<RailCell> cells, RailCell start, RailCell end,
        Func<RailCell, bool> usable = null, Func<RailCell,RailCell,bool> edgeAllowed=null)
    {
        if (!cells.Contains(start) || !cells.Contains(end) ||
            (usable != null && (!usable(start) || !usable(end)))) return null;
        var queue = new PriorityQueue<RailCell, int>();
        var costs = new Dictionary<RailCell, int> { [start] = 0 };
        var previous = new Dictionary<RailCell, RailCell> { [start] = start };
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var current, out int cost))
        {
            if (cost != costs[current]) continue;
            if (current == end)
            {
                var path = new List<RailCell> { end };
                while (current != start) { current = previous[current]; path.Add(current); }
                path.Reverse(); return path;
            }
            foreach (var next in current.Neighbors())
                if (Connected(cells,current,next) && (usable == null || usable(next)) && (edgeAllowed==null||edgeAllowed(current,next)))
                {
                    int candidate = cost + EdgeDuration(current,next);
                    if (costs.TryGetValue(next, out int old) && old <= candidate) continue;
                    costs[next] = candidate; previous[next] = current; queue.Enqueue(next,candidate);
                }
        }
        return null;
    }

    public static bool WorthTaking(float directTicks, float approachTicks, float egressTicks, int edges,
        int ticksPerEdge = 3, int boardingTicks = 60, float savingFraction = .25f, int minSavingTicks = 120)
    {
        if (edges <= 0 || directTicks <= 0 || approachTicks < 0 || egressTicks < 0) return false;
        float railTicks = approachTicks + egressTicks + edges * ticksPerEdge + boardingTicks;
        return railTicks <= directTicks * (1 - savingFraction) && directTicks - railTicks >= minSavingTicks;
    }
}
