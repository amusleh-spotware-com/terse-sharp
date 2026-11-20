namespace TerseSharp.Server;

internal readonly record struct MemoryBudget(long Resident, int LoadedProjects, int IncomingProjects, long Available, long InUse);

internal static class LoadAdmission
{
    private const long Baseline = 512 * BytesPerMegabyte;
    private const long MinimumRefusal = 1024 * BytesPerMegabyte;
    private const double Ceiling = 1.0;

    private const long BytesPerMegabyte = 1024 * 1024;

    public static async Task<string?> RefusalAsync(WorkspaceRegistry registry, string target, CancellationToken cancellationToken)
    {
        var loaded = registry.All();

        if (loaded.Count is 0 || loaded.Any(workspace => PathBoundary.SameFile(workspace.SolutionPath, Path.GetFullPath(target))))
            return null;

        var memory = GC.GetGCMemoryInfo();
        var budget = new MemoryBudget(
            Environment.WorkingSet,
            loaded.Sum(workspace => workspace.Solution.ProjectIds.Count),
            await IncomingAsync(target, cancellationToken).ConfigureAwait(false),
            memory.TotalAvailableMemoryBytes,
            memory.MemoryLoadBytes);

        return Describe(budget, [.. loaded.Select(workspace => Path.GetFileName(workspace.SolutionPath))])?.Render();
    }

    internal static TerseError? Describe(MemoryBudget budget, IReadOnlyList<string> loaded)
    {
        var needed = Needed(budget);

        return needed < MinimumRefusal || budget.InUse + needed <= budget.Available * Ceiling
            ? null
            : new TerseError(
                TerseErrorCode.InsufficientMemory,
                string.Create(CultureInfo.InvariantCulture, $"loading {budget.IncomingProjects} more project(s) would need about {needed / BytesPerMegabyte}MB beside the {budget.Resident / BytesPerMegabyte}MB this server holds for {string.Join(", ", loaded)}, with {budget.InUse / BytesPerMegabyte}MB of {budget.Available / BytesPerMegabyte}MB already in use - the load was refused rather than risk the server being killed mid-load"),
                "unload_workspace path=<one of the loaded solutions> first, then load again - replace=true would still hold both while the new one loads; reload=true loads it anyway, without targetFramework=");
    }

    private static long Needed(MemoryBudget budget) => budget.LoadedProjects is 0
        ? 0
        : Math.Max(0, budget.Resident - Baseline) / budget.LoadedProjects * budget.IncomingProjects;

    private static async Task<int> IncomingAsync(string target, CancellationToken cancellationToken)
    {
        try
        {
            return SolutionFile.IsSolutionFile(target)
                ? (await SolutionFile.ProjectsAsync(target, cancellationToken).ConfigureAwait(false)).Count
                : 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or System.Text.Json.JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return 0;
        }
    }
}
