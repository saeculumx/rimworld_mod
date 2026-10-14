using Verse;
using Verse.AI;

namespace LocalRail;

public enum TransitPhase { Approaching, Boarding, Riding, Alighting, Waiting }

public class TransitState : IExposable
{
    public Pawn pawn;
    public LocalTargetInfo originalTarget;
    public PathEndMode originalMode;
    public int jobId;
    public int toilIndex;
    public Building_RailStation start;
    public Building_RailStation end;
    public int startPlatform,endPlatform;
    public IntVec3 BoardingCell => start.PlatformCell(startPlatform);
    public List<IntVec3> route;
    public TransitPhase phase;
    public int index;
    public int ticksLeft;
    public int startedAt;
    public int serviceId;
    public int seat;
    public long queueOrder;
    public bool recoverAfterLoad;
    // Visual segment state is transient; load recovery resumes the original native path.
    public bool animating;
    public int segmentStartedAt;
    public void ExposeData()
    {
        Scribe_References.Look(ref pawn, "pawn");
        Scribe_TargetInfo.Look(ref originalTarget, "originalTarget");
        Scribe_Values.Look(ref originalMode, "originalMode");
        Scribe_Values.Look(ref jobId, "jobId");
        Scribe_Values.Look(ref toilIndex, "toilIndex");
        Scribe_References.Look(ref start, "start"); Scribe_References.Look(ref end, "end");
        Scribe_Values.Look(ref startPlatform,"startPlatform");Scribe_Values.Look(ref endPlatform,"endPlatform");
        Scribe_Collections.Look(ref route, "route", LookMode.Value);
        Scribe_Values.Look(ref phase, "phase"); Scribe_Values.Look(ref index, "index");
        Scribe_Values.Look(ref ticksLeft, "ticksLeft"); Scribe_Values.Look(ref startedAt, "startedAt");
        Scribe_Values.Look(ref serviceId, "serviceId"); Scribe_Values.Look(ref seat, "seat");
        Scribe_Values.Look(ref queueOrder, "queueOrder");
        if (Scribe.mode == LoadSaveMode.PostLoadInit) recoverAfterLoad = true;
    }
}
