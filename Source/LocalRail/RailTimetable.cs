namespace LocalRail;

public static class RailTimetable
{
    public static int Wait(IReadOnlyList<int> cumulative, IReadOnlyList<int> stops, IReadOnlyList<int> dwellTimes,
        int index, int direction, int remainingDwell, int boardingStop, int destinationStop,bool loop=false,int circuitTicks=0)
    {
        if (stops.Count < 2 || boardingStop == destinationStop) return int.MaxValue;
        if(loop)
        {
            if(index==stops[boardingStop]&&remainingDwell>0)return 0;
            int distance=RingDistance(cumulative[index],cumulative[stops[boardingStop]],direction,circuitTicks);
            if(distance==0)distance=circuitTicks;
            int wait=remainingDwell+distance;
            for(int s=0;s<stops.Count;s++)
            {int d=RingDistance(cumulative[index],cumulative[stops[s]],direction,circuitTicks);if(d>0&&d<distance)wait+=dwellTimes[s];}
            return wait;
        }
        int wanted = destinationStop > boardingStop ? 1 : -1;
        if (index == stops[boardingStop] && remainingDwell > 0 && direction == wanted) return 0;
        int elapsed = remainingDwell;
        for (int visit=0; visit < stops.Count*2+2; visit++)
        {
            int next = -1;
            if (direction > 0) { for(int s=0;s<stops.Count;s++) if(stops[s]>index) { next=s; break; } }
            else { for(int s=stops.Count-1;s>=0;s--) if(stops[s]<index) { next=s; break; } }
            if(next<0) { direction=-direction; continue; }
            elapsed += Math.Abs(cumulative[stops[next]]-cumulative[index]); index=stops[next];
            if(next==0 || next==stops.Count-1) direction=-direction;
            if(next==boardingStop && direction==wanted) return elapsed;
            elapsed += dwellTimes[next];
        }
        return int.MaxValue;
    }
    public static int RingDistance(int from,int to,int direction,int circuitTicks)=>direction>0?(to-from+circuitTicks)%circuitTicks:(from-to+circuitTicks)%circuitTicks;
    public static int Travel(IReadOnlyList<int> cumulative,IReadOnlyList<int> stops,IReadOnlyList<int> dwell,
        int from,int to,int direction,bool loop,int circuitTicks)
    {
        if(from==to)return 0;
        int result=loop?RingDistance(cumulative[stops[from]],cumulative[stops[to]],direction,circuitTicks):Math.Abs(cumulative[stops[to]]-cumulative[stops[from]]);
        if(!loop)direction=Math.Sign(to-from);
        for(int s=(from+direction+stops.Count)%stops.Count;s!=to;s=(s+direction+stops.Count)%stops.Count)result+=dwell[s];
        return result;
    }
    public static bool Prefer(float direct, float access, float egress, int travel, int wait, bool prefer)
    {
        if(direct<=0 || travel<24 || access<0 || egress<0 || wait<0 || wait==int.MaxValue) return false;
        float total=access+egress+travel+(float)wait+30;
        return prefer ? access+egress<=direct*.8f && total<=direct*2f
            : total<=direct*.75f && direct-total>=120;
    }
}
