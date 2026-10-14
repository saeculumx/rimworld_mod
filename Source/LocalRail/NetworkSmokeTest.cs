using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace LocalRail;

// Only explicitly invoked isolated test profiles run this fixture.
public class NetworkSmokeTest : MapComponent
{
    internal static bool Load=>GenCommandLine.CommandLineArgPassed("localrail-network-load-smoke");
    internal static bool Enabled=>(Load||GenCommandLine.CommandLineArgPassed("localrail-network-smoke"))&&
        GenCommandLine.TryGetCommandLineArg("savedatafolder",out string path)&&Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)).StartsWith("LocalRailTestNetwork",StringComparison.Ordinal);
    private bool Visual=>GenCommandLine.CommandLineArgPassed("localrail-visual-smoke");
    private RailMapComponent Rail=>map.GetComponent<RailMapComponent>();
    private RailServiceState South=>Rail.Service(southId);
    private RailServiceState North=>Rail.Service(northId);
    private RailServiceState Circle=>Rail.Service(circleId);
    private int phase,started,southId,northId,circleId,originalJob,laps,previousIndex=-1;
    private Pawn rider,loader;
    private Thing freight,cargo;
    private Building_RailStation dualA,dualB,four;
    private List<Building_RailStation> ring=new();
    private List<int> savedTrainIds=new();
    private IntVec3 goal;
    private readonly List<string> passed=new();
    private int screenshotStep,screenshotFrames;
    private bool screenshotsComplete;
    private Window screenshotWindow;
    public NetworkSmokeTest(Map map):base(map){}
    public override void ExposeData()
    {
        Scribe_References.Look(ref rider,"networkRider");Scribe_References.Look(ref loader,"networkLoader");
        Scribe_References.Look(ref freight,"networkFreight");Scribe_References.Look(ref dualA,"networkDualA");Scribe_References.Look(ref dualB,"networkDualB");Scribe_References.Look(ref four,"networkFour");
        Scribe_Collections.Look(ref ring,"networkRing",LookMode.Reference);Scribe_Collections.Look(ref savedTrainIds,"networkTrainIds",LookMode.Value);
        Scribe_Values.Look(ref southId,"networkSouth");Scribe_Values.Look(ref northId,"networkNorth");Scribe_Values.Look(ref circleId,"networkCircle");
        Scribe_Values.Look(ref goal,"networkGoal");Scribe_Values.Look(ref originalJob,"networkJob");
        if(Scribe.mode==LoadSaveMode.PostLoadInit&&Enabled){phase=110;started=GenTicks.TicksGame;}
    }
    public override void MapComponentUpdate()
    {
        if(!Enabled||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||phase>=200)return;
        if(phase==0){try{Setup();}catch(Exception ex){Fail(ex);return;}}
        if(Visual&&phase==51&&!screenshotsComplete)
        {
            Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            if(screenshotFrames==0)
            {
                Find.CameraDriver.SetRootPosAndSize(new IntVec3(95,0,85).ToVector3Shifted(),45);
                if(screenshotStep==0){RailManager.Open();RailManager.Active.SelectedService=Circle;}
                else if(screenshotStep==1)RailManager.Open(dualA);
                else
                {
                    Find.MainTabsRoot.SetCurrentTab(null);
                    screenshotWindow=screenshotStep==2?new Dialog_RailDwell(dualA.PlatformSettings(0),_=>{}):new RailRouteEditor(Rail,ring[0],Circle);
                    Find.WindowStack.Add(screenshotWindow);
                }
            }
            string[] names={"rail-network-manager.png","rail-platform-manager.png","rail-dwell-editor.png","rail-loop-editor.png"};
            if(screenshotFrames++==20)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,names[screenshotStep]));
            if(screenshotFrames<60)return;
            screenshotWindow?.Close();screenshotWindow=null;screenshotFrames=0;screenshotStep++;
            if(screenshotStep<4)return;screenshotsComplete=true;
        }
        Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    }
    private void Check(bool value,string text){if(!value)throw new Exception(text);passed.Add(text);Log.Message("[RailNetworkSmoke] PASS "+text);}
    private void Phase(int value){phase=value;started=GenTicks.TicksGame;}
    private Building_Rail Spawn(IntVec3 cell,string def="LR_Track")
    {
        if(def=="LR_Track"&&cell.GetEdifice(map) is Building_RailStation station&&station.RailCells.Contains(cell))return station;
        var rail=(Building_Rail)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(def));rail.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(rail,cell,map);return rail;
    }
    private void RemoveTrack(IntVec3 cell)=>cell.GetThingList(map).OfType<Building_Rail>().Single().Destroy();
    private Pawn Colonist(IntVec3 cell)
    {
        var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,allowAddictions:false,
            validatorPreGear:p=>!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)));
        GenSpawn.Spawn(pawn,cell,map);return pawn;
    }
    private void Position(Pawn pawn,IntVec3 cell)
    {pawn.drafter.Drafted=false;pawn.jobs.EndCurrentJob(JobCondition.InterruptForced,false);pawn.pather.StopDead();pawn.Position=cell;pawn.pather.ResetToCurrentPosition();}
    private void Park(RailServiceState service,int stop)
    {
        service.train.Position=service.Stop(stop).Cell;
        Check(Rail.ConfigureService(Enumerable.Range(0,service.stops.Count).Select(service.Stop).ToList(),service.loop,service.reverseLoop,service,service.routeName,service.colorIndex,out _),"route reconfiguration retains train and chosen platform");
    }
    private void BeginFreight(RailServiceState service,int from,int to,int count,IntVec3 workerCell,IntVec3 itemCell)
    {
        Position(loader,workerCell);freight=ThingMaker.MakeThing(ThingDefOf.Steel);freight.stackCount=count;GenSpawn.Spawn(freight,itemCell,map);
        var a=service.Stop(from);var b=service.Stop(to);Rail.OrderFreight(loader,freight,a.Station,b.Station,service.id,a.Platform,b.Platform);
        Check(loader.CurJob?.def.defName=="LR_LoadFreight","native loading job accepted with explicit route and platform targets");
    }
    private void BeginGoto(IntVec3 from,IntVec3 to)
    {
        Position(rider,from);goal=to;Check(rider.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,goal)),"native route passenger job accepted");originalJob=rider.CurJob.loadID;
    }
    private void Setup()
    {
        RailMod.Settings.automatic=true;RailMod.Settings.preferRail=true;RailMod.Settings.minDistance=25;RailMod.Settings.diagnostics=true;
        foreach(var cell in new CellRect(25,55,160,60))
        {
            map.roofGrid.SetRoof(cell,null);
            foreach(var thing in cell.GetThingList(map).Where(t=>t is Building or Plant || t.def.category==ThingCategory.Item).ToArray())if(thing.def.destroyable)thing.Destroy();else thing.DeSpawn();
            map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.fogGrid.Unfog(cell);
        }
        foreach(var pawn in map.mapPawns.FreeColonistsSpawned){pawn.drafter.Drafted=true;pawn.Position=new(30,0,110);pawn.pather.ResetToCurrentPosition();}
        foreach(string name in new[]{"LR_PlatformStation","LR_TwoPlatformStation","LR_FourPlatformStation"})
        {
            var def=DefDatabase<ThingDef>.GetNamed(name);
            Check(def.designationCategory.ResolvedAllowedDesignators.OfType<Designator_Build>().Any(d=>d.PlacingDef==def),name+" visible in actual architect menu");
            Check(def.costList==null||def.costList.Count==0,name+" remains free of materials");
        }
        Check(DefDatabase<MainButtonDef>.GetNamed("LR_Rail").TabWindow is RailManager,"native Rail main tab instantiates management window");
        dualA=(Building_RailStation)Spawn(new(40,0,100),"LR_TwoPlatformStation");dualB=(Building_RailStation)Spawn(new(140,0,100),"LR_TwoPlatformStation");
        four=(Building_RailStation)Spawn(new(170,0,100),"LR_FourPlatformStation");dualA.RenamableLabel="Farm hub";dualB.RenamableLabel="Factory hub";
        for(int x=43;x<=137;x++){Spawn(new(x,0,99));Spawn(new(x,0,101));}
        Check(dualA.PlatformCount==2&&four.PlatformCount==4&&dualA.RailCells.Count()==10&&four.RailCells.Count()==20,"multi-platform buildings contain independent full-width track rows");
        Check(!Rail.PlatformsConnected(new(dualA,0),new(dualA,1)),"same station building does not implicitly join its tracks");
        Check(Rail.PlatformsConnected(new(dualA,0),new(dualB,0))&&!Rail.PlatformsConnected(new(dualA,0),new(dualB,1)),"connectivity report distinguishes correct destination platforms");
        Check(Rail.ConfigureService(new(){new(dualA,0),new(dualB,0)},false,false,null,"Farm shuttle",1,out _),"south platform route creates persistent train");southId=Rail.Services[^1].id;
        Check(Rail.ConfigureService(new(){new(dualA,1),new(dualB,1)},false,false,null,"Factory shuttle",2,out _),"north platform route creates independent persistent train");northId=Rail.Services[^1].id;
        Check(!Rail.ConfigureService(new(){new(dualA,0),new(dualB,0)},false,false,null,"Conflict",0,out var reason)&&reason.Contains("Farm shuttle"),"overlapping track rejected with conflicting route name");
        for(int z=99;z<=101;z++)Spawn(new(37,0,z));
        Check(Rail.PlatformsConnected(new(dualA,0),new(dualA,1)),"outside crossover explicitly joins platform networks");
        var form=new RailDwellForm(new()){manual="1.75"};Check(form.TryRead(out var settings,out _)&&settings.manualTicks==105,"dwell form accepts fractional seconds without a slider");
        form.manual="invalid";Check(!form.TryRead(out _,out _),"dwell form rejects invalid text without committing");form.manual="1";form.minimum="20";form.maximum="5";
        form.automatic=true;Check(!form.TryRead(out _,out _),"dwell form rejects automatic minimum above maximum");
        rider=Colonist(new(37,0,103));rider.drafter.Drafted=true;loader=Colonist(new(40,0,94));loader.drafter.Drafted=true;
        RemoveTrack(new(90,0,99));Phase(10);
    }
    private void SetupRing()
    {
        foreach(var cell in new[]{new IntVec3(40,0,65),new(140,0,65),new(140,0,85),new(40,0,85)})ring.Add((Building_RailStation)Spawn(cell,"LR_PlatformStation"));
        for(int x=42;x<=138;x++){Spawn(new(x,0,65));Spawn(new(x,0,85));}
        for(int z=65;z<=85;z++){Spawn(new(38,0,z));Spawn(new(142,0,z));}
        foreach(int z in new[]{65,85}){Spawn(new(39,0,z));Spawn(new(141,0,z));}
        for(int i=0;i<ring.Count;i++)ring[i].RenamableLabel="Ring "+(i+1);
        Check(Rail.ConfigureService(ring.Select(s=>new RailStop(s)).ToList(),true,false,null,"Perimeter loop",5,out var reason),"four-stop ring validates real closing leg: "+reason);circleId=Rail.Services[^1].id;
        Check(Circle.path.Distinct().Count()==Circle.path.Count&&Circle.CircuitTicks==744,"loop stores unique cells and includes weighted closing edge");
        Check(!Rail.ConfigureService(new(){new(dualA,0),new(dualB,0)},true,false,null,"Invalid loop",0,out _),"outbound retracing cannot masquerade as a closed loop");
    }
    public override void MapComponentTick()
    {
        if(!Enabled||phase==0||phase>=200)return;
        try
        {
            if(GenTicks.TicksGame-started>10000)throw new Exception($"Timeout phase {phase}, rider {rider?.Position}, job {rider?.CurJob}, freight {freight?.Spawned}, routes {string.Join(";",Rail.Services.Select(s=>s.Name+":"+s.fault))}");
            foreach(var pawn in new[]{rider,loader})if(pawn?.needs!=null)
            {if(pawn.needs.food!=null)pawn.needs.food.CurLevelPercentage=1;if(pawn.needs.rest!=null)pawn.needs.rest.CurLevelPercentage=1;if(pawn.needs.mood!=null)pawn.needs.mood.CurLevelPercentage=1;}
            switch(phase)
            {
                case 10:
                    if(GenTicks.TicksGame-started<130)break;
                    Check(South.fault!=null&&North.Available&&North.index>0,"breaking south track leaves north platform route moving in same connected network");
                    Check(!Rail.ReachablePlatforms(new(dualA,0)).Contains(new(dualB,0))&&Rail.ReachablePlatforms(new(dualA,1)).Contains(new(dualB,1)),"platform connection cache invalidates on track removal");
                    Spawn(new(90,0,99));Phase(11);break;
                case 11:
                    if(GenTicks.TicksGame-started<5)break;
                    Check(South.Available&&North.Available&&Rail.Services.Count==2,"repair restores two disjoint routes without another train");
                    dualA.SetPlatformSettings(0,new(){automatic=true,minimumTicks=30,maximumTicks=600});Park(South,0);
                    BeginFreight(South,0,1,19,new(40,0,94),new(40,0,93));Phase(14);break;
                case 14:
                    if(GenTicks.TicksGame-started<80)break;
                    Check(South.index==0&&South.dwellElapsed>=80&&South.dwell>0,"automatic dwell holds beyond minimum for nearby real loading worker");Phase(15);break;
                case 15:
                    if(!Rail.Freight.Any(f=>f.train==South.train))break;
                    Check(Rail.Freight.Single().originPlatform==0&&Rail.Freight.Single().destinationPlatform==0&&Rail.Freight.Single().contents.Contains(freight),"train loads same real freight with explicit platform pair");
                    loader.drafter.Drafted=true;Phase(16);break;
                case 16:
                    if(!freight.Spawned||Rail.Freight.Count!=0)break;
                    Check(freight.stackCount==19&&freight.Position.DistanceToSquared(dualB.PlatformCell(0))<=25,"freight delivered intact within destination platform unload radius");
                    freight.Destroy();dualA.SetPlatformSettings(0,new(){automatic=true,minimumTicks=30,maximumTicks=90});Park(South,0);
                    BeginFreight(South,0,1,11,new(40,0,94),new(40,0,93));Phase(17);break;
                case 17:
                    if(GenTicks.TicksGame-started<120)break;
                    Check(South.index>0&&South.dwellElapsed==90,"automatic maximum departs while loader is still working");Phase(18);break;
                case 18:
                    if(!Rail.Freight.Any(f=>f.train==South.train))break;loader.drafter.Drafted=true;Phase(19);break;
                case 19:
                    if(!freight.Spawned||Rail.Freight.Count!=0)break;
                    Check(freight.stackCount==11,"late loading order waits for next visit without losing cargo");freight.Destroy();
                    dualA.SetPlatformSettings(0,new(){automatic=true,minimumTicks=120,maximumTicks=900});
                    dualA.SetPlatformSettings(1,new(){automatic=true,minimumTicks=300,maximumTicks=600});Park(North,0);
                    Position(rider,new(37,0,103));goal=new(145,0,103);cargo=ThingMaker.MakeThing(ThingDefOf.Steel);cargo.stackCount=25;GenSpawn.Spawn(cargo,new(36,0,103),map);
                    var stock=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,map.zoneManager);stock.settings.filter.SetDisallowAll();stock.settings.filter.SetAllow(ThingDefOf.Steel,true);map.zoneManager.RegisterZone(stock);stock.AddCell(goal);
                    Check(goal.IsValidStorageFor(map,cargo),"isolated native destination stockpile is clear and accepts steel");
                    var job=JobMaker.MakeJob(JobDefOf.HaulToCell,cargo,goal);job.count=25;job.haulMode=HaulMode.ToCellStorage;job.haulOpportunisticDuplicates=false;
                    Check(rider.jobs.TryTakeOrderedJob(job),"native hauling job accepted for multi-platform passenger");originalJob=rider.CurJob.loadID;Phase(25);break;
                case 25:
                    if(rider.CurJob?.loadID!=originalJob)throw new Exception($"Native hauling job changed before boarding: cargo at {cargo.Position}, spawned {cargo.Spawned}, carried {rider.carryTracker.CarriedThing}, goal {goal}, current {rider.CurJob}, plans {Rail.PlanAttempts}.");
                    if(Rail.ActiveTrip(rider)?.phase!=TransitPhase.Riding)break;
                    var trip=Rail.ActiveTrip(rider);Check(trip.serviceId==northId&&trip.startPlatform==1&&trip.endPlatform==1&&rider.carryTracker.CarriedThing==cargo&&rider.CurJob.loadID==originalJob,
                        "carrying passenger boards platform 2 and retains same original hauling job and steel");Phase(26);break;
                case 26:
                    if(!cargo.Spawned||cargo.Position!=goal)break;
                    Check(cargo.stackCount==25&&rider.carryTracker.CarriedThing==null,"platform 2 hauling passenger delivers exactly 25 steel to original stockpile");rider.drafter.Drafted=true;cargo.Destroy();SetupRing();laps=0;previousIndex=Circle.index;Phase(30);break;
                case 30:
                    CheckDirection();if(previousIndex==Circle.path.Count-1&&Circle.index==0)laps++;previousIndex=Circle.index;if(laps<2)break;
                    Check(Circle.direction==1,"empty loop crosses closing edge twice without endpoint reversal");RemoveTrack(new(38,0,70));Phase(31);break;
                case 31:
                    if(GenTicks.TicksGame-started<5)break;Check(Circle.fault!=null,"removing closing arc suspends loop rather than retracing outbound route");Spawn(new(38,0,70));Phase(32);break;
                case 32:
                    if(GenTicks.TicksGame-started<5)break;Check(Circle.Available,"repair restores same loop train");
                    ring[3].SetPlatformSettings(0,new(){automatic=true,minimumTicks=300,maximumTicks=900});Park(Circle,3);
                    BeginFreight(Circle,3,0,23,new(40,0,88),new(40,0,89));BeginGoto(new(42,0,88),new(35,0,61));Phase(41);break;
                case 41:
                    if(Rail.ActiveTrip(rider)?.phase!=TransitPhase.Riding||!Rail.Freight.Any(f=>f.train==Circle.train))break;
                    Check(Rail.ActiveTrip(rider).start==ring[3]&&Rail.ActiveTrip(rider).end==ring[0]&&Circle.direction==1,"passenger and freight board last ring stop towards first");loader.drafter.Drafted=true;Phase(42);break;
                case 42:
                    if(rider.Position==goal)rider.drafter.Drafted=true;if(!rider.Drafted||!freight.Spawned||Rail.Freight.Count!=0)break;
                    Check(freight.stackCount==23&&freight.Position.DistanceToSquared(ring[0].Position)<=25,"loop passenger resumes original target and freight unloads after closing edge");freight.Destroy();
                    ring[0].SetPlatformSettings(0,new(){automatic=true,minimumTicks=300,maximumTicks=900});
                    Check(Rail.ConfigureService(ring.Select(s=>new RailStop(s)).ToList(),true,true,Circle,"Perimeter loop",5,out _),"existing loop switches reverse direction with same train");Park(Circle,0);
                    BeginFreight(Circle,0,3,29,new(40,0,62),new(40,0,61));BeginGoto(new(42,0,62),new(35,0,89));Phase(51);break;
                case 51:
                    if(Rail.ActiveTrip(rider)?.phase!=TransitPhase.Riding||!Rail.Freight.Any(f=>f.train==Circle.train))break;
                    if(Visual&&!screenshotsComplete)break;
                    Check(Circle.direction==-1&&Circle.reverseLoop&&Rail.ActiveTrip(rider).end==ring[3],"reverse loop carries passenger and freight toward last listed stop");loader.drafter.Drafted=true;
                    savedTrainIds=Rail.Services.Select(s=>s.train.thingIDNumber).ToList();GameDataSaveLoader.SaveGame("LocalRailNetworkInTransit");
                    Check(File.Exists(GenFilePaths.FilePathForSavedGame("LocalRailNetworkInTransit")),"saved colored multi-platform routes, automatic dwell and reverse-loop freight in transit");Finish();break;
                case 110:
                    if(GenTicks.TicksGame-started<5)break;
                    Check(Rail.Services.Count==3&&Rail.Services.Select(s=>s.train.thingIDNumber).SequenceEqual(savedTrainIds),"load restores exact three permanent trains without duplication");
                    Check(dualA.PlatformCount==2&&four.PlatformCount==4&&North.platforms.SequenceEqual(new[]{1,1}),"load restores multi-platform stations and explicit route platform indices");
                    Check(South.Name=="Farm shuttle"&&North.colorIndex==2&&Circle.Name=="Perimeter loop"&&Circle.colorIndex==5&&Circle.loop&&Circle.reverseLoop,"load preserves names, colors and reverse loop mode");
                    Check(dualA.PlatformSettings(0).automatic&&dualA.PlatformSettings(0).minimumTicks==120&&dualA.PlatformSettings(0).maximumTicks==900,"load preserves per-platform automatic dwell bounds");
                    Check(Rail.Freight.Single().contents.Contains(freight)&&freight.stackCount==29&&Rail.Freight.Single().train==Circle.train,"load retains actual 29 steel in reverse-loop train holder");
                    Check(rider.CurJob?.loadID==originalJob&&rider.pather.Moving&&rider.pather.Destination.Cell==goal&&Rail.ActiveTrip(rider)==null,"load releases rider onto original native job and target");Phase(111);break;
                case 111:
                    if(rider.Position==goal)rider.drafter.Drafted=true;if(!rider.Drafted||!freight.Spawned||Rail.Freight.Count!=0)break;
                    Check(freight.stackCount==29&&freight.Position.DistanceToSquared(ring[3].Position)<=25&&Circle.direction==-1,
                        $"loaded reverse loop delivers all freight at intended stop and passenger reaches original goal (qty {freight.stackCount}, at {freight.Position}, stop {ring[3].Position}, direction {Circle.direction})");Finish();break;
            }
        }
        catch(Exception ex){Fail(ex);}
    }
    private void CheckDirection(){if(Circle.direction!=1)throw new Exception("Loop reversed at a terminal");}
    private void Finish()
    {
        phase=200;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=true,tests=passed.Count,passed,
            routes=Rail.Services.Select(s=>new{s.id,name=s.Name,s.loop,s.reverseLoop,s.colorIndex,s.platforms,s.fault,train=s.train.thingIDNumber}),Rail.PeakPlansPerTick}));
        Log.Message("[RailNetworkSmoke] COMPLETED "+passed.Count+" checks.");Root.Shutdown();
    }
    private void Fail(Exception ex)
    {
        phase=201;File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(new{completed=false,passed,error=ex.ToString()}));
        Log.Error("[RailNetworkSmoke] FAILED "+ex);Root.Shutdown();
    }
}
