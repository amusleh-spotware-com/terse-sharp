using System.Buffers;

namespace TerseSharp.Server.Tools;

internal static class Aliases
{
    private static readonly SearchValues<char> GlobMarkers = SearchValues.Create("*?{[");

    public static Result<string?> Chosen(Func<ParameterSpelling, ParameterSpelling, string> remedy, params ReadOnlySpan<ParameterSpelling> spellings)
    {
        ParameterSpelling? chosen = null;

        foreach (var spelling in spellings)
        {
            if (spelling.Value is not { Length: > 0 })
                continue;

            if (chosen is { } first && !string.Equals(first.Value, spelling.Value, StringComparison.Ordinal))
                return Result.Fail<string?>(Conflict(first, spelling, remedy));

            chosen ??= spelling;
        }

        return Result.Ok(chosen?.Value);
    }

    public static string ScopeRemedy(ParameterSpelling first, ParameterSpelling second) =>
        Composed(first.Value!, second.Value!) is { } glob
            ? string.Create(CultureInfo.InvariantCulture, $"pass one of them - glob=\"{glob}\" searches the pattern inside that directory; several scopes OR-ed together go in paths= (globs= on find_files)")
            : "pass one of them - several scopes OR-ed together go in paths= (globs= on find_files)";

    public static string TextRemedy(ParameterSpelling first, ParameterSpelling second) =>
        string.Create(CultureInfo.InvariantCulture, $"pass one of them, or queries=[\"{first.Value}\", \"{second.Value}\"] to search for both in one pass");

    private static TerseError Conflict(ParameterSpelling first, ParameterSpelling second, Func<ParameterSpelling, ParameterSpelling, string> remedy) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{first.Name}' and '{second.Name}' name the same parameter and were passed different values, \"{first.Value}\" and \"{second.Value}\" - one of them would be silently dropped"),
        remedy(first, second));

    private static string? Composed(string first, string second) => (Literal(first), Literal(second)) switch
    {
        (true, false) => Under(first, second),
        (false, true) => Under(second, first),
        _ => null,
    };

    private static bool Literal(string scope) => !scope.AsSpan().ContainsAny(GlobMarkers);

    private static string? Under(string directory, string pattern) => pattern switch
    {
        _ when pattern.StartsWith("**/", StringComparison.Ordinal) => string.Concat(Trimmed(directory), "/", pattern),
        _ when FileGlob.IsPathPattern(pattern) => null,
        _ => string.Concat(Trimmed(directory), "/**/", pattern),
    };

    private static ReadOnlySpan<char> Trimmed(string directory) => directory.AsSpan().TrimEnd("/\\");
}
