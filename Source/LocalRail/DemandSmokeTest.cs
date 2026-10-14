using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace LocalRail;

// Explicit opt-in profile, never touches the player's colony or saves.
public class DemandSmokeTest : MapComponent
{
    internal static bool Load=>GenCommandLine.CommandLineArgPassed("localrail-demand-load-smoke");
    internal static bool Enabled=>(Load||GenCommandLine.CommandLineArgPassed("localrail-demand-smoke"))&&
        GenCommandLine.TryGetCommandLineArg("savedatafolder",out string path)&&Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))=="LocalRailTestDemand";
    private bool Visual=>GenCommandLine.CommandLineArgPassed("localrail-visual-smoke");
    private RailMapComponent Rail=>map.GetComponent<RailMapComponent>();
    private RailServiceState Route=>Rail.Service(serviceId);
    private Building_RailWarehouse origin,destination;
    private Pawn rider;
    private Thing steel,cloth;
    private int phase,started,frames,serviceId,trainId,originalJob,reversals;
    private bool sawRide;
    private readonly List<string> passed=new();
    private Dialog_RailDecision diagnostic;
    private RailRouteEditor editor;
    public DemandSmokeTest(Map map):base(map){}
    public override void ExposeData()
    {
        Scribe_References.Look(ref origin,"demandOrigin");Scribe_References.Look(ref destination,"demandDestination");Scribe_References.Look(ref rider,"demandRider");
        Scribe_References.Look(ref steel,"demandSteel");Scribe_References.Look(ref cloth,"demandCloth");
        Scribe_Values.Look(ref serviceId,"demandService");Scribe_Values.Look(ref trainId,"demandTrain");
        if(Scribe.mode==LoadSaveMode.PostLoadInit&&Enabled){phase=100;started=GenTicks.TicksGame;}
    }
    private void Check(bool value,string text){if(!value)throw new Exception(text);passed.Add(text);Log.Message("[RailDemandSmoke] PASS "+text);}
    private void Phase(int next){phase=next;started=GenTicks.TicksGame;frames=0;}
    private Building Spawn(string def,IntVec3 pos)
    {var d=DefDatabase<ThingDef>.GetNamed(def);var t=(Building)ThingMaker.MakeThing(d,d.MadeFromStuff?ThingDefOf.Steel:null);t.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(t,pos,map);return t;}
    private Thing Item(ThingDef def,int count,IntVec3 pos)
    {var t=ThingMaker.MakeThing(def);t.stackCount=count;GenSpawn.Spawn(t,pos,map);return t;}
    private void Setup()
    {
        RailMod.Settings.automatic=false;RailMod.Settings.preferRail=true;RailMod.Settings.stationAccessRadius=80;RailMod.Settings.minDistance=25;
        foreach(var p in map.mapPawns.FreeColonistsSpawned){p.drafter.Drafted=true;p.Position=new(20,0,70);p.pather.ResetToCurrentPosition();}
        foreach(var c in new CellRect(28,68,154,105))
        {
            map.roofGrid.SetRoof(c,null);
            foreach(var t in c.GetThingList(map).Where(t=>t is Building or Plant||t.def.category==ThingCategory.Item).ToArray())if(t.def.destroyable)t.Destroy();else t.DeSpawn();
            map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);map.fogGrid.Unfog(c);
        }
        origin=(Building_RailWarehouse)Spawn("LR_SinglePlatformWarehouse",new(50,0,120));origin.RenamableLabel="West depot";
        destination=(Building_RailWarehouse)Spawn("LR_SinglePlatformWarehouse",new(170,0,120));destination.RenamableLabel="East depot";
        for(int x=52;x<169;x++)Spawn("LR_Track",new(x,0,120));
        for(int x=30;x<=80;x++)Spawn(x==50?"Door":"Wall",new(x,0,90));
        foreach(var s in new[]{origin,destination})s.SetPlatformSettings(0,new(){automatic=true,minimumTicks=120,maximumTicks=600});
        Check(Rail.ConfigureService(new(){new(origin),new(destination)},false,false,null,"Long-distance commuter",0,out var reason,false,RailTrainKind.Passenger),"ordinary route creates without a driver: "+reason);
        serviceId=Rail.Services[^1].id;trainId=Route.train.thingIDNumber;
        Check(Route.train.def.defName=="LR_PassengerTrain"&&Route.trainKind==RailTrainKind.Passenger,"ordinary train has its own saved definition");
        origin.freightDestination=destination;origin.freightServiceId=serviceId;origin.SynchronizePriority();
        Check(origin.GetStoreSettings().Priority==StoragePriority.Preferred&&origin.orders.Count==0,"default destination gathers stock without advanced dispatch rules");
        steel=Item(ThingDefOf.Steel,75,origin.AllSlotCellsList()[0]);cloth=Item(ThingDefOf.Cloth,20,origin.AllSlotCellsList()[1]);
        rider=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,allowAddictions:false));
        GenSpawn.Spawn(rider,new(50,0,75),map);
        Check(rider.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(170,0,165))),"native long-distance work movement starts");originalJob=rider.CurJob.loadID;
        Phase(1);
    }
    public override void MapComponentUpdate()
    {
        if(!Enabled||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||phase>=200)return;
        try
        {
            if(phase==0)Setup();
            if(phase==5)
            {
                Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                if(frames==0){Find.CameraDriver.SetRootPosAndSize(new(110,0,120),70);diagnostic=new(rider);Find.WindowStack.Add(diagnostic);}
                if(frames==20)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-travel-diagnostic.png"));
                if(frames++>=40){diagnostic.Close();Phase(10);}return;
            }
            if(phase==35)
            {
                Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                if(frames==0){Find.Selector.ClearSelection();Find.Selector.Select(Route.train);Find.CameraDriver.SetRootPosAndSize(Route.train.DrawPos,8);}
                if(frames==20)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-empty-automatic-cargo.png"));
                if(frames==40){editor=new(Rail,origin,Route);Find.WindowStack.Add(editor);}
                if(frames==60)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-train-types.png"));
                if(frames++>=80){editor.Close();Phase(40);}return;
            }
            Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
        }
        catch(Exception ex){Fail(ex);}
    }
    public override void MapComponentTick()
    {
        if(!Enabled||phase==0||phase>=200||phase is 5 or 35)return;
        try
        {
            if(GenTicks.TicksGame-started>16000)throw new Exception($"Timeout phase {phase}; route={Route.index}, cargo={Rail.Freight.Count}, rider={rider.Position}, trip={Rail.ActiveTrip(rider)?.phase}");
            if(rider.needs.food!=null)rider.needs.food.CurLevelPercentage=1;if(rider.needs.rest!=null)rider.needs.rest.CurLevelPercentage=1;
            if(phase==10&&Rail.ActiveTrip(rider)?.phase==TransitPhase.Riding&&!sawRide)
            {Check(rider.CurJob.loadID==originalJob,"ordinary train automatically boards a 45-tile-access colonist with original job");sawRide=true;}
            switch(phase)
            {
                case 1:
                    if(rider.pather.curPath?.Found!=true||!DriverAccess.IsTravelToil(rider))break;
                    RailMod.Settings.automatic=true;RailMod.Settings.stationAccessRadius=24;
                    var oldReport=Rail.Diagnose(rider);
                    Check(oldReport.reason=="LR_ReasonNoStations","old 24-tile limit reproduces rejected commuting at both ends: "+oldReport.reason);
                    RailMod.Settings.stationAccessRadius=80;int before=Rail.Freight.Count;var report=Rail.Diagnose(rider);
                    Check(report.reason=="LR_ReasonSelected"&&report.access>0&&report.egress>0,"expanded access reaches platforms through an ordinary closed door: "+report.reason);
                    Check(report.Costs.Contains((report.direct/60f).ToString("0.0"))&&report.Costs.Contains((report.access/60f).ToString("0.0")),"native localized diagnostic displays real walking and access estimates");
                    Check(Rail.ActiveTrip(rider)==null&&rider.CurJob.loadID==originalJob&&before==Rail.Freight.Count,"diagnostic estimates without changing orders, trips or cargo");
                    Check(rider.GetGizmos().Any(g=>g is Command_Action a&&a.defaultLabel=="LR_RailDecision".Translate()),"colonist exposes native travel diagnostic gizmo");
                    Phase(Visual?5:10);break;
                case 10:
                    if(rider.Position!=new IntVec3(170,0,165)||Rail.ActiveTrip(rider)!=null)break;
                    Check(sawRide,"long-distance commuter completes door access, train ride and final 45-tile walk");rider.drafter.Drafted=true;reversals=Rail.Reversals;Phase(20);break;
                case 20:
                    if(Rail.Reversals<reversals+2)break;
                    Check(origin.Inventory.Contains(steel)&&origin.Inventory.Contains(cloth)&&Rail.Freight.Count==0,"ordinary train leaves warehouse inventory untouched across an empty round trip");
                    var graphic=Route.train.Graphic;
                    Check(Rail.ConfigureService(new(){new(origin),new(destination)},false,false,Route,Route.Name,Route.colorIndex,out _,false,RailTrainKind.Automatic),"same route switches to automatic transport");
                    Check(Route.train.thingIDNumber==trainId&&Route.train.Graphic!=graphic&&Route.train.LabelNoCount==Route.KindLabel,"type switch retains train identity while updating graphic and label");Phase(30);break;
                case 30:
                    if(Rail.Freight.Count(f=>f.train==Route.train)!=2||Route.index<10)break;
                    Check(Rail.RiderCount(Route)==0&&Rail.Freight.Any(f=>f.contents.Contains(steel))&&Rail.Freight.Any(f=>f.contents.Contains(cloth)),"empty automatic train loads actual warehouse steel and cloth using only default destination");
                    Check(Rail.CargoVisuals(Route).Count==2&&steel.stackCount==75&&cloth.stackCount==20,"empty train displays real cargo quantities without duplication");
                    GameDataSaveLoader.SaveGame("LocalRailDemandInTransit");Check(File.Exists(GenFilePaths.FilePathForSavedGame("LocalRailDemandInTransit")),"save records train type, default destination and actual unmanned cargo");Phase(Visual?35:40);break;
                case 40:
                    if(!destination.Inventory.Contains(steel)||!destination.Inventory.Contains(cloth))break;
                    Check(steel.stackCount==75&&cloth.stackCount==20&&Rail.RiderCount(Route)==0&&Rail.WasDelivered(steel),"automatic train unloads both original stacks with zero passengers");reversals=Rail.Reversals;Phase(50);break;
                case 50:
                    if(Rail.Reversals<reversals+2)break;
                    Check(destination.Inventory.Contains(steel)&&destination.Inventory.Contains(cloth)&&Rail.Freight.Count==0,"default delivery remains at receiving depot through another empty round trip");
                    Check(Rail.PeakPlansPerTick<=2&&Route.fault==null,"expanded access preserves per-tick planning limit and healthy operation");Finish();break;
                case 100:
                    Check(Route.trainKind==RailTrainKind.Automatic&&Route.train.thingIDNumber==trainId,"reload retains edited automatic type and original train identity");
                    Check(origin.freightDestination==destination&&origin.orders.Count==0,"reload retains default dispatch destination without advanced rules");
                    Check(Rail.Freight.Any(f=>f.contents.Contains(steel))&&Rail.Freight.Any(f=>f.contents.Contains(cloth))&&Rail.RiderCount(Route)==0,"reload retains real unmanned cargo");Phase(110);break;
                case 110:
                    if(!destination.Inventory.Contains(steel)||!destination.Inventory.Contains(cloth))break;
                    Check(steel.stackCount==75&&cloth.stackCount==20&&Rail.WasDelivered(steel),"reloaded unmanned train completes stock delivery");Finish();break;
            }
        }
        catch(Exception ex){Fail(ex);}
    }
    private void Finish(){phase=200;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=true,tests=passed.Count,passed,Rail.PlanAttempts,Rail.AccessExpanded,Rail.PeakPlansPerTick}));Root.Shutdown();}
    private void Fail(Exception ex){phase=201;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=false,passed,error=ex.ToString()}));Log.Error("[RailDemandSmoke] FAILED "+ex);Root.Shutdown();}
}
