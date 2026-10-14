using HarmonyLib;
using Verse;
namespace LocalRail;

// Vanilla spawns small buildings before larger ones when loading. Old non-edifice
// track overlays would otherwise make it refund and destroy the saved station.
[HarmonyPatch(typeof(GenSpawn),nameof(GenSpawn.SpawnBuildingAsPossible))]
internal static class RailPhysicalMigration
{
    private static void Prefix(Building building,Map map,bool respawningAfterLoad)
    {
        if(!respawningAfterLoad||building is not Building_RailStation station)return;
        int removed=0,nonPlatform=0;
        var platformCells=station.RailCells.ToHashSet();
        foreach(var cell in station.OccupiedRect())
        {
            if(!cell.InBounds(map))continue;
            foreach(var old in cell.GetThingList(map).OfType<Building_Rail>().Where(t=>t.def.defName=="LR_Track").ToArray())
            {
                if(!platformCells.Contains(cell))nonPlatform++;
                old.Destroy(DestroyMode.Vanish);removed++;
            }
        }
        if(removed>0)Log.Message($"[LocalRail] Physical station migration {station.ThingID}: removed {removed} old overlapping track cells ({nonPlatform} outside platform rows). Station and route references retained; review changed connections.");
    }
}
