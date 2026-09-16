namespace TerseSharp.Server;

public static class StaleRun
{
    public static string Annotated(string verdict, int before, int after) =>
        Note(before, after) is { } note ? verdict + "\n" + note : verdict;

    public static string? Note(int before, int after) => after > before
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"STALE {after - before} document(s) changed after this run started - this verdict describes the tree as it was when the run began, not as it is now; re-run it")
        : null;
}
