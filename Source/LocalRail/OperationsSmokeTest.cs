using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace LocalRail;

public class OperationsSmokeTest : MapComponent
{
    internal static bool Load=>GenCommandLine.CommandLineArgPassed("localrail-operations-load-smoke");
    internal static bool Enabled=>(Load||GenCommandLine.CommandLineArgPassed("localrail-operations-smoke"))&&
        GenCommandLine.TryGetCommandLineArg("savedatafolder",out string path)&&Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))=="LocalRailTestOperations";
    private bool Visual=>GenCommandLine.CommandLineArgPassed("localrail-visual-smoke");
    private RailMapComponent Rail=>map.GetComponent<RailMapComponent>();
    private RailServiceState Route=>Rail.Service(serviceId);
    private Building_RailWarehouse hub,destination;
    private Zone_Stockpile ordinary;
    private Thing steel,cloth;
    private Pawn rider,worker;
    private int phase,started,serviceId,savedIndex,trainId,originalJob,frames;
    private bool sawOutbound,sawReturn;
    private readonly List<string> passed=new();
    public OperationsSmokeTest(Map map):base(map){}
    public override void ExposeData()
    {
        Scribe_References.Look(ref hub,"operationsHub");Scribe_References.Look(ref destination,"operationsDestination");Scribe_References.Look(ref ordinary,"operationsOrdinary");
        Scribe_References.Look(ref steel,"operationsSteel");Scribe_References.Look(ref cloth,"operationsCloth");
        Scribe_References.Look(ref rider,"operationsRider");Scribe_References.Look(ref worker,"operationsWorker");
        Scribe_Values.Look(ref serviceId,"operationsService");Scribe_Values.Look(ref savedIndex,"operationsIndex");Scribe_Values.Look(ref trainId,"operationsTrain");
        if(Scribe.mode==LoadSaveMode.PostLoadInit&&Enabled){phase=100;started=GenTicks.TicksGame;}
    }
    private void Check(bool value,string text){if(!value)throw new Exception(text);passed.Add(text);Log.Message("[RailOperationsSmoke] PASS "+text);}
    private void Phase(int next){phase=next;started=GenTicks.TicksGame;frames=0;}
    private Building Spawn(string name,IntVec3 pos,Rot4? rot=null)
    {
        var def=DefDatabase<ThingDef>.GetNamed(name);var thing=(Building)ThingMaker.MakeThing(def,def.MadeFromStuff?ThingDefOf.Steel:null);
        thing.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(thing,pos,map,rot??Rot4.North);return thing;
    }
    private void Track(int x,int z){var cell=new IntVec3(x,0,z);if(!Rail.HasRail(cell))Spawn("LR_Track",cell);}
    private Thing Item(ThingDef def,int count,IntVec3 pos)
    {var t=ThingMaker.MakeThing(def);t.stackCount=count;GenSpawn.Spawn(t,pos,map);return t;}
    private Pawn Colonist(IntVec3 pos)
    {
        var p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,allowAddictions:false,
            validatorPreGear:p=>!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)));
        GenSpawn.Spawn(p,pos,map);return p;
    }
    private void Setup()
    {
        RailMod.Settings.automatic=true;RailMod.Settings.preferRail=true;RailMod.Settings.minDistance=25;
        foreach(var p in map.mapPawns.FreeColonistsSpawned){p.drafter.Drafted=true;p.Position=new(25,0,145);p.pather.ResetToCurrentPosition();}
        foreach(var cell in new CellRect(25,85,110,65))
        {
            map.roofGrid.SetRoof(cell,null);
            foreach(var t in cell.GetThingList(map).Where(t=>t is Building or Plant||t.def.category==ThingCategory.Item).ToArray())if(t.def.destroyable)t.Destroy();else t.DeSpawn();
            map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.fogGrid.Unfog(cell);
        }
        int column=40;
        foreach(string name in new[]{"LR_PlatformStation","LR_TwoPlatformStation","LR_FourPlatformStation","LR_SinglePlatformWarehouse","LR_FreightWarehouse","LR_FourPlatformWarehouse"})
        {
            var def=DefDatabase<ThingDef>.GetNamed(name);Check(def.rotatable&&def.defaultPlacingRot==Rot4.North,name+" supports native placement rotation");
            var station=(Building_RailStation)Spawn(name,new(column,0,96),Rot4.East);column+=14;
            Check(station.RailCells.All(c=>c.GetEdifice(map)==station)&&station.RailCells.Select(c=>c.x).Distinct().Count()==station.PlatformCount,name+" rotated tracks match native occupied footprint");
            if(station is Building_RailWarehouse warehouse)Check(warehouse.AllSlotCellsList().All(c=>!Rail.HasRail(c)&&c.GetSlotGroup(map)?.parent==warehouse),name+" rotated storage avoids all platform rails");
        }
        hub=(Building_RailWarehouse)Spawn("LR_FourPlatformWarehouse",new(60,0,130));hub.RenamableLabel="Turnback hub";
        destination=(Building_RailWarehouse)Spawn("LR_SinglePlatformWarehouse",new(110,0,133),Rot4.East);destination.RenamableLabel="North-south depot";
        for(int x=45;x<=107;x++)Track(x,133);for(int x=45;x<=57;x++)Track(x,129);for(int z=129;z<=133;z++)Track(45,z);
        for(int z=134;z<=137;z++)Track(107,z);for(int x=108;x<=110;x++)Track(x,137);for(int z=135;z<=136;z++)Track(110,z);
        Check(Rail.PortConnected(new(destination),-1)&&!Rail.PortConnected(new(destination),1),"rotated depot computes the north connected and south free rail portal");
        var stops=new List<RailStop>{new(hub,3),new(hub,1),new(destination)};
        foreach(var s in stops)s.Station.SetPlatformSettings(s.Platform,new(){automatic=true,minimumTicks=120,maximumTicks=600});
        destination.SetPlatformSettings(0,new(){automatic=true,minimumTicks=480,maximumTicks=1200});
        Check(Rail.ConfigureService(stops,false,false,null,"A4 → A2 → B1 turnback",0,out var reason),"same-building platforms and reused trunk create a shuttle: "+reason);
        serviceId=Rail.Services[^1].id;
        Check(Route.path.Count>Route.path.Distinct().Count()&&Route.stopIndices.SequenceEqual(Route.stopIndices.Order()),"shuttle stores distinct ordered visits to repeated physical cells");
        Check(!Rail.ConfigureService(new(){new(hub,1),new(destination)},false,false,null,"conflict",1,out _),"another route still cannot occupy this shuttle's reserved track");
        Check(destination.GetStoreSettings().Priority==StoragePriority.Low,"receiving depot defaults to low priority");
        hub.orders.Add(new(){serviceId=serviceId,originPlatform=3,destination=destination,itemDef=ThingDefOf.Steel});
        hub.orders.Add(new(){serviceId=serviceId,originPlatform=3,destination=destination,itemDef=ThingDefOf.Cloth});hub.SynchronizePriority();
        Check(hub.GetStoreSettings().Priority==StoragePriority.Preferred&&hub.autoManagePriority,"dispatch depot automatically gathers at preferred priority");
        var probe=(Building_RailWarehouse)Rail.Stations.First(s=>s.def.defName=="LR_FreightWarehouse"&&s!=hub);
        probe.GetStoreSettings().Priority=StoragePriority.Important;Check(!probe.autoManagePriority,"manual native priority selection disables automatic management");
        probe.autoManagePriority=true;probe.SynchronizePriority();Check(probe.GetStoreSettings().Priority==StoragePriority.Low,"explicit automatic management restores receiving priority");
        steel=Item(ThingDefOf.Steel,75,hub.AllSlotCellsList()[0]);cloth=Item(ThingDefOf.Cloth,35,hub.AllSlotCellsList()[1]);
        ordinary=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,map.zoneManager);map.zoneManager.RegisterZone(ordinary);
        foreach(var cell in new CellRect(117,130,5,6))ordinary.AddCell(cell);
        ordinary.settings.filter.SetDisallowAll();ordinary.settings.filter.SetAllow(ThingDefOf.Steel,true);ordinary.settings.filter.SetAllow(ThingDefOf.Cloth,true);ordinary.settings.Priority=StoragePriority.Normal;
        worker=Colonist(new(113,0,136));worker.drafter.Drafted=true;
        rider=Colonist(new(63,0,135));Check(rider.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(113,0,135))),"native outbound journey starts without requesting a train");originalJob=rider.CurJob.loadID;Phase(10);
    }
    public override void MapComponentUpdate()
    {
        if(!Enabled||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||phase>=200)return;
        try
        {
            if(phase==0)Setup();
            if(phase==22)
            {
                Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                if(frames==0){Find.Selector.ClearSelection();Find.Selector.Select(Route.train);Find.CameraDriver.SetRootPosAndSize(Route.train.DrawPos,7);}
                if(frames==25)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-visible-cargo.png"));
                if(frames==50){Find.Selector.ClearSelection();Find.CameraDriver.SetRootPosAndSize(new(80,0,101),24);}
                if(frames==75)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-rotated-stations.png"));
                if(frames++>=95)Phase(30);return;
            }
            Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
        }
        catch(Exception ex){Fail(ex);}
    }
    public override void MapComponentDraw()
    {
        if(!Enabled||!Visual||phase!=22||frames<50)return;
        int x=40;
        foreach(string name in new[]{"LR_PlatformStation","LR_TwoPlatformStation","LR_FourPlatformStation","LR_SinglePlatformWarehouse","LR_FreightWarehouse","LR_FourPlatformWarehouse"})
        {GhostDrawer.DrawGhostThing(new(x,0,110),Rot4.East,DefDatabase<ThingDef>.GetNamed(name),null,Color.green,AltitudeLayer.Blueprint);x+=14;}
    }
    public override void MapComponentTick()
    {
        if(!Enabled||phase==0||phase>=200||phase==22)return;
        try
        {
            if(GenTicks.TicksGame-started>14000)throw new Exception($"Timeout phase {phase}, index={Route.index}, cargo={Rail.Freight.Count}, outbound={sawOutbound}, return={sawReturn}");
            foreach(var p in new[]{rider,worker})if(p?.needs!=null){if(p.needs.food!=null)p.needs.food.CurLevelPercentage=1;if(p.needs.rest!=null)p.needs.rest.CurLevelPercentage=1;if(p.needs.mood!=null)p.needs.mood.CurLevelPercentage=1;}
            if(!Load&&Rail.ActiveTrip(rider)?.phase==TransitPhase.Riding)
            {
                if(phase<50&&!sawOutbound){Check(rider.CurJob.loadID==originalJob&&Rail.ActiveTrip(rider).end==destination,"automatic outbound rider boards turnback shuttle with original job");sawOutbound=true;}
                if(phase>=50&&!sawReturn&&rider.CurJob.def==JobDefOf.Goto)
                {Check(Rail.ActiveTrip(rider).start==destination&&Rail.ActiveTrip(rider).end==hub,"automatic return rider boards opposite shuttle direction");sawReturn=true;}
            }
            switch(phase)
            {
                case 10:
                    if(Rail.Freight.Count(f=>f.train==Route.train)!=2)break;
                    Check(Rail.CargoVisuals(Route).Count==2&&Rail.TrainCargoSummary(Route).Contains("75")&&Rail.TrainCargoSummary(Route).Contains("35"),"vehicle cargo icons and summary represent actual steel and cloth");Phase(20);break;
                case 20:
                    if(Route.index<=Route.stopIndices[1]+8||Route.path.IndexOf(Route.train.Position)==Route.index)break;
                    savedIndex=Route.index;trainId=Route.train.thingIDNumber;Spawn("LR_Track",new(29,0,119));Phase(21);break;
                case 21:
                    Check(Route.index>=savedIndex&&Route.index<=savedIndex+2&&Route.fault==null,"topology refresh retains second physical visit instead of jumping to first");
                    savedIndex=Route.index;GameDataSaveLoader.SaveGame("LocalRailOperationsInTransit");
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame("LocalRailOperationsInTransit")),"save records rotated depot, two cargo stacks and repeated itinerary index");Phase(Visual?22:30);break;
                case 30:
                    if(!destination.Inventory.Contains(steel)||!destination.Inventory.Contains(cloth)||rider.Position!=new IntVec3(113,0,135))break;
                    rider.drafter.Drafted=true;
                    Check(sawOutbound&&steel.stackCount==75&&cloth.stackCount==35,"turnback shuttle unloads both original stacks into rotated warehouse and delivers rider");
                    Check(Rail.CargoVisuals(Route).Count==0&&Rail.TrainCargoSummary(Route)==null,"vehicle display clears when the actual cargo has unloaded");
                    destination.orders.Add(new(){serviceId=serviceId,originPlatform=0,destination=hub,destinationPlatform=3,itemDef=ThingDefOf.Cloth});destination.SynchronizePriority();
                    Check(destination.GetStoreSettings().Priority==StoragePriority.Preferred&&StoreUtility.CurrentStoragePriorityOf(steel)==StoragePriority.Low,"mixed dispatch/receiving depot still treats delivered steel as low priority");
                    worker.drafter.Drafted=false;var job=new WorkGiver_HaulGeneral().JobOnThing(worker,steel);
                    Check(job?.def==JobDefOf.HaulToCell&&job.targetB.Cell.GetSlotGroup(map)?.parent==ordinary,"native hauling chooses ordinary stockpile and excludes preferred source depots");
                    Check(worker.jobs.TryTakeOrderedJob(job),"native delivered-resource hauling job starts");Phase(40);break;
                case 40:
                    if(steel.GetSlotGroup()?.parent!=ordinary||worker.carryTracker.CarriedThing!=null)break;
                    worker.drafter.Drafted=true;
                    Check(steel.stackCount==75&&Rail.WasDelivered(steel),"colonist unloads original steel into normal colony storage with delivery marker intact");
                    var split=steel.SplitOff(10);Check(Rail.WasDelivered(split),"native partial stack split preserves delivery protection");
                    var merge=Item(ThingDefOf.Steel,20,new(120,0,135));GenPlace.TryPlaceThing(split,merge.Position,map,ThingPlaceMode.Direct);
                    Check(merge.stackCount==30&&Rail.WasDelivered(merge),"native stack merge propagates delivery protection to receiving stack");
                    rider.drafter.Drafted=false;Check(rider.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(63,0,135))),"native return journey starts without summoning a train");Phase(50);break;
                case 50:
                    if(rider.Position!=new IntVec3(63,0,135)||!sawReturn)break;
                    rider.drafter.Drafted=true;
                    Check(Route.operating&&Route.fault==null&&Rail.Reversals>0,"return rider arrives while permanent turnback train continues autonomous service");
                    Check(ordinary.slotGroup.HeldThings.Where(t=>t.def==ThingDefOf.Steel).Sum(t=>t.stackCount)==95&&destination.Inventory.Contains(cloth),"delivered stock does not return to source or enter received-cargo shipment loops");Finish();break;
                case 100:
                    Check(Route.train.thingIDNumber==trainId&&Route.index>=savedIndex&&Route.index<=savedIndex+2,"reload retains train identity and second itinerary visit");
                    Check(destination.Rotation==Rot4.East&&destination.RailCells.All(c=>c.x==110),"reload retains rotated warehouse and real north-south track");
                    Check(Rail.CargoVisuals(Route).Count==2&&Rail.Freight.Any(f=>f.contents.Contains(steel))&&steel.stackCount==75,"reload retains actual cargo and rebuilds vehicle display");
                    Check(hub.autoManagePriority&&hub.GetStoreSettings().Priority==StoragePriority.Preferred&&destination.GetStoreSettings().Priority==StoragePriority.Low,"reload retains receiving and dispatch priority roles");
                    rider.drafter.Drafted=true;Phase(110);break;
                case 110:
                    if(!destination.Inventory.Contains(steel)||!destination.Inventory.Contains(cloth))break;
                    Check(steel.stackCount==75&&cloth.stackCount==35&&Rail.WasDelivered(steel),"reloaded repeated itinerary completes unloading and registers delivery protection");
                    Check(Rail.CargoVisuals(Route).Count==0&&Route.fault==null,"reloaded autonomous train continues with cleared cargo display");Finish();break;
            }
        }
        catch(Exception ex){Fail(ex);}
    }
    private void Finish(){phase=200;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=true,tests=passed.Count,passed,Rail.Reversals,Rail.PeakPlansPerTick}));Root.Shutdown();}
    private void Fail(Exception ex){phase=201;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=false,passed,error=ex.ToString()}));Log.Error("[RailOperationsSmoke] FAILED "+ex);Root.Shutdown();}
}
