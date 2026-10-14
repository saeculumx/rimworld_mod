using RimWorld;
using UnityEngine;
using Verse;
namespace LocalRail;

public class DrawStyle_RailDiagonal : DrawStyle
{
    public override void Update(IntVec3 origin,IntVec3 target,List<IntVec3> buffer)
    {
        buffer.Clear();int x=origin.x,z=origin.z,dx=Math.Abs(target.x-x),dz=Math.Abs(target.z-z),sx=x<target.x?1:-1,sz=z<target.z?1:-1,err=dx-dz;
        while(true){buffer.Add(new(x,origin.y,z));if(x==target.x&&z==target.z)break;int twice=2*err;if(twice>-dz){err-=dz;x+=sx;}if(twice<dx){err+=dx;z+=sz;}}
    }
}

public class RailRouteEditor : Window
{
    internal static RailRouteEditor Active;
    internal readonly RailMapComponent component;
    internal readonly List<RailStop> stops;
    private readonly RailServiceState existing;
    private readonly Dictionary<RailStop,RailPlatformSettings> dwell=new();
    internal bool loop,reverse;
    private bool freightOnly;
    private RailTrainKind trainKind;
    internal int color;
    private string name,error;
    private Vector2 scroll;
    internal bool Picking {get;private set;}
    internal List<RailStop> Candidates {get;private set;}=new();
    internal RailStop? Highlighted;
    private int replaceSlot;
    private Rect expandedRect;
    private Vector2 pickScroll;
    public override Vector2 InitialSize=>new(660,630);
    public RailRouteEditor(RailMapComponent component,Building_RailStation start,RailServiceState existing)
    {
        this.component=component;this.existing=existing;
        stops=existing==null?new(){new RailStop(start)}:Enumerable.Range(0,existing.stops.Count).Select(existing.Stop).ToList();
        foreach(var stop in stops)dwell[stop]=stop.Settings.Copy();
        name=existing?.Name??"";loop=existing?.loop??false;reverse=existing?.reverseLoop??false;color=existing?.colorIndex??component.Services.Count%8;
        freightOnly=existing?.freightOnly??false;
        trainKind=existing?.trainKind??RailTrainKind.Automatic;
        forcePause=true;doCloseX=true;closeOnAccept=false;absorbInputAroundWindow=false;draggable=true;preventCameraMotion=false;
    }
    public override void PostOpen(){base.PostOpen();Active=this;component.PreviewChanged();}
    public override void OnCancelKeyPressed(){if(Picking)Find.Targeter.StopTargeting();else base.OnCancelKeyPressed();}
    internal void ResetDwell(RailStop stop)=>dwell.TryAdd(stop,stop.Settings.Copy());
    public override void PostClose(){if(Picking)Find.Targeter.StopTargeting();if(Active==this)Active=null;base.PostClose();component.PreviewChanged();}
    protected override void SetInitialSizeAndPosition()=>windowRect=new(16,55,InitialSize.x,InitialSize.y);
    internal bool CanChoose(RailStop stop,out string reason)
    {
        reason=null;
        if(stop.Station?.Spawned!=true){reason="LR_StationMissing";return false;}
        if(stops.Contains(stop)&&!(replaceSlot>=0&&stops[replaceSlot]==stop)){reason="LR_PickerDuplicate";return false;}
        int previous=replaceSlot<0?stops.Count-1:replaceSlot-1;
        int next=replaceSlot<0?-1:replaceSlot+1;
        if(previous<0&&loop&&stops.Count>1)previous=stops.Count-1;
        if(next>=stops.Count)next=loop&&stops.Count>1?0:-1;
        if((previous>=0&&!component.PlatformsConnected(stops[previous],stop))||
            (next>=0&&!component.PlatformsConnected(stop,stops[next]))||
            (replaceSlot<0&&loop&&stops.Count>0&&!component.PlatformsConnected(stop,stops[0])))
        {reason="LR_NoConnection";return false;}
        return true;
    }
    internal RailStop? StopAtCell(IntVec3 cell)=>Candidates.Where(s=>s.Station.OccupiedRect().Contains(cell))
        .OrderBy(s=>s.Cell.DistanceToSquared(cell)).Select(s=>(RailStop?)s).FirstOrDefault();
    internal void ChooseStop(int replace=-1)
    {
        if(Picking)return;
        replaceSlot=replace;Candidates=component.AllPlatforms.ToList();Picking=true;Highlighted=null;
        expandedRect=windowRect;windowRect=new(16,55,350,Math.Min(600,UI.screenHeight-110));pickScroll=default;
        if(RailManager.Active!=null)Find.MainTabsRoot.SetCurrentTab(null);
        Find.Targeter.BeginTargeting(new TargetingParameters{canTargetLocations=true,canTargetBuildings=false,canTargetPawns=false,canTargetItems=false},
            target=>{if(StopAtCell(target.Cell) is {} stop)ApplyChoice(stop);},
            target=>Highlighted=StopAtCell(target.Cell),
            target=>StopAtCell(target.Cell) is {} stop&&CanChoose(stop,out _),
            actionWhenFinished:EndPicking,mouseAttachment:TexButton.Rename,playSoundOnAction:false);
        component.PreviewChanged();
    }
    internal bool ApplyChoice(RailStop stop)
    {
        if(!Picking||!CanChoose(stop,out error))return false;
        if(replaceSlot<0)stops.Add(stop);else stops[replaceSlot]=stop;
        dwell.TryAdd(stop,stop.Settings.Copy());error=null;component.PreviewChanged();return true;
    }
    private void EndPicking()
    {if(!Picking)return;Picking=false;Highlighted=null;windowRect=expandedRect;component.PreviewChanged();}
    private void DrawPicker(Rect rect)
    {
        Text.Font=GameFont.Medium;Widgets.Label(new(0,0,rect.width,32),"LR_PickerTitle".Translate());Text.Font=GameFont.Small;
        Widgets.Label(new(0,40,rect.width,95),"LR_PickerHelp".Translate());
        var view=new Rect(0,142,rect.width,rect.height-188);var content=new Rect(0,0,rect.width-20,Candidates.Count*79);
        Widgets.BeginScrollView(view,ref pickScroll,content);
        for(int i=0;i<Candidates.Count;i++)
        {
            var stop=Candidates[i];float y=i*79;bool allowed=CanChoose(stop,out string reason);
            var row=new Rect(0,y,content.width-65,46);
            if(Mouse.IsOver(row))Highlighted=stop;
            GUI.color=allowed?Color.white:Color.gray;
            if(Widgets.ButtonText(row,$"{i+1}. {stop.Label}")&&allowed&&ApplyChoice(stop))Find.Targeter.StopTargeting();
            GUI.color=Color.white;
            if(Widgets.ButtonText(new(content.width-60,y,60,46),"LR_Locate".Translate())){Highlighted=stop;CameraJumper.TryJump(stop.Cell,stop.Station.Map);}
            if(!allowed)Widgets.Label(new(0,y+48,content.width,25),reason.Translate());
        }
        Widgets.EndScrollView();
        if(Widgets.ButtonText(new(0,rect.height-32,rect.width,30),"LR_PickerBack".Translate()))Find.Targeter.StopTargeting();
    }
    public override void DoWindowContents(Rect rect)
    {
        if(Picking){DrawPicker(rect);return;}
        Text.Font=GameFont.Medium;Widgets.Label(new(0,0,rect.width,32),"LR_RouteEditor".Translate());Text.Font=GameFont.Small;
        Widgets.Label(new(0,42,100,28),"LR_RouteName".Translate());name=Widgets.TextField(new(110,42,rect.width-110,30),name,40);
        Widgets.Label(new(0,82,100,28),"LR_RouteColor".Translate());
        for(int c=0;c<RailColors.Palette.Length;c++)
        {var box=new Rect(110+c*48,82,36,28);Widgets.DrawBoxSolid(box,RailColors.Get(c));if(c==color)Widgets.DrawBox(box,2);if(Widgets.ButtonInvisible(box)){color=c;component.PreviewChanged();}}
        var typeRect=new Rect(495,82,rect.width-495,30);
        if(Widgets.ButtonText(typeRect,(trainKind==RailTrainKind.Passenger?"LR_PassengerTrainKind":"LR_AutomaticTrainKind").Translate()))
            Find.WindowStack.Add(new FloatMenu(Enum.GetValues<RailTrainKind>().Select(kind=>new FloatMenuOption(
                (kind==RailTrainKind.Passenger?"LR_PassengerTrainKind":"LR_AutomaticTrainKind").Translate(),()=>{trainKind=kind;if(kind==RailTrainKind.Passenger)freightOnly=false;})).ToList()));
        TooltipHandler.TipRegion(typeRect,"LR_TrainKindsHelp".Translate());
        if(Widgets.ButtonText(new(0,124,190,30),(loop?"LR_Loop":"LR_Shuttle").Translate())){loop=!loop;component.PreviewChanged();error=null;}
        if(loop&&Widgets.ButtonText(new(205,124,190,30),(reverse?"LR_Reverse":"LR_Forward").Translate())){reverse=!reverse;component.PreviewChanged();}
        if(trainKind==RailTrainKind.Automatic){if(Widgets.ButtonText(new(410,124,220,30),(freightOnly?"LR_FreightOnly":"LR_MixedTrain").Translate()))freightOnly=!freightOnly;}
        else Widgets.Label(new(410,124,220,30),"LR_PassengerTrainKind".Translate());
        Widgets.Label(new(0,161,rect.width,42),"LR_EditorNetworkHelp".Translate());
        var view=new Rect(0,208,rect.width,rect.height-350);var content=new Rect(0,0,rect.width-20,stops.Count*48);
        Widgets.BeginScrollView(view,ref scroll,content);
        for(int i=0;i<stops.Count;i++)
        {
            int slot=i;var stop=stops[i];float y=i*48;
            if(Widgets.ButtonText(new(0,y,248,32),$"{i+1}. {stop.Label}"))ChooseStop(slot);
            if(Widgets.ButtonText(new(252,y,33,32),"◎"))CameraJumper.TryJump(stop.Cell,stop.Station.Map);
            if(Widgets.ButtonText(new(291,y,160,32),dwell[stop].Summary))Find.WindowStack.Add(new Dialog_RailDwell(dwell[stop],s=>dwell[stop]=s));
            if(i>0&&Widgets.ButtonText(new(457,y,32,32),"↑")){(stops[i],stops[i-1])=(stops[i-1],stops[i]);component.PreviewChanged();}
            if(i<stops.Count-1&&Widgets.ButtonText(new(494,y,32,32),"↓")){(stops[i],stops[i+1])=(stops[i+1],stops[i]);component.PreviewChanged();}
            if(Widgets.ButtonText(new(531,y,38,32),"×")){stops.RemoveAt(slot);component.PreviewChanged();break;}
        }
        Widgets.EndScrollView();
        if(stops.Count<16&&Widgets.ButtonText(new(0,rect.height-130,180,30),"LR_AddStop".Translate()))ChooseStop();
        if(stops.Count>=2&&component.PreviewErrorFor(this) is {} pathError)Widgets.Label(new(0,rect.height-91,rect.width,42),pathError.Translate());
        else if(error!=null)Widgets.Label(new(0,rect.height-91,rect.width,42),error.Translate());
        if(Widgets.ButtonText(new(0,rect.height-36,150,32),"CancelButton".Translate()))Close();
        if(Widgets.ButtonText(new(rect.width-210,rect.height-36,210,32),"LR_ConfirmRoute".Translate()))
        {
            if(component.ConfigureService(stops,loop,reverse,existing,name,color,out error,freightOnly,trainKind))
            {foreach(var stop in stops)stop.Station.SetPlatformSettings(stop.Platform,dwell[stop]);Close();}
        }
    }
}

public partial class RailMapComponent
{
    internal IEnumerable<Building_RailStation> Stations=>stations.Where(s=>s.Spawned&&s.Faction==Faction.OfPlayer);
    private List<IntVec3> previewPath=new();
    private List<RailStop> previewStops=new();
    private int previewRevision=-1;
    private object previewSelection;
    private bool previewLoop,previewReverse;
    private Color previewColor;
    private string previewError;
    internal void PreviewChanged()=>previewRevision=-1;
    internal string PreviewErrorFor(RailRouteEditor editor)=>previewSelection==editor?previewError:null;
    private RailServiceState SelectedService()
    {
        if(RailManager.Active?.SelectedService is {} managed&&services.Contains(managed))return managed;
        if(RailManager.Active!=null)return services.FirstOrDefault();
        return Find.Selector.SingleSelectedThing switch{Building_RailStation station=>StationService(station),Building_RailTrain train=>TrainService(train),_=>null};
    }
    public override void MapComponentDraw()
    {
        var editor=RailRouteEditor.Active;var service=SelectedService();
        object selection=editor?.component==this?editor:service;
        if(selection==null){previewSelection=null;return;}
        if(previewSelection!=selection||previewRevision!=topologyRevision)
        {
            previewSelection=selection;previewRevision=topologyRevision;
            previewStops=editor?.component==this?new(editor.stops):Enumerable.Range(0,service.stops.Count).Select(service.Stop).ToList();
            previewLoop=editor?.component==this?editor.loop:service.loop;previewReverse=previewLoop&&(editor?.component==this?editor.reverse:service.reverseLoop);
            previewColor=editor?.component==this?RailColors.Get(editor.color):service.RouteColor;
            if(!BuildServicePath(previewStops,previewLoop,out previewPath,out _,out previewError))previewPath=new();
        }
        if(RailManager.Active?.ShowAll==true&&editor==null)
            foreach(var other in services.Where(s=>s!=service))DrawRoute(other.path,other.loop,other.reverseLoop,other.RouteColor);
        DrawRoute(previewPath,previewLoop,previewReverse,previewColor);
        var view=Find.CameraDriver.CurrentViewRect.ExpandedBy(3);
        if(editor?.component==this&&editor.Picking)
            foreach(var stop in editor.Candidates.Where(s=>s.Station.Spawned&&view.Contains(s.Cell)))
                GenDraw.DrawCircleOutline(stop.Cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays),editor.Highlighted==stop?1.5f:.7f,
                    editor.Highlighted==stop?SimpleColor.Yellow:editor.CanChoose(stop,out _)?SimpleColor.Cyan:SimpleColor.Red);
        for(int i=0;i<previewStops.Count;i++)
        {
            var stop=previewStops[i];if(stop.Station?.Spawned!=true||!view.Contains(stop.Cell))continue;
            GenDraw.DrawCircleOutline(stop.Cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays),1.2f,
                i==0?SimpleColor.Green:!previewLoop&&i==previewStops.Count-1?SimpleColor.Red:SimpleColor.Yellow);
        }
    }
    private void DrawRoute(List<IntVec3> path,bool loop,bool reverse,Color color)
    {
        if(path.Count<2)return;var material=SolidColorMaterials.SimpleSolidColorMaterial(color);
        var view=Find.CameraDriver.CurrentViewRect.ExpandedBy(3);float scale=Math.Max(1,view.Height/30f);
        for(int i=1;i<path.Count+(loop?1:0);i++)
        {
            int next=i%path.Count;if(!view.Contains(path[next])&&!view.Contains(path[i-1]))continue;
            var a=path[i-1].ToVector3Shifted();var b=path[next].ToVector3Shifted();a.y=b.y=AltitudeLayer.MetaOverlays.AltitudeFor();
            GenDraw.DrawLineBetween(a,b,material,.12f*scale);
            if(i%6==0)
            {
                if(reverse)(a,b)=(b,a);var direction=(b-a).normalized;var side=new Vector3(-direction.z,0,direction.x);
                GenDraw.DrawLineBetween(b,b-direction*.7f*scale+side*.35f*scale,material,.12f*scale);
                GenDraw.DrawLineBetween(b,b-direction*.7f*scale-side*.35f*scale,material,.12f*scale);
            }
        }
    }
    public override void MapComponentOnGUI()
    {
        if(Find.Selector.SingleSelectedThing is Building_RailTrain selected&&selected.Map==map&&TrainService(selected) is {} cargoService)
        {
            var summary=TrainCargoSummary(cargoService,6);
            if(!string.IsNullOrEmpty(summary))GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(selected,-1.1f),summary,Color.white);
        }
        object current=RailRouteEditor.Active?.component==this?RailRouteEditor.Active:SelectedService();
        if(current==null||previewSelection!=current)return;
        var picker=RailRouteEditor.Active;
        if(picker?.component==this&&picker.Picking)
        {
            for(int i=0;i<picker.Candidates.Count;i++)
            {
                var stop=picker.Candidates[i];if(!stop.Station.Spawned||!Find.CameraDriver.CurrentViewRect.Contains(stop.Cell))continue;
                var anchor=stop.Cell+stop.Station.RailOffset(stop.Station.def.size.x/2+1,0);
                GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(anchor),$"{i+1} · P{stop.Platform+1}",
                    picker.Highlighted==stop?Color.yellow:picker.CanChoose(stop,out _)?Color.cyan:Color.gray);
                if(picker.Highlighted==stop)
                    GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(anchor+IntVec3.North),stop.Label,Color.yellow);
            }
            return;
        }
        for(int index=0;index<previewStops.Count;index++)
        {
            var stop=previewStops[index];if(stop.Station?.Spawned!=true||!Find.CameraDriver.CurrentViewRect.Contains(stop.Cell))continue;
            string role=index==0?"LR_Start":!previewLoop&&index==previewStops.Count-1?"LR_End":"LR_Stop";
            var label=$"{index+1} {role.Translate()}: {stop.Label}";
            GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(stop.Cell+IntVec3.North),label,Color.white);
        }
    }
}
