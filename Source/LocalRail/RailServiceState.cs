using Verse;

namespace LocalRail;

public class RailServiceState : IExposable
{
    public int id;
    public Building_RailStation a, b;
    public Building_RailTrain train;
    public List<Building_RailStation> stops = new();
    public List<int> platforms = new();
    public bool loop, reverseLoop;
    public bool freightOnly;
    public RailTrainKind trainKind=RailTrainKind.Automatic;
    internal string KindLabel=>(trainKind==RailTrainKind.Passenger?"LR_PassengerTrainKind":"LR_AutomaticTrainKind").Translate();
    public int unloadBlockedAt=-1;
    public int dwellVisit;
    public string routeName;
    public int colorIndex;
    public int dwellElapsed, quietTicks, nextBoardAt, nextFreightAt;
    public List<int> stopIndices = new(), cumulative = new();
    public int segmentDuration = RailServicePolicy.EdgeTicks;
    public List<IntVec3> path = new();
    public int index, direction = 1, dwell = RailServicePolicy.DwellTicks;
    public bool operating = true;
    public int ticksLeft = RailServicePolicy.EdgeTicks;
    public bool animating;
    public int segmentStartedAt;
    public string fault;
    public int blockedTicks;
    internal int cargoRevision=-1;
    internal List<RailCargoVisual> cargoVisuals=new();
    public bool Available => operating && fault == null && train?.Spawned == true;
    public bool CanRetainPassengers => operating && (fault==null||fault=="LR_DoorWaiting"||fault=="LR_TrainAhead") && train?.Spawned==true;
    public string Name => string.IsNullOrWhiteSpace(routeName) ? "LR_LineNumber".Translate(id) : routeName;
    public string Itinerary => string.Join(" → ", Enumerable.Range(0,stops.Count).Select(i=>Stop(i).Label)) + (loop ? " → ↻" : "");
    public UnityEngine.Color RouteColor => RailColors.Get(colorIndex);
    public int Platform(int stop) => stop < platforms.Count ? platforms[stop] : 0;
    public RailStop Stop(int stop) => new(stops[stop],Platform(stop));
    public int FindStop(Building_RailStation station,int platform) => Enumerable.Range(0,stops.Count).FirstOrDefault(i=>stops[i]==station && Platform(i)==platform,-1);
    public int NextIndex => loop ? (index+direction+path.Count)%path.Count : index+direction;
    public int CircuitTicks => cumulative.LastOrDefault() + (loop && path.Count>1 ? RailGraph.EdgeDuration(new(path[^1].x,path[^1].z),new(path[0].x,path[0].z)) : 0);
    public bool HeadsTo(int from,int to) => from>=0 && to>=0 && from!=to && (loop || Math.Sign(to-from)==direction);
    public int StopAt(int pathIndex) => stopIndices.IndexOf(pathIndex);
    public int WaitingLimit => Math.Max(2400, (CircuitTicks + Enumerable.Range(0,stops.Count).Sum(i=>
        Stop(i).Settings.automatic?Stop(i).Settings.maximumTicks:Stop(i).Settings.manualTicks))*(loop?1:2)*4);
    public void ExposeData()
    {
        Scribe_Values.Look(ref id, "id");
        Scribe_References.Look(ref a, "stationA"); Scribe_References.Look(ref b, "stationB");
        Scribe_References.Look(ref train, "train");
        Scribe_Collections.Look(ref stops, "stops", LookMode.Reference);
        Scribe_Collections.Look(ref platforms,"platforms",LookMode.Value);
        Scribe_Values.Look(ref loop,"loop");Scribe_Values.Look(ref reverseLoop,"reverseLoop");
        Scribe_Values.Look(ref freightOnly,"freightOnly");
        Scribe_Values.Look(ref trainKind,"trainKind",RailTrainKind.Automatic);
        Scribe_Values.Look(ref routeName,"routeName");Scribe_Values.Look(ref colorIndex,"colorIndex",-1);
        Scribe_Values.Look(ref dwellElapsed,"dwellElapsed");Scribe_Values.Look(ref quietTicks,"quietTicks");
        Scribe_Collections.Look(ref path, "path", LookMode.Value);
        Scribe_Values.Look(ref index, "index"); Scribe_Values.Look(ref direction, "direction", 1);
        Scribe_Values.Look(ref dwell, "dwell", RailServicePolicy.DwellTicks);
        Scribe_Values.Look(ref operating, "operating", true);
        // Resume from the last committed cell, retaining the real train and terminal dwell.
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (stops == null || stops.Count < 2) stops = new() { a,b };
            platforms ??= new();while(platforms.Count<stops.Count)platforms.Add(0);
            if(colorIndex<0)colorIndex=(id-1+8)%8;
            animating = false; ticksLeft = RailServicePolicy.EdgeTicks;
        }
    }
}
