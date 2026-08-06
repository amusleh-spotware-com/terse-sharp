namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ProjectToolsE2ETests(TerseServerFixture server)
{
    private const string Project = "src/Fixture.Trading/Fixture.Trading.csproj";

    [Fact]
    public async Task SolutionProjects_ReadsTheSlnxItself()
    {
        var text = await server.CallAsync("solution_projects", []);

        Assert.Contains("Fixture.Trading.csproj", text, StringComparison.Ordinal);
        Assert.Contains("1 projects", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolutionAddProject_WithDryRun_ShowsTheAddedEntry()
    {
        var text = await server.CallAsync("solution_add_project", new()
        {
            ["project"] = "src/Fixture.Extra/Fixture.Extra.csproj",
            ["dryRun"] = true,
        });

        Assert.Contains("Fixture.Extra", text, StringComparison.Ordinal);
        Assert.Contains("dryRun", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolutionAddProject_ForAProjectAlreadyPresent_IsRefused()
    {
        var text = await server.CallAsync("solution_add_project", new() { ["project"] = Project, ["dryRun"] = true });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("already in the solution", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolutionRemoveProject_WithDryRun_ShowsTheRemoval()
    {
        var text = await server.CallAsync("solution_remove_project", new() { ["project"] = Project, ["dryRun"] = true });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Contains("-", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectCreate_WithDryRun_ProducesAnSdkStyleProject()
    {
        var text = await server.CallAsync("project_create", new()
        {
            ["project"] = "src/Fixture.New/Fixture.New.csproj",
            ["kind"] = "console",
            ["dryRun"] = true,
        });

        Assert.Contains("Microsoft.NET.Sdk", text, StringComparison.Ordinal);
        Assert.Contains("OutputType", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectCreate_ForAnExistingProject_IsRefused()
    {
        var text = await server.CallAsync("project_create", new() { ["project"] = Project, ["dryRun"] = true });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("already exists", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectProperties_ReadsTheDeclaredProperties()
    {
        var text = await server.CallAsync("project_properties", new() { ["project"] = Project });

        Assert.Contains(" properties", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectSetProperty_WithDryRun_ShowsTheNewProperty()
    {
        var text = await server.CallAsync("project_set_property", new()
        {
            ["project"] = Project,
            ["name"] = "LangVersion",
            ["value"] = "preview",
            ["dryRun"] = true,
        });

        Assert.Contains("LangVersion", text, StringComparison.Ordinal);
        Assert.Contains("preview", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageList_ReportsReferences()
    {
        var text = await server.CallAsync("package_list", new() { ["project"] = Project });

        Assert.Contains("references", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageAdd_WhenCentralPackageManagementSitsAboveTheWorkspace_IsRefused()
    {
        var text = await server.CallAsync("package_add", new()
        {
            ["project"] = Project,
            ["package"] = "Serilog",
            ["version"] = "4.0.0",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("above the workspace root", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageRemove_ForAPackageThatIsNotThere_IsRefused()
    {
        var text = await server.CallAsync("package_remove", new()
        {
            ["project"] = Project,
            ["package"] = "NotReferenced",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectAddReference_WithDryRun_AddsAProjectReference()
    {
        var text = await server.CallAsync("project_add_reference", new()
        {
            ["project"] = Project,
            ["target"] = "src/Fixture.Other/Fixture.Other.csproj",
            ["dryRun"] = true,
        });

        Assert.Contains("Fixture.Other", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectRemoveReference_ForAMissingReference_IsRefused()
    {
        var text = await server.CallAsync("project_remove_reference", new()
        {
            ["project"] = Project,
            ["target"] = "src/Fixture.Absent/Fixture.Absent.csproj",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
    }
}
