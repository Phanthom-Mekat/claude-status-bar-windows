namespace ClaudeStatusBar;

/// <summary>Numeric component-wise version compare so "0.0.10" > "0.0.9".</summary>
public static class VersionCompare
{
    public static bool IsNewer(string a, string b)
    {
        int[] pa = Parse(a), pb = Parse(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    static int[] Parse(string v) =>
        (v ?? "").TrimStart('v').Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
}
