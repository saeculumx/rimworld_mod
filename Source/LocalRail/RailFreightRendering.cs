using UnityEngine;
using Verse;
namespace LocalRail;

internal sealed record RailCargoVisual(Thing Item,int Count);

public partial class RailMapComponent
{
    private int freightRevision;
    private void FreightChanged()=>freightRevision++;
    internal IReadOnlyList<RailCargoVisual> CargoVisuals(RailServiceState service)
    {
        if(service.cargoRevision==freightRevision)return service.cargoVisuals;
        service.cargoRevision=freightRevision;
        service.cargoVisuals=freight.Where(f=>f.train==service.train).SelectMany(f=>f.contents).Where(t=>!t.Destroyed)
            .GroupBy(t=>(t.def,t.Stuff)).Select(g=>new RailCargoVisual(g.First(),g.Sum(t=>t.stackCount))).ToList();
        return service.cargoVisuals;
    }
    internal string TrainCargoSummary(RailServiceState service,int maximumTypes=12)
    {
        var cargo=CargoVisuals(service);if(cargo.Count==0)return null;
        string names=string.Join(" · ",cargo.Take(maximumTypes).Select(c=>$"{c.Item.LabelNoCount} ×{c.Count}"));
        return cargo.Count>maximumTypes?names+$" +{cargo.Count-maximumTypes}":names;
    }
    internal void DrawTrainFreight(RailServiceState service)
    {
        var cargo=CargoVisuals(service);var root=TrainVisualPosition(service);
        var rotation=Quaternion.Euler(0,TrainAngle(service),0);
        for(int i=0;i<Math.Min(6,cargo.Count);i++)
        {
            var item=cargo[i].Item;if(item.Destroyed)continue;
            var position=root+rotation*new Vector3(i%3-1,0,i<3?-.32f:.32f);
            position.y=AltitudeLayer.Pawn.AltitudeFor()+.10f;
            var pad=MaterialPool.MatFrom(BaseContent.WhiteTex,ShaderDatabase.Cutout,new Color(.23f,.25f,.24f));
            GenDraw.DrawMeshNowOrLater(MeshPool.plane10,Matrix4x4.TRS(position,rotation,new Vector3(.44f,1,.44f)),pad,false);
            position.y+=.015f;var graphic=item.Graphic;float scale=.38f/Math.Max(1,Math.Max(graphic.drawSize.x,graphic.drawSize.y));
            GenDraw.DrawMeshNowOrLater(graphic.MeshAt(Rot4.North),Matrix4x4.TRS(position,Quaternion.identity,Vector3.one*scale),graphic.MatAt(Rot4.North,item),false);
        }
    }
}
