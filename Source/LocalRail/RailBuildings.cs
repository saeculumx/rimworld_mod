using RimWorld;
using UnityEngine;
using Verse;

namespace LocalRail;

public interface IRailNode
{
    IEnumerable<IntVec3> RailCells { get; }
}

public class Building_Rail : Building, IRailNode
{
    public virtual IEnumerable<IntVec3> RailCells { get { yield return Position; } }
    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        map.GetComponent<RailMapComponent>().Register(this);
    }
    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        Map.GetComponent<RailMapComponent>().Unregister(this);
        base.DeSpawn(mode);
    }
    public override void Print(SectionLayer layer)
    {
        if(this is Building_RailWarehouse warehouse)warehouse.PrintStorage(layer);
        else if (this is Building_RailMultiStation multi) multi.PrintPlatforms(layer);
        else if (this is Building_RailStation) base.Print(layer);
        PrintTracks(layer, Map, RailCells);
    }
    internal static void PrintTracks(SectionLayer layer, Map map, IEnumerable<IntVec3> cells)
    {
        var component = map.GetComponent<RailMapComponent>();
        // Use native building cutout materials, not overlay shaders. All narrow
        // strips are flat; the plane printer's default tilted top causes occlusion.
        var steel = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Cutout, new Color(.72f,.74f,.73f));
        var outline = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Cutout, new Color(.18f,.19f,.18f));
        var wood = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Cutout, new Color(.40f,.31f,.22f));
        var ballast = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Cutout, new Color(.38f,.36f,.31f));
        foreach (var cell in cells)
        {
            var origin = cell.ToVector3Shifted();
            origin.y = AltitudeLayer.FloorEmplacement.AltitudeFor() + .02f;
            var connections = new List<IntVec3>(8);
            foreach (var delta in GenAdj.AdjacentCells)
            {
                if (!component.HasConnection(cell, cell + delta)) continue;
                connections.Add(delta);
            }
            if (connections.Count == 0)
            {
                Printer_Plane.PrintPlane(layer, origin-Vector3.up*.012f, new Vector2(.96f,.80f), ballast, topVerticesAltitudeBias:0);
                PrintSleeper(origin + Vector3.left * .21f, Vector3.right);
                PrintSleeper(origin + Vector3.right * .21f, Vector3.right);
                for (int sign = -1; sign <= 1; sign += 2)
                    PrintRail(origin + new Vector3(-.42f,0,.19f*sign), origin + new Vector3(.42f,0,.19f*sign));
                continue;
            }
            Vector3 joint = default;
            if (connections.Count == 2) joint = Joint(Direction(connections[0]), Direction(connections[1]));
            for (int index = 0; index < connections.Count; index++)
            {
                var delta = connections[index];
                var direction = Direction(delta);
                var side = Side(direction);
                float distance = delta.x != 0 && delta.z != 0 ? 1.414214f : 1;
                Printer_Plane.PrintPlane(layer, origin+direction*(distance*.25f)-Vector3.up*.012f,
                    new Vector2(distance*.5f+.06f,.80f), ballast, Angle(direction), topVerticesAltitudeBias:0);
                PrintSleeper(origin + direction * (distance * .125f), direction);
                PrintSleeper(origin + direction * (distance * .375f), direction);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    // Both halves of a bend share one gauge-offset intersection. At the
                    // neighbor boundary, a small overlap prevents rasterized hairline seams.
                    var start = origin + (connections.Count == 2 ? joint * (index == 0 ? sign : -sign) : side * (.19f * sign));
                    var end = origin + direction * (distance * .5f + .012f) + side * (.19f * sign);
                    PrintRail(start, end);
                }
            }
            // Branches retain every connection, including diagonal arms; fill their
            // central joins without adding extra sleepers at the switch.
            if (connections.Count > 2)
                for (int a = 0; a < connections.Count; a++)
                    for (int b = a + 1; b < connections.Count; b++)
                    {
                        var first = Direction(connections[a]); var second = Direction(connections[b]);
                        var bend = Joint(first, second);
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            var center = origin + bend * sign;
                            PrintRail(origin + Side(first) * (.19f * sign), center);
                            PrintRail(center, origin - Side(second) * (.19f * sign));
                        }
                    }
            void PrintSleeper(Vector3 center, Vector3 direction) =>
                Printer_Plane.PrintPlane(layer, center, new Vector2(.11f, .70f), wood, Angle(direction), topVerticesAltitudeBias:0);
            void PrintRail(Vector3 start, Vector3 end)
            {
                var delta = end - start; float length = delta.magnitude;
                if (length < .001f) return;
                var center = (start + end) * .5f; center.y += .002f;
                float angle = Angle(delta);
                Printer_Plane.PrintPlane(layer, center, new Vector2(length + .025f, .13f), outline, angle, topVerticesAltitudeBias:0);
                center.y += .002f;
                Printer_Plane.PrintPlane(layer, center, new Vector2(length + .025f, .075f), steel, angle, topVerticesAltitudeBias:0);
            }
        }
    }
    private static Vector3 Direction(IntVec3 delta) => new Vector3(delta.x,0,delta.z).normalized;
    private static Vector3 Side(Vector3 direction) => new(-direction.z,0,direction.x);
    private static float Angle(Vector3 direction) => (float)(Math.Atan2(-direction.z,direction.x)*180/Math.PI);
    private static Vector3 Joint(Vector3 first, Vector3 second) =>
        (Side(first)-Side(second)) * (.19f / (1-Vector3.Dot(first,second)));
}

public class Building_RailStation : Building_Rail, IRenameable, IThingHolder
{
    public virtual int PlatformCount => 1;
    private int railLayoutVersion=1;
    public IntVec3 RailOffset(int x,int z)
    {var offset=Rotation.AsQuat*new Vector3(x,0,z);return new(Mathf.RoundToInt(offset.x),0,Mathf.RoundToInt(offset.z));}
    public IntVec3 PlatformCell(int platform) => Position+RailOffset(0,PlatformCount==1?0:2*platform-(PlatformCount-1));
    public int PlatformAt(IntVec3 cell) => Enumerable.Range(0,PlatformCount).FirstOrDefault(p=>PlatformCell(p)==cell,-1);
    public string PlatformLabel(int platform) => "LR_StationPlatform".Translate(RenamableLabel,platform+1);
    private List<RailPlatformSettings> platformSettings=new();
    public RailPlatformSettings PlatformSettings(int platform)
    {
        while(platformSettings.Count<PlatformCount)platformSettings.Add(new());
        if(platform==0)platformSettings[0].manualTicks=dwellTicks;
        return platformSettings[Math.Clamp(platform,0,PlatformCount-1)];
    }
    internal void SetPlatformSettings(int platform,RailPlatformSettings settings)
    {
        PlatformSettings(platform);platformSettings[platform]=settings.Copy();
        if(platform==0)dwellTicks=settings.manualTicks;
        if(Spawned)Map.GetComponent<RailMapComponent>().NotifyDwellChanged(this,platform);
    }
    internal IntVec3 UnloadCell(int platform) => PlatformCell(platform)+RailOffset(0,1);
    public int freightServiceId,freightOriginPlatform,freightDestinationPlatform;
    private ThingOwner<Thing> emptyHolder;
    public ThingOwner GetDirectlyHeldThings()=>emptyHolder??=new(this);
    public void GetChildHolders(List<IThingHolder> children) {if(Spawned)Map.GetComponent<RailMapComponent>().FreightChildren(this,children);}
    public int dwellTicks = 90;
    public Building_RailStation freightDestination;
    public override IEnumerable<IntVec3> RailCells
    {
        get { for(int p=0;p<PlatformCount;p++) for (int x = -(def.size.x / 2); x <= def.size.x / 2; x++) yield return PlatformCell(p) + RailOffset(x,0); }
    }
    private string stationName;
    public string RenamableLabel
    {
        get => string.IsNullOrWhiteSpace(stationName) ? $"{def.LabelCap} ({Position.x}, {Position.z})" : stationName;
        set => stationName = string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 28)];
    }
    public string BaseLabel => def.label;
    public string InspectLabel => RenamableLabel;
    public override string Label => RenamableLabel;
    public override void ExposeData()
    {
        base.ExposeData(); Scribe_Values.Look(ref stationName, "localRailStationName");
        Scribe_Values.Look(ref railLayoutVersion,"railLayoutVersion",0);
        if(Scribe.mode==LoadSaveMode.PostLoadInit&&railLayoutVersion==0)
        {Rotation=Rot4.North;railLayoutVersion=1;}
        Scribe_Values.Look(ref dwellTicks,"localRailDwellTicks",90);
        Scribe_References.Look(ref freightDestination,"localRailFreightDestination");
        Scribe_Values.Look(ref freightServiceId,"freightServiceId");Scribe_Values.Look(ref freightOriginPlatform,"freightOriginPlatform");
        Scribe_Values.Look(ref freightDestinationPlatform,"freightDestinationPlatform");
        Scribe_Collections.Look(ref platformSettings,"railPlatformSettings",LookMode.Deep);
        if(Scribe.mode==LoadSaveMode.PostLoadInit)platformSettings ??= new();
        dwellTicks=Math.Clamp(dwellTicks,30,7200);
    }
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var gizmo in base.GetGizmos()) yield return gizmo;
        if (Faction == Faction.OfPlayer)
        {
            yield return new Command_Action { defaultLabel = "Rename".Translate(), defaultDesc = "LR_RenameStation".Translate(),
                icon = TexButton.Rename, action = () => Find.WindowStack.Add(new Dialog_RenameRailStation(this)) };
            var component = Map.GetComponent<RailMapComponent>();
            var service = component.StationService(this);
            yield return new Command_Action {defaultLabel="LR_Manager".Translate(),defaultDesc="LR_ManagerHelp".Translate(),icon=TexButton.Rename,
                action=()=>RailManager.Open(this)};
            yield return new Command_Action {defaultLabel="LR_SetDwell".Translate(),defaultDesc="LR_SetDwellDesc".Translate(),icon=TexButton.Rename,
                action=()=>Find.WindowStack.Add(new FloatMenu(Enumerable.Range(0,PlatformCount).Select(p=>new FloatMenuOption(PlatformLabel(p),
                    ()=>Find.WindowStack.Add(new Dialog_RailDwell(PlatformSettings(p),s=>SetPlatformSettings(p,s))))).ToList()))};
            yield return new Command_Action { defaultLabel = "LR_CreateService".Translate(), defaultDesc = "LR_CreateServiceDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("LocalRail/CartTop"), action = () => Find.WindowStack.Add(new RailRouteEditor(component,this,null)) };
            if (service != null) foreach (var gizmo in Building_RailTrain.ServiceGizmos(component, service)) yield return gizmo;
            if(component.Services.Any(s=>s.stops.Contains(this)))
                yield return new Command_Action {defaultLabel=(this is Building_RailWarehouse?"LR_DefaultInventoryDestination":"LR_FreightDestination").Translate(),defaultDesc="LR_FreightDestinationDesc".Translate(),icon=TexButton.Rename,
                    action=()=>component.ShowFreightDestinations(this)};
        }
    }
    private void ChooseServiceEndpoint(RailMapComponent component)
    {
        var options = new List<FloatMenuOption>();
        foreach (var station in Map.listerThings.AllThings.OfType<Building_RailStation>())
        {
            if (station == this || station.Faction != Faction.OfPlayer) continue;
            var target = station;
            bool canCreate = component.CanCreateService(this, target, out var reason);
            options.Add(new FloatMenuOption(canCreate ? target.RenamableLabel : $"{target.RenamableLabel}: {reason.Translate()}",
                canCreate ? () => component.CreateService(this, target) : null));
        }
        if (options.Count == 0) options.Add(new FloatMenuOption("LR_NoOtherStation".Translate(), null));
        Find.WindowStack.Add(new FloatMenu(options));
    }
    public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn)
    {
        foreach (var option in base.GetFloatMenuOptions(pawn)) yield return option;
        if (pawn.Drafted || !pawn.IsColonistPlayerControlled) yield break;
        if(freightDestination?.Spawned==true && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling))
        {
            var component=Map.GetComponent<RailMapComponent>();
            var item=component.FreightCandidate(pawn,this);
            if(item!=null && component.CanQueueFreight(this,freightDestination,freightServiceId,freightOriginPlatform,freightDestinationPlatform))
                yield return new FloatMenuOption("LR_LoadFreight".Translate(item.Label,freightDestination.PlatformLabel(freightDestinationPlatform)),
                    ()=>component.OrderFreight(pawn,item,this,freightDestination,freightServiceId,freightOriginPlatform,freightDestinationPlatform));
        }
        foreach (var station in Map.listerThings.AllThings.OfType<Building_RailStation>())
        {
            if (station == this || station.Faction != Faction.OfPlayer) continue;
            var target = station;
            yield return new FloatMenuOption("LR_TravelToNamed".Translate(target.RenamableLabel),
                () => pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, target.Position)));
        }
    }
    public override string GetInspectString()
    {
        var component = Map.GetComponent<RailMapComponent>(); var service = component.StationService(this);
        return RailText.Inspect(base.GetInspectString(), "LR_StationInfo".Translate(component.StationCount),
            (service == null ? "LR_NoService".Translate() : "LR_ServiceInfo".Translate(service.Name, component.ServiceStatus(service),
                component.RiderCount(service), RailServicePolicy.Capacity, component.WaitingCount(service))),
            string.Join("\n",Enumerable.Range(0,PlatformCount).Select(p=>"LR_PlatformDwell".Translate(p+1,PlatformSettings(p).Summary))),component.FreightInspect(this));
    }
}

public class Building_RailMultiStation : Building_RailStation
{
    public override int PlatformCount => def.size.z/2;
    internal void PrintPlatforms(SectionLayer layer)
    {
        for(int p=0;p<PlatformCount;p++)
            Printer_Plane.PrintPlane(layer,PlatformCell(p).ToVector3ShiftedWithAltitude(AltitudeLayer.FloorEmplacement),
                new Vector2(def.size.x,1.9f),Graphic.MatSingle,Rotation.AsAngle,topVerticesAltitudeBias:0);
    }
}

public class Building_RailTrain : Building, IThingHolder
{
    public override string LabelNoCount=>Spawned&&Map.GetComponent<RailMapComponent>().TrainService(this) is {} service?service.KindLabel:base.LabelNoCount;
    public override Graphic Graphic=>Spawned&&Map.GetComponent<RailMapComponent>().TrainService(this) is {} service?
        DefDatabase<ThingDef>.GetNamed(service.trainKind==RailTrainKind.Passenger?"LR_PassengerTrain":"LR_AutomaticTrain").graphic:base.Graphic;
    private ThingOwner<Thing> emptyHolder;
    public ThingOwner GetDirectlyHeldThings()=>emptyHolder??=new(this);
    public void GetChildHolders(List<IThingHolder> children) {if(Spawned)Map.GetComponent<RailMapComponent>().FreightChildren(this,children);}
    public override Vector3 DrawPos => Map?.GetComponent<RailMapComponent>().TrainService(this) is { } service
        ? Map.GetComponent<RailMapComponent>().TrainVisualPosition(service) : base.DrawPos;
    protected override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        var component=Map.GetComponent<RailMapComponent>(); var service=component.TrainService(this);
        Graphic.DrawWorker(DrawPos,Rot4.North,def,this,service==null?Rotation.AsAngle-90:component.TrainAngle(service));
        if(service!=null)GenDraw.DrawLineBetween(DrawPos+new Vector3(-.25f,.15f,0),DrawPos+new Vector3(.25f,.15f,0),
            SolidColorMaterials.SimpleSolidColorMaterial(service.RouteColor),.12f);
        if(service!=null)component.DrawPassengerCargo(service);
        if(service!=null)component.DrawTrainFreight(service);
    }
    public override void Destroy(DestroyMode mode=DestroyMode.Vanish)
    {
        if(Spawned) Map.GetComponent<RailMapComponent>().NotifyTrainLost(this);
        base.Destroy(mode);
    }
    public override string GetInspectString()
    {
        var component = Map.GetComponent<RailMapComponent>(); var service = component.TrainService(this);
        return service == null ? RailText.Inspect(base.GetInspectString()) : RailText.Inspect("LR_ServiceInfo".Translate(service.Name, component.ServiceStatus(service),
            component.RiderCount(service), RailServicePolicy.Capacity, component.WaitingCount(service)),
            "LR_TrainFreight".Translate(component.Freight.Count(f=>f.train==this),RailMapComponent.FreightCapacity),
            service.KindLabel,component.TrainCargoSummary(service),
            component.RiderCount(service)>0?"LR_TrainRiders".Translate(component.PassengerNames(service)):null);
    }
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var gizmo in base.GetGizmos()) yield return gizmo;
        var component = Map.GetComponent<RailMapComponent>();
        yield return new Command_Action {defaultLabel="LR_CargoSlots".Translate(),defaultDesc="LR_CargoSlotsHelp".Translate(),
            icon=ContentFinder<Texture2D>.Get("LocalRail/WarehouseDeck"),action=()=>Find.WindowStack.Add(new Dialog_RailCargo(this))};
        if (component.TrainService(this) is { } service)
            foreach (var gizmo in ServiceGizmos(component, service)) yield return gizmo;
    }
    internal static IEnumerable<Gizmo> ServiceGizmos(RailMapComponent component, RailServiceState service)
    {
        var icon = ContentFinder<Texture2D>.Get("LocalRail/CartTop");
        yield return new Command_Action { defaultLabel = (service.operating ? "LR_PauseService" : "LR_ResumeService").Translate(),
            defaultDesc = "LR_ToggleServiceDesc".Translate(), icon = icon,
            action = () => component.SetServiceOperating(service, !service.operating) };
        yield return new Command_Action {defaultLabel="LR_EditRoute".Translate(),defaultDesc="LR_EditRouteDesc".Translate(),icon=icon,
            action=()=>Find.WindowStack.Add(new RailRouteEditor(component,service.a,service))};
        if (service.train?.Spawned != true)
            yield return new Command_Action { defaultLabel = "LR_ReplaceTrain".Translate(), defaultDesc = "LR_ReplaceTrainDesc".Translate(),
                icon = icon, action = () => component.ReplaceTrain(service) };
        else
            yield return new Command_Action { defaultLabel = "LR_SelectTrain".Translate(), defaultDesc = "LR_SelectTrainDesc".Translate(),
                icon = icon, action = () => CameraJumper.TryJumpAndSelect(service.train) };
        yield return new Command_Action { defaultLabel = "LR_RemoveService".Translate(), defaultDesc = "LR_RemoveServiceDesc".Translate(),
            icon = TexButton.Delete, action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("LR_RemoveConfirm".Translate(service.Name),()=>component.RemoveService(service))) };
    }
}

internal sealed class Dialog_RenameRailStation : Dialog_Rename<Building_RailStation>
{
    public Dialog_RenameRailStation(Building_RailStation station) : base(station) { }
}
