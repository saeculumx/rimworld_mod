using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace LocalRail;

public class WarehouseSmokeTest : MapComponent
{
    internal static bool Load=>GenCommandLine.CommandLineArgPassed("localrail-warehouse-load-smoke");
    internal static bool Enabled=>(Load||GenCommandLine.CommandLineArgPassed("localrail-warehouse-smoke"))&&
        GenCommandLine.TryGetCommandLineArg("savedatafolder",out string path)&&Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))=="LocalRailTestWarehouse";
    private bool Visual=>GenCommandLine.CommandLineArgPassed("localrail-visual-smoke");
    private RailMapComponent Rail=>map.GetComponent<RailMapComponent>();
    private RailServiceState Freight=>Rail.Service(freightId);
    private RailServiceState Mixed=>Rail.Service(mixedId);
    private Building_RailWarehouse a,b,c;
    private List<Building_RailDoubleDoor> gates=new();
    private Building_RailDoor steelDoor,plasteelDoor;
    private Blueprint_Build doorPlan;
    private Frame doorFrame;
    private Pawn worker,rider;
    private Thing steel,cloth,silver,uranium,partial;
    private List<Thing> blockers=new();
    private int freightId,mixedId,phase,started,frames,trainId,originalJob;
    private bool mixedBoarded,gateOpened,gateClosed,crossedClosed;
    private readonly List<string> passed=new();
    public WarehouseSmokeTest(Map map):base(map){}
    public override void ExposeData()
    {
        Scribe_References.Look(ref a,"warehouseA");Scribe_References.Look(ref b,"warehouseB");Scribe_References.Look(ref c,"warehouseC");
        Scribe_References.Look(ref worker,"warehouseWorker");Scribe_References.Look(ref rider,"warehouseRider");
        Scribe_References.Look(ref steelDoor,"warehouseSteelDoor");Scribe_References.Look(ref plasteelDoor,"warehousePlasteelDoor");
        Scribe_References.Look(ref doorPlan,"warehouseDoorPlan");Scribe_References.Look(ref doorFrame,"warehouseDoorFrame");
        Scribe_References.Look(ref partial,"warehousePartial");Scribe_Collections.Look(ref gates,"warehouseGates",LookMode.Reference);
        Scribe_Collections.Look(ref blockers,"warehouseBlockers",LookMode.Reference);
        Scribe_Values.Look(ref freightId,"warehouseFreightId");Scribe_Values.Look(ref mixedId,"warehouseMixedId");Scribe_Values.Look(ref trainId,"warehouseTrainId");
        if(Scribe.mode==LoadSaveMode.PostLoadInit&&Enabled){phase=100;started=GenTicks.TicksGame;}
    }
    private void Check(bool value,string text){if(!value)throw new Exception(text);passed.Add(text);Log.Message("[RailWarehouseSmoke] PASS "+text);}
    private void Phase(int value){phase=value;started=GenTicks.TicksGame;}
    private Building Spawn(string name,IntVec3 cell,ThingDef stuff=null,Rot4? rotation=null)
    {
        var def=DefDatabase<ThingDef>.GetNamed(name);var result=(Building)ThingMaker.MakeThing(def,stuff??(def.MadeFromStuff?ThingDefOf.Steel:null));
        result.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(result,cell,map,rotation??Rot4.North);return result;
    }
    private Thing Item(ThingDef def,int count,IntVec3 cell)
    {var item=ThingMaker.MakeThing(def);item.stackCount=count;GenSpawn.Spawn(item,cell,map);return item;}
    private int Quantity(Building_RailWarehouse warehouse,ThingDef def)=>warehouse.Inventory.Where(t=>t.def==def).Sum(t=>t.stackCount);
    private Pawn Colonist(IntVec3 cell)
    {
        var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,allowAddictions:false,
            validatorPreGear:p=>!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)));
        GenSpawn.Spawn(pawn,cell,map);return pawn;
    }
    private void FullDestination(int firstCount=75)
    {
        blockers.Clear();int index=0;
        foreach(var cell in b.AllSlotCellsList())for(int stack=0;stack<3;stack++)blockers.Add(Item(ThingDefOf.Steel,index++==0?firstCount:75,cell));
    }
    private void Setup()
    {
        RailMod.Settings.automatic=true;RailMod.Settings.preferRail=true;RailMod.Settings.minDistance=25;
        foreach(var cell in map.AllCells)map.roofGrid.SetRoof(cell,null);
        foreach(var cell in new CellRect(25,55,135,55))
        {
            foreach(var thing in cell.GetThingList(map).Where(t=>t is Building or Plant||t.def.category==ThingCategory.Item).ToArray())if(thing.def.destroyable)thing.Destroy();else thing.DeSpawn();
            map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.fogGrid.Unfog(cell);
        }
        foreach(var pawn in map.mapPawns.FreeColonistsSpawned){pawn.drafter.Drafted=true;pawn.Position=new(30,0,105);pawn.pather.ResetToCurrentPosition();}
        foreach(var name in new[]{"LR_RailDoor","LR_DoubleRailDoor"})
        {
            var def=DefDatabase<ThingDef>.GetNamed(name);
            Check(def.designationCategory.defName=="Structure"&&def.designationCategory.ResolvedAllowedDesignators.OfType<Designator_Build>().Any(d=>d.PlacingDef==def),name+" appears in native Structure menu");
            Check(def.stuffCategories.Count==1&&def.stuffCategories[0].defName=="Metallic"&&def.costStuffCount==(name=="LR_RailDoor"?40:80),name+" uses Metallic stuff and intended material cost");
        }
        var metals=DefDatabase<ThingDef>.AllDefs.Where(d=>d.stuffProps?.categories.Any(s=>s.defName=="Metallic")==true).ToList();
        var specimens=new List<Building>();int x=30;
        foreach(var metal in metals){specimens.Add(Spawn("LR_RailDoor",new(x++,0,60),metal));}
        Check(specimens.All(d=>d.Stuff!=null&&d.MaxHitPoints>0),"all installed Metallic materials create native material-based doors");
        plasteelDoor=(Building_RailDoor)specimens.Single(d=>d.Stuff==ThingDefOf.Plasteel);steelDoor=(Building_RailDoor)specimens.Single(d=>d.Stuff==ThingDefOf.Steel);
        Check(plasteelDoor.MaxHitPoints==specimens.Max(d=>d.MaxHitPoints)&&plasteelDoor.MaxHitPoints>steelDoor.MaxHitPoints,"vanilla plasteel has highest metal door durability");
        steelDoor.HitPoints-=40;
        var planned=DefDatabase<ThingDef>.GetNamed("LR_RailDoor");
        doorPlan=GenConstruct.PlaceBlueprintForBuild(planned,new(30,0,64),map,Rot4.North,Faction.OfPlayer,ThingDefOf.Steel);
        doorFrame=(Frame)ThingMaker.MakeThing(planned.frameDef,ThingDefOf.Steel);doorFrame.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(doorFrame,new(33,0,64),map);doorFrame.workDone=20;
        Check(doorPlan.Spawned&&doorFrame.Spawned,"native door blueprint and incomplete frame save alongside built doors");
        Check(specimens.All(d=>d.GetStatValue(StatDefOf.DoorOpenSpeed)>0)&&specimens.Cast<Building_RailDoor>().Select(d=>d.TicksToOpenNow).Distinct().Count()>1,"native metal opening-speed factors are respected across available materials");
        a=(Building_RailWarehouse)Spawn("LR_FreightWarehouse",new(40,0,80));b=(Building_RailWarehouse)Spawn("LR_FreightWarehouse",new(90,0,80));c=(Building_RailWarehouse)Spawn("LR_FreightWarehouse",new(140,0,80));
        a.RenamableLabel="Source depot";b.RenamableLabel="Factory depot";c.RenamableLabel="Trade depot";
        var four=(Building_RailWarehouse)Spawn("LR_FourPlatformWarehouse",new(150,0,100));
        Check(a.AllSlotCellsList().Count==15&&four.AllSlotCellsList().Count==25&&four.PlatformCount==4,"two/four platform warehouses have separate native storage and track rows");
        Check(map.haulDestinationManager.AllGroups.Contains(a.GetSlotGroup())&&a.AllSlotCellsList().All(cell=>cell.GetSlotGroup(map)==a.GetSlotGroup()),"warehouse registers as native hauling destination with slot groups");
        Check(a.RailCells.All(cell=>cell.GetSlotGroup(map)==null),"rail rows are excluded from warehouse storage slots");
        for(int n=0;n<2;n++)foreach(var station in new[]{a,b,c})station.SetPlatformSettings(n,new(){automatic=true,minimumTicks=120,maximumTicks=360});
        b.GetStoreSettings().filter.SetDisallowAll();b.GetStoreSettings().filter.SetAllow(ThingDefOf.Steel,true);
        foreach(int gateX in new[]{65,110})gates.Add((Building_RailDoubleDoor)Spawn("LR_DoubleRailDoor",new(gateX,0,80),ThingDefOf.Steel,Rot4.East));
        for(int moduleX=43;moduleX<=136;moduleX+=3)
        {
            var rect=new CellRect(moduleX-1,79,3,3);
            if(rect.Cells.Any(cell=>cell.GetEdifice(map)!=null))continue;
            Spawn("LR_DoubleTrack",new(moduleX,0,80));
        }
        for(int railX=42;railX<=138;railX++)foreach(int z in new[]{79,81})if(!Rail.HasRail(new(railX,0,z)))Spawn("LR_Track",new(railX,0,z));
        var vertical=(Building_RailDoubleTrack)Spawn("LR_DoubleTrack",new(55,0,101),rotation:Rot4.East);
        Check(vertical.RailCells.Contains(new(54,0,100))&&vertical.RailCells.Contains(new(56,0,102)),"rotated double-track module creates north-south parallel rails");
        Spawn("LR_DoubleTrack",new(55,0,104),rotation:Rot4.East);
        Check(Rail.HasConnection(new(54,0,102),new(54,0,103)),"correctly aligned rotated double modules connect along rail axis");
        Spawn("LR_DoubleTrack",new(70,0,100));Spawn("LR_DoubleTrack",new(70,0,103));
        Check(!Rail.HasConnection(new(70,0,101),new(70,0,102)),"side-adjacent double modules do not create accidental crossovers");
        var designator=DefDatabase<ThingDef>.GetNamed("LR_DoubleTrack").designationCategory.ResolvedAllowedDesignators.OfType<Designator_Build>().Single(d=>d.PlacingDef.defName=="LR_DoubleTrack");
        Find.DesignatorManager.Select(designator);var modules=new List<IntVec3>();Find.DesignatorManager.SelectedStyle.DrawStyleWorker.Update(new(30,0,95),new(42,0,95),modules);Find.DesignatorManager.Deselect();
        Check(modules.Count==5&&modules[1]==new IntVec3(33,0,95),"native double-track drag spaces physical modules without footprint overlap");
        Check(gates.All(g=>g.RailCells.Count()==2&&g.RailCells.All(Rail.HasRail)&&g.IsAirtight),"three-cell doors own two independent rail portals and native airtight state");
        Check(!Rail.PlatformsConnected(new(a,0),new(a,1)),"parallel modules and double doors do not implicitly join platforms");
        Check(Rail.HasConnection(new(64,0,79),new(65,0,79))&&!Rail.HasConnection(new(65,0,79),new(65,0,80)),"double door admits normal rail axis and excludes pedestrian lane crossover");
        for(int railX=58;railX<=118;railX++)foreach(int z in new[]{78,82})if(new IntVec3(railX,0,z).GetEdifice(map) is not Building_RailWarehouse)Spawn("Wall",new(railX,0,z),ThingDefOf.Steel);
        for(int wallX=87;wallX<=93;wallX++)foreach(int z in new[]{77,83})Spawn("Wall",new(wallX,0,z),ThingDefOf.Steel);
        for(int roofX=87;roofX<=93;roofX++)for(int z=78;z<=82;z++)map.roofGrid.SetRoof(new(roofX,0,z),RoofDefOf.RoofConstructed);
        for(int roofX=58;roofX<=118;roofX++)for(int z=79;z<=81;z++)map.roofGrid.SetRoof(new(roofX,0,z),RoofDefOf.RoofConstructed);
        Check(gates.All(g=>DoorUtility.WallRequirementCells(g.def,g.Position,g.Rotation).All(cell=>DoorUtility.EncapsulatingWallAt(cell,map))),"rotated double gate uses vanilla end-wall requirements for enclosure");
        Check(Rail.ConfigureService(new(){new(a,0),new(b,0),new(c,0)},false,false,null,"Warehouse freight",1,out var reason,true),"freight-only route connects three warehouse platforms: "+reason);freightId=Rail.Services[^1].id;
        Check(Rail.ConfigureService(new(){new(a,1),new(b,1),new(c,1)},false,false,null,"Mixed freight",2,out _,false),"independent mixed route uses second platform through same double gates");mixedId=Rail.Services[^1].id;
        Rail.SetServiceOperating(Freight,false);Rail.SetServiceOperating(Mixed,false);
        a.orders.Add(new(){serviceId=freightId,originPlatform=0,destination=b,destinationPlatform=0,itemDef=ThingDefOf.Steel});
        a.orders.Add(new(){serviceId=freightId,originPlatform=0,destination=c,destinationPlatform=0,itemDef=ThingDefOf.Cloth});
        a.orders.Add(new(){serviceId=mixedId,originPlatform=1,destination=c,destinationPlatform=1,itemDef=ThingDefOf.Silver});
        c.orders.Add(new(){serviceId=freightId,originPlatform=0,destination=a,destinationPlatform=0,itemDef=ThingDefOf.Cloth});
        c.orders.Add(new(){serviceId=mixedId,originPlatform=1,destination=a,destinationPlatform=1,itemDef=ThingDefOf.Silver});
        cloth=Item(ThingDefOf.Cloth,53,a.AllSlotCellsList()[1]);silver=Item(ThingDefOf.Silver,17,a.AllSlotCellsList()[2]);
        var competing=new RailWarehouseOrder{serviceId=mixedId,originPlatform=1,destination=c,destinationPlatform=1,itemDef=ThingDefOf.Cloth};a.orders.Add(competing);
        Check(a.DispatchFor(cloth).serviceId==freightId&&a.ReadyCargo(Mixed,1).item==silver,"warehouse rule priority is global across routes rather than first arriving train");
        a.orders[1].enabled=false;
        Check(a.DispatchFor(cloth)==competing,"disabled higher priority rule allows explicit lower priority dispatch");
        a.orders[1].enabled=true;a.orders.Remove(competing);
        FullDestination();steel=Item(ThingDefOf.Steel,40,new(35,0,85));worker=Colonist(new(36,0,85));
        var job=new WorkGiver_HaulGeneral().JobOnThing(worker,steel);
        Check(job?.def==JobDefOf.HaulToCell&&job.targetB.Cell.GetSlotGroup(map)?.parent==a,"native automatic hauling workgiver chooses nearby warehouse storage");
        Check(worker.jobs.TryTakeOrderedJob(job),"native warehouse hauling job starts with real steel");originalJob=worker.CurJob.loadID;
        rider=Colonist(new(36,0,84));rider.drafter.Drafted=true;Phase(10);
    }
    public override void MapComponentUpdate()
    {
        if(!Enabled||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||phase>=200)return;
        if(phase==0){try{Setup();}catch(Exception ex){Fail(ex);return;}}
        if(Visual&&phase==70)
        {
            Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            if(frames==0){Find.Selector.ClearSelection();Find.CameraDriver.SetRootPosAndSize(new(90.5f,0,80.5f),36);}
            if(frames==20)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-warehouse-network.png"));
            if(frames==60)Find.WindowStack.Add(new Dialog_RailWarehouse(a));
            if(frames==80)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-warehouse-rules.png"));
            if(frames++<120)return;SaveAndFinish();return;
        }
        Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    }
    public override void MapComponentTick()
    {
        if(!Enabled||phase==0||phase>=200)return;
        try
        {
            if(GenTicks.TicksGame-started>15000)throw new Exception($"Timeout phase {phase}; freight index {Freight?.index}, cargo {string.Join(",",Rail.Freight.Select(f=>f.contents.TotalStackCount+":"+f.destination?.RenamableLabel))}.");
            foreach(var pawn in new[]{worker,rider})if(pawn?.needs!=null){if(pawn.needs.food!=null)pawn.needs.food.CurLevelPercentage=1;if(pawn.needs.rest!=null)pawn.needs.rest.CurLevelPercentage=1;if(pawn.needs.mood!=null)pawn.needs.mood.CurLevelPercentage=1;}
            if(gates.Any(g=>g.Open))gateOpened=true;
            if(gateOpened&&gates.All(g=>!g.Open))gateClosed=true;
            foreach(var route in Rail.Services)if(route.train.Position.GetDoor(map) is Building_RailDoor gate&&!gate.TrainReady)crossedClosed=true;
            if(!mixedBoarded&&Rail.ActiveTrip(rider)?.phase==TransitPhase.Riding){Check(Rail.ActiveTrip(rider).serviceId==mixedId,"automatic passenger chooses mixed route and never freight-only train");mixedBoarded=true;}
            if(rider?.Position==new IntVec3(146,0,84))rider.drafter.Drafted=true;
            switch(phase)
            {
                case 10:
                    if(Quantity(a,ThingDefOf.Steel)!=40||worker.carryTracker.CarriedThing!=null)break;
                    Check(a.Inventory.Contains(steel)&&steel.stackCount==40,"colonist delivers original 40 steel into native warehouse storage");worker.drafter.Drafted=true;
                    Rail.SetServiceOperating(Freight,true);Rail.SetServiceOperating(Mixed,true);rider.drafter.Drafted=false;
                    Check(rider.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(146,0,84))),"mixed-route commuter starts original native journey");Phase(20);break;
                case 20:
                    if(!Rail.Freight.Any(f=>f.train==Freight.train&&f.contents.Contains(steel))||!Rail.Freight.Any(f=>f.train==Freight.train&&f.contents.Contains(cloth)))break;
                    Check(steel.stackCount==40&&cloth.stackCount==53&&Quantity(a,ThingDefOf.Steel)==0,"automatic warehouse loading transfers actual distinct cargo to multiple destinations");Phase(30);break;
                case 30:
                    if(Rail.UnloadWarningCount==0||Freight.index<=Freight.stopIndices[1])break;
                    Check(Rail.Freight.Any(f=>f.contents.Contains(steel))&&steel.stackCount==40,"full warehouse retains exact 40 steel aboard");
                    Check(Freight.fault==null&&Freight.operating&&Find.TickManager.CurTimeSpeed==TimeSpeed.Superfast,"blocked unloading emits info without stopping train or pausing game");
                    Check(Quantity(b,ThingDefOf.Steel)==3375,"full warehouse does not spill freight outside or replace stored cargo");Phase(40);break;
                case 40:
                    if(Quantity(c,ThingDefOf.Cloth)!=53||Quantity(c,ThingDefOf.Silver)!=17||!mixedBoarded)break;
                    Check(gateOpened,"both independent trains trigger shared double-track gates");
                    Check(!crossedClosed,"double-track trains never occupy a gate before fully open");
                    Check(Quantity(c,ThingDefOf.Cloth)==53&&Quantity(c,ThingDefOf.Silver)==17,"different routes and destination platforms unload their respective cargo");
                    Check(Rail.Freight.Any(f=>f.contents.Contains(steel)),"later stop unloading retains earlier blocked shipment aboard");
                    foreach(var item in blockers)item.Destroy();blockers.Clear();Phase(45);break;
                case 45:
                    if(Quantity(b,ThingDefOf.Steel)!=40)break;
                    Check(!Rail.Freight.Any(f=>f.contents.Contains(steel)),"next warehouse visit retries and completes previously blocked unloading");
                    Check(Quantity(c,ThingDefOf.Cloth)==53&&Quantity(c,ThingDefOf.Silver)==17,"received cargo does not automatically enter cyclic return rules");
                    uranium=Item(ThingDefOf.Uranium,13,a.AllSlotCellsList()[0]);a.orders.Add(new(){serviceId=freightId,originPlatform=0,destination=b,destinationPlatform=0,itemDef=ThingDefOf.Uranium});Phase(50);break;
                case 50:
                    var rejected=Rail.Freight.FirstOrDefault(f=>f.contents.Contains(uranium));
                    if(rejected==null||rejected.blockedVisit<0||Freight.index<=Freight.stopIndices[1])break;
                    Check(uranium.stackCount==13&&Quantity(b,ThingDefOf.Uranium)==0,"warehouse filter rejection retains all real uranium in train");
                    Check(Rail.UnloadWarningCount==1,"repeated same route/platform unloading messages are rate limited");
                    b.GetStoreSettings().filter.SetAllow(ThingDefOf.Uranium,true);Phase(55);break;
                case 55:
                    if(Quantity(b,ThingDefOf.Uranium)!=13)break;
                    Check(!Rail.Freight.Any(f=>f.contents.Contains(uranium)),"changing native storage filter allows next-visit delivery");
                    foreach(var item in b.Inventory.ToArray())item.Destroy();FullDestination(60);partial=Item(ThingDefOf.Steel,25,a.AllSlotCellsList()[0]);Phase(60);break;
                case 60:
                    var remainder=Rail.Freight.FirstOrDefault(f=>f.contents.Contains(partial));
                    if(remainder==null||partial.stackCount!=10||Freight.index<=Freight.stopIndices[1])break;
                    Check(Quantity(b,ThingDefOf.Steel)==3375&&partial.stackCount==10,"partial unload merges 15 steel and keeps exact remaining 10 aboard");
                    Check(Quantity(c,ThingDefOf.Cloth)==53&&Quantity(c,ThingDefOf.Silver)==17,"received-cargo loop prevention survives multiple train circuits");
                    Check(gateClosed,"double gates automatically seal after both lines clear");
                    trainId=Freight.train.thingIDNumber;Phase(70);if(!Visual)SaveAndFinish();break;
                case 100:
                    if(GenTicks.TicksGame-started<5)break;
                    Check(Freight.train.thingIDNumber==trainId&&Freight.freightOnly&&!Mixed.freightOnly,"load preserves exact permanent train and freight/mixed modes");
                    Check(a.orders.Count==4&&c.orders.Count==2&&a.orders[2].destinationPlatform==1,"load preserves ordered warehouse dispatch rules and exact platform targets");
                    Check(b.GetStoreSettings().filter.Allows(ThingDefOf.Uranium)&&b.GetStoreSettings().Priority==StoragePriority.Low&&a.GetStoreSettings().Priority==StoragePriority.Preferred,"load preserves native filter and applies receiving/dispatch priority roles");
                    Check(partial.stackCount==10&&Rail.Freight.Any(f=>f.contents.Contains(partial)),"load preserves actual partly unloaded 10 steel in train holder");
                    Check(steelDoor.Stuff==ThingDefOf.Steel&&steelDoor.HitPoints==steelDoor.MaxHitPoints-40&&plasteelDoor.Stuff==ThingDefOf.Plasteel,"load preserves damaged steel door and plasteel material");
                    Check(doorPlan.stuffToUse==ThingDefOf.Steel&&doorFrame.Stuff==ThingDefOf.Steel&&doorFrame.workDone==20,"load preserves or migrates steel material of unfinished door plans without losing work");
                    Check(Quantity(c,ThingDefOf.Cloth)==53&&Quantity(c,ThingDefOf.Silver)==17&&!c.Exportable.Any(),"load preserves received cargo markers preventing automatic return loops");
                    blockers[^1].Destroy();Phase(110);break;
                case 110:
                    if(Rail.Freight.Any(f=>f.contents.Contains(partial)))break;
                    Check(Quantity(b,ThingDefOf.Steel)==3310,"loaded train completes remaining 10 steel with exact quantity after freeing one slot");
                    Check(Quantity(c,ThingDefOf.Cloth)==53&&Quantity(c,ThingDefOf.Silver)==17,"received stock remains at destination after load and continued operation");
                    var stock=map.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t=>t.stackCount);b.Destroy();
                    Check(map.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t=>t.stackCount)==stock,"warehouse demolition keeps all actual resource quantities through native repositioning");
                    Check(!map.haulDestinationManager.AllHaulDestinations.Contains(b),"warehouse demolition deregisters native storage destination");Phase(120);break;
                case 120:
                    if(GenTicks.TicksGame-started<5)break;
                    Check(Freight.fault=="LR_StationMissing"&&Mixed.fault=="LR_StationMissing","warehouse demolition safely suspends both routes with missing station");Finish();break;
            }
        }
        catch(Exception ex){Fail(ex);}
    }
    private void SaveAndFinish(){GameDataSaveLoader.SaveGame("LocalRailWarehouseInTransit");Check(File.Exists(GenFilePaths.FilePathForSavedGame("LocalRailWarehouseInTransit")),"save records warehouse storage, modes, materials and partially unloaded freight");Finish();}
    private void Finish(){phase=200;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=true,tests=passed.Count,passed,Rail.UnloadWarningCount,Rail.PeakPlansPerTick}));Root.Shutdown();}
    private void Fail(Exception ex){phase=201;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=false,passed,error=ex.ToString()}));Log.Error("[RailWarehouseSmoke] FAILED "+ex);Root.Shutdown();}
}
