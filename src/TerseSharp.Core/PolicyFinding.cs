
namespace TerseSharp.Core;

public sealed record PolicyFinding(
    PolicyRule Rule,
    PolicyAction Action,
    string Path,
    int Line,
    int Column,
    string Declaration,
    string Measured,
    string Allowed)
{
    public string Key => string.Create(
            CultureInfo.InvariantCulture,
            $"{PolicyRules.Of(Rule).Id}|{Path}|{Declaration}");

    public string Render() => string.Create(
        CultureInfo.InvariantCulture,
        $"{PolicyRules.Of(Rule).Id}  {Path}:{Line}  {Declaration}  {Measured} exceeds {Allowed}");

    public string Diagnostic() => string.Create(
        CultureInfo.InvariantCulture,
        $"{PolicyRules.Of(Rule).Id} {Severity()} Policy {Path}:{Line}:{Column}: {Declaration} - {Measured} exceeds {Allowed}");

    public string Explain() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Render()}\n  fix: {PolicyRules.Of(Rule).Remedy}");

    private string Severity() => Action is PolicyAction.Reject ? "warning" : "info";
}
