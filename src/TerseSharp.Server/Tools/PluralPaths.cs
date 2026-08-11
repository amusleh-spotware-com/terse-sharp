using System.Collections.Immutable;

namespace TerseSharp.Server.Tools;

internal static class PluralPaths
{
    public const int MaxPaths = 10;

    public static Result<ImmutableArray<string>> Combine(string? path, string?[]? paths, string plural)
    {
        var combined = ImmutableArray.CreateBuilder<string>();

        if (path is { Length: > 0 })
            combined.Add(path);

        foreach (var entry in paths ?? [])
        {
            if (entry is not { Length: > 0 })
                return Blank(plural);

            combined.Add(entry);
        }

        return Verified(combined.DrainToImmutable(), plural);
    }

    private static Result<ImmutableArray<string>> Blank(string plural) =>
        Result.Fail<ImmutableArray<string>>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"'{plural}' carries a blank entry"),
            "drop it, or pass the path you meant"));

    private static Result<ImmutableArray<string>> Verified(ImmutableArray<string> combined, string plural) => combined switch
    {
        [] => Result.Fail<ImmutableArray<string>>(Errors.Blank("path", plural)),
        { Length: > MaxPaths } => Result.Fail<ImmutableArray<string>>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"{combined.Length} paths were requested - path plus {plural} - more than the {MaxPaths} one call answers"),
            string.Create(CultureInfo.InvariantCulture, $"send at most {MaxPaths} per call"))),
        _ => Result.Ok(combined),
    };
}
