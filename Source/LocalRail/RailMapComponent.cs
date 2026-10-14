using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LocalRail;

public partial class RailMapComponent : MapComponent
{
    private readonly Dictionary<IntVec3, Building> rails = new();
    private readonly HashSet<RailCell> graph = new();
    private readonly List<Building_RailStation> stations = new();
    private List<TransitState> trips = new();
    private readonly Dictionary<Pawn, TransitState> tripsByPawn = new();
    private readonly Dictionary<(int x, int z), List<Building_RailStation>> stationBuckets = new();
    private readonly RailRouteCache routeCache = new();
    private readonly RailPlanningBudget planningBudget = new();
    internal int PlanAttempts { get; private set; }
    internal int AccessExpanded { get; private set; }
    internal int CacheHits => routeCache.Hits;
    internal int RouteSearches => routeCache.Searches;
    internal int PeakPlansPerTick => planningBudget.Peak;
    private int renderTick = -1;
    private float renderFraction;
    private readonly Dictionary<Pawn, (int job, int tick)> lastChecks = new();
    private int internalCall;
    public int StationCount => stations.Count;
    private static readonly HashSet<string> SupportedJobs = new()
    {
        "Goto", "Mine", "Sow", "Harvest", "HarvestDesignated", "CutPlant", "CutPlantDesignated",
        "Research", "Clean", "Repair", "Ingest", "LayDown", "LayDownAwake", "LayDownResting",
        "HaulToCell", "HaulToContainer"
    };
    private static RailCell Cell(IntVec3 c) => new(c.x, c.z);
    private static IntVec3 Vec(RailCell c) => new(c.X, 0, c.Z);
    private static (int, int) Bucket(IntVec3 c) => (c.x / 24, c.z / 24);
    public RailMapComponent(Map map) : base(map) { }
    public bool HasRail(IntVec3 c) => rails.ContainsKey(c);
    public void Register(Building rail)
    {
        foreach (var cell in ((IRailNode)rail).RailCells) { rails[cell] = rail; graph.Add(Cell(cell)); }
        if (rail is Building_RailStation station && !stations.Contains(station))
        {
            stations.Add(station);
            var key = Bucket(station.Position);
            if (!stationBuckets.TryGetValue(key, out var list)) stationBuckets[key] = list = new();
            list.Add(station);
        }
        routeCache.Invalidate();
        topologyRevision++;
        DirtyRailMesh(rail);
    }
    public void Unregister(Building rail)
    {
        foreach (var cell in ((IRailNode)rail).RailCells)
            if (rails.GetValueOrDefault(cell) == rail) { rails.Remove(cell); graph.Remove(Cell(cell)); }
        if (rail is Building_RailStation station)
        {
            stations.Remove(station);
            if (stationBuckets.TryGetValue(Bucket(station.Position), out var list)) list.Remove(station);
        }
        routeCache.Invalidate();
        topologyRevision++;
        DirtyRailMesh(rail);
    }
    private void DirtyRailMesh(Building rail)
    {
        foreach(var cell in ((IRailNode)rail).RailCells)
            map.mapDrawer.MapMeshDirty(cell,MapMeshFlagDefOf.Buildings,true,true);
    }
    public override void FinalizeInit()
    {
        rails.Clear(); graph.Clear(); stations.Clear(); stationBuckets.Clear();
        tripsByPawn.Clear();
        foreach (var trip in trips) if (trip.pawn != null) tripsByPawn[trip.pawn] = trip;
        foreach (var rail in map.listerThings.AllThings.OfType<Building>().Where(b=>b is IRailNode)) Register(rail);
        RebuildServiceIndexes();
        RebuildDeliveredCargo();
    }
    public override void ExposeData()
    {
        if(Scribe.mode==LoadSaveMode.Saving){deliveredCargo.RemoveAll(t=>t==null||t.Destroyed);deliveredCargoIndex.RemoveWhere(t=>t==null||t.Destroyed);}
        Scribe_Collections.Look(ref trips, "localRailTrips", LookMode.Deep);
        Scribe_Collections.Look(ref services, "localRailServices", LookMode.Deep);
        Scribe_Collections.Look(ref freight, "localRailFreight", LookMode.Deep);
        Scribe_Collections.Look(ref deliveredCargo,"localRailDeliveredCargo",LookMode.Reference);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) freight ??= new();
        Scribe_Values.Look(ref nextServiceId, "localRailNextServiceId", 1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) services ??= new();
        if (Scribe.mode == LoadSaveMode.PostLoadInit) trips ??= new();
    }
    public override void MapRemoved() { trips.Clear(); tripsByPawn.Clear(); serviceTrips.Clear(); services.Clear(); byServiceId.Clear(); byTrain.Clear(); lastChecks.Clear();planReports.Clear();loadingWorkers.Clear();connectionCache.Clear();routeCache.Invalidate();deliveredCargo.Clear();deliveredCargoIndex.Clear(); }
    public override void MapComponentTick()
    {
        if(GenTicks.TicksGame%250==0)foreach(var warehouse in stations.OfType<Building_RailWarehouse>())warehouse.SynchronizePriority();
        // Only active trips are inspected, never all map pawns.
        for (int i = trips.Count - 1; i >= 0; i--)
        {
            var trip = trips[i];
            if (!SameJob(trip)) { RemoveTrip(trip); continue; }
            if (trip.recoverAfterLoad) { trip.recoverAfterLoad = false; Resume(trip, "load recovery"); }
        }
        TickServices();
        RecoverFreight();
        if (GenTicks.TicksGame % 2500 == 0)
        {
            deliveredCargo.RemoveAll(t=>t==null||t.Destroyed);deliveredCargoIndex.RemoveWhere(t=>t==null||t.Destroyed);
            foreach (var pawn in lastChecks.Keys.Where(p => !p.Spawned || p.Map != map).ToArray()) lastChecks.Remove(pawn);
            foreach(var pawn in planReports.Keys.Where(p=>!p.Spawned||p.Map!=map).ToArray())planReports.Remove(pawn);
            foreach(var pawn in loadingWorkers.Keys.Where(p=>!p.Spawned||p.Map!=map||p.CurJob?.def.defName!="LR_LoadFreight").ToArray())loadingWorkers.Remove(pawn);
        }
    }
    private TransitState Trip(Pawn pawn) => tripsByPawn.GetValueOrDefault(pawn);
    private void RemoveTrip(TransitState trip)
    {
        trips.Remove(trip);
        if (trip.pawn != null) tripsByPawn.Remove(trip.pawn);
        if (serviceTrips.TryGetValue(trip.serviceId, out var assigned)) assigned.Remove(trip);
        if (trip.pawn?.Spawned == true) trip.pawn.Drawer.tweener.ResetTweenedPosToRoot();
    }
    internal TransitState ActiveTrip(Pawn pawn) => Trip(pawn);
    private static bool Eligible(Pawn pawn) => pawn.Spawned && pawn.IsColonistPlayerControlled &&
        pawn.RaceProps.Humanlike && !pawn.Drafted && !pawn.Downed && !pawn.Dead && !pawn.InMentalState &&
        pawn.carryTracker?.CarriedThing is not Pawn && pawn.CurJob != null && pawn.jobs.curDriver != null &&
        SupportedJobs.Contains(pawn.CurJob.def.defName) && !pawn.CurJob.exitMapOnArrival;
    private static bool SameJob(TransitState t) => t.pawn?.Spawned == true && t.pawn.CurJob != null &&
        t.pawn.CurJob.loadID == t.jobId && t.pawn.jobs.curDriver?.CurToilIndex == t.toilIndex;
    private bool SafeCell(Pawn pawn, IntVec3 c) => c.InBounds(map) && c.WalkableBy(map, pawn) &&
        !c.Fogged(map) && (c.GetDoor(map) is not { } door || (door.FreePassage||door.PawnCanOpen(pawn)) && !door.IsForbidden(pawn)) &&
        (pawn.playerSettings?.EffectiveAreaRestrictionInPawnCurrentMap is not Area area || area[c]) &&
        !PawnUtility.KnownDangerAt(c, map, pawn);
    private bool SafeRail(Pawn pawn, IntVec3 c) => rails.TryGetValue(c, out var rail) &&
        rail.Faction == Faction.OfPlayer && !rail.IsForbidden(pawn) && SafeCell(pawn, c);

    // Bounded multi-goal A*: removing reached goals raises remaining priorities lazily.
    private Dictionary<IntVec3, float> AccessCosts(Pawn pawn, IntVec3 root,
        IEnumerable<IntVec3> targets, Func<IntVec3, bool> safe, bool reverse = false)
    {
        var result = new Dictionary<IntVec3, float>();
        if (!safe(root)) return result;
        var pending = targets.ToHashSet();
        int radius=RailMod.Settings.stationAccessRadius;
        float factor=pawn.kindDef.moveSpeedFactorByTerrainTag.Values.DefaultIfEmpty(1f).Max();
        float minimum=Math.Max(1,Math.Min(pawn.TicksPerMoveCardinal,pawn.TicksPerMoveDiagonal/1.414214f)/Math.Max(1,factor)*(pawn.CurJob.locomotionUrgency==LocomotionUrgency.Sprint?.75f:1));
        float Heuristic(IntVec3 cell)
        {
            float nearest=float.MaxValue;
            foreach(var goal in pending){int dx=Math.Abs(goal.x-cell.x),dz=Math.Abs(goal.z-cell.z);nearest=Math.Min(nearest,(dx+dz-.585786f*Math.Min(dx,dz))*minimum);}
            return pending.Count==0?0:nearest;
        }
        var queue = new PriorityQueue<IntVec3, float>(); result[root] = 0; queue.Enqueue(root, 0);
        var expanded=new HashSet<IntVec3>();
        while (queue.TryDequeue(out var current, out float priority)&&expanded.Count<8192)
        {
            if(expanded.Contains(current))continue;
            float cost=result[current],adjusted=cost+Heuristic(current);
            if(priority+.01f<adjusted){queue.Enqueue(current,adjusted);continue;}
            expanded.Add(current);
            AccessExpanded++;
            pending.Remove(current);
            if (pending.Count == 0) break;
            foreach (var direction in GenAdj.AdjacentCells)
            {
                var next = current + direction;
                if(next.DistanceToSquared(root)>radius*radius||!safe(next))continue;
                if(direction.x!=0&&direction.z!=0&&(!safe(new(current.x,0,next.z))||!safe(new(next.x,0,current.z))))continue;
                float candidate = cost + (reverse ? WalkingCost(pawn, next, current) : WalkingCost(pawn, current, next));
                if (result.TryGetValue(next, out var old) && old <= candidate) continue;
                result[next] = candidate; queue.Enqueue(next, candidate+Heuristic(next));
            }
        }
        return result;
    }
    private float WalkingCost(Pawn pawn, IntVec3 from, IntVec3 to)
    {
        float value = from.x != to.x && from.z != to.z ? pawn.TicksPerMoveDiagonal : pawn.TicksPerMoveCardinal;
        value += map.pathing.For(pawn).pathGrid.CalculatedCostAt(to, false, from,
            Pawn_PathFollower.GetPawnCellBaseCostOverride(pawn, to));
        if (to.GetEdifice(map) is Building building) value += building.PathWalkCostFor(pawn);
        var terrain = to.GetTerrain(map);
        if (terrain.tags != null)
            foreach (var tag in terrain.tags)
                if (pawn.kindDef.moveSpeedFactorByTerrainTag.TryGetValue(tag, out var factor)) value /= factor;
        value = Math.Min(450, value);
        value = pawn.CurJob.locomotionUrgency switch
        {
            LocomotionUrgency.Amble => Math.Max(60, value * 3),
            LocomotionUrgency.Walk => Math.Max(50, value * 2),
            LocomotionUrgency.Sprint => Mathf.Round(value * .75f),
            _ => value
        };
        return Math.Max(1, value);
    }
    private float RemainingWalkingCost(Pawn pawn, PawnPath path)
    {
        float cost = 0; var current = pawn.Position;
        for (int i = Math.Min(path.NodesLeftCount - 1, path.NodesReversed.Count - 1); i >= 0; i--)
        {
            var next = path.NodesReversed[i];
            if (next != current) cost += WalkingCost(pawn, current, next);
            current = next;
        }
        return cost;
    }
    public bool BeforePatherTick(Pawn pawn, Pawn_PathFollower pather, PathEndMode mode)
    {
        var trip = Trip(pawn);
        if (trip != null)
        {
            if (!SameJob(trip)) { RemoveTrip(trip); return true; }
            if (!Eligible(pawn) || !RailMod.Settings.automatic) { Resume(trip, "interrupted"); return true; }
            if (trip.recoverAfterLoad) { Resume(trip, "load recovery"); return true; }
            var service = Service(trip.serviceId);
            if (service?.CanRetainPassengers != true || trip.end?.Spawned != true || trip.end.Map != map)
            { Resume(trip, "service unavailable"); return true; }
            if (trip.phase == TransitPhase.Approaching)
            {
                if (trip.start?.Spawned != true || GenTicks.TicksGame - trip.startedAt > service.WaitingLimit)
                    Resume(trip, "approach unavailable");
                return true;
            }
            if(trip.phase==TransitPhase.Riding&&service.fault=="LR_DoorWaiting")return false;
            if (!SafeRail(pawn, pawn.Position) || pawn.stances.FullBodyBusy)
            { Resume(trip, "passenger unavailable"); return true; }
            if (trip.phase == TransitPhase.Waiting && GenTicks.TicksGame - trip.startedAt > service.WaitingLimit)
            { Resume(trip, "waiting timed out"); return true; }
            // Passenger movement is driven once by the existing train, never by this pawn.
            return false;
        }
        if (internalCall > 0 || !RailMod.Settings.automatic || services.Count == 0 || !Eligible(pawn) ||
            !pather.Moving || pather.curPath?.Found != true || !DriverAccess.IsTravelToil(pawn)) return true;
        if (lastChecks.TryGetValue(pawn, out var checkedAt) && checkedAt.job == pawn.CurJob.loadID &&
            GenTicks.TicksGame - checkedAt.tick < 120) return true;
        if (pawn.Position.DistanceTo(pather.Destination.Cell) < RailMod.Settings.minDistance) return true;
        if (!planningBudget.TryTake(GenTicks.TicksGame)) return true;
        lastChecks[pawn] = (pawn.CurJob.loadID, GenTicks.TicksGame);
        PlanAttempts++;
        TryPlan(pawn, pather, mode);
        return Trip(pawn)?.phase is null or TransitPhase.Approaching;
    }
    private void TryPlan(Pawn pawn, Pawn_PathFollower pather, PathEndMode mode,bool commit=true)
    {
        var report=new RailPlanReport{tick=GenTicks.TicksGame,reason="LR_ReasonNoStations"};planReports[pawn]=report;
        var starts = NearbyStations(pawn, pawn.Position);
        var endpoint = pather.curPath.LastNode;
        var ends = NearbyStations(pawn, endpoint);
        report.starts=starts.Count;report.ends=ends.Count;
        if (starts.Count == 0 || ends.Count == 0) return;
        starts.RemoveAll(s => !services.Any(r => r.CanRetainPassengers&&!r.freightOnly&&r.stops.Contains(s)));
        ends.RemoveAll(s => !services.Any(r => r.CanRetainPassengers&&!r.freightOnly&&r.stops.Contains(s)));
        report.reason="LR_ReasonNoService";
        if (starts.Count == 0 || ends.Count == 0) return;
        var safety = new Dictionary<IntVec3, bool>();
        bool Safe(IntVec3 cell)
        {
            if (!safety.TryGetValue(cell, out bool value)) safety[cell] = value = SafeCell(pawn, cell);
            return value;
        }
        var servedPlatforms=services.Where(r=>r.CanRetainPassengers&&!r.freightOnly).SelectMany(r=>Enumerable.Range(0,r.stops.Count).Select(r.Stop)).Distinct().ToList();
        var access = AccessCosts(pawn, pawn.Position, servedPlatforms.Where(s=>starts.Contains(s.Station)).Select(s=>s.Cell), Safe);
        var egress = AccessCosts(pawn, endpoint, servedPlatforms.Where(s=>ends.Contains(s.Station)).Select(s=>s.Cell), Safe, true);
        float direct = RemainingWalkingCost(pawn, pather.curPath), best = float.MaxValue;
        report.direct=direct;report.reason="LR_ReasonAccess";
        Building_RailStation bestStart = null, bestEnd = null;
        RailServiceState bestService = null;
        int bestStartPlatform=0,bestEndPlatform=0;
        var usability = new Dictionary<RailCell, bool>();
        bool Usable(RailCell c)
        {
            if (!usability.TryGetValue(c, out bool value)) usability[c] = value = SafeRail(pawn, Vec(c));
            return value;
        }
        foreach (var service in services)
        {
        if (!service.CanRetainPassengers||service.freightOnly) continue;
        var dwell=Enumerable.Range(0,service.stops.Count).Select(s=>service.Stop(s).Settings.Estimate).ToArray();
        for(int startStop=0;startStop<service.stops.Count;startStop++)
        for(int endStop=0;endStop<service.stops.Count;endStop++)
        {
            var start=service.stops[startStop];var end=service.stops[endStop];
            var startCell=service.Stop(startStop).Cell;var endCell=service.Stop(endStop).Cell;
            if (!starts.Contains(start) || !ends.Contains(end)) continue;
            if (startStop == endStop || !access.TryGetValue(startCell, out float a) || !egress.TryGetValue(endCell, out float b)) continue;
            int first=service.stopIndices[startStop],last=service.stopIndices[endStop];
            int direction=service.loop?service.direction:Math.Sign(last-first);
            bool safeRoute=true;
            int i=first;
            for(int count=0;count<service.path.Count;count++)
            {
                if(!Usable(Cell(service.path[i]))) {safeRoute=false;break;}
                if(i==last)break;
                int next=(i+direction+service.path.Count)%service.path.Count;
                var aCell=service.path[i];var bCell=service.path[next];
                if(aCell.x!=bCell.x&&aCell.z!=bCell.z&&(!Safe(new(aCell.x,0,bCell.z))||!Safe(new(bCell.x,0,aCell.z)))){safeRoute=false;break;}
                i=next;
            }
            if(!safeRoute){report.reason="LR_ReasonUnsafe";continue;}
            int travel=RailTimetable.Travel(service.cumulative,service.stopIndices,dwell,startStop,endStop,direction,service.loop,service.CircuitTicks);
            int wait=RailTimetable.Wait(service.cumulative,service.stopIndices,dwell,
                service.index,service.direction,service.dwell,startStop,endStop,service.loop,service.CircuitTicks);
            if(!RailTimetable.Prefer(direct,a,b,travel,wait,RailMod.Settings.preferRail))
            {report.reason="LR_ReasonCost";if(report.line==null){report.line=service.Name;report.access=a;report.egress=b;report.travel=travel;report.wait=wait;}continue;}
            float total=a+b+travel+(float)wait+30;
            if (total >= best) continue;
            best = total; bestStart = start; bestEnd = end; bestService = service;
            bestStartPlatform=service.Platform(startStop);bestEndPlatform=service.Platform(endStop);
        }
        }
        if (bestService == null)
        {
            Diagnostic($"{pawn.LabelShort}: no suitable service, stations {starts.Count}/{ends.Count}, access {access.Count}/{egress.Count}, direct {direct:0}.");
            return;
        }
        report.reason="LR_ReasonSelected";report.line=bestService.Name;
        int selectedFrom=bestService.FindStop(bestStart,bestStartPlatform),selectedTo=bestService.FindStop(bestEnd,bestEndPlatform);
        var selectedDwell=Enumerable.Range(0,bestService.stops.Count).Select(s=>bestService.Stop(s).Settings.Estimate).ToArray();
        report.access=access[bestStart.PlatformCell(bestStartPlatform)];report.egress=egress[bestEnd.PlatformCell(bestEndPlatform)];
        report.travel=RailTimetable.Travel(bestService.cumulative,bestService.stopIndices,selectedDwell,selectedFrom,selectedTo,bestService.loop?bestService.direction:Math.Sign(selectedTo-selectedFrom),bestService.loop,bestService.CircuitTicks);
        report.wait=RailTimetable.Wait(bestService.cumulative,bestService.stopIndices,selectedDwell,bestService.index,bestService.direction,bestService.dwell,selectedFrom,selectedTo,bestService.loop,bestService.CircuitTicks);
        if(!commit)return;
        var trip = new TransitState
        {
            pawn = pawn, originalTarget = pather.Destination, originalMode = mode,
            jobId = pawn.CurJob.loadID, toilIndex = pawn.jobs.curDriver.CurToilIndex,
            start = bestStart, end = bestEnd, startPlatform=bestStartPlatform,endPlatform=bestEndPlatform,
            route = null, startedAt = GenTicks.TicksGame, serviceId = bestService.id
        };
        trips.Add(trip); tripsByPawn[pawn] = trip;
        Assigned(bestService.id).Add(trip);
        Diagnostic($"{pawn.LabelShort}: planned {bestService.path.Count - 1} rail cells, estimated {best:0}/{direct:0} ticks.");
        Internal(() => { pather.StopDead(); pather.StartPath(bestStart.PlatformCell(bestStartPlatform), PathEndMode.OnCell); });
    }
    public bool HandleArrival(Pawn pawn)
    {
        var trip = Trip(pawn);
        if (trip == null || trip.phase != TransitPhase.Approaching || internalCall > 1) return false;
        if (!SameJob(trip)) { RemoveTrip(trip); return false; }
        if (trip.start?.Spawned != true || pawn.Position != trip.BoardingCell || !SafeRail(pawn, pawn.Position))
        { Resume(trip, "boarding unavailable"); return true; }
        Internal(pawn.pather.StopDead);
        trip.phase = TransitPhase.Waiting; trip.startedAt = GenTicks.TicksGame;
        trip.queueOrder = ++nextQueueOrder;
        Diagnostic($"{pawn.LabelShort}: waiting for existing service {trip.serviceId}.");
        return true; // Never notify the original Job that the access station was its target.
    }
    public bool HandleFailure(Pawn pawn)
    {
        var trip = Trip(pawn);
        if (trip == null || trip.phase != TransitPhase.Approaching) return false;
        Resume(trip, "approach failed"); return true;
    }
    public void ExternalStop(Pawn pawn)
    {
        if (internalCall == 0) { var trip = Trip(pawn); if (trip != null) RemoveTrip(trip); }
    }
    private void Resume(TransitState trip, string reason)
    {
        RemoveTrip(trip);
        var pawn = trip.pawn;
        if (!SameJob(trip) || pawn.Map != map) return;
        lastChecks[pawn] = (trip.jobId, GenTicks.TicksGame);
        Diagnostic($"{pawn.LabelShort}: {reason}, resuming original target.");
        Internal(() =>
        {
            pawn.pather.StopDead();
            if (!pawn.Dead && !pawn.Downed && !pawn.Drafted && !pawn.InMentalState &&
                trip.originalTarget.IsValid && (!trip.originalTarget.HasThing || trip.originalTarget.Thing.Spawned && trip.originalTarget.Thing.Map == map))
                pawn.pather.StartPath(trip.originalTarget, trip.originalMode);
        });
    }
    private void Internal(Action action) { internalCall++; try { action(); } finally { internalCall--; } }
    private static void Diagnostic(string message) { if (RailMod.Settings.diagnostics) Log.Message("[LocalRail] " + message); }
    private List<Building_RailStation> NearbyStations(Pawn pawn, IntVec3 root)
    {
        var result = new List<Building_RailStation>();
        var (x, z) = Bucket(root);
        int radius=RailMod.Settings.stationAccessRadius,buckets=(radius+23)/24+1;
        for (int dx = -buckets; dx <= buckets; dx++)
        for (int dz = -buckets; dz <= buckets; dz++)
            if (stationBuckets.TryGetValue((x + dx, z + dz), out var list))
                foreach (var station in list)
                    if (Enumerable.Range(0,station.PlatformCount).Any(p=>station.PlatformCell(p).DistanceToSquared(root)<=radius*radius&&SafeRail(pawn,station.PlatformCell(p)))) result.Add(station);
        result.Sort((a, b) => a.Position.DistanceToSquared(root).CompareTo(b.Position.DistanceToSquared(root)));
        if (result.Count > 8) result.RemoveRange(8, result.Count - 8);
        return result;
    }
    public override void MapComponentUpdate()
    {
        if (renderTick != GenTicks.TicksGame) { renderTick = GenTicks.TicksGame; renderFraction = 0; }
        else if (!Find.TickManager.Paused)
            renderFraction = Math.Min(1, renderFraction + Time.deltaTime * 60 * Find.TickManager.TickRateMultiplier);
    }
    internal bool TryVisualPosition(Pawn pawn, out Vector3 position)
    {
        position = default;
        var trip = Trip(pawn);
        if (trip == null || trip.phase != TransitPhase.Riding || !SameJob(trip) || Service(trip.serviceId) is not { train.Spawned: true } service) return false;
        position = TrainVisualPosition(service);
        var seat = new Vector3(trip.seat % 2 == 0 ? -.45f : .45f, 0, trip.seat < 2 ? -.32f : .32f);
        position += Quaternion.Euler(0, TrainAngle(service), 0) * seat;
        return true;
    }
}

