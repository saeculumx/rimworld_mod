namespace LocalRail;
public static class RailText
{
    public static string Inspect(params string[] parts) => string.Join("\n", parts.Where(p=>p!=null)
        .SelectMany(p=>p.Replace("\r", "").Split('\n')).Select(s=>s.Trim()).Where(s=>s.Length>0));
}
