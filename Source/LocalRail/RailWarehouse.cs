using RimWorld;
using UnityEngine;
using Verse;
using HarmonyLib;
namespace LocalRail;

public class RailWarehouseOrder : IExposable
{
    public int serviceId,originPlatform,destinationPlatform;
    public Building_RailStation destination;
    public ThingDef itemDef;
    public bool enabled=true;
    public void ExposeData()
    {
        Scribe_Values.Look(ref serviceId,"serviceId");Scribe_Values.Look(ref originPlatform,"originPlatform");
        Scribe_Values.Look(ref destinationPlatform,"destinationPlatform");Scribe_References.Look(ref destination,"destination");
        Scribe_Defs.Look(ref itemDef,"itemDef");Scribe_Values.Look(ref enabled,"enabled",true);
    }
}

public class Building_RailWarehouse : Building_RailMultiStation,ISlotGroupParent
{
    private StorageSettings storage;
    private readonly SlotGroup slots;
    private List<IntVec3> storageCells;
    private List<Thing> received=new();
    public List<RailWarehouseOrder> orders=new();
    public bool exportReceived;
    public bool autoManagePriority=true;
    private bool changingPriority;
    private int priorityModeVersion=1;
    internal StoragePriority AutomaticPriority=>orders.Any(o=>o.enabled)||freightDestination?.Spawned==true?StoragePriority.Preferred:StoragePriority.Low;
    internal void SynchronizePriority()
    {
        if(!autoManagePriority||storage==null||storage.Priority==AutomaticPriority)return;
        changingPriority=true;
        try{storage.Priority=AutomaticPriority;}finally{changingPriority=false;}
    }
    internal void NotifyPriorityChoice()
    {if(!changingPriority&&Spawned&&Scribe.mode==LoadSaveMode.Inactive&&storage.Priority!=AutomaticPriority)autoManagePriority=false;}
    public Building_RailWarehouse()=>slots=new SlotGroup(this);
    public bool StorageTabVisible=>true;
    public bool HaulDestinationEnabled=>Spawned&&Faction==Faction.OfPlayer;
    public bool IgnoreStoredThingsBeauty=>false;
    public string GroupingLabel=>LabelCap;
    public int GroupingOrder=>0;
    public StorageSettings GetParentStoreSettings()=>StorageSettings.EverStorableFixedSettings();
    public StorageSettings GetStoreSettings()=>storage;
    public void Notify_SettingsChanged(){if(Spawned)Map.listerHaulables.Notify_SlotGroupChanged(slots);}
    // Keep native haul destination checks consistent with ITab_Storage, including corpses.
    public bool Accepts(Thing item)=>item is not Pawn&&storage.AllowedToAccept(item);
    public SlotGroup GetSlotGroup()=>slots;
    public string SlotYielderLabel()=>LabelCap;
    public void Notify_ReceivedThing(Thing item){}
    public void Notify_LostThing(Thing item)=>received.Remove(item);
    public IEnumerable<IntVec3> AllSlotCells()=>Spawned?this.OccupiedRect().Where(c=>!RailCells.Contains(c)):Enumerable.Empty<IntVec3>();
    public List<IntVec3> AllSlotCellsList()=>storageCells??=AllSlotCells().ToList();
    internal IEnumerable<Thing> Inventory=>slots.HeldThings;
    internal IEnumerable<Thing> ReceivedCargo=>received;
    internal IEnumerable<Thing> Exportable=>Inventory.Where(t=>Accepts(t)&&!t.IsForbidden(Faction.OfPlayer)&&(exportReceived||!received.Contains(t)));
    internal RailWarehouseOrder DispatchFor(Thing item)
    {
        var rail=Map.GetComponent<RailMapComponent>();
        var explicitOrder=orders.FirstOrDefault(o=>o.enabled&&(o.itemDef==null||o.itemDef==item.def)&&o.destination?.Spawned==true&&
            rail.Service(o.serviceId) is {trainKind:RailTrainKind.Automatic} service&&service.FindStop(this,o.originPlatform)>=0&&service.FindStop(o.destination,o.destinationPlatform)>=0);
        if(explicitOrder!=null)return explicitOrder;
        if(freightDestination?.Spawned!=true)return null;
        var route=rail.FreightService(this,freightDestination,freightServiceId,freightOriginPlatform,freightDestinationPlatform);
        return route?.trainKind==RailTrainKind.Automatic?new RailWarehouseOrder{serviceId=route.id,originPlatform=freightOriginPlatform,
            destination=freightDestination,destinationPlatform=freightDestinationPlatform}:null;
    }
    internal (Thing item,RailWarehouseOrder order) ReadyCargo(RailServiceState service,int platform)
    {
        var byType=new Dictionary<ThingDef,RailWarehouseOrder>();
        foreach(var item in Exportable)
        {
            if(!byType.TryGetValue(item.def,out var order)){order=DispatchFor(item);byType[item.def]=order;}
            if(order!=null&&order.serviceId==service.id&&order.originPlatform==platform&&
                service.HeadsTo(service.FindStop(this,platform),service.FindStop(order.destination,order.destinationPlatform)))return(item,order);
        }
        return(default,default);
    }
    public override void PostMake()
    {base.PostMake();storage=new StorageSettings(this);storage.CopyFrom(GetParentStoreSettings());SynchronizePriority();}
    public override void SpawnSetup(Map map,bool respawningAfterLoad)
    {storageCells=null;storage??=new StorageSettings(this);base.SpawnSetup(map,respawningAfterLoad);}
    public override void DeSpawn(DestroyMode mode=DestroyMode.Vanish)
    {base.DeSpawn(mode);storageCells=null;}
    public override void ExposeData()
    {
        base.ExposeData();Scribe_Deep.Look(ref storage,"warehouseStorage",this);
        Scribe_Collections.Look(ref orders,"warehouseOrders",LookMode.Deep);
        Scribe_Collections.Look(ref received,"warehouseReceived",LookMode.Reference);
        Scribe_Values.Look(ref exportReceived,"warehouseExportReceived");
        Scribe_Values.Look(ref autoManagePriority,"warehouseAutoPriority",true);
        Scribe_Values.Look(ref priorityModeVersion,"warehousePriorityVersion",0);
        if(Scribe.mode==LoadSaveMode.PostLoadInit)
        {
            orders??=new();received??=new();received.RemoveAll(t=>t==null);
            if(priorityModeVersion==0){if(storage?.Priority!=StoragePriority.Preferred)autoManagePriority=false;priorityModeVersion=1;}
            SynchronizePriority();
        }
    }
    internal void PrintStorage(SectionLayer layer)
    {
        Graphic_RailWarehouse.PrintDecks(layer,Position.ToVector3ShiftedWithAltitude(AltitudeLayer.FloorEmplacement),def.size,Graphic.MatSingle,Rotation);
    }
    internal bool Receive(RailFreight shipment)
    {
        if(this.IsForbidden(Faction.OfPlayer))return false;
        bool changed=false;
        foreach(var item in shipment.contents.ToList())
        {
            if(!Accepts(item))continue;
            foreach(var cell in AllSlotCellsList())
            {
                if(!shipment.contents.Contains(item))break;
                if(!cell.Standable(Map)||cell.GetDoor(Map)!=null||Map.reservationManager.IsReserved(cell))continue;
                int before=item.stackCount;
                shipment.contents.TryDrop(item,cell,Map,ThingPlaceMode.Direct,out var result,
                    placedAction:(placed,count)=>{if(placed!=null){if(!received.Contains(placed))received.Add(placed);Map.GetComponent<RailMapComponent>().MarkDelivered(placed);}},
                    nearPlaceValidator:c=>AllSlotCellsList().Contains(c));
                changed|=!shipment.contents.Contains(item)||item.stackCount!=before;
                if(result?.Spawned==true){if(!received.Contains(result))received.Add(result);Map.GetComponent<RailMapComponent>().MarkDelivered(result);}
            }
        }
        return changed;
    }
    public override string GetInspectString()=>RailText.Inspect(base.GetInspectString(),"LR_WarehouseStatus".Translate(Inventory.Sum(t=>t.stackCount),orders.Count),
        (autoManagePriority?(AutomaticPriority==StoragePriority.Low?"LR_ReceivePriority":"LR_DispatchPriority"):"LR_ManualPriority").Translate());
    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach(var gizmo in base.GetGizmos())yield return gizmo;
        foreach(var gizmo in StorageSettingsClipboard.CopyPasteGizmosFor(storage))yield return gizmo;
        yield return new Command_Toggle{defaultLabel="LR_AutoStoragePriority".Translate(),defaultDesc="LR_AutoStoragePriorityHelp".Translate(),
            icon=TexButton.Rename,isActive=()=>autoManagePriority,toggleAction=()=>{autoManagePriority=!autoManagePriority;SynchronizePriority();}};
        yield return new Command_Action{defaultLabel="LR_WarehouseOrders".Translate(),defaultDesc="LR_WarehouseHelp".Translate(),
            icon=ContentFinder<Texture2D>.Get("LocalRail/WarehouseDeck"),action=()=>Find.WindowStack.Add(new Dialog_RailWarehouse(this))};
    }
}

public class Dialog_RailWarehouse : Window
{
    private readonly Building_RailWarehouse warehouse;
    private Vector2 scroll;
    public override Vector2 InitialSize=>new(840,610);
    public Dialog_RailWarehouse(Building_RailWarehouse warehouse){this.warehouse=warehouse;doCloseX=true;absorbInputAroundWindow=true;draggable=true;}
    private void AddOrder(RailWarehouseOrder existing=null)
    {
        var options=new List<FloatMenuOption>();
        foreach(var route in warehouse.Map.GetComponent<RailMapComponent>().Services)
        for(int from=0;from<route.stops.Count;from++)for(int to=0;to<route.stops.Count;to++)
        {
            if(from==to||route.stops[from]!=warehouse)continue;
            var a=route.Stop(from);var b=route.Stop(to);int id=route.id;
            options.Add(new FloatMenuOption($"{route.Name}: {a.Label} → {b.Label}",()=>
            {
                var order=existing??new RailWarehouseOrder();order.serviceId=id;order.originPlatform=a.Platform;order.destination=b.Station;order.destinationPlatform=b.Platform;
                if(existing==null)warehouse.orders.Add(order);
                warehouse.SynchronizePriority();
            }));
        }
        if(options.Count==0)options.Add(new("LR_NoWarehouseRoutes".Translate(),null));Find.WindowStack.Add(new FloatMenu(options));
    }
    public override void DoWindowContents(Rect rect)
    {
        Text.Font=GameFont.Medium;Widgets.Label(new(0,0,rect.width,32),warehouse.RenamableLabel+" — "+"LR_WarehouseOrders".Translate());Text.Font=GameFont.Small;
        Widgets.Label(new(0,40,rect.width,50),"LR_WarehouseHelp".Translate());
        bool automatic=warehouse.autoManagePriority;
        Widgets.CheckboxLabeled(new(0,88,rect.width,25),"LR_AutoStoragePriority".Translate(),ref automatic);
        TooltipHandler.TipRegion(new(0,88,rect.width,25),"LR_AutoStoragePriorityHelp".Translate());
        if(automatic!=warehouse.autoManagePriority){warehouse.autoManagePriority=automatic;warehouse.SynchronizePriority();}
        Widgets.CheckboxLabeled(new(0,113,rect.width,30),"LR_ExportReceived".Translate(),ref warehouse.exportReceived);
        if(Widgets.ButtonText(new(0,153,220,30),"LR_DefaultInventoryDestination".Translate()))warehouse.Map.GetComponent<RailMapComponent>().ShowFreightDestinations(warehouse);
        var destinationRect=new Rect(245,153,rect.width-245,30);
        string destination=warehouse.freightDestination?.PlatformLabel(warehouse.freightDestinationPlatform)??"LR_None".Translate();
        Widgets.LabelEllipses(destinationRect,destination);TooltipHandler.TipRegion(destinationRect,"LR_DefaultInventoryHelp".Translate()+"\n"+destination);
        if(warehouse.orders.Count<32&&Widgets.ButtonText(new(0,190,220,30),"LR_AddWarehouseOrder".Translate()))AddOrder();
        Widgets.Label(new(245,190,rect.width-245,30),"LR_WarehouseRulePriority".Translate(warehouse.orders.Count));
        var view=new Rect(0,234,rect.width,rect.height-277);var content=new Rect(0,0,rect.width-20,Math.Max(50,warehouse.orders.Count*91));
        Widgets.BeginScrollView(view,ref scroll,content);
        for(int i=0;i<warehouse.orders.Count;i++)
        {
            var order=warehouse.orders[i];float y=i*91;
            var service=warehouse.Map.GetComponent<RailMapComponent>().Service(order.serviceId);
            string label=$"{i+1}. {service?.Name??"?"}: {warehouse.PlatformLabel(order.originPlatform)} → {order.destination?.PlatformLabel(order.destinationPlatform)??"?"}";
            var labelRect=new Rect(0,y,content.width-110,28);Widgets.LabelEllipses(labelRect,label);TooltipHandler.TipRegion(labelRect,label);
            Widgets.CheckboxLabeled(new(content.width-95,y,38,28),"",ref order.enabled);
            if(Widgets.ButtonText(new(content.width-45,y,40,28),"×")){warehouse.orders.RemoveAt(i);break;}
            if(Widgets.ButtonText(new(0,y+35,360,30),order.itemDef?.LabelCap??"LR_AllStoredCargo".Translate()))
            {
                var options=new List<FloatMenuOption>{new("LR_AllStoredCargo".Translate(),()=>order.itemDef=null)};
                foreach(var def in warehouse.GetStoreSettings().filter.AllowedThingDefs.Where(d=>d.EverHaulable).OrderBy(d=>d.label))
                {var selected=def;options.Add(new FloatMenuOption(def.LabelCap,()=>order.itemDef=selected));}
                Find.WindowStack.Add(new FloatMenu(options));
            }
            if(i>0&&Widgets.ButtonText(new(375,y+35,45,30),"↑")){(warehouse.orders[i],warehouse.orders[i-1])=(warehouse.orders[i-1],warehouse.orders[i]);break;}
            if(i<warehouse.orders.Count-1&&Widgets.ButtonText(new(430,y+35,45,30),"↓")){(warehouse.orders[i],warehouse.orders[i+1])=(warehouse.orders[i+1],warehouse.orders[i]);break;}
            if(Widgets.ButtonText(new(490,y+35,content.width-490,30),"LR_ChangeWarehouseDestination".Translate()))AddOrder(order);
            if(service?.Available!=true||service.FindStop(order.destination,order.destinationPlatform)<0)Widgets.Label(new(0,y+66,content.width,24),"LR_NoConnection".Translate());
            else if(service.trainKind!=RailTrainKind.Automatic)Widgets.Label(new(0,y+66,content.width,24),"LR_AutomaticRequired".Translate());
        }
        Widgets.EndScrollView();
        warehouse.SynchronizePriority();
        if(Widgets.ButtonText(new(rect.width-130,rect.height-32,130,30),"CloseButton".Translate()))Close();
    }
}

[HarmonyPatch(typeof(StorageSettings),nameof(StorageSettings.Priority),MethodType.Setter)]
internal static class RailWarehouseManualPriority
{
    private static void Postfix(StorageSettings __instance)
    {if(__instance.owner is Building_RailWarehouse warehouse)warehouse.NotifyPriorityChoice();}
}
