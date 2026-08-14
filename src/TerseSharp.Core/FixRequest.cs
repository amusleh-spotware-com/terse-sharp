using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public enum FixMode
{
    None,
    Usings,
    Style,
    Analyzers,
    All,
}

public readonly record struct FixScope(string? Path, bool ChangedOnly);

public sealed record FixRequest(FixMode Mode, IReadOnlyList<string> Ids, DiagnosticSeverity Severity, bool Verify)
{
    public bool AppliesCodeFixes => Mode is FixMode.Style or FixMode.Analyzers or FixMode.All;

    public bool CleansUsings => Mode is FixMode.Usings or FixMode.All;

    public bool Wants(Diagnostic diagnostic) =>
        diagnostic.Severity >= Severity
        && !diagnostic.IsSuppressed
        && (Ids.Count is 0 || Ids.Contains(diagnostic.Id, StringComparer.OrdinalIgnoreCase))
        && InMode(diagnostic.Id);

    private bool InMode(string id) => Mode switch
    {
        FixMode.Style => IsStyle(id),
        FixMode.Analyzers => !IsStyle(id),
        _ => true,
    };

    private static bool IsStyle(string id) => id.StartsWith("IDE", StringComparison.Ordinal);

    public bool Reformats => Mode is not (FixMode.Style or FixMode.Analyzers);
}
