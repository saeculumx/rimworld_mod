namespace LocalRail;

public static class RailServicePolicy
{
    public const int Capacity = 4, EdgeTicks = 3, DwellTicks = 90;
    public static int EstimateWait(int edges, int index, int direction, int dwell, bool stationA)
    {
        if (edges <= 0) return int.MaxValue;
        index = Math.Clamp(index, 0, edges);
        if (dwell > 0 && (stationA ? index == 0 : index == edges)) return 0;
        int distance = stationA
            ? (direction < 0 ? index : 2 * edges - index)
            : (direction > 0 ? edges - index : edges + index);
        bool mustTurn = stationA ? direction > 0 : direction < 0;
        // If already dwelling at the opposite terminal, count only its remaining dwell.
        bool oppositeTerminal = dwell > 0 && (stationA ? index == edges : index == 0);
        return distance * EdgeTicks + dwell + (mustTurn && !oppositeTerminal ? DwellTicks : 0);
    }
    public static bool ShouldRide(float direct, float access, float egress, int edges, int wait, bool prefer)
    {
        return edges>=8&&RailTimetable.Prefer(direct,access,egress,edges*EdgeTicks,wait,prefer);
    }
    public static int WaitingLimit(int edges) => Math.Max(2400, (edges * EdgeTicks * 2 + DwellTicks * 2) * 4);
}
