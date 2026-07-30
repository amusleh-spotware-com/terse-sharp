namespace TerseSharp.Core;

public enum Confidence
{
    Exact,
    Heuristic,
}

public static class ConfidenceTag
{
    public static string Of(Confidence confidence) => confidence switch
    {
        Confidence.Exact => "EXACT",
        Confidence.Heuristic => "HEURISTIC",
        _ => throw new ArgumentOutOfRangeException(nameof(confidence)),
    };
}
