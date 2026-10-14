namespace LocalRail;

// Repeated physical cells are distinct visits in a shuttle itinerary.
public static class RailRoutePosition
{
    public static int Resolve<T>(IReadOnlyList<T> oldPath,int oldIndex,IReadOnlyList<int> oldStops,
        IReadOnlyList<T> path,IReadOnlyList<int> stops,T cell)
    {
        var comparer=EqualityComparer<T>.Default;
        if(oldIndex>=0&&oldIndex<oldPath.Count&&oldIndex<path.Count&&comparer.Equals(oldPath[oldIndex],cell)&&
            comparer.Equals(path[oldIndex],cell)&&oldPath.SequenceEqual(path))return oldIndex;
        for(int s=0;s<Math.Min(oldStops.Count,stops.Count);s++)
            if(oldStops[s]==oldIndex&&stops[s]<path.Count&&comparer.Equals(path[stops[s]],cell))return stops[s];
        for(int leg=0;leg+1<Math.Min(oldStops.Count,stops.Count);leg++)
        {
            if(oldIndex<=oldStops[leg]||oldIndex>=oldStops[leg+1])continue;
            double fraction=(oldIndex-oldStops[leg])/(double)(oldStops[leg+1]-oldStops[leg]);
            double expected=stops[leg]+fraction*(stops[leg+1]-stops[leg]);
            int result=Closest(path,cell,stops[leg],stops[leg+1],expected);if(result>=0)return result;
        }
        return Closest(path,cell,0,path.Count-1,oldIndex);
    }
    private static int Closest<T>(IReadOnlyList<T> path,T cell,int first,int last,double expected)
    {
        int result=-1;double best=double.MaxValue;
        for(int i=Math.Max(0,first);i<=last&&i<path.Count;i++)
        {if(!EqualityComparer<T>.Default.Equals(path[i],cell))continue;double distance=Math.Abs(i-expected);if(distance<best){best=distance;result=i;}}
        return result;
    }
}
