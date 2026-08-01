namespace TerseSharp.Core;

public readonly record struct WorkspaceGenerations(int Code, int Project, int Xaml, int Resx)
{
    public int Of(ChangeKind kind) => kind switch
    {
        ChangeKind.Code => Code,
        ChangeKind.Project => Project,
        ChangeKind.Xaml => Xaml,
        ChangeKind.Resx => Resx,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public WorkspaceGenerations Bump(ChangeKind kind) => kind switch
    {
        ChangeKind.Code => this with { Code = Code + 1 },
        ChangeKind.Project => this with { Project = Project + 1 },
        ChangeKind.Xaml => this with { Xaml = Xaml + 1 },
        ChangeKind.Resx => this with { Resx = Resx + 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public WorkspaceGenerations Reloaded() => this with { Code = Code + 1, Project = Project + 1 };

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"c{Code}/p{Project}/x{Xaml}/r{Resx}");
}
