using UnityEngine;
using Verse;
namespace LocalRail;

public class Building_RailDoubleTrack : Building_Rail
{
    internal bool AllowsEdge(IntVec3 a,IntVec3 b)=>Rotation.IsHorizontal?a.x==b.x:a.z==b.z;
    public override IEnumerable<IntVec3> RailCells
    {
        get
        {
            for(int lane=-1;lane<=1;lane+=2)for(int along=-1;along<=1;along++)
            {var offset=Rotation.AsQuat*new Vector3(along,0,lane);yield return Position+new IntVec3(Mathf.RoundToInt(offset.x),0,Mathf.RoundToInt(offset.z));}
        }
    }
}

public class DrawStyle_RailModules : DrawStyle
{
    public override void Update(IntVec3 origin,IntVec3 target,List<IntVec3> buffer)
    {
        buffer.Clear();bool east=Math.Abs(target.x-origin.x)>=Math.Abs(target.z-origin.z);
        int distance=east?target.x-origin.x:target.z-origin.z;int sign=Math.Sign(distance);
        for(int n=0;n<=Math.Abs(distance);n+=3)buffer.Add(origin+(east?IntVec3.East:IntVec3.North)*n*sign);
    }
}
