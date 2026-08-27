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

    public bool Quiet => Rejected.IsEmpty && Warned.IsEmpty && Notice is null;
}
