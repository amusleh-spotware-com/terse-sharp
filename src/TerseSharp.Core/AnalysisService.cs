using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class AnalysisService
{
    public static async Task<string> AnalyzeAsync(
        LoadedWorkspace workspace,
        string? path,
        DiagnosticSeverity minimum,
        IReadOnlyList<string> ids,
        bool includeDeadCode,
        int maxResults,
        bool sinceLast,
        bool changed,
        TouchedLines? touched = null,
        string baseRef = "",
        string? exclude = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var collected = await CollectedAsync(workspace, path, includeDeadCode, changed, ids, exclude, timeout ?? Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

        if (!collected.IsOk)
            return collected.Error!.Render();

        var value = collected.Value;
        var all = Filter(value.Found, value.Scope, minimum, ids);
        var split = touched is null ? default : BaselineFold.Split(all, touched);
        var found = touched is null ? all : split.Kept;
        var declaration = await DiagnosticDeclarations.ResolverAsync(found, cancellationToken).ConfigureAwait(false);

        return Render(
            workspace.Root,
            path,
            value,
            found,
            declaration,
            maxResults,
            sinceLast,
            minimum,
            ids,
            includeDeadCode,
            changed,
            new Narrowed(touched, baseRef, all.Length - found.Length - split.Fileless.Count, split)) + ExcludedNote(path, changed, exclude) + PartialTrailer(value.Partial);
    }

    public static async Task<Result<GateFindings>> FindingsAsync(
        LoadedWorkspace workspace,
        string? path,
        DiagnosticSeverity minimum,
        bool includeDeadCode,
        bool changed,
        TouchedLines? touched,
        CancellationToken cancellationToken)
    {
        var collected = await CollectedAsync(workspace, path, includeDeadCode, changed, [], null, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

        if (!collected.IsOk)
            return Result.Fail<GateFindings>(collected.Error!);

        var all = Filter(collected.Value.Found, collected.Value.Scope, minimum, []);
        var found = Introduced(all, touched);
        string[] every = [.. collected.Value.Extra];
        var extra = Introduced(every, touched);
        var declaration = await DiagnosticDeclarations.ResolverAsync(found, cancellationToken).ConfigureAwait(false);

        return Result.Ok(new GateFindings(
            Grouped(DiagnosticFold.Findings(workspace.Root, found, DiagnosticFormat.Head, declaration), extra),
            all.Length - found.Length + every.Length - extra.Length));
    }

    private static async Task<Result<Collected>> CollectedAsync(
            LoadedWorkspace workspace,
            string? path,
            bool includeDeadCode,
            bool changed,
            IReadOnlyList<string> ids,
            string? exclude,
            TimeSpan timeout,
            CancellationToken cancellationToken)
    {
        var unscoped = path is null && !changed && exclude is not { Length: > 0 };
        var selected = unscoped ? [] : DocumentScope.Select(workspace, path, changed);

        if (!unscoped && selected.Length is 0)
            return Result.Fail<Collected>(Empty(path, changed));

        var documents = Excluded(workspace, selected, exclude);

        if (!unscoped && documents.Length is 0)
            return Result.Fail<Collected>(AllExcluded(path, exclude!));

        var targets = Targets(workspace, documents, unscoped).ToArray();
        var scope = Scope(workspace, documents, unscoped);
        var harvest = new Harvest();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var bound = new Bound(deadline.Token, cancellationToken);

        deadline.CancelAfter(timeout);

        await Parallel.ForEachAsync(
            targets,
            ParallelWork.Options(cancellationToken),
            (project, _) => CollectAsync(project, harvest, bound)).ConfigureAwait(false);

        var unfinished = new HashSet<ProjectId>(harvest.Unfinished);
        var finished = unfinished.Count is 0 ? targets : Array.FindAll(targets, project => !unfinished.Contains(project.Id));
        var (Findings, Finished) = await DeadCodeAsync(workspace, finished, scope, includeDeadCode, bound).ConfigureAwait(false);
        var policy = await PolicyAsync(workspace, finished, documents, cancellationToken).ConfigureAwait(false);
        var options = await PolicyCache.ForAsync(workspace.Root, cancellationToken).ConfigureAwait(false);
        var partial = new Partial(targets.Length, Unfinished(workspace, targets, documents, unfinished), !Finished, timeout);

        return Result.Ok(new Collected(harvest, [.. Findings, .. policy], ProjectDiagnostics.Unsupported(targets, ids, harvest.Found, options.EnforcedIds()), scope, partial));
    }

    private readonly record struct Collected(
            Harvest Harvest,
            IReadOnlyList<string> Extra,
            IReadOnlyList<string> Unsupported,
            DiagnosticScope Scope,
            Partial Partial)
    {
        public ConcurrentBag<Diagnostic> Found => Harvest.Found;

        public ConcurrentBag<string> Analyzed => Harvest.Analyzed;
    }

    private static List<string> Engines(ConcurrentBag<string> analyzed, bool includeDeadCode)
    {
        var engines = new List<string>(analyzed.Count + 2) { "compiler" };

        engines.AddRange(analyzed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        if (includeDeadCode)
            engines.Add("dead-code");

        return engines;
    }

    private static string[] Keep(IReadOnlyList<string> findings, IReadOnlyList<string> ids, DiagnosticSeverity minimum) =>
        [.. findings.Where(finding => Meets(finding, minimum) && (ids.Count is 0 || ids.Any(id => finding.AsSpan().StartsWith(DiagnosticIds.Stem(id), StringComparison.OrdinalIgnoreCase))))];

    private static IEnumerable<Project> Targets(LoadedWorkspace workspace, DocumentId[] documents, bool unscoped) =>
        unscoped
            ? workspace.Solution.Projects
            : documents
                .Select(id => workspace.Solution.GetDocument(id)?.Project)
                .OfType<Project>()
                .DistinctBy(project => project.Id);

    private static async ValueTask CollectAsync(Project project, Harvest harvest, Bound bound)
    {
        try
        {
            harvest.Keep(project, await ProjectFindingsAsync(project, bound.Deadline).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (bound.Expired)
        {
            harvest.Unfinished.Add(project.Id);
        }
    }

    private static Diagnostic[] Filter(
        ConcurrentBag<Diagnostic> found,
        DiagnosticScope scope,
        DiagnosticSeverity minimum,
        IReadOnlyList<string> ids) =>
        [.. found.Where(diagnostic => Keep(diagnostic, scope, minimum, ids))];

    private static bool Keep(Diagnostic diagnostic, DiagnosticScope scope, DiagnosticSeverity minimum, IReadOnlyList<string> ids) =>
        diagnostic.Severity >= minimum
        && !diagnostic.IsSuppressed
        && (ids.Count is 0 || DiagnosticIds.MatchesAny(diagnostic.Id, ids))
        && scope.Includes(diagnostic);

    private static string[] Grouped(DiagnosticFold.Finding[] findings, IReadOnlyList<string> extra) =>
    [
        .. DiagnosticFold
            .Lines(findings)
            .Concat(extra
                .GroupBy(text => text, StringComparer.Ordinal)
                .Select(group => DiagnosticFold.Repeated(group.Key, group.Count())))
            .Order(StringComparer.Ordinal),
    ];

    private static string Render(
        string root,
        string? path,
        Collected collected,
        Diagnostic[] found,
        Func<Location, string?> declaration,
        int maxResults,
        bool sinceLast,
        DiagnosticSeverity minimum,
        IReadOnlyList<string> ids,
        bool includeDeadCode,
        bool changed,
        Narrowed narrowed)
    {
        var kept = Keep(collected.Extra, ids, minimum);
        var extra = Introduced(kept, narrowed.Touched);
        var findings = DiagnosticFold.Findings(root, found, DiagnosticFormat.Head, declaration);
        var occurrences = Occurrences(findings, extra);
        var scope = string.Create(CultureInfo.InvariantCulture, $"analyze|{root}|{path ?? "solution"}|{changed}|{minimum}|{string.Join(",", ids)}|{includeDeadCode}|{narrowed.BaseRef}");
        var delta = Recorded(scope, occurrences, collected.Partial);
        var shown = sinceLast ? delta.Appeared : Grouped(findings, extra);

        var response = new ResponseBuilder("analyze", path ?? "solution");

        response.Summary(
            ResultCap.Shown(shown.Count, maxResults),
            shown.Count,
            Unit(sinceLast && !collected.Partial.IsPartial, shown.Count),
            "minSeverity=, ids= or path=");
        response.Note("engines=" + string.Join("+", Engines(collected.Analyzed, includeDeadCode)));

        if (narrowed.Touched is not null && narrowed.PreExisting + kept.Length - extra.Length > 0)
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"{narrowed.PreExisting + kept.Length - extra.Length} pre-existing finding(s) on these files were not reported - they sit outside every line the working tree changed against {narrowed.BaseRef} - baseRef=\"\" lists every finding in these files at once"));
        }

        foreach (var note in new[] { BaselineFold.UnchangedErrorNote(narrowed.Split), BaselineFold.FilelessNote(narrowed.Split, narrowed.BaseRef) })
        {
            if (note is not null)
                response.Note(note);
        }

        if (!sinceLast && occurrences.Length != shown.Count)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"total={occurrences.Length} occurrence(s) folded onto {shown.Count} record(s)"));

        if (collected.Unsupported.Count > 0)
            response.Note("NOT_ENABLED " + string.Join(", ", collected.Unsupported) + " - no analyzer these projects reference declares it, so this pass could not have found it");

        if (OutsideIds(collected, ids, narrowed.Touched) is > 0 and var outside)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"{outside} error(s) outside ids= not shown - drop ids= to see them"));

        if (changed)
            response.Note("gate runs this, format and cleanup fix=all as one call");

        if (sinceLast)
            response.Note(SinceLastNote(delta, occurrences.Length, collected.Partial.IsPartial));

        foreach (var line in shown.Capped(maxResults))
            response.Line(line);

        foreach (var line in sinceLast ? delta.Fixed.Capped(maxResults) : [])
            response.Line("FIXED " + line);

        return response.ToString();
    }

    private static string Unit(bool sinceLast, int shown) => (sinceLast, shown) switch
    {
        (true, _) => "new diagnostics, one record per occurrence",
        (false, 0) => "diagnostics",
        _ => "diagnostics, one record per id and message",
    };

    private static DiagnosticScope Scope(LoadedWorkspace workspace, DocumentId[] documents, bool unscoped) =>
            unscoped
                ? DiagnosticScope.For(workspace, null)
                : DiagnosticScope.Of(
                    workspace.Root,
                    documents.Select(id => workspace.Solution.GetDocument(id)?.FilePath).OfType<string>());

    private static TerseError Empty(string? path, bool changed) => changed
        ? Errors.Invalid(
            "no document under that scope was modified since this workspace started tracking changes",
            "drop changed=true to analyze the whole scope, or pass path= to name the files yourself")
        : Errors.DocumentNotFound(path ?? "solution");

    private static string[] Occurrences(DiagnosticFold.Finding[] findings, IReadOnlyList<string> extra) =>
        DiagnosticFold.PerOccurrence(findings.Select(finding => finding.Key).Concat(extra));

    private static async Task<IReadOnlyList<string>> PolicyAsync(
            LoadedWorkspace workspace,
            Project[] targets,
            DocumentId[] documents,
            CancellationToken cancellationToken)
    {
        var options = await PolicyCache.ForAsync(workspace.Root, cancellationToken).ConfigureAwait(false);

        if (!options.Active)
            return [];

        var scanned = documents.Length is 0 ? [.. targets.SelectMany(project => project.DocumentIds)] : documents;
        var found = await PolicyGate.ScanAsync(workspace, scanned, cancellationToken).ConfigureAwait(false);

        return [.. found.Select(finding => finding.Diagnostic())];
    }

    private readonly record struct Narrowed(TouchedLines? Touched, string BaseRef, int PreExisting, BaselineSplit Split = default);

    private static Diagnostic[] Introduced(Diagnostic[] found, TouchedLines? touched) =>
        touched is null ? found : Introduced(BaselineFold.Split(found, touched));

    private static Diagnostic[] Introduced(BaselineSplit split) => [.. split.Kept, .. split.Fileless.Errors];

    private static string[] Introduced(string[] extra, TouchedLines? touched) =>
        touched is null ? extra : [.. extra.Where(touched.CoversRecord)];

    private static bool Meets(string finding, DiagnosticSeverity minimum)
    {
        var rest = finding.AsSpan(finding.IndexOf(' ', StringComparison.Ordinal) + 1);
        var end = rest.IndexOf(' ');

        return RecordSeverity(end < 0 ? rest : rest[..end]) >= minimum;
    }

    private static DiagnosticSeverity RecordSeverity(ReadOnlySpan<char> token) => token switch
    {
        "hidden" => DiagnosticSeverity.Hidden,
        "info" => DiagnosticSeverity.Info,
        "warning" => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Error,
    };

    private static int OutsideIds(Collected collected, IReadOnlyList<string> ids, TouchedLines? touched) =>
        ids.Count is 0
            ? 0
            : Introduced(
                [.. collected.Found
                    .Where(diagnostic => Keep(diagnostic, collected.Scope, DiagnosticSeverity.Error, []) && !DiagnosticIds.MatchesAny(diagnostic.Id, ids))
                    .Distinct()],
                touched).Length;

    private static DocumentId[] Excluded(LoadedWorkspace workspace, DocumentId[] documents, string? exclude)
    {
        if (exclude is not { Length: > 0 } glob)
            return documents;

        var matcher = FileGlob.Compile(glob);

        return [.. documents.Where(id => workspace.Solution.GetDocument(id)?.FilePath is not { } file || !matcher.MatchesFile(workspace.Root, file))];
    }

    private static TerseError AllExcluded(string? path, string exclude) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"exclude={exclude} dropped every document {path ?? "the solution"} selected, so nothing was analyzed"),
        "narrow exclude= or widen path=");

    private const int MaxNextScope = 10;

    private sealed class Harvest
    {
        public ConcurrentBag<Diagnostic> Found { get; } = [];

        public ConcurrentBag<string> Analyzed { get; } = [];

        public ConcurrentBag<ProjectId> Unfinished { get; } = [];

        public void Keep(Project project, ProjectFindings findings)
        {
            foreach (var diagnostic in findings.Diagnostics)
                Found.Add(diagnostic);

            if (findings.Analyzed)
                Analyzed.Add("analyzers(" + project.Name + ")");
        }
    }

    private readonly record struct ProjectFindings(ImmutableArray<Diagnostic> Diagnostics, bool Analyzed);

    private readonly record struct Bound(CancellationToken Deadline, CancellationToken Caller)
    {
        public bool Expired => Deadline.IsCancellationRequested && !Caller.IsCancellationRequested;
    }

    private readonly record struct UnfinishedScope(string[] Projects, string[] Scope, bool Widened);

    private readonly record struct Partial(int Targets, UnfinishedScope Unfinished, bool DeadCodeUnfinished, TimeSpan Timeout)
    {
        public bool IsPartial => Unfinished.Projects is { Length: > 0 } || DeadCodeUnfinished;
    }

    private static async Task<ProjectFindings> ProjectFindingsAsync(Project project, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

        if (compilation is null)
            return new([], Analyzed: false);

        var compiler = await SuppressedDiagnostics.CompilerAsync(project, compilation, cancellationToken).ConfigureAwait(false);
        var analyzers = ProjectDiagnostics.Analyzers(project);

        if (analyzers.IsEmpty)
            return new(compiler, Analyzed: false);

        var analyzed = await ProjectDiagnostics.RunAsync(compilation, project, analyzers, cancellationToken).ConfigureAwait(false);

        return new(compiler.AddRange(analyzed), Analyzed: true);
    }

    private static async Task<(IReadOnlyList<string> Findings, bool Finished)> DeadCodeAsync(
        LoadedWorkspace workspace,
        Project[] finished,
        DiagnosticScope scope,
        bool includeDeadCode,
        Bound bound)
    {
        if (!includeDeadCode)
            return ([], true);

        try
        {
            return (await DeadCodeService.FindAsync(workspace, finished, scope, bound.Deadline).ConfigureAwait(false), true);
        }
        catch (OperationCanceledException) when (bound.Expired)
        {
            return ([], false);
        }
    }

    private static UnfinishedScope Unfinished(LoadedWorkspace workspace, Project[] targets, DocumentId[] documents, HashSet<ProjectId> unfinished)
    {
        if (unfinished.Count is 0)
            return new UnfinishedScope([], [], Widened: false);

        var projects = Array.FindAll(targets, project => unfinished.Contains(project.Id));
        var narrow = Array.FindAll(documents, id => unfinished.Contains(id.ProjectId));
        var widened = narrow.Length is 0 or > MaxNextScope;
        var scope = widened
            ? Array.ConvertAll(projects, project => Path.GetDirectoryName(project.FilePath))
            : Array.ConvertAll(narrow, id => workspace.Solution.GetDocument(id)?.FilePath);

        return new UnfinishedScope(Array.ConvertAll(projects, project => project.Name), Relatives(workspace.Root, scope), widened && documents.Length > 0);
    }

    private static string[] Relatives(string root, string?[] paths) =>
        [.. paths.OfType<string>().Select(path => PositionFormat.Relative(root, path)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static string PartialTrailer(Partial partial)
    {
        if (!partial.IsPartial)
            return string.Empty;

        var seconds = partial.Timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        var deadCode = partial.DeadCodeUnfinished
            ? string.Create(CultureInfo.InvariantCulture, $"\nPARTIAL the dead-code scan did not finish by the {seconds} s deadline, so no TERSE001 finding is reported - raise timeoutSeconds, or pass includeDeadCode=false")
            : string.Empty;

        return ProjectTrailer(partial, seconds) + deadCode;
    }

    private static string ProjectTrailer(Partial partial, string seconds)
    {
        var unfinished = partial.Unfinished;

        if (unfinished.Projects.Length is 0)
            return string.Empty;

        var widened = unfinished.Widened ? " - next: names their project directories, wider than this call's scope" : string.Empty;
        var next = unfinished.Scope.Length > 0 ? "\n" + ResponseBuilder.Next("analyze", "paths", unfinished.Scope) : string.Empty;

        return string.Create(CultureInfo.InvariantCulture, $"\nPARTIAL {partial.Targets - unfinished.Projects.Length} of {partial.Targets} project(s) analyzed by the {seconds} s deadline - not analyzed, so absent from this answer: {string.Join(", ", unfinished.Projects)}{widened}{next}");
    }

    private static string SinceLastNote(DiagnosticDelta delta, int occurrences, bool partial) => (partial, delta.Baseline) switch
    {
        (true, _) => string.Create(CultureInfo.InvariantCulture, $"no delta: this pass is PARTIAL, so all {occurrences} occurrence(s) are listed and none was compared with the previous analyze"),
        (false, true) => string.Create(CultureInfo.InvariantCulture, $"no previous analyze of this scope: this run is the baseline, all {occurrences} occurrence(s) are listed"),
        _ => string.Create(CultureInfo.InvariantCulture, $"since the previous analyze of this scope: appeared={delta.Appeared.Count} fixed={delta.Fixed.Count} unchanged={delta.Unchanged} total={occurrences} occurrence(s)"),
    };

    private static string ExcludedNote(string? path, bool changed, string? exclude) => path is null && !changed && exclude is { Length: > 0 }
        ? "\nNOTE exclude= scopes this pass to the documents it keeps, as path= does, so a diagnostic with no file (e.g. CS0006) or in a generated file is not reported - drop exclude= to see those"
        : string.Empty;

    private static DiagnosticDelta Recorded(string scope, IReadOnlyList<string> occurrences, Partial partial) =>
        partial.IsPartial ? new DiagnosticDelta(occurrences, [], 0, Baseline: true) : DiagnosticHistory.Record(scope, occurrences);
}

public static class DiagnosticFormat
{
    public static string Head(Diagnostic diagnostic) => string.Create(
        CultureInfo.InvariantCulture,
        $"{diagnostic.Id} {Severity(diagnostic)} {Category(diagnostic)}");

    private static string Severity(Diagnostic diagnostic) => diagnostic.Severity.ToString().ToLowerInvariant();

    private static string Category(Diagnostic diagnostic) =>
        string.IsNullOrEmpty(diagnostic.Descriptor.Category) ? "-" : diagnostic.Descriptor.Category;
}
