using RimWorld;
using Verse;
using UnityEngine;
using HarmonyLib;
namespace LocalRail;

// A real vanilla door owns the rail cell: no track/door edifice overlap.
public class Building_RailDoor : Building_Door, IRailNode
{
    public virtual IEnumerable<IntVec3> RailCells { get { yield return Position; } }
    internal virtual bool AllowsEdge(IntVec3 a,IntVec3 b)=>a.x==b.x||a.z==b.z;
    public override void ExposeData()
    {
        if(Scribe.mode==LoadSaveMode.PostLoadInit&&def.MadeFromStuff&&Stuff==null)
        {SetStuffDirect(ThingDefOf.Steel);Log.Message("[LocalRail] Legacy rail door assigned steel; no materials charged.");}
        base.ExposeData();
    }
    private int trainProtectedUntil=-1;
    internal bool TrainAllowed => Faction==Faction.OfPlayer && !this.IsForbidden(Faction.OfPlayer);
    internal bool TrainReady => TrainAllowed && Open && TicksTillFullyOpened==0;
    internal void RequestTrain(int protectionTicks=30)
    {
        if(!TrainAllowed)return;
        trainProtectedUntil=Math.Max(trainProtectedUntil,GenTicks.TicksGame+protectionTicks);
        DoorOpen(Math.Max(30,protectionTicks));
    }
    public override void SpawnSetup(Map map,bool respawningAfterLoad)
    {base.SpawnSetup(map,respawningAfterLoad);map.GetComponent<RailMapComponent>().Register(this);}
    public override void DeSpawn(DestroyMode mode=DestroyMode.Vanish)
    {Map.GetComponent<RailMapComponent>().Unregister(this);base.DeSpawn(mode);}
    protected override void Tick()
    {
        // Protect the actual car, including a paused/faulted car or a loaded save.
        if(RailCells.Any(Map.GetComponent<RailMapComponent>().TrainBodyAt))
        {trainProtectedUntil=GenTicks.TicksGame+30;DoorOpen(30);}
        if(GenTicks.TicksGame<=trainProtectedUntil&&TrainAllowed)DoorOpen(30);
        base.Tick();
    }
    public override void Print(SectionLayer layer)=>Building_Rail.PrintTracks(layer,Map,RailCells);
    public override string GetInspectString()=>RailText.Inspect(base.GetInspectString(),"LR_RailDoorHelp".Translate());
}

public class Building_RailDoubleDoor : Building_RailDoor
{
    public override IEnumerable<IntVec3> RailCells
    {get {var axis=Rotation.AsQuat*Vector3.right;yield return Position+new IntVec3(Mathf.RoundToInt(axis.x),0,Mathf.RoundToInt(axis.z));yield return Position-new IntVec3(Mathf.RoundToInt(axis.x),0,Mathf.RoundToInt(axis.z));}}
    internal override bool AllowsEdge(IntVec3 a,IntVec3 b)=>Rotation.IsHorizontal?a.z==b.z:a.x==b.x;
    protected override void DrawAt(Vector3 drawLoc,bool flip=false)
    {
        DoorPreDraw();
        // Leaf bitmap: 2172 px wide, opaque body spans x=144..2026.
        // A nominal half-width left a .20-cell slit. Compensate its alpha margin
        // with a slight center overlap, while keeping native full opening travel.
        DrawMovers(drawLoc,.25f+.5f*OpenPct,Graphic,AltitudeLayer.DoorMoveable.AltitudeFor(),new Vector3(.58f,1,1),null);
        Comps_PostDraw();
    }
}

[HarmonyPatch(typeof(Thing),nameof(Thing.ExposeData))]
internal static class RailDoorPlanMaterialMigration
{
    private static void Prefix(Thing __instance)
    {
        if(Scribe.mode!=LoadSaveMode.PostLoadInit||__instance is not (Blueprint_Build or Frame))return;
        if(__instance.def.entityDefToBuild is ThingDef {defName:"LR_RailDoor"})
        {
            if(__instance is Blueprint_Build blueprint&&blueprint.stuffToUse==null)
            {blueprint.stuffToUse=ThingDefOf.Steel;Log.Message("[LocalRail] Legacy rail door blueprint assigned steel.");}
            else if(__instance is Frame&&__instance.Stuff==null)
            {__instance.SetStuffDirect(ThingDefOf.Steel);Log.Message("[LocalRail] Legacy rail door frame assigned steel.");}
        }
    }
}

public partial class RailMapComponent
{
    private readonly HashSet<IntVec3> trainBodyCells=new();
    private int trainBodyTick=-1;
    internal bool TrainBodyAt(IntVec3 cell)
    {
        if(trainBodyTick!=GenTicks.TicksGame)
        {
            trainBodyTick=GenTicks.TicksGame;trainBodyCells.Clear();
            foreach(var service in services)
            {
                if(service.train?.Spawned!=true)continue;
                trainBodyCells.Add(service.train.Position);
                if(service.path.Count<2||service.index<0||service.index>=service.path.Count)continue;
                for(int offset=-2;offset<=2;offset++)
                {
                    int index=service.index+offset;
                    if(service.loop)index=(index+service.path.Count)%service.path.Count;
                    if(index>=0&&index<service.path.Count)trainBodyCells.Add(service.path[index]);
                }
            }
        }
        return trainBodyCells.Contains(cell);
    }
    private void PrepareDoors(RailServiceState service)
    {
        // 24 edges cover at least 72 game ticks, longer than this door's full
        // opening. Reduce by elapsed segment time and stop at intermediate dwell.
        int index=service.index,time=0;
        for(int step=0;step<24;step++)
        {
            int next=index+service.direction;
            if(service.loop)next=(next+service.path.Count)%service.path.Count;
            if(next<0||next>=service.path.Count)return;
            time+=step==0&&service.animating?Math.Max(0,service.ticksLeft):RailGraph.EdgeDuration(Cell(service.path[index]),Cell(service.path[next]));
            var cell=service.path[next];
            if(cell.GetDoor(map) is Building_RailDoor door && time<=door.TicksToOpenNow+12)door.RequestTrain();
            if(service.StopAt(next)>=0)return;
            index=next;
        }
    }
    private bool NoseDoorsReady(RailServiceState service)
    {
        // A 3.2-cell sprite must stop before its nose overlaps the closed leaf,
        // not merely when the center is about to enter the door's grid cell.
        int index=service.NextIndex;
        for(int ahead=0;ahead<=2;ahead++)
        {
            if(index<0||index>=service.path.Count)return true;
            if(service.path[index].GetDoor(map) is Building_RailDoor door)
            {
                door.RequestTrain();
                if(!door.TrainReady)return false;
            }
            if(service.StopAt(index)>=0)return true;
            index+=service.direction;
            if(service.loop)index=(index+service.path.Count)%service.path.Count;
        }
        return true;
    }
    private bool RailDoorEdge(IntVec3 a,IntVec3 b)=>
        (a.GetDoor(map) is not Building_RailDoor da||da.AllowsEdge(a,b))&&
        (b.GetDoor(map) is not Building_RailDoor db||db.AllowsEdge(a,b))&&
        (a.GetEdifice(map) is not Building_RailDoubleTrack ta||ta.AllowsEdge(a,b))&&
        (b.GetEdifice(map) is not Building_RailDoubleTrack tb||tb.AllowsEdge(a,b));
}
