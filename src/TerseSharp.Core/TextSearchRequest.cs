using System.Collections.Immutable;

namespace TerseSharp.Core;

public readonly record struct TextSearchRequest(
    ImmutableArray<string> Patterns,
    string Glob,
    bool Regex,
    int MaxResults,
    int Context = 0,
    bool Unique = false,
    string? Root = null,
    string? Exclude = null,
    bool MatchesOnly = false,
    bool CountOnly = false,
    bool Containers = false,
    bool Word = false,
    bool Chosen = false)
{
    public const int MaxContext = 5;

    public const int MaxPatterns = 10;

    public string Pattern => Patterns[0];

    public bool SeveralPatterns => Patterns.Length > 1;

    public string Tool => Regex ? "search_regex" : "search_text";

    public int Around => Math.Clamp(Context, 0, MaxContext);
}
