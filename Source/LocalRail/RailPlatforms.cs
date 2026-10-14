using UnityEngine;
using Verse;
namespace LocalRail;

public readonly record struct RailStop(Building_RailStation Station, int Platform = 0)
{
    public IntVec3 Cell => Station?.PlatformCell(Platform)??IntVec3.Invalid;
    public string Label => Station?.PlatformLabel(Platform)??"?";
    public RailPlatformSettings Settings => Station.PlatformSettings(Platform);
}

public class RailPlatformSettings : IExposable
{
    public bool automatic;
    public int manualTicks = 90, minimumTicks = 120, maximumTicks = 900;
    public RailPlatformSettings Copy() => new() {automatic=automatic,manualTicks=manualTicks,minimumTicks=minimumTicks,maximumTicks=maximumTicks};
    public int Estimate => automatic ? minimumTicks + 60 : manualTicks;
    public string Summary => automatic ? "LR_AutoRange".Translate((minimumTicks/60f).ToString("0.#"),(maximumTicks/60f).ToString("0.#"))
        : "LR_DwellSeconds".Translate((manualTicks/60f).ToString("0.#"));
    public void ExposeData()
    {
        Scribe_Values.Look(ref automatic,"automatic");Scribe_Values.Look(ref manualTicks,"manualTicks",90);
        Scribe_Values.Look(ref minimumTicks,"minimumTicks",120);Scribe_Values.Look(ref maximumTicks,"maximumTicks",900);
        if(Scribe.mode==LoadSaveMode.PostLoadInit)
        {manualTicks=Math.Clamp(manualTicks,30,7200);minimumTicks=Math.Clamp(minimumTicks,30,1800);maximumTicks=Math.Clamp(maximumTicks,minimumTicks,7200);}
    }
}

internal static class RailColors
{
    internal static readonly Color[] Palette = {new(.15f,.8f,.9f),new(.95f,.5f,.15f),new(.45f,.85f,.3f),new(.8f,.4f,.95f),
        new(1,.8f,.15f),new(.95f,.35f,.45f),new(.4f,.55f,1),new(.9f,.9f,.9f)};
    internal static Color Get(int index) => Palette[Math.Clamp(index,0,Palette.Length-1)];
}
