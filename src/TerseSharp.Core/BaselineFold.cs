using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public readonly record struct BaselineSplit(Diagnostic[] Kept, int PreExisting, int UnchangedErrors, int Fileless, string FilelessIds);

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
            fileless.Sum(entry => entry.Value),
            Ids(fileless));
    }

    public static string? FilelessNote(BaselineSplit split, string baseRef) => split.Fileless is 0
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"{split.Fileless} diagnostic(s) with no source position ({split.FilelessIds}) were not reported - no line of any file carries them, so baseRef={baseRef} cannot attribute them to this change; drop baseRef= to list them");

    public static string? UnchangedErrorNote(BaselineSplit split) => split.UnchangedErrors is 0
        ? null
        : string.Create(CultureInfo.InvariantCulture, $"{split.UnchangedErrors} error(s) on lines the working tree did not change are listed anyway - an error there is usually a consumer this change broke, and folding it would answer a tree that does not compile as clean");

    private static Placement Place(Diagnostic diagnostic, TouchedLines touched) => diagnostic switch
    {
        { Location.SourceTree: null } => Placement.Fileless,
        _ when touched.Covers(diagnostic) => Placement.Changed,
        { Severity: DiagnosticSeverity.Error } => Placement.UnchangedError,
        _ => Placement.PreExisting,
    };

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
