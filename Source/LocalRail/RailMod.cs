using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LocalRail;

public class RailSettings : ModSettings
{
    public bool automatic = true;
    public bool diagnostics;
    public bool smoothAnimation = true;
    public bool preferRail = true;
    public int minDistance = 25;
    public int stationAccessRadius=80;
    public override void ExposeData()
    {
        Scribe_Values.Look(ref automatic, "automatic", true);
        Scribe_Values.Look(ref diagnostics, "diagnostics", false);
        Scribe_Values.Look(ref smoothAnimation, "smoothAnimation", true);
        Scribe_Values.Look(ref preferRail, "preferRail", true);
        Scribe_Values.Look(ref minDistance, "minDistance", 25);
        Scribe_Values.Look(ref stationAccessRadius,"stationAccessRadius",80);
        stationAccessRadius=Math.Clamp(stationAccessRadius,24,96);
    }
}

public class RailMod : Mod
{
    public static RailSettings Settings;
    public RailMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<RailSettings>();
        new Harmony("localrail.poc").PatchAll();
        Log.Message("[LocalRail] Automatic rail POC loaded. Tested build target: 1.7.4977.");
    }
    public override string SettingsCategory() => "LR_Name".Translate();
    public override void DoSettingsWindowContents(Rect rect)
    {
        var listing = new Listing_Standard(); listing.Begin(rect);
        listing.CheckboxLabeled("LR_Automatic".Translate(), ref Settings.automatic);
        listing.CheckboxLabeled("LR_PreferRail".Translate(), ref Settings.preferRail);
        listing.CheckboxLabeled("LR_SmoothAnimation".Translate(), ref Settings.smoothAnimation);
        listing.CheckboxLabeled("LR_Diagnostics".Translate(), ref Settings.diagnostics);
        listing.Label("LR_MinDistance".Translate(Settings.minDistance));
        Settings.minDistance = Mathf.RoundToInt(listing.Slider(Settings.minDistance, 20, 100));
        if(listing.ButtonText("LR_AccessRadius".Translate(Settings.stationAccessRadius)))
            Find.WindowStack.Add(new FloatMenu(new[]{24,48,64,80,96}.Select(radius=>new FloatMenuOption(radius.ToString(),()=>Settings.stationAccessRadius=radius)).ToList()));
        listing.Label("LR_PocLimits".Translate()); listing.End();
    }
}

internal static class DriverAccess
{
    private static readonly Func<JobDriver, Toil> Current = AccessTools.MethodDelegate<Func<JobDriver, Toil>>(
        AccessTools.PropertyGetter(typeof(JobDriver), "CurToil"));
    public static bool IsTravelToil(Pawn pawn) =>
        Current(pawn.jobs.curDriver) is Toil { defaultCompleteMode: ToilCompleteMode.PatherArrival };
}

[HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawPos), MethodType.Getter)]
internal static class RailPassengerPosition
{
    private static void Postfix(Pawn ___pawn, ref Vector3 __result)
    {
        if (___pawn?.Map != null && ___pawn.Map.GetComponent<RailMapComponent>().TryVisualPosition(___pawn, out var position))
        { __result.x = position.x; __result.z = position.z; }
    }
}

[HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
internal static class RailPatherTick
{
    private static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn, PathEndMode ___peMode)
    {
        if (___pawn?.Map == null) return true;
        return ___pawn.Map.GetComponent<RailMapComponent>().BeforePatherTick(___pawn, __instance, ___peMode);
    }
}

[HarmonyPatch(typeof(Pawn_PathFollower), "PatherArrived")]
internal static class RailArrival
{
    private static bool Prefix(Pawn ___pawn) => ___pawn?.Map == null ||
        !___pawn.Map.GetComponent<RailMapComponent>().HandleArrival(___pawn);
}

[HarmonyPatch(typeof(Pawn_PathFollower), "PatherFailed")]
internal static class RailFailure
{
    private static bool Prefix(Pawn ___pawn) => ___pawn?.Map == null ||
        !___pawn.Map.GetComponent<RailMapComponent>().HandleFailure(___pawn);
}

[HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StopDead))]
internal static class RailStopPatch
{
    private static void Postfix(Pawn ___pawn) => ___pawn?.Map?.GetComponent<RailMapComponent>().ExternalStop(___pawn);
}

[HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
internal static class RailNewPath
{
    private static void Prefix(Pawn ___pawn) => ___pawn?.Map?.GetComponent<RailMapComponent>().ExternalStop(___pawn);
}
