using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace LocalRail;

// Only explicit fixture flags in the dedicated profile can alter a test map.
public class PhysicalSmokeTest : MapComponent
{
    internal static bool Load=>GenCommandLine.CommandLineArgPassed("localrail-physical-load-smoke");
    internal static bool Enabled=>(Load||GenCommandLine.CommandLineArgPassed("localrail-physical-smoke"))&&
        GenCommandLine.TryGetCommandLineArg("savedatafolder",out string path)&&Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)).StartsWith("LocalRailTestPhysical",StringComparison.Ordinal);
    private bool Visual=>GenCommandLine.CommandLineArgPassed("localrail-visual-smoke");
    private RailMapComponent Rail=>map.GetComponent<RailMapComponent>();
    private RailServiceState Service=>Rail.Service(serviceId);
    private Building_RailStation a,b;
    private List<Building_RailDoor> doors=new();
    private List<Pawn> riders=new();
    private Thing steel;
    private int phase,started,serviceId,trainId,frames;
    private bool advanceOpen,crossedClosed,captured,walkerOpened;
    private readonly List<string> passed=new();
    private readonly HashSet<Pawn> arrived=new();
    public PhysicalSmokeTest(Map map):base(map){}
    public override void ExposeData()
    {
        Scribe_References.Look(ref a,"physicalA");Scribe_References.Look(ref b,"physicalB");Scribe_References.Look(ref steel,"physicalSteel");
        Scribe_Collections.Look(ref doors,"physicalDoors",LookMode.Reference);Scribe_Collections.Look(ref riders,"physicalRiders",LookMode.Reference);
        Scribe_Values.Look(ref serviceId,"physicalService");Scribe_Values.Look(ref trainId,"physicalTrain");
        if(Scribe.mode==LoadSaveMode.PostLoadInit&&Enabled){phase=100;started=GenTicks.TicksGame;}
    }
    private void Check(bool value,string text){if(!value)throw new Exception(text);passed.Add(text);Log.Message("[RailPhysicalSmoke] PASS "+text);}
    private void Phase(int value){phase=value;started=GenTicks.TicksGame;}
    private Building Spawn(string def,IntVec3 cell,ThingDef stuff=null)
    {var definition=DefDatabase<ThingDef>.GetNamed(def);var building=(Building)ThingMaker.MakeThing(definition,stuff??(definition.MadeFromStuff?ThingDefOf.Steel:null));building.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(building,cell,map);return building;}
    private void Move(Pawn pawn,IntVec3 cell)
    {pawn.drafter.Drafted=false;pawn.jobs.EndCurrentJob(JobCondition.InterruptForced,false);pawn.pather.StopDead();pawn.Position=cell;pawn.pather.ResetToCurrentPosition();}
    private void Park(IntVec3 cell)
    {Service.train.Position=cell;Service.index=Service.path.IndexOf(cell);Service.direction=1;Service.animating=false;Service.dwell=0;Service.ticksLeft=1;Service.fault=null;}
    private void Setup()
    {
        RailMod.Settings.automatic=true;RailMod.Settings.preferRail=true;RailMod.Settings.minDistance=25;RailMod.Settings.diagnostics=true;
        // This is a freshly generated isolated map. Remove leftover ruin roofs
        // before clearing their supports, so the fixture does not trigger collapse.
        foreach(var cell in map.AllCells)map.roofGrid.SetRoof(cell,null);
        foreach(var cell in new CellRect(25,55,160,70))
        {
            map.roofGrid.SetRoof(cell,null);
            foreach(var thing in cell.GetThingList(map).Where(t=>t is Building or Plant || t.def.category==ThingCategory.Item).ToArray())if(thing.def.destroyable)thing.Destroy();else thing.DeSpawn();
            map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.fogGrid.Unfog(cell);
        }
        foreach(var pawn in map.mapPawns.FreeColonistsSpawned){pawn.drafter.Drafted=true;pawn.Position=new(30,0,120);pawn.pather.ResetToCurrentPosition();}
        a=(Building_RailStation)Spawn("LR_PlatformStation",new(40,0,90));b=(Building_RailStation)Spawn("LR_PlatformStation",new(140,0,90));
        foreach(int x in new[]{55,80,105})doors.Add((Building_RailDoor)Spawn("LR_RailDoor",new(x,0,90)));
        for(int x=42;x<=138;x++)if(!doors.Any(d=>d.Position.x==x))Spawn("LR_Track",new(x,0,90));
        for(int x=55;x<=105;x++){Spawn("Wall",new(x,0,88),ThingDefOf.Steel);Spawn("Wall",new(x,0,92),ThingDefOf.Steel);}
        foreach(int x in new[]{55,80,105})foreach(int z in new[]{89,91})Spawn("Wall",new(x,0,z),ThingDefOf.Steel);
        for(int x=56;x<105;x++)for(int z=89;z<=91;z++)map.roofGrid.SetRoof(new(x,0,z),RoofDefOf.RoofConstructed);
        foreach(int x in new[]{60,75,85,100})Spawn("TorchLamp",new(x,0,91)).GetComp<CompRefuelable>()?.Refuel(100);
        string[] terrains={"Soil","Sand","Gravel","Concrete"};
        for(int x=42;x<=138;x++)map.terrainGrid.SetTerrain(new(x,0,90),DefDatabase<TerrainDef>.GetNamed(terrains[Math.Min(3,(x-42)/25)]));
        var track=DefDatabase<ThingDef>.GetNamed("LR_Track");var door=DefDatabase<ThingDef>.GetNamed("LR_RailDoor");
        Check(track.IsEdifice()&&track.BlocksPlanting(),"track is a walkable physical edifice that blocks planting");
        Check(!GenConstruct.CanPlaceBlueprintAt(track,new(80,0,88),Rot4.North,map).Accepted,"track blueprint refuses a wall cell");
        Check(!GenConstruct.CanPlaceBlueprintAt(track,new(42,0,90),Rot4.North,map).Accepted,"track blueprint refuses duplicate track");
        Check(!GenConstruct.CanPlaceBlueprintAt(track,doors[1].Position,Rot4.North,map).Accepted,"track blueprint refuses overlap under a rail door");
        Check(GenConstruct.CanPlaceBlueprintAt(ThingDefOf.PowerConduit,new(42,0,90),Rot4.North,map).Accepted&&
            GenConstruct.CanPlaceBlueprintAt(ThingDefOf.PowerConduit,doors[1].Position,Rot4.North,map).Accepted,
            "vanilla buried power conduit can cross track and rail door for enclosed-colony lighting");
        Check(new IntVec3(42,0,90).GetMaxItemsAllowedInCell(map)==0&&doors[1].Position.GetMaxItemsAllowedInCell(map)==0,
            "track and rail door cells reject item storage that would hide the rails");
        Check(door.designationCategory.defName=="Structure"&&door.designationCategory.ResolvedAllowedDesignators.OfType<Designator_Build>().Any(d=>d.PlacingDef==door)&&door.costStuffCount==40,"metal rail door appears in actual Structure menu with material cost");
        Check(door.IsDoor&&doors[1].IsAirtight&&doors.All(d=>Rail.HasRail(d.Position)),"native airtight doors contribute their own rail cells");
        var designator=track.designationCategory.ResolvedAllowedDesignators.OfType<Designator_Build>().Single(d=>d.PlacingDef==track);
        Find.DesignatorManager.Select(designator);Check(Find.DesignatorManager.SelectedStyle.defName=="LR_DiagonalLine","native designator defaults to diagonal-capable drag style");
        var line=new List<IntVec3>();Find.DesignatorManager.SelectedStyle.DrawStyleWorker.Update(new(30,0,60),new(40,0,70),line);
        Check(line.Count==11&&line[1]==new IntVec3(31,0,61)&&line[^1]==new IntVec3(40,0,70),"default native draw worker generates a continuous diagonal drag");Find.DesignatorManager.Deselect();
        Check(Enumerable.Range(43,95).All(x=>Rail.HasConnection(new(x-1,0,90),new(x,0,90))),"terrain boundaries and integrated doors preserve track connections");
        var turnA=(Building_RailStation)Spawn("LR_PlatformStation",new(40,0,60));var turnB=(Building_RailStation)Spawn("LR_PlatformStation",new(65,0,80));
        Spawn("LR_RailDoor",new(60,0,60));for(int x=42;x<60;x++)Spawn("LR_Track",new(x,0,60));
        for(int z=61;z<=80;z++)Spawn("LR_Track",new(60,0,z));for(int x=61;x<=63;x++)Spawn("LR_Track",new(x,0,80));
        Check(!Rail.ConfigureService(new(){new(turnA),new(turnB)},false,false,null,"Invalid door curve",0,out var turnReason)&&!string.IsNullOrWhiteSpace(turnReason),
            "route rejects right-angle turn inside door and reports straight-entry requirement");
        var water=new IntVec3(60,0,110);map.terrainGrid.SetTerrain(water,DefDatabase<TerrainDef>.GetNamed("WaterDeep"));
        Check(!GenConstruct.CanPlaceBlueprintAt(track,water,Rot4.North,map).Accepted,"deep water rejects unsupported track");
        map.terrainGrid.SetTerrain(water,DefDatabase<TerrainDef>.GetNamed("Bridge"));Check(GenConstruct.CanPlaceBlueprintAt(track,water,Rot4.North,map).Accepted,"bridge permits supported track");
        for(int i=0;i<4;i++)
        {
            var pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,allowAddictions:false,
                validatorPreGear:p=>!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)&&!p.WorkTypeIsDisabled(WorkTypeDefOf.Construction)));
            GenSpawn.Spawn(pawn,new IntVec3(35+i,0,90),map);riders.Add(pawn);
        }
        var treeCell=new IntVec3(46,0,110);var tree=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Plant_TreeOak"));GenSpawn.Spawn(tree,treeCell,map);((Plant)tree).Growth=1;
        var bp=GenConstruct.PlaceBlueprintForBuild(track,treeCell,map,Rot4.North,Faction.OfPlayer,null);
        Check(GenConstruct.FirstBlockingThing(bp,riders[0])==tree,"native construction requires clearing mature tree before track completion");tree.Destroy();
        Check(bp.TryReplaceWithSolidThing(riders[0],out var solid,out _)&&solid is Frame,"cleared track blueprint enters native frame construction");
        ((Frame)solid).CompleteConstruction(riders[0]);Check(treeCell.GetEdifice(map)?.def==track,"native completed track owns the physical building cell");
        a.SetPlatformSettings(0,new(){automatic=true,minimumTicks=300,maximumTicks=900});
        Check(Rail.ConfigureService(new(){new(a),new(b)},false,false,null,"Enclosed corridor",2,out _),"route validates through closed rail doors");serviceId=Rail.Services[^1].id;trainId=Service.train.thingIDNumber;
        var stockpile=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,map.zoneManager);stockpile.settings.filter.SetDisallowAll();stockpile.settings.filter.SetAllow(ThingDefOf.Steel,true);
        map.zoneManager.RegisterZone(stockpile);stockpile.AddCell(new(145,0,90));steel=ThingMaker.MakeThing(ThingDefOf.Steel);steel.stackCount=25;GenSpawn.Spawn(steel,new(35,0,90),map);
        var haul=JobMaker.MakeJob(JobDefOf.HaulToCell,steel,new IntVec3(145,0,90));haul.count=25;haul.haulMode=HaulMode.ToCellStorage;
        Check(riders[0].jobs.TryTakeOrderedJob(haul),"native hauling passenger starts with real 25 steel");
        for(int i=1;i<4;i++)Check(riders[i].jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(145,0,90))),"native passenger "+i+" starts long journey");
        Phase(10);
    }
    public override void MapComponentUpdate()
    {
        if(!Enabled||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||phase>=200)return;
        if(phase==0){try{Setup();}catch(Exception ex){Fail(ex);return;}}
        if(Visual&&phase==11&&!captured)
        {
            Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            if(frames==0){Find.Selector.ClearSelection();Find.CameraDriver.SetRootPosAndSize(Service.train.DrawPos,12);}
            if(frames++==20)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-physical-train.png"));
            if(frames==60){Find.CameraDriver.SetRootPosAndSize(new IntVec3(80,0,90).ToVector3Shifted(),32);}
            if(frames==80)ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-terrain-door.png"));
            if(frames<120)return;captured=true;
        }
        Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    }
    public override void MapComponentTick()
    {
        if(!Enabled||phase==0||phase>=200)return;
        try
        {
            foreach(var pawn in riders)
            {if(pawn.needs?.food!=null)pawn.needs.food.CurLevelPercentage=1;if(pawn.needs?.rest!=null)pawn.needs.rest.CurLevelPercentage=1;if(pawn.needs?.mood!=null)pawn.needs.mood.CurLevelPercentage=1;}
            if(GenTicks.TicksGame-started>12000)throw new Exception($"physical phase {phase} timeout, train {Service?.index}, fault {Service?.fault}, riding {riders.Count(p=>Rail.ActiveTrip(p)?.phase==TransitPhase.Riding)}");
            switch(phase)
            {
                case 10:
                    if(!riders.All(p=>Rail.ActiveTrip(p)?.phase==TransitPhase.Riding))break;
                    Check(riders.Select(p=>Rail.ActiveTrip(p).seat).Distinct().Count()==4,"four native pawns occupy distinct train seats");
                    var positions=riders.Select(p=>{Rail.TryVisualPosition(p,out var pos);return pos;}).ToArray();
                    Check(positions.SelectMany((v,i)=>positions.Skip(i+1).Select(w=>(v-w).magnitude)).Min()>.60f,"passenger positions are visually separated on deck");
                    var parms=(PawnDrawParms)AccessTools.Method(typeof(PawnRenderer),"GetDrawParms").Invoke(riders[0].Drawer.renderer,new object[]{Vector3.zero,0f,Rot4.South,RotDrawMode.Fresh,PawnRenderFlags.None});
                    Check(parms.matrix.lossyScale.x<.9f&&parms.matrix.lossyScale.x>.4f&&riders[0].carryTracker.CarriedThing==steel,"native pawn render tree scales only seated world sprite and keeps carried steel");
                    var portrait=(PawnDrawParms)AccessTools.Method(typeof(PawnRenderer),"GetDrawParms").Invoke(riders[0].Drawer.renderer,new object[]{Vector3.zero,0f,Rot4.South,RotDrawMode.Fresh,PawnRenderFlags.Portrait});
                    Check(Math.Abs(portrait.matrix.lossyScale.x-1)<.001f,"riding pawn portrait retains original scale");
                    doors[1].SetForbidden(true);Phase(12);break;
                case 12:
                    if(Service.fault!="LR_DoorWaiting"||Service.train.Position!=new IntVec3(77,0,90))break;
                    Check(riders.All(p=>Rail.ActiveTrip(p)?.phase==TransitPhase.Riding)&&riders[0].carryTracker.CarriedThing==steel,
                        "occupied train waits at forbidden door retaining all passengers and carried resources");
                    doors[1].SetForbidden(false);Phase(11);break;
                case 11:
                    foreach(var d in doors)
                    {
                        if(d.Open&&Service.train.Position.x<=d.Position.x-2&&Service.direction==1)advanceOpen=true;
                        if(Service.train.Position==d.Position&&!d.TrainReady)crossedClosed=true;
                        if(Service.animating&&Service.path[Service.NextIndex]==d.Position&&!d.TrainReady)crossedClosed=true;
                    }
                    foreach(var p in riders)
                        if(p.Position==new IntVec3(145,0,90)&&Rail.ActiveTrip(p)==null&&(p!=riders[0]||p.carryTracker.CarriedThing==null))
                        {arrived.Add(p);p.drafter.Drafted=true;}
                    if(arrived.Count<4)break;
                    Check(advanceOpen&&!crossedClosed,"fast occupied train requests doors ahead and never enters before fully open");
                    Check(steel.Spawned&&steel.stackCount==25&&riders[0].carryTracker.CarriedThing==null,"hauling passenger delivers all carried resources through enclosed corridor");
                    Check(riders.All(p=>!RailRenderState.Riding(p)),"passenger rendering state clears on alighting");
                    var normal=(PawnDrawParms)AccessTools.Method(typeof(PawnRenderer),"GetDrawParms").Invoke(riders[0].Drawer.renderer,new object[]{Vector3.zero,0f,Rot4.South,RotDrawMode.Fresh,PawnRenderFlags.None});
                    Check(Math.Abs(normal.matrix.lossyScale.x-1)<.001f,"alighted pawn world rendering restores original scale");
                    foreach(var p in riders)p.drafter.Drafted=true;Rail.SetServiceOperating(Service,false);Park(new(140,0,90));Phase(20);break;
                case 20:
                    if(GenTicks.TicksGame-started<120)break;
                    Check(doors.All(d=>!d.Open),"all three rail doors automatically close after train tail clears");
                    Check(new IntVec3(70,0,91).GetRoom(map)!=new IntVec3(90,0,91).GetRoom(map)&&new IntVec3(70,0,91).GetRoom(map).PsychologicallyOutdoors==false,
                        "closed native rail doors preserve separate enclosed roofed rooms");
                    Move(riders[1],new(78,0,91));Check(riders[1].jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(82,0,91))),"walking colonist starts native route through rail door");Phase(21);break;
                case 21:
                    walkerOpened|=doors[1].Open;if(riders[1].Position!=new IntVec3(82,0,91))break;
                    Check(walkerOpened&&Rail.ActiveTrip(riders[1])==null,"colonist opens and crosses door normally without boarding");riders[1].drafter.Drafted=true;Phase(22);break;
                case 22:
                    if(GenTicks.TicksGame-started<400)break;
                    doors[1].SetForbidden(true);Park(new(77,0,90));Rail.SetServiceOperating(Service,true);Phase(23);break;
                case 23:
                    if(GenTicks.TicksGame-started<30)break;
                    Check(Service.train.Position==new IntVec3(77,0,90)&&Service.fault=="LR_DoorWaiting"&&!doors[1].Open,"forbidden door holds empty train nose clear without forcing opening");
                    doors[1].SetForbidden(false);Phase(24);break;
                case 24:
                    if(Service.train.Position.x<82)break;
                    Check(Service.fault==null&&doors[1].Open,"empty train resumes after door permission is restored");
                    Rail.SetServiceOperating(Service,false);Park(doors[1].Position);doors[1].SetForbidden(true);Phase(25);break;
                case 25:
                    if(GenTicks.TicksGame-started<80)break;
                    Check(doors[1].Open&&doors[1].TicksTillFullyOpened==0,"paused car occupying forbidden door prevents door closing through body");
                    doors[1].SetForbidden(false);GameDataSaveLoader.SaveGame("LocalRailPhysicalGate");Check(File.Exists(GenFilePaths.FilePathForSavedGame("LocalRailPhysicalGate")),"save records real door and paused train inside enclosed corridor");Finish();break;
                case 100:
                    if(GenTicks.TicksGame-started<10)break;
                    Check(Service.train.thingIDNumber==trainId&&Rail.Services.Count==1&&!Service.operating,"load preserves exact paused train and operating route");
                    Check(doors.Count==3&&doors.All(d=>d.Spawned&&Rail.HasRail(d.Position))&&doors[1].IsAirtight,"load rebuilds door track registry and physical airtight doors");
                    Check(doors[1].Open&&Service.train.Position==doors[1].Position,"load protects door occupied by saved car");
                    Check(steel.Spawned&&steel.stackCount==25,"load retains delivered real resource stack");Rail.SetServiceOperating(Service,true);Phase(101);break;
                case 101:
                    if(Service.train.Position.x<120)break;
                    Check(!doors[1].Open&&Service.fault==null,"loaded empty train clears door and automatic sealing resumes");
                    doors[1].Destroy();Phase(102);break;
                case 102:
                    if(GenTicks.TicksGame-started<5)break;
                    Check(!Rail.HasRail(new(80,0,90))&&Service.fault!=null,"destroying integrated door removes track and suspends route");Finish();break;
            }
        }
        catch(Exception ex){Fail(ex);}
    }
    private void Finish(){phase=200;Write(new{completed=true,tests=passed.Count,passed,advanceOpen,crossedClosed,PeakPlansPerTick=Rail.PeakPlansPerTick});}
    private void Fail(Exception ex){phase=201;Write(new{completed=false,passed,error=ex.ToString()});Log.Error("[RailPhysicalSmoke] "+ex);}
    private void Write(object result){File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"smoke-result.json"),System.Text.Json.JsonSerializer.Serialize(result));Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Root.Shutdown();}
}
