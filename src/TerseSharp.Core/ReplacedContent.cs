namespace TerseSharp.Core;

public static class ReplacedContent
{
    public const string Marker = "overwrote existing  ";

    private const int KeptShareDenominator = 4;

    public static Overlap Measure(string before, string after) =>
        before.Length is 0 || string.Equals(before, after, StringComparison.Ordinal)
            ? default
            : Counted(before, Hashed(after));

    public static TerseError Refusal(string path, Overlap overlap) => Errors.Invalid(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{path} already exists and this write keeps {overlap.Kept} of its {overlap.Significant} content lines, so it would replace an unrelated file"),
        "check what the path holds with get_file_outline or read_text and write the new file to another path; to replace it deliberately pass overwrite=true - dryRun=true overwrite=true previews the diff, and write_text ref=HEAD restores a tracked file");

    public static string Marked(string text, string before, string after, bool quiet) =>
        quiet && Replaced(before, after) ? Marker + text : text;

    private static bool Replaced(string before, string after) =>
        before.Length > 0 && !string.Equals(before, after, StringComparison.Ordinal);

    private static Overlap Counted(string before, HashSet<int> survivors)
    {
        var significant = 0;
        var kept = 0;

        foreach (var line in before.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();

            if (!Significant(trimmed))
                continue;

            significant++;
            kept += survivors.Contains(string.GetHashCode(trimmed)) ? 1 : 0;
        }

        return new Overlap(kept, significant);
    }

    private static HashSet<int> Hashed(string text)
    {
        var hashes = new HashSet<int>();

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();

            if (Significant(trimmed))
                hashes.Add(string.GetHashCode(trimmed));
        }

        return hashes;
    }

    private static bool Significant(ReadOnlySpan<char> line)
    {
        foreach (var character in line)
        {
            if (char.IsLetterOrDigit(character))
                return true;
        }

        return false;
    }

    public readonly record struct Overlap(int Kept, int Significant)
    {
        public bool Unrelated => Significant > 0 && Kept * KeptShareDenominator < Significant;
    }
}
