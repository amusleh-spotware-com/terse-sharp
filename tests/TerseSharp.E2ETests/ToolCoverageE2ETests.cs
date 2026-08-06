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
        "build", "clean", "run_tests", "rerun_failed", "list_tests",
        "analyze", "format", "cleanup",
        "extract_interface", "move_type_to_file", "move_type_to_namespace", "change_signature", "undo_last_change",
        "solution_projects", "solution_add_project", "solution_remove_project",
        "project_create", "project_properties", "project_set_property",
        "project_add_reference", "project_remove_reference",
        "package_list", "package_add", "package_remove",
        "xaml_outline", "xaml_names", "xaml_resources", "xaml_bindings", "xaml_validate", "xaml_find",
        "xaml_resolve", "xaml_codebehind", "xaml_set_property",
        "explore_symbol", "impact_of", "find_registrations", "list_endpoints",
        "xaml_add_element", "xaml_remove_element", "xaml_styles", "xaml_localization",
        "resx_files", "resx_get", "resx_find", "resx_usages",
        "resx_set", "resx_remove", "resx_rename", "resx_validate",
        "razor_outline", "razor_component", "razor_find", "razor_bindings", "razor_codebehind",
        "razor_validate", "razor_set_attribute", "razor_add_element", "razor_remove_element", "razor_set_directive",
        "changed_files", "diff_symbols", "diff_text",
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

    internal static int ExercisedCount => Exercised.Count;
}
