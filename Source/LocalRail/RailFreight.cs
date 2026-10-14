using RimWorld;
using Verse;
using Verse.AI;
namespace LocalRail;

public class RailFreight : IExposable, IThingHolder
{
    public ThingOwner<Thing> contents;
    public Building_RailStation origin,destination;
    public Building_RailTrain train;
    public int serviceId;
    public int originPlatform,destinationPlatform;
    public IntVec3 lastPosition;
    internal int blockedVisit=-1;
    public IThingHolder ParentHolder=>train?.Spawned==true?train:origin?.Spawned==true?origin:null;
    public RailFreight()=>contents=new(this,false,LookMode.Deep);
    public ThingOwner GetDirectlyHeldThings()=>contents;
    public void GetChildHolders(List<IThingHolder> children)=>ThingOwnerUtility.AppendThingHoldersFromThings(children,contents);
    public void ExposeData()
    {
        Scribe_References.Look(ref origin,"origin");Scribe_References.Look(ref destination,"destination");
        Scribe_References.Look(ref train,"train");Scribe_Values.Look(ref serviceId,"serviceId");
        Scribe_Values.Look(ref originPlatform,"originPlatform");Scribe_Values.Look(ref destinationPlatform,"destinationPlatform");
        Scribe_Values.Look(ref lastPosition,"lastPosition");Scribe_Deep.Look(ref contents,"contents",this);
    }
}

public partial class RailMapComponent
{
    internal const int FreightCapacity=12;
    private readonly Dictionary<(int service,int station,int platform),int> unloadWarnings=new();
    internal int UnloadWarningCount {get;private set;}
    private void WarnBlockedFreight(RailServiceState service,RailStop stop,RailFreight shipment)
    {
        shipment.blockedVisit=service.dwellVisit;
        var key=(service.id,stop.Station.thingIDNumber,stop.Platform);
        if(unloadWarnings.TryGetValue(key,out int last)&&GenTicks.TicksGame-last<60000)return;
        unloadWarnings[key]=GenTicks.TicksGame;UnloadWarningCount++;
        string text="LR_UnloadBlockedInfo".Translate(service.Name,stop.Label);
        Messages.Message(text,new LookTargets(stop.Station),MessageTypeDefOf.NeutralEvent,false);
        Log.Message("[LocalRail] INFO "+text);
    }
    private List<RailFreight> freight=new();
    internal IReadOnlyList<RailFreight> Freight=>freight;
    internal RailServiceState FreightService(Building_RailStation origin,Building_RailStation destination,int serviceId,int originPlatform,int destinationPlatform)
    {
        var candidates=serviceId!=0?services.Where(s=>s.id==serviceId):services;
        return candidates.FirstOrDefault(s=>s.FindStop(origin,originPlatform)>=0&&s.FindStop(destination,destinationPlatform)>=0);
    }
    internal bool CanQueueFreight(Building_RailStation origin,Building_RailStation destination,int serviceId=0,int originPlatform=0,int destinationPlatform=0)=>
        (origin!=destination||originPlatform!=destinationPlatform)&&FreightService(origin,destination,serviceId,originPlatform,destinationPlatform)?.Available==true&&
        freight.Count(f=>f.origin==origin&&f.originPlatform==originPlatform&&f.train==null)<16;
    internal void ShowFreightDestinations(Building_RailStation station)
    {
        var options=new List<FloatMenuOption>{new("LR_None".Translate(),()=>{station.freightDestination=null;station.freightServiceId=0;if(station is Building_RailWarehouse w)w.SynchronizePriority();})};
        foreach(var service in services)
        for(int from=0;from<service.stops.Count;from++)
        {
            if(service.stops[from]!=station)continue;
            for(int to=0;to<service.stops.Count;to++)
            {
                if(from==to)continue;var a=service.Stop(from);var b=service.Stop(to);int id=service.id;
                options.Add(new FloatMenuOption($"{service.Name}: {a.Label} → {b.Label}",()=>
                {station.freightServiceId=id;station.freightOriginPlatform=a.Platform;station.freightDestination=b.Station;station.freightDestinationPlatform=b.Platform;if(station is Building_RailWarehouse w)w.SynchronizePriority();}));
            }
        }
        Find.WindowStack.Add(new FloatMenu(options));
    }
    internal Thing FreightCandidate(Pawn pawn,Building_RailStation origin)
    {
        if(pawn.carryTracker.CarriedThing is { } carried && carried is not Pawn) return carried;
        return map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
            .Where(t=>t is not Pawn && t.def.EverHaulable && t.Position.DistanceToSquared(origin.Position)<=144 && !t.IsForbidden(pawn) &&
                pawn.CanReserveAndReach(t,PathEndMode.ClosestTouch,Danger.None))
            .OrderBy(t=>pawn.Position.DistanceToSquared(t.Position)).FirstOrDefault();
    }
    internal void OrderFreight(Pawn pawn,Thing item,Building_RailStation origin,Building_RailStation destination,int serviceId=0,int originPlatform=0,int destinationPlatform=0)
    {
        var service=FreightService(origin,destination,serviceId,originPlatform,destinationPlatform);if(service?.Available!=true)return;
        var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("LR_LoadFreight"),item,origin,destination);
        job.count=item.stackCount;
        job.targetQueueA=new(){service.train};job.targetQueueB=new(){origin.PlatformCell(originPlatform),destination.PlatformCell(destinationPlatform)};
        pawn.jobs.TryTakeOrderedJob(job);
    }
    internal bool EnqueueFreight(Pawn pawn,Building_RailStation origin,Building_RailStation destination,int serviceId,int originPlatform,int destinationPlatform)
    {
        var item=pawn.carryTracker.CarriedThing;
        if(item==null || item is Pawn || !CanQueueFreight(origin,destination,serviceId,originPlatform,destinationPlatform)) return false;
        var shipment=new RailFreight {origin=origin,destination=destination,serviceId=FreightService(origin,destination,serviceId,originPlatform,destinationPlatform).id,originPlatform=originPlatform,destinationPlatform=destinationPlatform,lastPosition=origin.PlatformCell(originPlatform)};
        int moved=pawn.carryTracker.innerContainer.TryTransferToContainer(item,shipment.contents,item.stackCount,false);
        if(moved<=0)return false;
        ForgetDelivery(item);freight.Add(shipment);return true;
    }
    internal string FreightInspect(Building_RailStation station)
    {
        int waiting=freight.Count(f=>f.origin==station&&f.originPlatform==station.freightOriginPlatform&&f.train==null);
        return "LR_FreightStatus".Translate(station.freightOriginPlatform+1,station.freightDestination?.PlatformLabel(station.freightDestinationPlatform)??"LR_None".Translate().ToString(),waiting);
    }
    internal void FreightChildren(Thing thing,List<IThingHolder> children)
    {
        foreach(var shipment in freight)
            if(shipment.ParentHolder==thing) children.Add(shipment);
    }
    private void ExchangeFreight(RailServiceState service)
    {
        int stopIndex=service.StopAt(service.index);if(stopIndex<0)return;
        if(GenTicks.TicksGame<service.nextFreightAt)return;
        var platform=service.Stop(stopIndex);var station=platform.Station;
        for(int i=freight.Count-1;i>=0;i--)
        {
            var shipment=freight[i];if(shipment.train!=service.train || shipment.destination!=station||shipment.destinationPlatform!=platform.Platform)continue;
            shipment.lastPosition=platform.Cell;
            if(shipment.blockedVisit==service.dwellVisit)continue;
            if(station is Building_RailWarehouse warehouse)
            {
                bool changed=warehouse.Receive(shipment);if(changed)FreightChanged();
                if(shipment.contents.Count==0)
                {freight.RemoveAt(i);service.nextFreightAt=GenTicks.TicksGame+30;service.quietTicks=0;return;}
                if(changed){service.nextFreightAt=GenTicks.TicksGame+30;service.quietTicks=0;return;}
                WarnBlockedFreight(service,platform,shipment);continue;
            }
            bool dropped=DropFreight(shipment,station.UnloadCell(platform.Platform),true);
            if(dropped)
            {freight.RemoveAt(i);service.nextFreightAt=GenTicks.TicksGame+30;service.quietTicks=0;return;}
            WarnBlockedFreight(service,platform,shipment);
        }
        int capacity=FreightCapacity-freight.Count(f=>f.train==service.train);
        foreach(var shipment in freight)
        {
            if(capacity<=0)break;
            if(shipment.serviceId!=service.id || shipment.train!=null || shipment.origin!=station||shipment.originPlatform!=platform.Platform)continue;
            int destination=service.FindStop(shipment.destination,shipment.destinationPlatform);
            if(!service.HeadsTo(stopIndex,destination))continue;
            shipment.train=service.train;shipment.lastPosition=service.train.Position;FreightChanged();
            service.nextFreightAt=GenTicks.TicksGame+30;service.quietTicks=0;return;
        }
        if(capacity>0&&service.trainKind==RailTrainKind.Automatic&&station is Building_RailWarehouse source)LoadWarehouseFreight(service,platform,source);
    }
    private void LoadWarehouseFreight(RailServiceState service,RailStop platform,Building_RailWarehouse warehouse)
    {
        if(warehouse.IsForbidden(Faction.OfPlayer))return;
        var (item,order)=warehouse.ReadyCargo(service,platform.Platform);
        if(item!=null)
        {
            var shipment=new RailFreight{origin=warehouse,destination=order.destination,originPlatform=platform.Platform,
                destinationPlatform=order.destinationPlatform,train=service.train,serviceId=service.id,lastPosition=platform.Cell};
            var origin=item.Position;var batch=item.SplitOff(Math.Min(item.stackCount,item.def.stackLimit));
            if(!shipment.contents.TryAdd(batch,false))
            {GenPlace.TryPlaceThing(batch,origin,map,ThingPlaceMode.Near);return;}
            ForgetDelivery(batch);freight.Add(shipment);FreightChanged();service.nextFreightAt=GenTicks.TicksGame+30;service.quietTicks=0;
        }
    }
    private bool DropFreight(RailFreight shipment,IntVec3 position,bool delivered=false)
    {
        if(!position.InBounds(map))position=shipment.lastPosition;
        int before=shipment.contents.TotalStackCount;
        bool dropped=shipment.contents.TryDropAll(position,map,ThingPlaceMode.Near,
            placeAction:delivered?(item,count)=>MarkDelivered(item):null,
            nearPlaceValidator:c=>c.InBounds(map)&&c.Standable(map)&&c.GetDoor(map)==null&&!HasRail(c)&&c.DistanceToSquared(shipment.lastPosition)<=25);
        if(dropped||shipment.contents.TotalStackCount!=before)FreightChanged();return dropped;
    }
    private void RecoverFreight()
    {
        foreach(var shipment in freight) shipment.contents.DoTick();
        if(GenTicks.TicksGame%60!=0)return;
        for(int i=freight.Count-1;i>=0;i--)
        {
            var shipment=freight[i];var service=Service(shipment.serviceId);
            if(shipment.contents.Count==0){freight.RemoveAt(i);FreightChanged();continue;}
            if(shipment.train?.Spawned==true)shipment.lastPosition=shipment.train.Position;
            bool invalid=service==null || shipment.origin?.Spawned!=true || shipment.destination?.Spawned!=true ||
                service.FindStop(shipment.origin,shipment.originPlatform)<0 || service.FindStop(shipment.destination,shipment.destinationPlatform)<0 ||
                shipment.train!=null && shipment.train.Spawned!=true;
            if(invalid && DropFreight(shipment,shipment.lastPosition+IntVec3.North*2)){freight.RemoveAt(i);FreightChanged();}
        }
    }
    private void RecoverInvalidFreight(RailServiceState service)
    {
        for(int i=freight.Count-1;i>=0;i--)
        {
            var shipment=freight[i];if(shipment.serviceId!=service.id)continue;
            if(service.FindStop(shipment.origin,shipment.originPlatform)>=0&&service.FindStop(shipment.destination,shipment.destinationPlatform)>=0)continue;
            if(shipment.train?.Spawned==true)shipment.lastPosition=shipment.train.Position;
            if(DropFreight(shipment,shipment.lastPosition+IntVec3.North)){freight.RemoveAt(i);FreightChanged();}
        }
    }
    private void ReleaseFreight(RailServiceState service)
    {
        for(int i=freight.Count-1;i>=0;i--)
            if(freight[i].serviceId==service.id)
            {
                var shipment=freight[i];
                if(shipment.train?.Spawned==true)shipment.lastPosition=shipment.train.Position;
                if(DropFreight(shipment,shipment.lastPosition+IntVec3.North*2)){freight.RemoveAt(i);FreightChanged();}
            }
    }
    internal void NotifyTrainLost(Building_RailTrain train)
    {
        for(int i=freight.Count-1;i>=0;i--)
            if(freight[i].train==train)
            {
                freight[i].lastPosition=train.Position;
                if(DropFreight(freight[i],train.Position+IntVec3.North*2)){freight.RemoveAt(i);FreightChanged();}
            }
    }
}

public class JobDriver_LoadFreight : JobDriver
{
    public IntVec3 OriginCell=>job.targetQueueB?.Count>0?job.targetQueueB[0].Cell:((Building_RailStation)TargetThingB).PlatformCell(0);
    private IntVec3 DestinationCell=>job.targetQueueB?.Count>1?job.targetQueueB[1].Cell:((Building_RailStation)TargetThingC).PlatformCell(0);
    public int ServiceId=>job.targetQueueA?.Count>0&&job.targetQueueA[0].Thing is Building_RailTrain train?
        pawn.Map.GetComponent<RailMapComponent>().TrainService(train)?.id??-1:
        pawn.Map.GetComponent<RailMapComponent>().FreightService((Building_RailStation)TargetThingB,(Building_RailStation)TargetThingC,0,OriginPlatform,DestinationPlatform)?.id??-1;
    private int OriginPlatform=>((Building_RailStation)TargetThingB).PlatformAt(OriginCell);
    private int DestinationPlatform=>((Building_RailStation)TargetThingC).PlatformAt(DestinationCell);
    public override bool TryMakePreToilReservations(bool errorOnFailed)=>
        !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling) &&
        (pawn.carryTracker.CarriedThing==TargetThingA || pawn.Reserve(TargetA,job,1,-1,null,errorOnFailed)) &&
        pawn.Reserve(OriginCell,job,1,-1,null,errorOnFailed);
    protected override IEnumerable<Toil> MakeNewToils()
    {
        var component=pawn.Map.GetComponent<RailMapComponent>();
        AddFinishAction(_=>component.UnregisterLoader(pawn));
        this.FailOnDestroyedOrNull(TargetIndex.B);this.FailOnDestroyedOrNull(TargetIndex.C);
        this.FailOn(()=> TargetThingB is not Building_RailStation origin || TargetThingC is not Building_RailStation destination ||
            !component.CanQueueFreight(origin,destination,ServiceId,OriginPlatform,DestinationPlatform));
        yield return Toils_General.Do(()=>component.RegisterLoader(pawn,ServiceId,OriginCell));
        if(pawn.carryTracker.CarriedThing!=TargetThingA)
        {
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
        }
        yield return Toils_Goto.GotoCell(OriginCell,PathEndMode.OnCell);
        yield return Toils_General.Wait(60,TargetIndex.B);
        yield return Toils_General.Do(()=>
        {
            if(!component.EnqueueFreight(pawn,(Building_RailStation)TargetThingB,(Building_RailStation)TargetThingC,ServiceId,OriginPlatform,DestinationPlatform))
                EndJobWith(JobCondition.Incompletable);
        });
    }
}
