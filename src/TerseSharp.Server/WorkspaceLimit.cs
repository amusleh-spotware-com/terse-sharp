namespace TerseSharp.Server;

public static class WorkspaceLimit
{
    public const int Default = 4;

    public const string Variable = "TERSE_MAX_WORKSPACES";

    public static int Resolve(int? option) => Resolve(option, Environment.GetEnvironmentVariable(Variable));

    public static int Resolve(int? option, string? environment) =>
        option is { } chosen ? Valid(chosen) ?? Default : Valid(Parse(environment)) ?? Default;

    private static int? Parse(string? environment) =>
        int.TryParse(environment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static int? Valid(int? candidate) => candidate is > 0 ? candidate : null;
}
