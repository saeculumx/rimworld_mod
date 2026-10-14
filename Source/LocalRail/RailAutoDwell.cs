using Verse;
namespace LocalRail;

public partial class RailMapComponent
{
    private readonly Dictionary<Pawn,(int service,IntVec3 cell)> loadingWorkers=new();
    internal void RegisterLoader(Pawn pawn,int service,IntVec3 cell)=>loadingWorkers[pawn]=(service,cell);
    internal void UnregisterLoader(Pawn pawn)=>loadingWorkers.Remove(pawn);
    internal void NotifyDwellChanged(Building_RailStation station,int platform)
    {
        foreach(var service in services)
        {
            int stop=service.StopAt(service.index);
            if(service.dwell<=0||stop<0||service.stops[stop]!=station||service.Platform(stop)!=platform)continue;
            var settings=station.PlatformSettings(platform);
            service.dwell=Math.Max(1,(settings.automatic?settings.minimumTicks:settings.manualTicks)-service.dwellElapsed);
        }
    }
    private void RebuildLoadingWorkers()
    {
        loadingWorkers.Clear();
        foreach(var pawn in map.mapPawns.FreeColonistsSpawned)
            if(pawn.jobs.curDriver is JobDriver_LoadFreight driver)RegisterLoader(pawn,driver.ServiceId,driver.OriginCell);
    }
    private bool PendingAtStop(RailServiceState service,int index)
    {
        var stop=service.Stop(index);
        if(RiderCount(service)<RailServicePolicy.Capacity)
            foreach(var trip in Assigned(service.id))
                if(trip.start==stop.Station&&trip.startPlatform==stop.Platform&&trip.phase is TransitPhase.Approaching or TransitPhase.Waiting&&
                    service.HeadsTo(index,service.FindStop(trip.end,trip.endPlatform))&&SameJob(trip)&&Eligible(trip.pawn)&&trip.pawn.Position.DistanceToSquared(stop.Cell)<=RailMod.Settings.stationAccessRadius*RailMod.Settings.stationAccessRadius)return true;
        int batches=freight.Count(f=>f.train==service.train);
        if(freight.Any(f=>f.train==service.train&&f.destination==stop.Station&&f.destinationPlatform==stop.Platform&&f.blockedVisit!=service.dwellVisit))return true;
        if(batches>=FreightCapacity)return false;
        if(service.trainKind==RailTrainKind.Automatic&&stop.Station is Building_RailWarehouse warehouse&&warehouse.ReadyCargo(service,stop.Platform).item!=null)return true;
        if(freight.Any(f=>f.serviceId==service.id&&f.train==null&&f.origin==stop.Station&&f.originPlatform==stop.Platform&&
            service.HeadsTo(index,service.FindStop(f.destination,f.destinationPlatform))))return true;
        foreach(var (worker,order) in loadingWorkers)
            if(order.service==service.id&&order.cell==stop.Cell&&worker.Spawned&&worker.Map==map&&!worker.Drafted&&!worker.Downed&&
                worker.CurJob?.def.defName=="LR_LoadFreight"&&worker.Position.DistanceToSquared(stop.Cell)<=12*12)return true;
        return false;
    }
}
