using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LocalRail;

[HarmonyPatch(typeof(Root_Entry), nameof(Root_Entry.Update))]
internal static class SmokeBootstrap
{
    private static bool queued;
    private static void Postfix()
    {
        if (queued || (!SmokeTest.Enabled&&!NetworkSmokeTest.Enabled&&!PhysicalSmokeTest.Enabled&&!WarehouseSmokeTest.Enabled&&!RetrofitSmokeTest.Enabled&&!OperationsSmokeTest.Enabled&&!DemandSmokeTest.Enabled) || !PlayDataLoader.Loaded || LongEventHandler.AnyEventNowOrWaiting) return;
        queued = true;
        if(DemandSmokeTest.Load)GameDataSaveLoader.LoadGame("LocalRailDemandInTransit");
        else if(OperationsSmokeTest.Load)GameDataSaveLoader.LoadGame("LocalRailOperationsInTransit");
        else if(WarehouseSmokeTest.Load)GameDataSaveLoader.LoadGame("LocalRailWarehouseInTransit");
        else if(PhysicalSmokeTest.Load)GameDataSaveLoader.LoadGame("LocalRailPhysicalGate");
        else if(NetworkSmokeTest.Load)GameDataSaveLoader.LoadGame("LocalRailNetworkInTransit");
        else if (SmokeTest.LegacyLoad) GameDataSaveLoader.LoadGame("LocalRailSmokeLegacy");
        else if (GenCommandLine.CommandLineArgPassed("localrail-previous-load-smoke")) GameDataSaveLoader.LoadGame("LocalRailSmokePrevious");
        else if (GenCommandLine.CommandLineArgPassed("localrail-load-smoke")) GameDataSaveLoader.LoadGame("LocalRailSmokeInTransit");
        else LongEventHandler.QueueLongEvent(() => { }, "Play", "GeneratingMap", true, null);
    }
}

// Opt-in, isolated real-engine harness; no test-map changes in ordinary gameplay.
public class SmokeTest : MapComponent
{
    internal static bool JunctionOnly => GenCommandLine.CommandLineArgPassed("localrail-junction-smoke");
    internal static bool LegacyLoad => GenCommandLine.CommandLineArgPassed("localrail-legacy-load-smoke");
    internal static bool Enabled { get; } = ReadEnabled();
    private static bool ReadEnabled() => (GenCommandLine.CommandLineArgPassed("localrail-smoke") ||
        GenCommandLine.CommandLineArgPassed("localrail-load-smoke") || GenCommandLine.CommandLineArgPassed("localrail-previous-load-smoke") || LegacyLoad || JunctionOnly) &&
        GenCommandLine.TryGetCommandLineArg("savedatafolder", out string path) &&
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)).StartsWith("LocalRailTest", StringComparison.Ordinal);
    private Pawn pawn;
    private Thing cargo, mine, freightThing, cornerWall;
    private Building_RailStation middle;
    private Pawn diagonalPawn,loader;
    private bool diagonalRode,diagonalAnimated,routeCaptured,diagonalCaptured;
    private int routeFrames,diagonalFrames;
    private bool junctionCaptured;
    private int junctionFrames;
    private RailRouteEditor routeEditor;
    private IntVec3 from, goal, gap, pausedPosition;
    private int phase, phaseTick, originalJob, expectedTrainId, captureFrames;
    private bool sawRide, checkedCargo, checkedAnimation, captured, sawQueue, checkedMine;
    private readonly List<string> passed = new();
    private readonly List<Pawn> commuters = new();
    private readonly HashSet<Pawn> commutersRode = new(), commutersArrived = new();
    private RailServiceState secondary;
    private RailMapComponent Rail => map.GetComponent<RailMapComponent>();
    private RailServiceState Primary => Rail.Services.FirstOrDefault();
    private int TrainCount => map.listerThings.AllThings.OfType<Building_RailTrain>().Count();
    public SmokeTest(Map map) : base(map) { }
    public override void ExposeData()
    {
        Scribe_References.Look(ref pawn, "smokePawn"); Scribe_References.Look(ref cargo, "smokeCargo");
        Scribe_References.Look(ref freightThing,"smokeFreightThing");
        Scribe_Values.Look(ref from, "smokeFrom"); Scribe_Values.Look(ref goal, "smokeGoal");
        Scribe_Values.Look(ref originalJob, "smokeOriginalJob"); Scribe_Values.Look(ref expectedTrainId, "smokeTrainId");
        if (Scribe.mode == LoadSaveMode.PostLoadInit && Enabled && pawn != null)
        { phase = 20; phaseTick = GenTicks.TicksGame; RailMod.Settings.diagnostics = true; }
    }
    public override void MapComponentUpdate()
    {
        if (!Enabled || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
        if (JunctionOnly)
        {
            Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            if(junctionFrames==0)
            {
                from=new(38,0,map.Center.z);SetupJunctionGallery();Find.Selector.ClearSelection();
                Find.CameraDriver.SetRootPosAndSize(new IntVec3(187,0,from.z+33).ToVector3Shifted(),18);phase=102;
            }
            if(junctionFrames++==20)UnityEngine.ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-junctions.png"));
            if(junctionFrames==60)Finish();
            return;
        }
        if (phase == 0)
        {
            try { Setup(); } catch (Exception ex) { Fail(ex); return; }
        }
        if (GenCommandLine.CommandLineArgPassed("localrail-visual-smoke") && !captured &&
            (captureFrames > 0 || phase == 14 && Rail.ActiveTrip(pawn)?.phase == TransitPhase.Riding && Primary.index >= 10))
        {
            if(captureFrames==0){Find.Selector.ClearSelection();Find.Selector.Select(Primary.a);}
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Find.CameraDriver.SetRootPosAndSize(pawn.Position.ToVector3Shifted(), 8);
            if (captureFrames++ == 20) UnityEngine.ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath, "rail-poc.png"));
            if (captureFrames < 60) return;
            captured = true;
        }
        if(GenCommandLine.CommandLineArgPassed("localrail-visual-smoke") && !routeCaptured && phase==62)
        {
            Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            if(routeFrames==0)
            {
                Find.CameraDriver.SetRootPosAndSize(new IntVec3(45,0,from.z).ToVector3Shifted(),65);
                routeEditor=new(Rail,Primary.a,Primary);Find.WindowStack.Add(routeEditor);
            }
            if(routeFrames++==20)UnityEngine.ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-route-editor.png"));
            if(routeFrames<60)return;
            routeEditor.Close();routeCaptured=true;
        }
        if(GenCommandLine.CommandLineArgPassed("localrail-visual-smoke") && !diagonalCaptured && phase==66 &&
            (diagonalFrames>0 || Rail.ActiveTrip(diagonalPawn)?.phase==TransitPhase.Riding && secondary.index>=5))
        {
            Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            if(diagonalFrames==0){Find.Selector.ClearSelection();Find.Selector.Select(secondary.train);Find.CameraDriver.SetRootPosAndSize(secondary.train.Position.ToVector3Shifted(),8);}
            if(diagonalFrames++==20)UnityEngine.ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-diagonal.png"));
            if(diagonalFrames<60)return;
            diagonalCaptured=true;
        }
        if(GenCommandLine.CommandLineArgPassed("localrail-visual-smoke") && diagonalCaptured && !junctionCaptured && phase==66)
        {
            Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            if(junctionFrames==0)
            {
                SetupJunctionGallery();Find.Selector.ClearSelection();
                Find.CameraDriver.SetRootPosAndSize(new IntVec3(187,0,from.z+33).ToVector3Shifted(),18);
            }
            if(junctionFrames++==20)UnityEngine.ScreenCapture.CaptureScreenshot(Path.Combine(GenFilePaths.SaveDataFolderPath,"rail-junctions.png"));
            if(junctionFrames<60)return;
            junctionCaptured=true;
        }
        Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
    }
    private void SetupJunctionGallery()
    {
        var area=new CellRect(165,from.z+15,42,37);
        foreach(var cell in area)
        {
            map.roofGrid.SetRoof(cell,null);
            foreach(var thing in cell.GetThingList(map).Where(t=>t is Building or Plant||t.def.category==ThingCategory.Item).ToArray())
                if(thing.def.destroyable)thing.Destroy();else thing.DeSpawn();
            map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.fogGrid.Unfog(cell);
        }
        foreach(var (x,z,dx,dz) in new[]{(177,23,1,1),(197,23,-1,1),(177,43,1,-1),(197,43,-1,-1)})
        {
            var station=new IntVec3(x,0,from.z+z);SpawnRail(station,"LR_PlatformStation");
            var port=station+new IntVec3(dx,0,0);
            for(int step=1;step<=5;step++)SpawnRail(port+new IntVec3(dx*step,0,dz*step));
            for(int step=1;step<=3;step++)SpawnRail(port+new IntVec3(dx*(5+step),0,dz*5));
        }
        map.mapDrawer.RegenerateEverythingNow();
    }
    private void Setup()
    {
        RailMod.Settings.automatic = true; RailMod.Settings.diagnostics = true;
        RailMod.Settings.preferRail = true; RailMod.Settings.minDistance = 25; RailMod.Settings.smoothAnimation = true;
        int z = map.Center.z; from = new(38,0,z); goal = new(142,0,z);
        for(int x=30;x<=148;x++)for(int dz=-3;dz<=12;dz++) map.roofGrid.SetRoof(new(x,0,z+dz),null);
        for (int x = 30; x <= 148; x++)
        for (int dz = -3; dz <= 12; dz++)
        {
            var cell = new IntVec3(x,0,z+dz);
            foreach (var thing in cell.GetThingList(map).Where(t => t is Building || t is Plant).ToArray())
                if(thing.def.destroyable)thing.Destroy();else thing.DeSpawn();
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete); map.fogGrid.Unfog(cell);
        }
        // Prevent native starter colonists from taking the test steel or standing on the rail.
        foreach (var colonist in map.mapPawns.FreeColonistsSpawned)
        {
            colonist.drafter.Drafted = true; colonist.pather.StopDead();
            colonist.Position = new(31,0,z+11); colonist.pather.ResetToCurrentPosition();
        }
        BuildLine(z);
        var platform = DefDatabase<ThingDef>.GetNamed("LR_PlatformStation");
        Require(platform.size == new IntVec2(3,3), "new station has real 3x3 footprint");
        Require(platform.designationCategory?.defName=="Misc" && platform.designationCategory.ResolvedAllowedDesignators
            .OfType<Designator_Build>().Any(d=>d.PlacingDef==platform),"3x3 station appears in normal Misc architect designators");
        Require(!Station(40,z).GetInspectString().Split('\n').Any(string.IsNullOrWhiteSpace),"unconfigured station inspect string has no empty lines");
        Require(Rail.HasRail(new(39,0,z)) && Rail.HasRail(new(40,0,z)) && Rail.HasRail(new(41,0,z)), "large station includes three through rail cells");
        pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            forceGenerateNewPawn: true, allowAddictions: false, validatorPreGear: p =>
                !p.WorkTypeIsDisabled(WorkTypeDefOf.Mining) && !p.WorkTypeIsDisabled(WorkTypeDefOf.Construction) && !p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)));
        GenSpawn.Spawn(pawn, from, map);
        var line = new List<IntVec3>();
        DefDatabase<DrawStyleDef>.GetNamed("Line").DrawStyleWorker.Update(from, from + new IntVec3(10,0,3), line);
        Require(line.Count == 11 && line.All(c => c.z == z), "native orthogonal drag line remains supported");
        foreach (var name in new[] { "LR_Track", "LR_PlatformStation" })
        {
            var def = DefDatabase<ThingDef>.GetNamed(name); var site = new IntVec3(34,0,z+5);
            Require((def.costList == null || def.costList.Count == 0) && def.costStuffCount == 0, name + " requires no materials");
            var bp = GenConstruct.PlaceBlueprintForBuild(def, site, map, Rot4.North, Faction.OfPlayer, null);
            Require(bp.TryReplaceWithSolidThing(pawn, out var solid, out _) && solid is Frame { resourceContainer.Count: 0 } frame && frame.IsCompleted(),
                name + " free blueprint produces ready empty frame");
            site.GetThingList(map).OfType<Frame>().Single().CompleteConstruction(pawn);
            var built = site.GetThingList(map).OfType<Building_Rail>().Single();
            Require(built.def == def, name + " native construction completes"); built.Destroy();
        }
        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
        stockpile.settings.filter.SetDisallowAll(); stockpile.settings.filter.SetAllow(ThingDefOf.Steel, true);
        map.zoneManager.RegisterZone(stockpile); stockpile.AddCell(goal);
        Station(140,z).RenamableLabel = "Mining terminus";
        BeginGoto(goal); SetPhase(1);
    }
    private Building_RailStation Station(int x, int z) => new IntVec3(x,0,z).GetThingList(map).OfType<Building_RailStation>().Single();
    private void SpawnRail(IntVec3 cell, string defName = "LR_Track")
    {
        var thing = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defName)); thing.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(thing, cell, map);
    }
    private void BuildLine(int z)
    {
        SpawnRail(new(40,0,z), "LR_PlatformStation"); SpawnRail(new(140,0,z), "LR_PlatformStation");
        for (int x = 42; x <= 138; x++) SpawnRail(new(x,0,z));
    }
    private void SetPhase(int value) { phase = value; phaseTick = GenTicks.TicksGame; }
    private void ResetPawn()
    {
        pawn.drafter.Drafted=false;
        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false); pawn.pather.StopDead();
        pawn.Position = from; pawn.pather.ResetToCurrentPosition();
    }
    private void HoldPawn() => pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 12000));
    private void BeginGoto(IntVec3 target)
    {
        var job = JobMaker.MakeJob(JobDefOf.Goto, target);
        if (!pawn.jobs.TryTakeOrderedJob(job)) throw new Exception("Goto rejected");
        originalJob = job.loadID; sawRide = false;
    }
    private void BeginCargo()
    {
        ResetPawn(); cargo = ThingMaker.MakeThing(ThingDefOf.Steel); cargo.stackCount = 25;
        GenSpawn.Spawn(cargo, from - IntVec3.East, map);
        var job = JobMaker.MakeJob(JobDefOf.HaulToCell, cargo, goal);
        job.count = 25; job.haulMode = HaulMode.ToCellStorage; job.haulOpportunisticDuplicates = false;
        if (!pawn.jobs.TryTakeOrderedJob(job)) throw new Exception("HaulToCell rejected");
        originalJob = job.loadID; checkedCargo = false; sawRide = false;
    }
    private bool CargoDelivered => cargo?.Spawned == true && cargo.Position == goal && pawn.carryTracker.CarriedThing == null;
    private void CheckCargoArrival(string name)
    {
        Require(cargo?.Spawned == true && cargo.Position == goal && cargo.stackCount == 25 && pawn.carryTracker.CarriedThing == null, name);
        cargo.Destroy(); cargo = null;
    }
    private void BeginFreight(int amount)
    {
        if(loader==null)
        {
            loader=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,
                forceGenerateNewPawn:true,allowAddictions:false,validatorPreGear:p=>!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)));
            GenSpawn.Spawn(loader,from,map);
        }
        loader.jobs.EndCurrentJob(JobCondition.InterruptForced,false);loader.pather.StopDead();
        loader.drafter.Drafted=false;
        loader.Position=from;loader.pather.ResetToCurrentPosition();
        freightThing=ThingMaker.MakeThing(ThingDefOf.Steel);freightThing.stackCount=amount;
        GenSpawn.Spawn(freightThing,from+IntVec3.North*3,map);
        Rail.OrderFreight(loader,freightThing,Primary.a,Primary.b);
    }
    private void SetupDiagonal()
    {
        int baseZ=from.z+18;
        for(int x=35;x<=66;x++)for(int dz=16;dz<=41;dz++)map.roofGrid.SetRoof(new(x,0,from.z+dz),null);
        for(int x=35;x<=66;x++)for(int dz=16;dz<=41;dz++)
        {
            var cell=new IntVec3(x,0,from.z+dz);
            foreach(var thing in cell.GetThingList(map).Where(t=>t is Building || t is Plant).ToArray())
                if(thing.def.destroyable)thing.Destroy();else thing.DeSpawn();
            map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.fogGrid.Unfog(cell);
        }
        SpawnRail(new(40,0,baseZ),"LR_PlatformStation");SpawnRail(new(62,0,baseZ+20),"LR_PlatformStation");
        var line=new List<IntVec3>();DefDatabase<DrawStyleDef>.GetNamed("LR_DiagonalLine").DrawStyleWorker.Update(new(41,0,baseZ),new(61,0,baseZ+20),line);
        Require(line.Count==21 && line.Zip(line.Skip(1)).All(p=>Math.Abs(p.First.x-p.Second.x)==1 && Math.Abs(p.First.z-p.Second.z)==1),"diagonal drag style creates true consecutive diagonal cells");
        foreach(var cell in line.Skip(1).SkipLast(1))SpawnRail(cell);
        secondary=Rail.CreateService(Station(40,baseZ),Station(62,baseZ+20));
        Require(secondary?.Available==true && secondary.cumulative[^1]==86,"real engine diagonal service uses weighted four-tick segments");
        diagonalPawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,
            forceGenerateNewPawn:true,allowAddictions:false));
        GenSpawn.Spawn(diagonalPawn,new IntVec3(38,0,baseZ),map);
        Require(diagonalPawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(64,0,baseZ+20))),"diagonal travel job accepted by native pawn job tracker");
    }
    public override void MapComponentTick()
    {
        if (!Enabled || phase == 0 || phase >= 100) return;
        try
        {
            if (GenTicks.TicksGame - phaseTick > 12000) throw new Exception($"Timeout phase {phase}, pawn {pawn.Position}, job {pawn.CurJob}, service {Primary?.fault}, train {Primary?.train?.Position}");
            var trip = Rail.ActiveTrip(pawn); var service = Primary;
            if (trip?.phase == TransitPhase.Riding) sawRide = true;
            if (!checkedAnimation && trip?.phase == TransitPhase.Riding && service.animating && GenTicks.TicksGame > service.segmentStartedAt)
            {
                Require(UnityEngine.Vector3.Distance(Rail.TrainVisualPosition(service), service.train.Position.ToVector3Shifted().WithY(AltitudeLayer.Item.AltitudeFor())) is > 0 and <= 1.001f,
                    "persistent train interpolates inside active segment");
                Require(Rail.TryVisualPosition(pawn, out var visual) && Math.Abs(pawn.DrawPos.x - visual.x) < .001f && Math.Abs(pawn.DrawPos.z - visual.z) < .001f,
                    "passenger draw position follows assigned train seat"); checkedAnimation = true;
            }
            if (!checkedCargo && cargo != null && trip?.phase == TransitPhase.Riding)
            {
                Require(pawn.carryTracker.CarriedThing == cargo && cargo.stackCount == 25 && pawn.CurJob.loadID == originalJob,
                    "hauling passenger keeps same steel stack and original job on train"); checkedCargo = true;
            }
            switch (phase)
            {
                case 1:
                    if (pawn.Position.x < 48) break;
                    Require(trip == null && TrainCount == 0 && Rail.Services.Count == 0, "unconfigured tracks never summon passenger trains");
                    ResetPawn(); HoldPawn();
                    service = Rail.CreateService(Station(40,from.z), Station(140,from.z));
                    Require(service != null && service.train.Spawned && TrainCount == 1, "route creates exactly one real persistent train");
                    expectedTrainId = service.train.thingIDNumber;
                    Require(!Rail.CanCreateService(service.a, service.b, out _), "duplicate service on same connected track rejected");
                    SetPhase(11); break;
                case 11:
                    if (Rail.Reversals < 2) break;
                    Require(Rail.EmptyTrainMoves >= 200 && TrainCount == 1 && service.train.thingIDNumber == expectedTrainId,
                        "same empty train completes outbound and return trips without passengers");
                    BeginGoto(goal); SetPhase(12); break;
                case 12:
                    if (pawn.Position != goal) break;
                    Require(sawRide && trip == null && pawn.Spawned && TrainCount == 1 && service.train.thingIDNumber == expectedTrainId,
                        "long trip uses existing service and releases passenger without deleting train");
                    BeginGoto(goal - IntVec3.East * 10); SetPhase(13); break;
                case 13:
                    if (pawn.Position != goal - IntVec3.East * 10) break;
                    Require(!sawRide, "short trip remains walking"); BeginCargo(); SetPhase(14); break;
                case 14:
                    if (!CargoDelivered) break;
                    Require(sawRide && checkedCargo, "resource hauling automatically uses established train");
                    CheckCargoArrival("same 25 steel delivered to native stockpile after ride");
                    BeginCargo(); SetPhase(16); break;
                case 16:
                    if (trip?.phase != TransitPhase.Riding || service.index < 20 || service.index > 80) break;
                    gap = service.path[service.index + service.direction];
                    gap.GetThingList(map).OfType<Building_Rail>().Single().Destroy(); SetPhase(17); break;
                case 17:
                    if (trip != null) break;
                    Require(pawn.CurJob?.loadID == originalJob && pawn.carryTracker.CarriedThing == cargo && cargo.stackCount == 25,
                        "broken track releases hauling passenger with cargo and job intact");
                    Require(pawn.pather.Moving && pawn.pather.Destination.Cell == goal && service.fault != null,
                        "broken track stops service and resumes original delivery target");
                    SpawnRail(gap); SetPhase(18); break;
                case 18:
                    if (!CargoDelivered) break;
                    CheckCargoArrival("interrupted cargo still delivered exactly once");
                    Require(service.Available && service.train.thingIDNumber == expectedTrainId && TrainCount == 1,
                        "track repair automatically restores same train service");
                    BeginCargo(); SetPhase(19); break;
                case 19:
                    if (trip?.phase != TransitPhase.Riding || service.index < 10 || service.index > 90) break;
                    pausedPosition = service.train.Position; Rail.SetServiceOperating(service, false); SetPhase(23); break;
                case 23:
                    if (GenTicks.TicksGame - phaseTick < 10) break;
                    Require(trip == null && service.train.Position == pausedPosition && TrainCount == 1 && pawn.carryTracker.CarriedThing == cargo,
                        "pausing parks persistent train and releases passenger with cargo");
                    Rail.SetServiceOperating(service, true); SetPhase(24); break;
                case 24:
                    if (!CargoDelivered) break;
                    CheckCargoArrival("paused passenger completes original steel delivery");
                    ResetPawn(); mine = ThingMaker.MakeThing(ThingDefOf.MineableSteel); GenSpawn.Spawn(mine, goal + IntVec3.East, map); mine.HitPoints = 20;
                    var work = JobMaker.MakeJob(JobDefOf.Mine, mine); pawn.jobs.TryTakeOrderedJob(work); originalJob = work.loadID;
                    sawRide = false; SetPhase(30); break;
                case 30:
                    if (!checkedMine && trip?.phase == TransitPhase.Riding)
                    { Require(pawn.CurJob.loadID == originalJob && mine.HitPoints == 20, "mining job remains pending during service ride"); checkedMine = true; }
                    if (!mine.Destroyed && mine.HitPoints >= 20) break;
                    Require(sawRide && trip == null && pawn.Position.DistanceTo(goal + IntVec3.East) < 2, "mining resumes at original target after scheduled service");
                    if (!mine.Destroyed) mine.Destroy(); ResetPawn(); BeginGoto(goal); SetPhase(31); break;
                case 31:
                    if (trip?.phase != TransitPhase.Riding || service.index < 10) break;
                    pawn.drafter.Drafted = true; SetPhase(32); break;
                case 32:
                    if (GenTicks.TicksGame - phaseTick < 3) break;
                    Require(trip == null && pawn.Spawned && TrainCount == 1, "drafting releases rider and retains persistent train");
                    pawn.drafter.Drafted = false; ResetPawn(); HoldPawn();
                    for (int i = 0; i < 12; i++)
                    {
                        var commuter = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                        GenSpawn.Spawn(commuter, from, map); commuters.Add(commuter);
                        commuter.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, goal));
                    }
                    SetPhase(40); break;
                case 40:
                    if (Rail.WaitingCount(service) > 0 && Rail.RiderCount(service) == RailServicePolicy.Capacity) sawQueue = true;
                    foreach (var commuter in commuters)
                    {
                        if (Rail.ActiveTrip(commuter)?.phase == TransitPhase.Riding) commutersRode.Add(commuter);
                        if (commutersArrived.Contains(commuter) || commuter.Position != goal || Rail.ActiveTrip(commuter) != null) continue;
                        commutersArrived.Add(commuter); commuter.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait, 12000));
                    }
                    if (commutersArrived.Count != commuters.Count) break;
                    Require(commutersRode.Count == 12 && TrainCount == 1, "twelve passengers all use one scheduled train");
                    Require(sawQueue && Rail.MaxPassengersObserved == 4, "four-seat capacity enforced and excess passengers wait for later trips");
                    Require(Rail.PeakPlansPerTick == 2, "complete journey planning remains capped at two per tick");
                    foreach(var commuter in commuters)commuter.drafter.Drafted=true;
                    BuildLine(from.z + 8);
                    secondary = Rail.CreateService(Station(40,from.z+8), Station(140,from.z+8));
                    Require(secondary != null && TrainCount == 2, "separate connected networks each allow one train");
                    for (int dz = 1; dz < 8; dz++) SpawnRail(new(80,0,from.z+dz));
                    SetPhase(50); break;
                case 50:
                    if (GenTicks.TicksGame - phaseTick < 2) break;
                    Require(Rail.Services.Count(s => s.Available) == 2, "connected network retains two routes with disjoint reserved paths");
                    Require(!Rail.CanCreateService(service.a, secondary.b, out _), "branch on occupied connected network cannot create another service");
                    pausedPosition = secondary.train.Position; SetPhase(51); break;
                case 51:
                    if (GenTicks.TicksGame - phaseTick < 10) break;
                    Require(secondary.Available && TrainCount == 2, "disjoint route keeps its permanent train after network connection");
                    for (int dz = 1; dz < 8; dz++) new IntVec3(80,0,from.z+dz).GetThingList(map).OfType<Building_Rail>().Single().Destroy();
                    SetPhase(52); break;
                case 52:
                    if (GenTicks.TicksGame - phaseTick < 2) break;
                    Require(Rail.Services.Count(s => s.Available) == 2, "splitting networks restores independent services");
                    Rail.RemoveService(secondary); Require(TrainCount == 1 && Rail.Services.Count == 1, "removing route removes only its own train");
                    for(int x=89;x<=91;x++)new IntVec3(x,0,from.z).GetThingList(map).OfType<Building_Rail>().Single().Destroy();
                    SpawnRail(new(90,0,from.z),"LR_PlatformStation");middle=Station(90,from.z);
                    middle.dwellTicks=180;service.a.dwellTicks=30;
                    Require(Rail.ConfigureService(new(){service.a,middle,service.b},service,out _),"existing service can be edited into ordered three-stop route without a second train");
                    Require(service.stops.Count==3 && service.stopIndices.SequenceEqual(new[]{0,50,100}) && TrainCount==1,"three-stop route has correct intermediate path index and one train");
                    SetPhase(60);break;
                case 60:
                    if(service.index!=50 || service.dwell!=180)break;
                    SetPhase(61);break;
                case 61:
                    if(GenTicks.TicksGame-phaseTick<30)break;
                    Require(service.index==50 && service.dwell==150,"intermediate station honors configured 180-tick dwell");
                    ResetPawn();BeginGoto(middle.Position+IntVec3.East*2);SetPhase(62);break;
                case 62:
                    if(pawn.Position!=middle.Position+IntVec3.East*2)break;
                    Require(sawRide && trip==null,"passenger alights at intermediate station and continues original target");
                    BeginGoto(goal);SetPhase(63);break;
                case 63:
                    if(pawn.Position!=goal)break;
                    Require(sawRide && trip==null,"passenger boards intermediate stop in correct direction for final station");
                    BeginGoto(from);SetPhase(64);break;
                case 64:
                    if(pawn.Position!=from)break;
                    Require(sawRide && trip==null,"return passenger travels through intermediate stop to first station");
                    pawn.drafter.Drafted=true;SetupDiagonal();SetPhase(66);break;
                case 66:
                    if(Rail.ActiveTrip(diagonalPawn)?.phase==TransitPhase.Riding)
                    {
                        diagonalRode=true;
                        if(!diagonalAnimated && secondary.animating && secondary.segmentDuration==4 && GenTicks.TicksGame>secondary.segmentStartedAt)
                        {
                            var draw=Rail.TrainVisualPosition(secondary);var root=secondary.train.Position.ToVector3Shifted().WithY(AltitudeLayer.Item.AltitudeFor());
                            Require(UnityEngine.Vector3.Distance(draw,root) is >0 and <1.415f && Math.Abs(Math.Abs(Rail.TrainAngle(secondary))-45)<.001,
                                "diagonal train uses interpolated position and 45-degree body heading");diagonalAnimated=true;
                        }
                    }
                    if(diagonalPawn.Position!=new IntVec3(64,0,from.z+38))break;
                    Require(diagonalRode && diagonalAnimated && Rail.ActiveTrip(diagonalPawn)==null,$"diagonal passenger arrives and resumes native final path (rode {diagonalRode}, animated {diagonalAnimated}, moving {diagonalPawn.pather.Moving})");
                    diagonalPawn.drafter.Drafted=true;SetPhase(67);break;
                case 67:
                    if(!secondary.animating || secondary.segmentDuration!=4 || secondary.index<3 || secondary.index>20)break;
                    var next=secondary.path[secondary.index+secondary.direction];pausedPosition=secondary.train.Position;
                    cornerWall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);
                    GenSpawn.Spawn(cornerWall,new IntVec3(pausedPosition.x,0,next.z),map);
                    SetPhase(68);break;
                case 68:
                    if(GenTicks.TicksGame-phaseTick<8)break;
                    Require(secondary.train.Position==pausedPosition && secondary.fault=="LR_TrackBlocked","diagonal train stops before cutting a newly blocked wall corner");
                    cornerWall.Destroy();SetPhase(69);break;
                case 69:
                    if(secondary.train.Position==pausedPosition)break;
                    Require(secondary.Available,"diagonal train resumes after corner obstruction removed");
                    Rail.RemoveService(secondary);BeginFreight(31);SetPhase(70);break;
                case 70:
                    if(!Rail.Freight.Any(f=>f.train==service.train))break;
                    Require(!freightThing.Spawned && freightThing.stackCount==31 && Rail.Freight.Single().contents.Contains(freightThing) && loader.carryTracker.CarriedThing==null,
                        "native loading job moves same 31 steel into real train freight holder");
                    loader.drafter.Drafted=true;SetPhase(71);break;
                case 71:
                    if(service.index!=50 || service.dwell==0)break;
                    Require(!freightThing.Spawned && freightThing.stackCount==31 && Rail.Freight.Single().destination==service.b,"freight passes intermediate stop without unloading early");
                    SetPhase(72);break;
                case 72:
                    if(!freightThing.Spawned)break;
                    Require(freightThing.stackCount==31 && freightThing.Position.DistanceTo(service.b.Position)<5 && Rail.Freight.Count==0,"train unloads exactly 31 steel at requested final station");
                    freightThing.Destroy();BeginFreight(17);SetPhase(73);break;
                case 73:
                    if(!Rail.Freight.Any(f=>f.train==service.train))break;
                    var stopList=new List<Building_RailStation>(service.stops);Rail.RemoveService(service);
                    Require(freightThing.Spawned && freightThing.stackCount==17 && Rail.Freight.Count==0 && TrainCount==0,"removing route drops loaded freight without loss or duplication");
                    freightThing.Destroy();freightThing=null;
                    Require(Rail.ConfigureService(stopList,null,out _),"three-stop service recreates after explicit route removal");
                    service=Primary;expectedTrainId=service.train.thingIDNumber;service.a.dwellTicks=300;service.dwell=300;
                    foreach(var other in map.mapPawns.FreeColonistsSpawned)
                        if(other!=pawn && other!=loader)other.drafter.Drafted=true;
                    foreach(var oldStack in goal.GetThingList(map).Where(t=>t.def==ThingDefOf.Steel).ToArray())oldStack.Destroy();
                    BeginFreight(23);SetPhase(74);break;
                case 74:
                    if(!Rail.Freight.Any(f=>f.train==service.train))break;
                    loader.drafter.Drafted=true;
                    BeginCargo();SetPhase(7);break;
                case 7:
                    if (trip?.phase != TransitPhase.Riding || service.index < 10) break;
                    Require(Rail.Freight.Single().train==service.train && freightThing.stackCount==23,"save includes independently loaded freight and carrying passenger together");
                    foreach(var other in map.mapPawns.FreeColonistsSpawned)if(other!=pawn)other.drafter.Drafted=true;
                    Require(!goal.GetThingList(map).Any(t=>t.def==ThingDefOf.Steel),"saved delivery destination is empty and other workers are isolated");
                    GameDataSaveLoader.SaveGame("LocalRailSmokeInTransit");
                    Require(File.Exists(GenFilePaths.FilePathForSavedGame("LocalRailSmokeInTransit")), "save created with persistent train and carried cargo in transit");
                    Finish(); break;
                case 20:
                    if (GenTicks.TicksGame - phaseTick < 5) break;
                    Require(trip == null && pawn.Spawned && pawn.CurJob?.loadID == originalJob && pawn.pather.Moving && pawn.pather.Destination.Cell == goal,
                        "load releases transit and resumes original passenger job and target");
                    Require(Station(140,from.z).RenamableLabel == "Mining terminus", "station name persists across save and load");
                    if (LegacyLoad) Require(TrainCount == 0 && Rail.Services.Count == 0, "legacy 0.2 stations and trip load without summoning a train");
                    else
                    {
                        Require(TrainCount == 1 && service.train.thingIDNumber == expectedTrainId && service.Available, "load restores exactly same permanent train and operating route");
                        Require(pawn.carryTracker.CarriedThing == cargo && cargo.stackCount == 25, "load preserves same carried steel stack and quantity");
                        if(freightThing!=null)
                        {
                            Require(service.stops.Count==3 && service.a.dwellTicks==300 && service.stops[1].dwellTicks==180,"load restores ordered three-stop route and per-station dwell");
                            Require(Rail.Freight.Single().contents.Contains(freightThing) && freightThing.stackCount==23 && Rail.Freight.Single().train==service.train,"load restores same real freight stack and its destination");
                        }
                    }
                    SetPhase(21); break;
                case 21:
                    if (LegacyLoad)
                    {
                        if (pawn.Position != goal) break;
                        Require(pawn.Spawned && !pawn.Dead, "legacy passenger reaches original destination after migration"); Finish(); break;
                    }
                    if(freightThing!=null)
                    {
                        int delivered=goal.GetThingList(map).Where(t=>t.def==ThingDefOf.Steel).Sum(t=>t.stackCount);
                        if(pawn.carryTracker.CarriedThing!=null || delivered<25)break;
                        Require(delivered==25 || delivered==48,$"loaded hauling job delivers 25 steel, allowing native merge with freight (destination {delivered})");
                    }
                    else
                    {
                        if(!CargoDelivered)break;
                        CheckCargoArrival("loaded hauling passenger delivers exactly 25 steel");
                    }
                    pawn.drafter.Drafted=true;
                    Require(TrainCount == 1 && Rail.EmptyTrainMoves > 0 && service.train.thingIDNumber == expectedTrainId,
                        "loaded train continues operating after passenger recovery");
                    if(freightThing!=null)SetPhase(22);else Finish();break;
                case 22:
                    if(Rail.Freight.Count!=0)break;
                    int combined=goal.GetThingList(map).Where(t=>t.def==ThingDefOf.Steel).Sum(t=>t.stackCount);
                    if(freightThing.Spawned && freightThing.Position!=goal)combined+=freightThing.stackCount;
                    Require(combined==48 && (!freightThing.Spawned || freightThing.Position.DistanceTo(service.b.Position)<5),
                        "loaded passenger and freight deliver all 48 steel without loss or duplication, including native stack merging");Finish();break;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void Require(bool value, string name)
    {
        if (!value) throw new Exception(name);
        passed.Add(name); Log.Message("[LocalRailSmoke] PASS " + name);
    }
    private void Fail(Exception ex)
    {
        phase = 101;
        File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "smoke-result.json"),
            System.Text.Json.JsonSerializer.Serialize(new { passed, completed = false, error = ex.ToString() }));
        Log.Error("[LocalRailSmoke] FAILED " + ex); Root.Shutdown();
    }
    private void Finish()
    {
        phase = 100;
        File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "smoke-result.json"),
            System.Text.Json.JsonSerializer.Serialize(new { passed, completed = true, tests = passed.Count,
                services = new { count = Rail.Services.Count, TrainCount, Rail.TrainMoves, Rail.EmptyTrainMoves, Rail.Reversals, Rail.MaxPassengersObserved },
                planning = new { Rail.PlanAttempts, Rail.AccessExpanded, Rail.RouteSearches, Rail.CacheHits, Rail.PeakPlansPerTick } }));
        Log.Message("[LocalRailSmoke] COMPLETED " + passed.Count + " real-engine checks."); Root.Shutdown();
    }
}
