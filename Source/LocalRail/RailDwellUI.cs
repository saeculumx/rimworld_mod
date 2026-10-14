using System.Globalization;
using UnityEngine;
using Verse;
namespace LocalRail;

internal sealed class RailDwellForm
{
    internal bool automatic;
    private readonly RailPlatformSettings original;
    internal string manual,minimum,maximum;
    internal RailDwellForm(RailPlatformSettings settings)
    {original=settings.Copy();automatic=settings.automatic;manual=Seconds(settings.manualTicks);minimum=Seconds(settings.minimumTicks);maximum=Seconds(settings.maximumTicks);}
    private static string Seconds(int ticks)=>(ticks/60f).ToString("0.##",CultureInfo.InvariantCulture);
    internal bool TryRead(out RailPlatformSettings settings,out string error)
    {
        settings=null;error="LR_InvalidSeconds".Translate();
        settings=original.Copy();settings.automatic=automatic;
        if(automatic)
        {
            if(!Read(minimum,30,1800,out int min)||!Read(maximum,30,7200,out int max)){settings=null;return false;}
            if(min>max){error="LR_MinAboveMax".Translate();settings=null;return false;}
            settings.minimumTicks=min;settings.maximumTicks=max;
        }
        else{if(!Read(manual,30,7200,out int ticks)){settings=null;return false;}settings.manualTicks=ticks;}
        error=null;return true;
    }
    private static bool Read(string text,int low,int high,out int ticks)
    {
        ticks=0;if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float seconds)&&
            !float.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out seconds))return false;
        if(!float.IsFinite(seconds)||seconds<low/60f||seconds>high/60f)return false;
        ticks=(int)Math.Round(seconds*60);return true;
    }
    internal void Draw(Rect rect)
    {
        Widgets.CheckboxLabeled(new(rect.x,rect.y,rect.width,28),"LR_AutoDwell".Translate(),ref automatic);
        Widgets.Label(new(rect.x,rect.y+38,rect.width,60),"LR_AutoDwellHelp".Translate());
        bool enabled=GUI.enabled;GUI.enabled=enabled&&!automatic;
        Widgets.Label(new(rect.x,rect.y+105,240,30),"LR_ManualSeconds".Translate());manual=Widgets.TextField(new(rect.x+250,rect.y+105,110,30),manual,12);
        GUI.enabled=enabled&&automatic;
        Widgets.Label(new(rect.x,rect.y+150,240,30),"LR_MinSeconds".Translate());minimum=Widgets.TextField(new(rect.x+250,rect.y+150,110,30),minimum,12);
        Widgets.Label(new(rect.x,rect.y+195,240,30),"LR_MaxSeconds".Translate());maximum=Widgets.TextField(new(rect.x+250,rect.y+195,110,30),maximum,12);
        GUI.enabled=enabled&&!automatic;
        Widgets.Label(new(rect.x,rect.y+245,rect.width,28),"LR_TimePresets".Translate());
        int[] presets={1,2,5,10,30};for(int i=0;i<presets.Length;i++)
            if(Widgets.ButtonText(new(rect.x+i*74,rect.y+280,68,30),$"{presets[i]} s"))manual=presets[i].ToString(CultureInfo.InvariantCulture);
        GUI.enabled=enabled;
    }
}

internal sealed class Dialog_RailDwell : Window
{
    private readonly RailDwellForm form;
    private readonly Action<RailPlatformSettings> apply;
    private string error;
    public override Vector2 InitialSize=>new(460,460);
    internal Dialog_RailDwell(RailPlatformSettings settings,Action<RailPlatformSettings> apply)
    {form=new(settings.Copy());this.apply=apply;forcePause=true;doCloseX=true;closeOnAccept=false;}
    public override void DoWindowContents(Rect rect)
    {
        Text.Font=GameFont.Medium;Widgets.Label(new(0,0,rect.width,34),"LR_SetDwell".Translate());Text.Font=GameFont.Small;
        form.Draw(new(0,45,rect.width,320));
        if(error!=null)Widgets.Label(new(0,rect.height-72,rect.width,35),error);
        if(Widgets.ButtonText(new(0,rect.height-34,140,32),"CancelButton".Translate()))Close();
        if(Widgets.ButtonText(new(rect.width-140,rect.height-34,140,32),"LR_Save".Translate())&&form.TryRead(out var settings,out error))
        {apply(settings);Close();}
    }
}
