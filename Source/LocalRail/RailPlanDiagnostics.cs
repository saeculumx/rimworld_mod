using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace LocalRail;

internal sealed class RailPlanReport
{
    internal int tick,starts,ends,travel,wait;
    internal float direct,access,egress;
    internal string reason,line;
    internal string Costs=>"LR_DecisionCosts".Translate((direct/60f).ToString("0.0"),(access/60f).ToString("0.0"),
        (egress/60f).ToString("0.0"),(travel/60f).ToString("0.0"),(wait/60f).ToString("0.0"));
}
public partial class RailMapComponent
{
    private readonly Dictionary<Pawn,RailPlanReport> planReports=new();
    internal RailPlanReport Diagnose(Pawn pawn)
    {
        string reason=!RailMod.Settings.automatic?"LR_ReasonDisabled":pawn.Drafted?"LR_ReasonDrafted":
            !Eligible(pawn)?"LR_ReasonJob":Trip(pawn)!=null?"LR_ReasonAssigned":
            !pawn.pather.Moving||pawn.pather.curPath?.Found!=true||!DriverAccess.IsTravelToil(pawn)?"LR_ReasonNotMoving":
            pawn.Position.DistanceTo(pawn.pather.Destination.Cell)<RailMod.Settings.minDistance?"LR_ReasonShort":null;
        if(reason!=null)return new(){tick=GenTicks.TicksGame,reason=reason};
        TryPlan(pawn,pawn.pather,PathEndMode.OnCell,false);
        return planReports[pawn];
    }
}
internal sealed class Dialog_RailDecision : Window
{
    private readonly Pawn pawn;
    private RailPlanReport report;
    public override Vector2 InitialSize=>new(680,450);
    internal Dialog_RailDecision(Pawn pawn)
    {this.pawn=pawn;report=pawn.Map.GetComponent<RailMapComponent>().Diagnose(pawn);doCloseX=true;draggable=true;preventCameraMotion=false;}
    public override void DoWindowContents(Rect rect)
    {
        Text.Font=GameFont.Medium;Widgets.Label(new(0,0,rect.width,35),"LR_RailDecision".Translate()+" — "+pawn.LabelShort);Text.Font=GameFont.Small;
        Widgets.Label(new(0,48,rect.width,76),report.reason.Translate());
        Widgets.Label(new(0,130,rect.width,38),"LR_DecisionContext".Translate(pawn.jobs.curDriver?.GetReport()??pawn.CurJob?.def.defName??"—",RailMod.Settings.stationAccessRadius,report.starts,report.ends));
        if(report.direct>0)Widgets.Label(new(0,175,rect.width,68),report.Costs);
        if(report.line!=null)Widgets.Label(new(0,246,rect.width,28),"LR_DecisionLine".Translate(report.line));
        Widgets.Label(new(0,285,rect.width,65),"LR_DecisionHelp".Translate());
        if(Widgets.ButtonText(new(0,rect.height-34,200,32),"LR_DecisionRefresh".Translate())&&pawn.Spawned)
            report=pawn.Map.GetComponent<RailMapComponent>().Diagnose(pawn);
    }
}
[HarmonyPatch(typeof(Pawn),nameof(Pawn.GetGizmos))]
internal static class RailDecisionGizmo
{
    private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result,Pawn __instance)
    {
        foreach(var gizmo in __result)yield return gizmo;
        if(__instance.Spawned&&__instance.IsColonistPlayerControlled)
            yield return new Command_Action{defaultLabel="LR_RailDecision".Translate(),defaultDesc="LR_DecisionHelp".Translate(),icon=TexButton.Rename,
                action=()=>Find.WindowStack.Add(new Dialog_RailDecision(__instance))};
    }
}
