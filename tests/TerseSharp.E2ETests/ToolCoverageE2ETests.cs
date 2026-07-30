namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ToolCoverageE2ETests(TerseServerFixture server)
{
    private static readonly HashSet<string> Exercised = new(StringComparer.Ordinal)
    {
        "load_workspace", "workspace_status", "list_workspaces", "unload_workspace", "list_projects",
        "search_symbols", "get_symbol", "get_file_outline", "get_type_outline", "get_symbol_source",
        "find_usages", "find_implementations", "get_diagnostics",
        "replace_symbol_body", "replace_symbol", "add_member", "delete_symbol", "rename_symbol",
        "read_text", "write_text", "edit_text", "find_files", "search_text", "search_regex",
        "build", "run_tests",
    };

    [Fact]
    public async Task EveryAdvertisedTool_HasAnE2ETest()
    {
        var advertised = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name)
            .ToArray();

        Assert.DoesNotContain(advertised, name => !Exercised.Contains(name));
    }

    [Fact]
    public async Task TheExercisedList_NamesNoToolThatNoLongerExists()
    {
        var advertised = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Exercised, name => !advertised.Contains(name));
    }
}
