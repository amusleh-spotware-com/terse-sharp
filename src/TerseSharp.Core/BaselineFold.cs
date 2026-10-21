using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct FilelessFold(int Count, string Ids, Diagnostic[] Errors);

public readonly record struct BaselineSplit(Diagnostic[] Kept, int PreExisting, int UnchangedErrors, FilelessFold Fileless);

public static class BaselineFold
{
    private const int MaxFilelessIds = 3;

    public static BaselineSplit Split(IReadOnlyCollection<Diagnostic> found, TouchedLines touched)
    {
        var placed = found.Select(diagnostic => (Diagnostic: diagnostic, Placement: Place(diagnostic, touched))).ToArray();
        var fileless = placed.Where(entry => entry.Placement is Placement.Fileless).CountBy(entry => entry.Diagnostic.Id, StringComparer.Ordinal).ToArray();

        return new BaselineSplit(
            [.. placed.Where(entry => entry.Placement is Placement.Changed or Placement.UnchangedError).Select(entry => entry.Diagnostic)],
            placed.Count(entry => entry.Placement is Placement.PreExisting),
            placed.Count(entry => entry.Placement is Placement.UnchangedError),
            new FilelessFold(
                fileless.Sum(entry => entry.Value),
                Ids(fileless),
                [.. placed.Where(entry => entry.Placement is Placement.Fileless && entry.Diagnostic.Severity is DiagnosticSeverity.Error).Select(entry => entry.Diagnostic)]));
    }

    public static string? FilelessNote(BaselineSplit split, string baseRef) => split.Fileless.Count is 0
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"{split.Fileless.Count} diagnostic(s) with no source position ({split.Fileless.Ids}){Errors(split.Fileless)} were not reported - no line of any file carries them, so baseRef={baseRef} cannot attribute them to this change; drop baseRef= to list them");

    public static string? UnchangedErrorNote(BaselineSplit split) => split.UnchangedErrors is 0
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"{split.UnchangedErrors} error(s) on lines the working tree did not change are listed anyway - an error there is usually a consumer this change broke, and folding it would answer a tree that does not compile as clean");

    private static string Errors(FilelessFold fileless) => fileless.Errors is { Length: > 0 } errors
        ? string.Create(CultureInfo.InvariantCulture, $", {errors.Length} of them error(s) that can mean the tree does not compile,")
        : string.Empty;

    private static Placement Place(Diagnostic diagnostic, TouchedLines touched) => diagnostic switch
    {
        { Location.Kind: LocationKind.ExternalFile } => Placed(diagnostic, ExternalCovered(diagnostic, touched)),
        { Location.SourceTree: null } => Placement.Fileless,
        _ => Placed(diagnostic, touched.Covers(diagnostic)),
    };

    private static Placement Placed(Diagnostic diagnostic, bool covered) => (covered, diagnostic.Severity) switch
    {
        (true, _) => Placement.Changed,
        (false, DiagnosticSeverity.Error) => Placement.UnchangedError,
        _ => Placement.PreExisting,
    };

    private static bool ExternalCovered(Diagnostic diagnostic, TouchedLines touched)
    {
        var span = diagnostic.Location.GetLineSpan();

        return span.Path is { Length: > 0 } path && touched.Covers(Path.GetFullPath(path), span.StartLinePosition.Line + 1);
    }

    private static string Ids(KeyValuePair<string, int>[] fileless) => string.Join(
        ", ",
        fileless
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(MaxFilelessIds)
            .Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Key} x{entry.Value}")));

    private enum Placement
    {
        Changed,
        UnchangedError,
        PreExisting,
        Fileless,
    }
}
