using RimWorld;
using UnityEngine;
using Verse;
namespace LocalRail;

public partial class RailMapComponent
{
    private List<RailServiceState> services = new();
    private readonly Dictionary<int,RailServiceState> byServiceId = new();
    private readonly Dictionary<Building_RailTrain,RailServiceState> byTrain = new();
    private readonly Dictionary<int,List<TransitState>> serviceTrips = new();
    private readonly Dictionary<RailCell,int> networks = new();
    private int topologyRevision,networksRevision=-1,validatedRevision=-1,nextServiceId=1;
    private long nextQueueOrder;
    private readonly Dictionary<RailStop,List<RailStop>> connectionCache=new();
    private int connectionRevision=-1;
    internal int MaxPassengersObserved {get;private set;}
    internal int TrainMoves {get;private set;}
    internal int EmptyTrainMoves {get;private set;}
    internal int Reversals {get;private set;}
    internal IReadOnlyList<RailServiceState> Services=>services;
    internal RailServiceState Service(int id)=>byServiceId.GetValueOrDefault(id);
    internal RailServiceState TrainService(Building_RailTrain train)=>byTrain.GetValueOrDefault(train);
    internal RailServiceState StationService(Building_RailStation station)=>services.FirstOrDefault(s=>s.stops.Contains(station));
    private List<TransitState> Assigned(int id)
    {if(!serviceTrips.TryGetValue(id,out var list))serviceTrips[id]=list=new();return list;}
    private void RebuildServiceIndexes()
    {
        byServiceId.Clear();byTrain.Clear();serviceTrips.Clear();validatedRevision=-1;
        foreach(var service in services)
        {byServiceId[service.id]=service;if(service.train!=null)byTrain[service.train]=service;nextServiceId=Math.Max(nextServiceId,service.id+1);}
        foreach(var trip in trips)Assigned(trip.serviceId).Add(trip);
        RebuildLoadingWorkers();
    }
    private void EnsureNetworks()
    {
        if(networksRevision==topologyRevision)return;
        networks.Clear();int id=0;var queue=new Queue<RailCell>();
        foreach(var root in graph)
        {
            if(networks.ContainsKey(root))continue;networks[root]=++id;queue.Enqueue(root);
            while(queue.TryDequeue(out var current))foreach(var next in current.Neighbors())
                if(HasConnection(Vec(current),Vec(next))&&networks.TryAdd(next,id))queue.Enqueue(next);
        }
        networksRevision=topologyRevision;
    }
    private bool Connected(IntVec3 a,IntVec3 b)=>networks.TryGetValue(Cell(a),out int network)&&networks.GetValueOrDefault(Cell(b),-1)==network;
    internal bool PlatformsConnected(RailStop a,RailStop b){EnsureNetworks();return a!=b&&Connected(a.Cell,b.Cell);}
    internal IEnumerable<RailStop> AllPlatforms=>Stations.SelectMany(s=>Enumerable.Range(0,s.PlatformCount).Select(p=>new RailStop(s,p)));
    internal bool PortConnected(RailStop stop,int side)
    {
        var port=stop.Cell+stop.Station.RailOffset(stop.Station.def.size.x/2*side,0);
        return GenAdj.AdjacentCells.Any(d=>rails.TryGetValue(port+d,out var rail)&&rail!=stop.Station&&HasConnection(port,port+d));
    }
    internal List<RailStop> ReachablePlatforms(RailStop stop)
    {
        if(connectionRevision!=topologyRevision){connectionCache.Clear();connectionRevision=topologyRevision;}
        if(!connectionCache.TryGetValue(stop,out var list))connectionCache[stop]=list=AllPlatforms.Where(other=>PlatformsConnected(stop,other)).ToList();
        return list;
    }
    internal bool CanCreateService(Building_RailStation a,Building_RailStation b,out string reason)
    {
        if(a==null||b==null){reason="LR_InvalidEndpoints";return false;}
        if(!BuildServicePath(new[]{new RailStop(a),new RailStop(b)},false,out var path,out _,out reason))return false;
        return RouteAvailable(path,null,out reason);
    }
    internal RailServiceState CreateService(Building_RailStation a,Building_RailStation b)
    {
        if(!ConfigureService(new List<Building_RailStation>{a,b},null,out var reason))
        {Messages.Message(reason.Translate(),MessageTypeDefOf.RejectInput,false);return null;}
        return services[^1];
    }
    private void SpawnTrain(RailServiceState service)
    {
        var train=(Building_RailTrain)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(service.trainKind==RailTrainKind.Passenger?"LR_PassengerTrain":"LR_AutomaticTrain"));
        train.SetFaction(Faction.OfPlayer);service.train=train;byTrain[train]=service;
        service.index=0;service.direction=service.loop&&service.reverseLoop?-1:1;service.animating=false;
        GenSpawn.Spawn(train,service.Stop(0).Cell,map);StartDwell(service,0);
        train.Rotation=Rot4.FromIntVec3(service.path[service.NextIndex]-service.path[0]);
    }
    internal void ReplaceTrain(RailServiceState service)
    {
        if(service.train?.Spawned==true||service.a?.Spawned!=true)return;
        ReleaseService(service,"train replaced");if(service.train!=null)byTrain.Remove(service.train);
        SpawnTrain(service);validatedRevision=-1;
    }
    internal void SetServiceOperating(RailServiceState service,bool operating)
    {
        service.operating=operating;service.animating=false;service.ticksLeft=3;validatedRevision=-1;
        if(!operating)ReleaseService(service,"service paused");
    }
    internal void RemoveService(RailServiceState service)
    {
        ReleaseFreight(service);ReleaseService(service,"service removed");services.Remove(service);
        byServiceId.Remove(service.id);serviceTrips.Remove(service.id);
        if(service.train!=null){byTrain.Remove(service.train);if(!service.train.Destroyed)service.train.Destroy();}
        validatedRevision=-1;PreviewChanged();
    }
    private void ReleaseService(RailServiceState service,string reason)
    {var passengers=Assigned(service.id);for(int i=passengers.Count-1;i>=0;i--)Resume(passengers[i],reason);}
    private void ValidateServices()
    {
        if(validatedRevision==topologyRevision)return;
        EnsureNetworks();var occupied=new HashSet<IntVec3>();
        foreach(var service in services)
        {
            service.fault=null;service.animating=false;service.ticksLeft=3;
            if(service.stops.Any(s=>s?.Spawned!=true||s.Map!=map))service.fault="LR_StationMissing";
            else if(service.train?.Spawned!=true||service.train.Map!=map)service.fault="LR_TrainMissing";
            else if(!BuildServicePath(Enumerable.Range(0,service.stops.Count).Select(service.Stop).ToArray(),service.loop,out var path,out var indices,out var reason))service.fault=reason;
            else
            {
                int itineraryIndex=RailRoutePosition.Resolve(service.path,service.index,service.stopIndices,path,indices,service.train.Position);
                service.path=path;service.stopIndices=indices;service.cumulative=new(){0};
                for(int i=1;i<path.Count;i++)service.cumulative.Add(service.cumulative[^1]+RailGraph.EdgeDuration(Cell(path[i-1]),Cell(path[i])));
                service.index=itineraryIndex;
                if(service.index<0)service.fault="LR_TrainOffRoute";
                else if(path.Any(occupied.Contains))service.fault="LR_PathConflict";
                else foreach(var cell in path)occupied.Add(cell);
            }
            if(service.fault!=null)ReleaseService(service,service.fault);
        }
        validatedRevision=topologyRevision;
    }
    internal int RiderCount(RailServiceState service)=>Assigned(service.id).Count(t=>t.phase==TransitPhase.Riding);
    internal int WaitingCount(RailServiceState service)=>Assigned(service.id).Count(t=>t.phase==TransitPhase.Waiting);
    internal string ServiceStatus(RailServiceState service)=>(service.fault??(!service.operating?"LR_ServicePaused":service.dwell>0?"LR_ServiceDwelling":"LR_ServiceRunning")).Translate();
    private bool PhysicalTrack(IntVec3 cell)=>rails.TryGetValue(cell,out var rail)&&rail.Faction==Faction.OfPlayer&&PhysicalClear(cell);
    private void StartDwell(RailServiceState service,int stop)
    {
        var settings=service.Stop(stop).Settings;
        service.dwell=settings.automatic?settings.minimumTicks:settings.manualTicks;
        service.dwellElapsed=service.quietTicks=0;service.nextBoardAt=service.nextFreightAt=GenTicks.TicksGame;
        service.unloadBlockedAt=-1;
        service.dwellVisit++;
    }
    private void TickServices()
    {
        ValidateServices();
        foreach(var service in services)
        {
            if(service.train?.Spawned!=true){service.fault="LR_TrainMissing";ReleaseService(service,"train missing");continue;}
            if(!service.operating||service.fault!=null&&service.fault!="LR_TrackBlocked"&&service.fault!="LR_TrainAhead"&&service.fault!="LR_DoorWaiting")continue;
            if(service.path.Count<2||service.index<0)continue;
            if(service.dwell<=0)PrepareDoors(service);
            if(service.dwell>0)
            {
                ExchangePassengers(service);ExchangeFreight(service);service.dwellElapsed++;service.quietTicks++;
                var stop=service.StopAt(service.index);
                var settings=stop>=0?service.Stop(stop).Settings:null;
                if(settings?.automatic==true)
                {
                    bool hold=service.dwellElapsed<settings.minimumTicks||service.quietTicks<60||PendingAtStop(service,stop);
                    if(service.dwellElapsed<settings.maximumTicks&&hold)
                    {service.dwell=Math.Max(1,settings.minimumTicks-service.dwellElapsed);continue;}
                    service.dwell=0;
                }
                else if(--service.dwell>0)continue;
                BeginTrainSegment(service);continue;
            }
            if(--service.ticksLeft>0)continue;
            if(!service.animating){BeginTrainSegment(service);continue;}
            int nextIndex=service.NextIndex;var next=service.path[nextIndex];if(!CanMoveTrain(service,next))continue;
            var passengers=Assigned(service.id);
            for(int i=passengers.Count-1;i>=0;i--)
            {
                var trip=passengers[i];
                if(trip.phase==TransitPhase.Riding&&(!SameJob(trip)||!Eligible(trip.pawn)||!SafeRail(trip.pawn,next)||
                    service.train.Position.x!=next.x&&service.train.Position.z!=next.z&&
                    (!SafeCell(trip.pawn,new(service.train.Position.x,0,next.z))||!SafeCell(trip.pawn,new(next.x,0,service.train.Position.z)))))Resume(trip,"passenger interrupted");
            }
            service.train.Position=next;service.index=nextIndex;TrainMoves++;if(RiderCount(service)==0)EmptyTrainMoves++;
            foreach(var trip in passengers)
            {
                if(trip.phase!=TransitPhase.Riding)continue;
                trip.pawn.Position=next;trip.pawn.Rotation=Rot4.South;trip.pawn.filth.Notify_EnteredNewCell();trip.index=service.index;
                Internal(trip.pawn.pather.ResetToCurrentPosition);
            }
            service.animating=false;
            if(!service.loop&&(service.index==0||service.index==service.path.Count-1)){service.direction*=-1;Reversals++;}
            int at=service.StopAt(service.index);
            if(at>=0){StartDwell(service,at);ExchangePassengers(service);ExchangeFreight(service);}
            else BeginTrainSegment(service);
        }
    }
    private bool CanMoveTrain(RailServiceState service,IntVec3 next)
    {
        var current=service.train.Position;bool diagonal=current.x!=next.x&&current.z!=next.z;
        if(!NoseDoorsReady(service))
        {service.fault="LR_DoorWaiting";service.animating=false;service.ticksLeft=1;return false;}
        if(current.GetDoor(map) is Building_RailDoor currentDoor && !currentDoor.TrainAllowed)
        {service.fault="LR_DoorWaiting";service.animating=false;service.ticksLeft=1;return false;}
        if(next.GetDoor(map) is Building_RailDoor door && RailDoorEdge(current,next))
        {
            door.RequestTrain();
            if(!door.TrainReady){service.fault="LR_DoorWaiting";service.animating=false;service.ticksLeft=1;return false;}
        }
        if(!PhysicalTrack(next)||!PhysicalTrack(current)||diagonal&&(!PhysicalClear(new(current.x,0,next.z))||!PhysicalClear(new(next.x,0,current.z))))
        {service.fault="LR_TrackBlocked";service.animating=false;service.ticksLeft=10;ReleaseService(service,"track blocked");return false;}
        if(services.Any(s=>s!=service&&s.train?.Spawned==true&&s.train.Position==next))
        {service.fault="LR_TrainAhead";service.animating=false;service.ticksLeft=10;return false;}
        service.fault=null;service.blockedTicks=0;return true;
    }
    private void BeginTrainSegment(RailServiceState service)
    {
        int next=service.NextIndex;
        if(next<0||next>=service.path.Count){service.direction*=-1;StartDwell(service,service.StopAt(service.index));return;}
        if(!CanMoveTrain(service,service.path[next]))return;
        service.train.Rotation=Rot4.FromIntVec3(service.path[next]-service.train.Position);
        service.segmentStartedAt=GenTicks.TicksGame;service.segmentDuration=RailGraph.EdgeDuration(Cell(service.train.Position),Cell(service.path[next]));
        service.animating=true;service.ticksLeft=service.segmentDuration;
    }
    private void ExchangePassengers(RailServiceState service)
    {
        if(service.freightOnly)return;
        int stop=service.StopAt(service.index);if(stop<0)return;
        var platform=service.Stop(stop);var assigned=Assigned(service.id);
        for(int i=assigned.Count-1;i>=0;i--)
            if(assigned[i].phase==TransitPhase.Riding&&assigned[i].end==platform.Station&&assigned[i].endPlatform==platform.Platform)
            {Resume(assigned[i],"arrived at service stop");service.quietTicks=0;}
        if(GenTicks.TicksGame<service.nextBoardAt)return;
        Span<bool> occupied=stackalloc bool[RailServicePolicy.Capacity];occupied.Clear();
        foreach(var trip in assigned)if(trip.phase==TransitPhase.Riding&&trip.seat>=0&&trip.seat<occupied.Length)occupied[trip.seat]=true;
        int seat=occupied.IndexOf(false);if(seat<0)return;
        TransitState waiting=null;
        foreach(var trip in assigned)
            if(trip.phase==TransitPhase.Waiting&&trip.start==platform.Station&&trip.startPlatform==platform.Platform&&trip.pawn.Position==platform.Cell&&
                service.HeadsTo(stop,service.FindStop(trip.end,trip.endPlatform))&&SameJob(trip)&&Eligible(trip.pawn)&&SafeRail(trip.pawn,platform.Cell)&&
                (waiting==null||trip.queueOrder<waiting.queueOrder))waiting=trip;
        if(waiting==null)return;
        waiting.phase=TransitPhase.Riding;waiting.seat=seat;waiting.index=service.index;waiting.pawn.Rotation=Rot4.South;
        service.nextBoardAt=GenTicks.TicksGame+15;service.quietTicks=0;
        MaxPassengersObserved=Math.Max(MaxPassengersObserved,RiderCount(service));
        Diagnostic($"{waiting.pawn.LabelShort}: boarded train {service.train.thingIDNumber}, service {service.id}, platform {platform.Platform+1}, seat {seat}.");
    }
    internal Vector3 TrainVisualPosition(RailServiceState service)
    {
        var position=service.train.Position.ToVector3Shifted();
        if(service.Available&&service.animating&&RailMod.Settings.smoothAnimation)
            position=Vector3.Lerp(position,service.path[service.NextIndex].ToVector3Shifted(),RailAnimation.Progress(GenTicks.TicksGame,service.segmentStartedAt,
                renderTick==GenTicks.TicksGame?renderFraction:0,service.segmentDuration));
        position.y=AltitudeLayer.Item.AltitudeFor();return position;
    }
    private bool PhysicalClear(IntVec3 c)=>c.InBounds(map)&&c.Walkable(map)&&!c.Fogged(map)&&
        (c.GetDoor(map) is not { } door || door is Building_RailDoor railDoor && railDoor.TrainAllowed);
    internal float TrainAngle(RailServiceState service)
    {
        var delta=service.path[Math.Clamp(service.NextIndex,0,service.path.Count-1)]-service.train.Position;
        return delta==IntVec3.Zero?service.train.Rotation.AsAngle-90:(float)(Math.Atan2(-delta.z,delta.x)*180/Math.PI);
    }
    public bool HasConnection(IntVec3 a,IntVec3 b)=>RailGraph.Connected(graph,Cell(a),Cell(b))&&RailDoorEdge(a,b);
    internal bool BuildServicePath(IReadOnlyList<Building_RailStation> stops,out List<IntVec3> path,out List<int> indices,out string reason)=>
        BuildServicePath(stops.Select(s=>new RailStop(s)).ToArray(),false,out path,out indices,out reason);
    internal bool BuildServicePath(IReadOnlyList<RailStop> stops,bool loop,out List<IntVec3> path,out List<int> indices,out string reason)
    {
        path=new();indices=new();reason="LR_InvalidRoute";
        if(stops.Count<2||stops.Count>16||stops.Distinct().Count()!=stops.Count||
            stops.Any(s=>s.Station?.Spawned!=true||s.Station.Map!=map||s.Station.Faction!=Faction.OfPlayer||s.Platform<0||s.Platform>=s.Station.PlatformCount))return false;
        var used=new HashSet<IntVec3>();path.Add(stops[0].Cell);used.Add(path[0]);indices.Add(0);
        int legs=loop?stops.Count:stops.Count-1;
        for(int leg=0;leg<legs;leg++)
        {
            var from=stops[leg].Cell;var to=stops[(leg+1)%stops.Count].Cell;
            var others=stops.Where(s=>s.Cell!=from&&s.Cell!=to).Select(s=>s.Cell).ToHashSet();
            bool Allowed(RailCell c){var v=Vec(c);return !loop||v==from||v==to||!used.Contains(v)&&!others.Contains(v);}
            var segment=routeCache.Get(graph,Cell(from),Cell(to));
            if(segment==null||segment.Skip(1).Any(c=>!Allowed(c))||segment.Zip(segment.Skip(1),(a,b)=>HasConnection(Vec(a),Vec(b))).Any(ok=>!ok))
                segment=RailGraph.FindPath(graph,Cell(from),Cell(to),Allowed,(a,b)=>RailDoorEdge(Vec(a),Vec(b)));
            if(segment==null){reason="LR_DisconnectedPlatforms".Translate(stops[leg].Label,stops[(leg+1)%stops.Count].Label);return false;}
            foreach(var cell in segment.Skip(1))
            {
                var v=Vec(cell);if(loop&&leg==legs-1&&v==path[0])continue;
                if(!used.Add(v)&&loop)return false;path.Add(v);
            }
            if(leg<stops.Count-1)indices.Add(path.Count-1);
        }
        if(loop&&(path.Count<3||!HasConnection(path[^1],path[0]))){reason="LR_LoopNotClosed";return false;}
        for(int i=0;i<path.Count;i++)
            if(path[i].GetDoor(map) is Building_RailDoor && (loop||i>0&&i<path.Count-1))
            {
                var before=path[(i-1+path.Count)%path.Count];var after=path[(i+1)%path.Count];
                if(before-path[i]!=path[i]-after){reason="LR_DoorStraight".Translate(path[i]);return false;}
            }
        reason=null;return true;
    }
    private bool RouteAvailable(List<IntVec3> path,RailServiceState existing,out string reason)
    {
        var cells=path.ToHashSet();var conflict=services.FirstOrDefault(s=>s!=existing&&s.path.Any(cells.Contains));
        reason=conflict==null?null:"LR_OverlapRoute".Translate(conflict.Name);return conflict==null;
    }
    internal bool ConfigureService(List<Building_RailStation> stops,RailServiceState existing,out string reason)=>
        ConfigureService(stops.Select(s=>new RailStop(s)).ToList(),false,false,existing,existing?.routeName,existing?.colorIndex??services.Count%8,out reason);
    internal bool ConfigureService(List<RailStop> stops,bool loop,bool reverse,RailServiceState existing,string name,int color,out string reason,bool? freightOnly=null,RailTrainKind? trainKind=null)
    {
        if(!BuildServicePath(stops,loop,out var path,out var indices,out reason)||!RouteAvailable(path,existing,out reason))return false;
        var service=existing??new RailServiceState{id=nextServiceId++};
        int previousIndex=existing==null?0:RailRoutePosition.Resolve(service.path,service.index,service.stopIndices,path,indices,service.train?.Position??path[0]);
        if(existing!=null)ReleaseService(service,"route edited");
        service.stops=stops.Select(s=>s.Station).ToList();service.platforms=stops.Select(s=>s.Platform).ToList();
        service.a=stops[0].Station;service.b=stops[^1].Station;service.path=path;service.stopIndices=indices;
        service.loop=loop;service.reverseLoop=reverse;service.routeName=name?.Trim();service.colorIndex=Math.Clamp(color,0,7);
        service.freightOnly=freightOnly??service.freightOnly;
        service.trainKind=trainKind??service.trainKind;
        if(service.trainKind==RailTrainKind.Passenger)service.freightOnly=false;
        service.animating=false;service.dwell=0;
        if(existing==null){services.Add(service);byServiceId[service.id]=service;SpawnTrain(service);}
        else if(service.train?.Spawned!=true)ReplaceTrain(service);
        else
        {
            if(!path.Contains(service.train.Position))service.train.Position=path[0];
            service.index=previousIndex<0?0:previousIndex;service.direction=loop?(reverse?-1:1):service.index==path.Count-1?-1:1;
            int stop=indices.IndexOf(service.index);if(stop>=0)StartDwell(service,stop);
        }
        RecoverInvalidFreight(service);validatedRevision=-1;ValidateServices();PreviewChanged();return true;
    }
}
