using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace LocalRail;

// Reproduce reported native hauling and mixed station regressions in a separate profile.
public class RetrofitSmokeTest : MapComponent
{
    internal static bool Enabled=>GenCommandLine.CommandLineArgPassed("localrail-retrofit-smoke")&&
        GenCommandLine.TryGetCommandLineArg("savedatafolder",out string path)&&Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))=="LocalRailTestRetrofit";
    private bool Visual=>GenCommandLine.CommandLineArgPassed("localrail-visual-smoke");
    private RailMapComponent Rail=>map.GetComponent<RailMapComponent>();
    private Building_RailWarehouse warehouse;
    private Building_RailStation passenger;
    private RailServiceState route;
    private Pawn hauler;
    private Corpse corpse;
    private Thing reverseCargo;
    private RailRouteEditor editor;
    private List<Building_RailDoubleDoor> doors=new();
    private int phase,started,frames;
    private readonly List<string> passed=new();
    public RetrofitSmokeTest(Map map):base(map){}
    private void Check(bool value,string message){if(!value)throw new Exception(message);passed.Add(message);Log.Message("[RailRetrofitSmoke] PASS "+message);}
    private Building Spawn(string name,IntVec3 pos,Rot4? rotation=null)
    {
        var def=DefDatabase<ThingDef>.GetNamed(name);var thing=(Building)ThingMaker.MakeThing(def,def.MadeFromStuff?ThingDefOf.Steel:null);
        thing.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(thing,pos,map,rotation??Rot4.North);return thing;
    }
    private Pawn PawnAt(IntVec3 pos)
    {
        var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,allowAddictions:false,
            validatorPreGear:p=>!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)));
        GenSpawn.Spawn(pawn,pos,map);return pawn;
    }
    private Thing Item(ThingDef def,int count,IntVec3 pos)
    {var item=ThingMaker.MakeThing(def);item.stackCount=count;GenSpawn.Spawn(item,pos,map);return item;}
    private void SetPhase(int next){phase=next;started=GenTicks.TicksGame;}
    private int DeliveredSteel=>map.listerThings.ThingsOfDef(ThingDefOf.Steel).Where(t=>t.Position.DistanceToSquared(passenger.Position)<=25).Sum(t=>t.stackCount);
    private void Setup()
    {
        RailMod.Settings.automatic=true;
        foreach(var pawn in map.mapPawns.FreeColonistsSpawned){pawn.drafter.Drafted=true;pawn.Position=new(25,0,160);pawn.pather.ResetToCurrentPosition();}
        foreach(var cell in new CellRect(25,120,105,60))
        {
            map.roofGrid.SetRoof(cell,null);
            foreach(var thing in cell.GetThingList(map).Where(t=>t is Building or Plant||t.def.category==ThingCategory.Item).ToArray())
                if(thing.def.destroyable)thing.Destroy();else thing.DeSpawn();
            map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.fogGrid.Unfog(cell);
        }
        warehouse=(Building_RailWarehouse)Spawn("LR_SinglePlatformWarehouse",new(40,0,140));passenger=(Building_RailStation)Spawn("LR_PlatformStation",new(100,0,140));
        warehouse.RenamableLabel="Industrial A";passenger.RenamableLabel="Passenger B";
        for(int x=42;x<=98;x++)Spawn("LR_Track",new(x,0,140));
        foreach(string name in new[]{"LR_SinglePlatformWarehouse","LR_FreightWarehouse","LR_FourPlatformWarehouse"})
        {
            var def=DefDatabase<ThingDef>.GetNamed(name);
            Check(def.designationCategory.ResolvedAllowedDesignators.OfType<Designator_Build>().Any(d=>d.PlacingDef==def),name+" is offered in Architect");
            Check(def.graphic is Graphic_RailWarehouse&&def.blueprintDef.graphic is Graphic_RailWarehouse&&
                GhostUtility.GhostGraphicFor(def.graphic,def,Color.green) is Graphic_RailWarehouse,name+" shares composed layout in built/blueprint/ghost graphics");
            Check(def.graphic.drawSize==new Vector2(def.size.x,def.size.z),name+" preview dimensions equal footprint");
        }
        Check(warehouse.PlatformCount==1&&warehouse.RailCells.Count()==3&&warehouse.AllSlotCellsList().Count==6,"single industrial depot has one through platform and six storage cells");
        Check(warehouse.AllSlotCellsList().All(c=>c.GetSlotGroup(map)==warehouse.GetSlotGroup())&&warehouse.RailCells.All(c=>c.GetSlotGroup(map)==null),"native storage rows exclude the industrial through track");
        foreach(var station in new[]{warehouse,passenger})station.SetPlatformSettings(0,new(){automatic=true,minimumTicks=120,maximumTicks=1200});
        Check(Rail.ConfigureService(new(){new(warehouse),new(passenger)},false,false,null,"Industrial ↔ Passenger",0,out var error,false),"warehouse and passenger platforms create a mixed route: "+error);
        route=Rail.Services[^1];Rail.SetServiceOperating(route,false);
        var dead=PawnAt(new(35,0,144));dead.Kill(null);corpse=dead.Corpse;corpse.SetForbidden(false);
        hauler=PawnAt(new(36,0,144));
        Check(warehouse.GetStoreSettings().filter.Allows(corpse)&&warehouse.Accepts(corpse),"default native filter and Accepts both allow corpses");
        Check(warehouse.Accepts(ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial))&&warehouse.Accepts(ThingMaker.MakeThing(ThingDefOf.Steel)),"default warehouse accepts medicines and resources");
        var job=new WorkGiver_HaulCorpses().JobOnThing(hauler,corpse);
        Check(job?.def==JobDefOf.HaulToCell&&job.targetB.Cell.GetSlotGroup(map)?.parent==warehouse,"native haul workgiver chooses industrial depot for corpse");
        Check(hauler.jobs.TryTakeOrderedJob(job),"native corpse hauling starts without custom workgiver");
        Spawn("LR_FreightWarehouse",new(68,0,159));Spawn("LR_FourPlatformWarehouse",new(80,0,159));
        Spawn("LR_SinglePlatformWarehouse",new(55,0,159));
        for(int i=0;i<4;i++)
        {
            var gate=(Building_RailDoubleDoor)Spawn("LR_DoubleRailDoor",new(93+i*8,0,159),new Rot4(i));doors.Add(gate);
            foreach(var offset in new[]{-1,1})Spawn("Wall",gate.Position+(gate.Rotation.IsHorizontal?IntVec3.North:IntVec3.East)*2*offset);
        }
        Check(doors.All(d=>d.IsAirtight),"all four rotated double gates use native airtight definitions");
        editor=new RailRouteEditor(Rail,warehouse,null);Find.WindowStack.Add(editor);editor.ChooseStop();
        Check(editor.Picking&&!editor.preventCameraMotion&&Find.Targeter.IsTargeting&&editor.windowRect.width==350,"map platform picker permits camera motion in compact sidebar");
        Check(editor.StopAtCell(passenger.PlatformCell(0))==new RailStop(passenger),"map hit chooses exact passenger platform");
        var four=Rail.Stations.Single(s=>s.def.defName=="LR_FourPlatformWarehouse");
        Check(editor.StopAtCell(four.PlatformCell(3))==new RailStop(four,3),"map hit distinguishes fourth industrial platform");
        Check(!editor.CanChoose(new(four,0),out _),"disconnected candidate is visible but cannot be picked");
        Check(editor.ApplyChoice(new(passenger)),"connected map candidate is added to draft");Find.Targeter.StopTargeting();
        Check(!editor.Picking&&editor.stops.Count==2&&editor.windowRect.width==660,"finishing picker restores route draft");
        editor.ChooseStop(1);editor.OnCancelKeyPressed();Check(editor.IsOpen&&!editor.Picking&&editor.stops.Count==2,"Escape cancels picking and preserves route draft");editor.Close();
        SetPhase(10);
    }
    public override void MapComponentUpdate()
    {
        if(!Enabled||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||phase>=200)return;
        try
        {
            if(phase==0)Setup();
            if(phase==90&&Visual)
            {
                Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                if(frames==0){Find.Selector.ClearSelection();Find.CameraDriver.SetRootPosAndSize(new(86,0,165),22);}
                if(frames==25)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-industrial-gates.png"));
                if(frames==50){editor=new RailRouteEditor(Rail,warehouse,null);Find.WindowStack.Add(editor);editor.ChooseStop();editor.Highlighted=new(passenger);Find.CameraDriver.SetRootPosAndSize(new(92,0,153),19);}
                if(frames==80)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-platform-picker.png"));
                if(frames==105){editor.OnCancelKeyPressed();editor.Close();Find.WindowStack.Add(new Dialog_RailCargo(route.train));}
                if(frames==130)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-cargo-slots.png"));
                if(frames++>155)Finish();return;
            }
            Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
        }
        catch(Exception ex){Fail(ex);}
    }
    public override void MapComponentDraw()
    {
        if(!Enabled||!Visual||phase!=90||frames>=50)return;
        foreach(var spec in new[]{("LR_SinglePlatformWarehouse",55),("LR_FreightWarehouse",68),("LR_FourPlatformWarehouse",80)})
            GhostDrawer.DrawGhostThing(new(spec.Item2,0,173),Rot4.North,DefDatabase<ThingDef>.GetNamed(spec.Item1),null,Color.green,AltitudeLayer.Blueprint);
    }
    public override void MapComponentTick()
    {
        if(!Enabled||phase==0||phase>=90)return;
        try
        {
            if(GenTicks.TicksGame%600==0)File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"retrofit-progress.txt"),$"phase={phase} tick={GenTicks.TicksGame} index={route.index} dwell={route.dwell} delivered={DeliveredSteel} freight={Rail.Freight.Count}");
            if(GenTicks.TicksGame-started>12000)throw new Exception($"Timeout phase {phase}; delivered={DeliveredSteel}, freight={Rail.Freight.Count}, index={route.index}");
            if(hauler.needs.food!=null)hauler.needs.food.CurLevelPercentage=1;
            if(hauler.needs.rest!=null)hauler.needs.rest.CurLevelPercentage=1;
            switch(phase)
            {
                case 10:
                    if(!warehouse.Inventory.Contains(corpse)||hauler.carryTracker.CarriedThing!=null)break;
                    hauler.drafter.Drafted=true;
                    // Door spawn/close timing is independent of the generated pawn's speed.
                    if(doors.Any(d=>d.Open))break;
                    Check(corpse.Spawned&&corpse.Position.GetSlotGroup(map)?.parent==warehouse,"reported native corpse hauling completes with same corpse");hauler.drafter.Drafted=true;
                    Check(doors.All(d=>!d.Open),"all four rotated double gates automatically close between their end walls");
                    warehouse.orders.Add(new(){serviceId=route.id,destination=passenger,itemDef=ThingDefOf.Steel});
                    var cells=warehouse.AllSlotCellsList();int n=0;
                    foreach(var cell in cells)for(int slot=cell.GetThingList(map).Count(t=>t.def.category==ThingCategory.Item);slot<3&&n<13;slot++,n++)Item(ThingDefOf.Steel,75,cell);
                    Check(n==13,"13 full stacks stocked for twelve-slot capacity test");Rail.SetServiceOperating(route,true);SetPhase(20);break;
                case 20:
                    if(route.index<=0||Rail.Freight.Count(f=>f.train==route.train)!=12)break;
                    Check(warehouse.Inventory.Where(t=>t.def==ThingDefOf.Steel).Sum(t=>t.stackCount)==75,"train leaves one full stack behind after loading exactly twelve");
                    Check(route.train.GetInspectString().Contains("12"),"train inspector uses twelve-slot capacity");SetPhase(30);break;
                case 30:
                    if(Rail.Freight.Any(f=>f.train==route.train)||DeliveredSteel!=900)break;
                    Check(route.fault==null&&route.operating,"warehouse-to-passenger route unloads twelve stacks without fault");
                    Check(map.listerThings.ThingsOfDef(ThingDefOf.Steel).Where(t=>t.Position.DistanceToSquared(passenger.Position)<=25).All(Rail.WasDelivered),"passenger station ground deliveries are protected from automatic source collection");
                    Check(warehouse.Inventory.Contains(corpse),"unmatched corpse stays safely in industrial storage");
                    hauler.drafter.Drafted=false;hauler.Position=new(102,0,142);hauler.pather.ResetToCurrentPosition();
                    reverseCargo=Item(ThingDefOf.Uranium,23,hauler.Position);reverseCargo=reverseCargo.SplitOff(23);
                    Check(hauler.carryTracker.TryStartCarry(reverseCargo)&&hauler.carryTracker.CarriedThing.stackCount==23,"native carry container receives detached original 23 uranium");
                    Check(Rail.EnqueueFreight(hauler,passenger,warehouse,route.id,0,0),"passenger station queues actual carried cargo toward industrial warehouse");hauler.drafter.Drafted=true;SetPhase(40);break;
                case 40:
                    if(!warehouse.Inventory.Contains(reverseCargo))break;
                    Check(reverseCargo.stackCount==23&&!Rail.Freight.Any(f=>f.contents.Contains(reverseCargo)),"passenger-to-warehouse unload preserves original uranium and quantity");
                    Check(Rail.UnloadWarningCount==0,"both station kinds unload without blocked-cargo warnings");
                    warehouse.orders.Add(new(){serviceId=route.id,destination=passenger,itemDef=corpse.def});SetPhase(50);break;
                case 50:
                    if(!corpse.Spawned||corpse.Position.DistanceToSquared(passenger.Position)>25)break;
                    Check(!warehouse.Inventory.Contains(corpse)&&!Rail.Freight.Any(f=>f.contents.Contains(corpse)),"automatic depot dispatch carries and unloads original corpse at passenger station");
                    SetPhase(90);if(!Visual)Finish();break;
            }
        }
        catch(Exception ex){Fail(ex);}
    }
    private void Finish(){phase=200;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=true,tests=passed.Count,passed}));Root.Shutdown();}
    private void Fail(Exception ex){phase=201;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=false,passed,error=ex.ToString()}));Log.Error("[RailRetrofitSmoke] FAILED "+ex);Root.Shutdown();}
}
