using RimWorld;
using UnityEngine;
using Verse;
namespace LocalRail;

public class RailManager : MainTabWindow
{
    internal static RailManager Active;
    internal RailServiceState SelectedService;
    internal bool ShowAll=true;
    private Building_RailStation station;
    private int page;
    private string search="";
    private Vector2 scroll;
    public override Vector2 RequestedTabSize=>new(1050,470);
    internal static void Open(Building_RailStation target=null)
    {
        var def=DefDatabase<MainButtonDef>.GetNamed("LR_Rail");var window=(RailManager)def.TabWindow;
        if(target!=null){window.station=target;window.page=1;window.scroll=default;}
        if(Find.MainTabsRoot.OpenTab!=def)Find.MainTabsRoot.SetCurrentTab(def);
    }
    public override void PostOpen(){base.PostOpen();Active=this;}
    public override void PostClose(){if(Active==this)Active=null;base.PostClose();}
    public override void DoWindowContents(Rect rect)
    {
        var map=Find.CurrentMap;if(map==null){Widgets.Label(rect,"LR_NoMap".Translate());return;}
        var rail=map.GetComponent<RailMapComponent>();
        Text.Font=GameFont.Medium;Widgets.Label(new(0,0,250,32),"LR_Manager".Translate());Text.Font=GameFont.Small;
        if(Widgets.ButtonText(new(260,0,130,30),"LR_Routes".Translate())){page=0;scroll=default;}
        if(Widgets.ButtonText(new(400,0,130,30),"LR_Stations".Translate())){page=1;scroll=default;}
        Widgets.CheckboxLabeled(new(560,0,280,30),"LR_ShowAllRoutes".Translate(),ref ShowAll);
        if(SelectedService!=null&&!rail.Services.Contains(SelectedService))SelectedService=null;
        if(page==0)DrawRoutes(rail,rect);else DrawStations(rail,rect);
    }
    private void DrawRoutes(RailMapComponent rail,Rect rect)
    {
        search=Widgets.TextField(new(0,44,300,30),search,40);
        if(Widgets.ButtonText(new(320,44,190,30),"LR_CreateService".Translate()))
        {
            var options=rail.Stations.Select(s=>new FloatMenuOption(s.RenamableLabel,()=>Find.WindowStack.Add(new RailRouteEditor(rail,s,null)))).ToList();
            if(options.Count==0)options.Add(new("LR_NoStations".Translate(),null));Find.WindowStack.Add(new FloatMenu(options));
        }
        Widgets.Label(new(530,44,rect.width-530,45),"LR_ManagerNetworkRule".Translate());
        var list=rail.Services.Where(s=>search.Length==0||s.Name.Contains(search,StringComparison.OrdinalIgnoreCase)||s.Itinerary.Contains(search,StringComparison.OrdinalIgnoreCase)).ToList();
        var view=new Rect(0,98,rect.width,rect.height-98);var content=new Rect(0,0,rect.width-20,Math.Max(50,list.Count*88));
        Widgets.BeginScrollView(view,ref scroll,content);
        if(list.Count==0)Widgets.Label(new(0,0,content.width,50),"LR_NoRoutes".Translate());
        for(int i=0;i<list.Count;i++)
        {
            var service=list[i];float y=i*88;
            Widgets.DrawBoxSolid(new(0,y,12,78),service.RouteColor);
            if(service==SelectedService)Widgets.DrawBox(new(0,y,content.width,80),2);
            if(Widgets.ButtonText(new(20,y+4,180,28),service.Name)){SelectedService=service;rail.PreviewChanged();}
            Widgets.LabelEllipses(new(208,y+4,160,28),rail.ServiceStatus(service));
            TooltipHandler.TipRegion(new(208,y+4,160,28),rail.ServiceStatus(service));
            Widgets.Label(new(370,y+4,100,28),service.KindLabel);
            TooltipHandler.TipRegion(new(370,y+4,100,28),(service.freightOnly?"LR_FreightOnly":service.loop?"LR_Loop":"LR_Shuttle").Translate()+"\n"+"LR_TrainKindsHelp".Translate());
            Widgets.Label(new(476,y+4,210,28),"LR_LoadSummary".Translate(rail.RiderCount(service),rail.WaitingCount(service),rail.Freight.Count(f=>f.train==service.train)));
            if(Widgets.ButtonText(new(690,y+4,80,28),(service.operating?"LR_Pause":"LR_Resume").Translate()))rail.SetServiceOperating(service,!service.operating);
            if(Widgets.ButtonText(new(775,y+4,65,28),"LR_Edit".Translate())){SelectedService=service;Find.WindowStack.Add(new RailRouteEditor(rail,service.a,service));}
            if(Widgets.ButtonText(new(845,y+4,65,28),"LR_Locate".Translate())&&service.train?.Spawned==true)CameraJumper.TryJumpAndSelect(service.train);
            if(Widgets.ButtonText(new(915,y+4,70,28),"LR_Remove".Translate()))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("LR_RemoveConfirm".Translate(service.Name),()=>rail.RemoveService(service)));
            Widgets.LabelEllipses(new(20,y+39,content.width-32,32),service.Itinerary);
            TooltipHandler.TipRegion(new(20,y+39,content.width-32,32),service.Itinerary);
        }
        Widgets.EndScrollView();
    }
    private void DrawStations(RailMapComponent rail,Rect rect)
    {
        if(station?.Spawned!=true||station.Map!=Find.CurrentMap)station=rail.Stations.FirstOrDefault();
        if(Widgets.ButtonText(new(0,44,360,30),station?.RenamableLabel??"LR_NoStations".Translate()))
            Find.WindowStack.Add(new FloatMenu(rail.Stations.Select(s=>new FloatMenuOption(s.RenamableLabel,()=>{station=s;scroll=default;})).ToList()));
        if(station==null)return;
        if(Widgets.ButtonText(new(375,44,100,30),"LR_Locate".Translate()))CameraJumper.TryJumpAndSelect(station);
        if(Widgets.ButtonText(new(485,44,190,30),"LR_FreightDestination".Translate()))rail.ShowFreightDestinations(station);
        if(station is Building_RailWarehouse warehouse&&Widgets.ButtonText(new(690,44,250,30),"LR_WarehouseOrders".Translate()))Find.WindowStack.Add(new Dialog_RailWarehouse(warehouse));
        Widgets.Label(new(0,88,rect.width,35),"LR_PlatformHelp".Translate());
        var view=new Rect(0,128,rect.width,rect.height-128);var content=new Rect(0,0,rect.width-20,station.PlatformCount*95);
        Widgets.BeginScrollView(view,ref scroll,content);
        for(int p=0;p<station.PlatformCount;p++)
        {
            int platform=p;var stop=new RailStop(station,p);float y=p*95;
            var routes=rail.Services.Where(s=>s.FindStop(station,p)>=0).ToList();
            Widgets.Label(new(0,y,150,30),"LR_PlatformNumber".Translate(p+1));
            Widgets.Label(new(155,y,430,30),"LR_Ports".Translate(rail.PortConnected(stop,-1)?"✓":"×",rail.PortConnected(stop,1)?"✓":"×"));
            Widgets.Label(new(450,y,280,30),stop.Settings.Summary);
            if(Widgets.ButtonText(new(745,y,130,30),"LR_SetDwell".Translate()))Find.WindowStack.Add(new Dialog_RailDwell(stop.Settings,s=>station.SetPlatformSettings(platform,s)));
            if(Widgets.ButtonText(new(880,y,105,30),"LR_NewRoute".Translate()))
            {var editor=new RailRouteEditor(rail,station,null);editor.stops[0]=stop;editor.ResetDwell(stop);Find.WindowStack.Add(editor);}
            var reachable=rail.ReachablePlatforms(stop);string text="LR_Reachable".Translate(reachable.Count,string.Join("; ",reachable.Select(s=>s.Label)));
            Widgets.LabelEllipses(new(0,y+33,content.width,27),text);TooltipHandler.TipRegion(new(0,y+33,content.width,27),text);
            Widgets.Label(new(0,y+62,content.width,27),"LR_PlatformRoutes".Translate(string.Join(", ",routes.Select(s=>s.Name))));
        }
        Widgets.EndScrollView();
    }
}
