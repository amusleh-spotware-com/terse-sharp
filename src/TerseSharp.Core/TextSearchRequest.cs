namespace TerseSharp.Core;

public readonly record struct TextSearchRequest(
    string Pattern,
    string Glob,
    bool Regex,
    int MaxResults,
    int Context = 0,
    bool Unique = false,
    string? Root = null)
{
    public const int MaxContext = 5;

    public string Tool => Regex ? "search_regex" : "search_text";

    public int Around => Math.Clamp(Context, 0, MaxContext);
}
