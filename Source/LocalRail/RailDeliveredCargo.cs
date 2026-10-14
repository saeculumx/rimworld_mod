using HarmonyLib;
using RimWorld;
using Verse;
namespace LocalRail;

// A train delivery remains marked when the vanilla hauler picks it up, so a
// preferred source depot cannot reclaim it ahead of ordinary colony storage.
public partial class RailMapComponent
{
    private List<Thing> deliveredCargo=new();
    private readonly HashSet<Thing> deliveredCargoIndex=new();
    internal bool WasDelivered(Thing item)=>deliveredCargoIndex.Contains(item);
    internal void MarkDelivered(Thing item)
    {
        if(item==null||item.Destroyed||!deliveredCargoIndex.Add(item))return;
        deliveredCargo.Add(item);
        if(item.Spawned&&item.Map==map)map.listerHaulables.Notify_AddedThing(item);
    }
    internal void ForgetDelivery(Thing item)
    {if(deliveredCargoIndex.Remove(item))deliveredCargo.Remove(item);}
    private void RebuildDeliveredCargo()
    {
        deliveredCargo??=new();deliveredCargo.RemoveAll(t=>t==null||t.Destroyed);
        deliveredCargoIndex.Clear();foreach(var item in deliveredCargo)deliveredCargoIndex.Add(item);
        foreach(var warehouse in stations.OfType<Building_RailWarehouse>())foreach(var item in warehouse.ReceivedCargo)MarkDelivered(item);
    }
}

[HarmonyPatch(typeof(StoreUtility),nameof(StoreUtility.StoragePriorityAtFor),new[]{typeof(IHaulDestination),typeof(Thing),typeof(bool)})]
internal static class RailDeliveredPriority
{
    private static void Postfix(IHaulDestination at,Thing t,ref StoragePriority __result)
    {
        if(at is Building_RailWarehouse {autoManagePriority:true} warehouse&&warehouse.Map?.GetComponent<RailMapComponent>().WasDelivered(t)==true)
            __result=StoragePriority.Low;
    }
}

[HarmonyPatch(typeof(StoreUtility),nameof(StoreUtility.IsInValidBestStorage))]
internal static class RailDeliveredBestStorage
{
    private static void Postfix(Thing t,ref bool __result)
    {
        if(t.Spawned&&t.GetSlotGroup()?.parent is Building_RailWarehouse {autoManagePriority:true}&&t.Map.GetComponent<RailMapComponent>().WasDelivered(t))
            __result=!StoreUtility.TryFindBestBetterStorageFor(t,null,t.Map,StoragePriority.Low,Faction.OfPlayer,out _,out _,false);
    }
}

[HarmonyPatch(typeof(Thing),nameof(Thing.SplitOff))]
internal static class RailDeliveredSplit
{
    private static void Prefix(Thing __instance,out RailMapComponent __state)
    {__state=__instance.MapHeld?.GetComponent<RailMapComponent>();if(__state?.WasDelivered(__instance)!=true)__state=null;}
    private static void Postfix(Thing __result,RailMapComponent __state)=>__state?.MarkDelivered(__result);
}

[HarmonyPatch(typeof(Thing),nameof(Thing.TryAbsorbStack))]
internal static class RailDeliveredMerge
{
    private static void Prefix(Thing __instance,Thing __0,out (RailMapComponent component,int count) __state)
    {
        var component=__0.MapHeld?.GetComponent<RailMapComponent>()??__instance.MapHeld?.GetComponent<RailMapComponent>();
        __state=(component?.WasDelivered(__0)==true?component:null,__instance.stackCount);
    }
    private static void Postfix(Thing __instance,(RailMapComponent component,int count) __state)
    {if(__instance.stackCount>__state.count)__state.component?.MarkDelivered(__instance);}
}

[HarmonyPatch(typeof(StoreUtility),nameof(StoreUtility.IsGoodStoreCell))]
internal static class RailDeliveredCargoHauling
{
    private static void Postfix(IntVec3 c,Map map,Thing t,ref bool __result)
    {
        if(__result&&map.GetComponent<RailMapComponent>()?.WasDelivered(t)==true&&
            c.GetSlotGroup(map)?.parent is Building_RailWarehouse {autoManagePriority:true})__result=false;
    }
}
