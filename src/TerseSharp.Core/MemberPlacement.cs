namespace TerseSharp.Core;

public enum MemberPosition
{
    Last,
    First,
    AfterFields,
}

public readonly record struct MemberPlacement(string? Before, string? After, MemberPosition Position);
