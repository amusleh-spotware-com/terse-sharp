namespace TerseSharp.Core;

internal static class HealthDecline
{
    public static bool IsUnscored(PolicyFinding finding) => finding.Health is null;

    public static IEnumerable<PolicyFinding> Charged(IReadOnlyList<PolicyFinding> before, IReadOnlyList<PolicyFinding> after)
    {
        var previous = Lowest(before).ToDictionary(finding => finding.Key, finding => finding.Health ?? CodeHealth.Perfect, StringComparer.Ordinal);

        return Lowest(after).Where(finding => finding.Health is { } health && (!previous.TryGetValue(finding.Key, out var prior) || health < prior));
    }

    private static IEnumerable<PolicyFinding> Lowest(IReadOnlyList<PolicyFinding> findings) => findings
        .Where(finding => finding.Health is not null)
        .GroupBy(finding => finding.Key, StringComparer.Ordinal)
        .Select(group => group.OrderBy(finding => finding.Health).First());
}
