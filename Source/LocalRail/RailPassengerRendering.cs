using HarmonyLib;
using UnityEngine;
using Verse;
namespace LocalRail;

internal static class RailRenderState
{
    internal static bool Riding(Pawn pawn)=>pawn?.Map?.GetComponent<RailMapComponent>().ActiveTrip(pawn)?.phase==TransitPhase.Riding;
}

// Use the original pawn render tree (clothes, headgear and carried resources),
// with compact seated scale. Never scale portraits or cached atlas contents.
[HarmonyPatch(typeof(PawnRenderer),"ParallelGetPreRenderResults")]
internal static class RailPassengerCache
{
    private static void Prefix(Pawn ___pawn,ref bool disableCache)
    {if(RailRenderState.Riding(___pawn))disableCache=true;}
}
[HarmonyPatch(typeof(PawnRenderer),"GetDrawParms")]
internal static class RailPassengerScale
{
    private static void Postfix(Pawn ___pawn,ref PawnDrawParms __result)
    {
        if(!RailRenderState.Riding(___pawn)||__result.flags.FlagSet(PawnRenderFlags.Portrait)||__result.flags.FlagSet(PawnRenderFlags.Cache))return;
        __result.matrix*=Matrix4x4.Scale(Vector3.one*.55f);
        __result.flags|=PawnRenderFlags.NeverAimWeapon;
        // The train draws a compact carried-item icon in the corresponding seat.
        __result.carriedThing=null;
    }
}
[HarmonyPatch(typeof(PawnRenderer),"DrawShadowInternal")]
internal static class RailPassengerShadow
{
    private static bool Prefix(Pawn ___pawn)=>!RailRenderState.Riding(___pawn);
}

[HarmonyPatch(typeof(PawnUIOverlay),nameof(PawnUIOverlay.DrawPawnGUIOverlay))]
internal static class RailPassengerLabels
{
    private static bool Prefix(Pawn ___pawn)=>!RailRenderState.Riding(___pawn);
}

[HarmonyPatch(typeof(PawnRenderUtility),nameof(PawnRenderUtility.DrawCarriedThing))]
internal static class RailPassengerCarriedItem
{
    private static bool Prefix(Pawn pawn)=>!RailRenderState.Riding(pawn);
}

public partial class RailMapComponent
{
    internal string PassengerNames(RailServiceState service)=>string.Join(", ",Assigned(service.id).Where(t=>t.phase==TransitPhase.Riding).Select(t=>t.pawn.LabelShort));
    internal void DrawPassengerCargo(RailServiceState service)
    {
        foreach(var trip in Assigned(service.id))
        {
            if(trip.phase!=TransitPhase.Riding||trip.pawn.carryTracker?.CarriedThing is not { } held||!TryVisualPosition(trip.pawn,out var pos))continue;
            pos+=new Vector3(.18f,0,-.12f);pos.y=AltitudeLayer.Pawn.AltitudeFor()+.12f;
            GenDraw.DrawMeshNowOrLater(held.Graphic.MeshAt(Rot4.North),Matrix4x4.TRS(pos,Quaternion.identity,Vector3.one*.5f),held.Graphic.MatAt(Rot4.North,held),false);
        }
    }
}
