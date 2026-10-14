using UnityEngine;
using RimWorld;
using Verse;
namespace LocalRail;

// Reuse the original deck art as rows. Ghosts and blueprints share built geometry,
// instead of stretching a single decorative strip over a multi-platform footprint.
public class Graphic_RailWarehouse : Graphic_Single
{
    internal static IEnumerable<int> DeckRows(IntVec2 size)
    {for(int z=-size.z/2;z<=size.z/2;z+=2)yield return z;}
    internal static void PrintDecks(SectionLayer layer,Vector3 center,IntVec2 size,Material material,Rot4 rotation)
    {
        foreach(int z in DeckRows(size))Printer_Plane.PrintPlane(layer,center+rotation.AsQuat*(Vector3.forward*z),new Vector2(size.x,2.8f),material,rotation.AsAngle,topVerticesAltitudeBias:0);
    }
    protected virtual IEnumerable<(Vector3 offset,Vector2 size)> Decorations(IntVec2 size)
    {foreach(int z in DeckRows(size))yield return(Vector3.forward*z,new(size.x,2.8f));}
    private static IntVec2 SizeFor(ThingDef def)=>(def.entityDefToBuild as ThingDef??def).size;
    public override Graphic GetColoredVersion(Shader shader,Color color,Color colorTwo)=>
        GraphicDatabase.Get(GetType(),path,shader,drawSize,color,colorTwo,data,null);
    public override void DrawWorker(Vector3 loc,Rot4 rot,ThingDef def,Thing thing,float extraRotation)
    {
        var size=SizeFor(def);
        foreach(var part in Decorations(size))DrawPlane(loc+rot.AsQuat*part.offset,part.size,MatSingle,rot);
        DrawRails(size,loc,rot,(center,scale)=>DrawPlane(center,scale,RailMaterial(),rot));
    }
    public override void Print(SectionLayer layer,Thing thing,float extraRotation)
    {
        var size=SizeFor(thing.def);var center=thing.TrueCenter();center.y=AltitudeLayer.Blueprint.AltitudeFor();
        var rotation=thing.Rotation;
        foreach(var part in Decorations(size))Printer_Plane.PrintPlane(layer,center+rotation.AsQuat*part.offset,part.size,MatSingle,rotation.AsAngle,topVerticesAltitudeBias:0);
        DrawRails(size,center,rotation,(position,scale)=>Printer_Plane.PrintPlane(layer,position,scale,RailMaterial(),rotation.AsAngle,topVerticesAltitudeBias:0));
    }
    private Material RailMaterial()=>MaterialPool.MatFrom(BaseContent.WhiteTex,ShaderDatabase.Cutout,color);
    private static void DrawRails(IntVec2 size,Vector3 center,Rot4 rotation,Action<Vector3,Vector2> plane)
    {
        int count=size.z/2;
        for(int p=0;p<count;p++)
        {
            var row=center+rotation.AsQuat*(Vector3.forward*(2*p-(count-1)));row.y+=.025f;
            foreach(int sign in new[]{-1,1})plane(row+rotation.AsQuat*(Vector3.forward*(sign*.19f)),new(size.x,.075f));
            for(int x=-size.x/2;x<=size.x/2;x++)plane(row+rotation.AsQuat*(Vector3.right*x)-Vector3.up*.01f,new(.11f,.7f));
        }
    }
    private static void DrawPlane(Vector3 center,Vector2 scale,Material material,Rot4 rotation)=>
        Graphics.DrawMesh(MeshPool.plane10,Matrix4x4.TRS(center,rotation.AsQuat,new(scale.x,1,scale.y)),material,0);
}

public class Graphic_RailPlatforms : Graphic_RailWarehouse
{
    protected override IEnumerable<(Vector3 offset,Vector2 size)> Decorations(IntVec2 size)
    {for(int p=0;p<size.z/2;p++)yield return(Vector3.forward*(2*p-(size.z/2-1)),new(size.x,1.9f));}
}
