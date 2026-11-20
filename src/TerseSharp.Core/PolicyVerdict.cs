using System.Collections.Immutable;

namespace TerseSharp.Core;

public sealed record PolicyVerdict(
    ImmutableArray<PolicyFinding> Rejected,
    ImmutableArray<PolicyFinding> Warned,
    bool AllowOverride,
    bool Overridden,
    string? Notice = null)
{
    public static PolicyVerdict Clean { get; } = new([], [], true, false);

    public bool Refused => !Rejected.IsEmpty && Overridden && !AllowOverride;

    public bool Blocks => !Rejected.IsEmpty && (!Overridden || !AllowOverride);

    public bool Bypassed => !Rejected.IsEmpty && Overridden && AllowOverride;

    public PolicyVerdict Joined(PolicyVerdict other) =>
        new([.. Rejected, .. other.Rejected], [.. Warned, .. other.Warned], AllowOverride && other.AllowOverride, Overridden, Notice ?? other.Notice);

    public IEnumerable<string> Lines(bool dryRun)
    {
        if (Notice is { } notice)
            yield return "WARNING " + notice;

        foreach (var finding in Warned)
            yield return string.Create(CultureInfo.InvariantCulture, $"WARNING policy  {finding.Render()}");

        foreach (var finding in Bypassed ? Rejected : [])
            yield return string.Create(CultureInfo.InvariantCulture, $"WARNING policy overridden  {finding.Render()}");

        foreach (var finding in dryRun && Blocks ? Rejected : [])
            yield return string.Create(CultureInfo.InvariantCulture, $"WARNING would be rolled back by policy  {finding.Explain()}");
    }

    public string Prefixed(bool dryRun, string response) =>
        Notice is null && Warned.IsEmpty && Rejected.IsEmpty
            ? response
            : string.Join('\n', [.. Lines(dryRun), response]);
}
