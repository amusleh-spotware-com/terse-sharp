using System.Collections.Immutable;

namespace TerseSharp.Server.Tools;

internal static class PluralPaths
{
    public const int MaxPaths = 10;

    public static Result<ImmutableArray<string>> Combine(string? path, string?[]? paths, string plural)
    {
        var gathered = Gathered(path, paths, plural);

        return gathered.IsOk ? Verified(gathered.Value, plural) : gathered;
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

    public static Result<Spilled> Spill(string? path, string?[]? paths, string plural)
    {
        var gathered = Gathered(path, paths, plural);

        return gathered switch
        {
            { IsOk: false } => Result.Fail<Spilled>(gathered.Error!),
            { Value: [] } => Result.Fail<Spilled>(Errors.Blank("path", plural)),
            _ => Result.Ok(Spilled.From(gathered.Value)),
        };
    }

    private static Result<ImmutableArray<string>> Gathered(string? path, string?[]? paths, string plural)
    {
        var combined = ImmutableArray.CreateBuilder<string>((paths?.Length ?? 0) + 1);

        if (path is { Length: > 0 })
            combined.Add(path);

        foreach (var entry in paths ?? [])
        {
            if (entry is not { Length: > 0 })
                return Blank(plural);

            combined.Add(entry);
        }

        return Result.Ok(combined.ToImmutable());
    }

    public static string Trailer(string tool, string parameter, int answered, IReadOnlyList<string> overflow, string extra = "") => overflow.Count is 0
        ? string.Empty
        : string.Create(CultureInfo.InvariantCulture, $"\nPARTIAL {answered} of {answered + overflow.Count} {parameter} answered, {answered} per call - the rest, with this call's other arguments:\n{ResponseBuilder.Next(tool, parameter, overflow)}{extra}");

    public static string Argument(string name, string? value) => value is { Length: > 0 } ? " " + name + "=" + ResponseBuilder.Quoted(value) : string.Empty;

    public static Task<string> TrailedAsync(Task<string> answer, string trailer) => trailer.Length is 0 ? answer : AppendedAsync(answer, trailer);

    private static async Task<string> AppendedAsync(Task<string> answer, string trailer) => await answer.ConfigureAwait(false) + trailer;

    internal readonly record struct Spilled(ImmutableArray<string> Taken, ImmutableArray<string> Overflow)
    {
        public static Spilled From(ImmutableArray<string> all) => all.Length <= MaxPaths
            ? new(all, [])
            : new(ImmutableArray.Create(all, 0, MaxPaths), ImmutableArray.Create(all, MaxPaths, all.Length - MaxPaths));

        public string Trailer(string tool, string parameter, string extra) => PluralPaths.Trailer(tool, parameter, Taken.Length, Overflow, extra);
    }
}
