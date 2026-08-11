namespace TerseSharp.E2ETests;

internal sealed record ToolProbe(string Tool, Dictionary<string, object?> Arguments);

internal sealed record ToolExemption(string Tool, string Reason);

internal sealed record ToolVerdict(string Tool, string Prefix, Dictionary<string, object?> Arguments, string Reason);

internal sealed record ToolBudget(string Tool, int Tokens, string Reason);

internal static class ToolCensus
{
    public const int MaxHappyPathExemptions = 4;

    public const int MaxRobustnessExclusions = 7;

    public const int MaxVerdictPrefixes = 2;

    public const int MaxBudgetOverrides = 2;

    public const int GlobalTokenCap = 800;

    private const string Card = "src/Fixture.Blazor/Components/Card.razor";

    private const string TestProject = "tests/Fixture.Trading.Tests/Fixture.Trading.Tests.csproj";

    public static ToolExemption[] HappyPathExempt =>
    [
        new(
            "unload_workspace",
            "unloading the shared fixture server's only workspace would break every later test in the collection; RemainingToolsE2ETests owns its response"),
        new(
            "undo_last_change",
            "needs a prior mutation, and the happy-path sweep asserts the fixture is byte-for-byte unchanged; RefactorToolsE2ETests owns the applied-then-undone sequence"),
        new(
            "package_add",
            "fixtures/FixtureSolution resolves its Directory.Packages.props above its own root, so every call is refused by design; ProjectToolsE2ETests asserts that refusal"),
        new(
            "package_remove",
            "the fixture declares no removable PackageReference, so no success path exists there; ProjectToolsE2ETests asserts the refusal"),
    ];

    public static ToolExemption[] RobustnessExcluded =>
    [
        new(
            "build",
            "each sweep calls every tool three times; a dotnet child process per call would add minutes for an answer DotnetRunnerTests already asserts at the render level"),
        new(
            "run_tests",
            "same child-process cost as build, and a garbage filter still builds the fixture before it can be refused"),
        new(
            "rerun_failed",
            "same child-process cost, and it re-runs whatever the last run left failing rather than anything the sweep controls"),
        new(
            "list_tests",
            "same child-process cost; the render path is covered by DotnetRunnerTests and by the happy-path probe"),
        new(
            "load_workspace",
            "the empty-argument sweep resolves an empty path against the server's working directory, so it can reload the shared fixture workspace mid-collection"),
        new(
            "unload_workspace",
            "the empty-argument sweep can unload the shared fixture server's only workspace and every later test in the collection then fails to resolve one"),
        new(
            "clean",
            "deletes bin and obj; a garbage argument that resolved would remove the fixture output the E2E child server runs from"),
    ];

    public static ToolProbe[] ProcessProbes =>
    [
        new("build", []),
        new("list_tests", new() { ["project"] = TestProject }),
        new("run_tests", new() { ["project"] = TestProject }),
        new("rerun_failed", []),
    ];

    public static ToolProbe[] RazorProbes =>
    [
        new("razor_outline", new() { ["path"] = Card }),
        new("razor_component", new() { ["name"] = "Badge" }),
        new("razor_find", new() { ["query"] = "Card", ["kind"] = "component" }),
        new("razor_bindings", new() { ["path"] = Card }),
        new("razor_codebehind", new() { ["path"] = Card }),
        new("razor_validate", new() { ["scope"] = "solution" }),
        new("razor_set_attribute", new()
        {
            ["path"] = Card,
            ["target"] = "div/Badge",
            ["attribute"] = "Count",
            ["value"] = "0",
            ["dryRun"] = true,
        }),
        new("razor_add_element", new()
        {
            ["path"] = Card,
            ["parent"] = "div",
            ["markup"] = "<Badge Kind=\"info\" />",
            ["dryRun"] = true,
        }),
        new("razor_remove_element", new()
        {
            ["path"] = Card,
            ["target"] = "div/button",
            ["dryRun"] = true,
            ["allowErrors"] = true,
        }),
        new("razor_set_directive", new()
        {
            ["path"] = Card,
            ["directive"] = "using",
            ["value"] = "System.Text",
            ["dryRun"] = true,
        }),
    ];

    public static ToolVerdict[] VerdictPrefixed =>
    [
        new(
            "build",
            "build ok  ",
            [],
            "the quiet success line is a verdict, not a request echo; BuildWarningsE2ETests asserts that exact prefix and the one-line shape it guarantees"),
        new(
            "run_tests",
            "run_tests PASSED  ",
            new() { ["project"] = TestProject, ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.Passes" },
            "the quiet green line is a verdict, not a request echo; it only appears on a green run, so the probe selects the one fixture test that passes"),
    ];

    public static ToolBudget[] BudgetOverrides =>
    [
        new(
            "search_text",
            1000,
            "returns a full default page of 100 matches, each a path:line and the matched source line; the response is bounded by maxResults=, not by the read-tool cap"),
        new(
            "search_regex",
            2300,
            "same full page as search_text, on a pattern that matches every public declaration in the fixture; bounded by maxResults="),
    ];

    public static int Budget(string tool) =>
        Array.Find(BudgetOverrides, budget => string.Equals(budget.Tool, tool, StringComparison.Ordinal))?.Tokens
        ?? GlobalTokenCap;

    public static bool OpensWithItsOwnName(string tool, string text)
    {
        var first = FirstLine(text);
        var verdict = Array.Find(VerdictPrefixed, entry => string.Equals(entry.Tool, tool, StringComparison.Ordinal));

        if (verdict is not null && first.StartsWith(verdict.Prefix, StringComparison.Ordinal))
            return false;

        return first.StartsWith(tool, StringComparison.Ordinal) && first.Length > tool.Length && first[tool.Length] is ' ';
    }

    public static string FirstLine(string text) =>
        text.IndexOf('\n', StringComparison.Ordinal) is var end and >= 0 ? text[..end] : text;

    public static int Tokens(string text) => (text.Length + 3) / 4;

    public const int MinShellReplacements = 10;
    public static readonly string[] ReadOnlyTools =
    [
        "changed_files", "diff_symbols", "diff_text",
    "explore_symbol", "find_files", "find_implementations", "find_registrations", "find_usages",
    "get_diagnostics", "get_file_outline", "get_symbol", "get_symbol_source", "get_type_outline",
    "impact_of", "list_endpoints", "list_projects", "list_workspaces",
    "package_list", "project_properties", "solution_projects",
    "razor_bindings", "razor_codebehind", "razor_component", "razor_find", "razor_outline", "razor_validate",
    "read_text", "resx_files", "resx_find", "resx_get", "resx_usages", "resx_validate",
    "search_regex", "search_symbols", "search_text", "workspace_status",
    "xaml_bindings", "xaml_codebehind", "xaml_find", "xaml_localization", "xaml_names",
    "xaml_outline", "xaml_resolve", "xaml_resources", "xaml_styles", "xaml_validate",
];
    public static readonly string[] DestructiveTools =
    [
        "clean", "delete_symbol", "package_remove", "project_remove_reference", "razor_remove_element",
    "resx_remove", "solution_remove_project", "unload_workspace", "xaml_remove_element",
];
    public static readonly string[] MutatingTools =
    [
        "add_member", "analyze", "build", "change_signature", "cleanup", "edit_text", "extract_interface",
    "format", "gate", "list_tests", "load_workspace", "move_type_to_file", "move_type_to_namespace",
    "package_add", "project_add_reference", "project_create", "project_set_property",
    "razor_add_element", "razor_set_attribute", "razor_set_directive", "rename_symbol",
    "replace_symbol", "replace_symbol_body", "rerun_failed", "resx_rename", "resx_set", "run_tests",
    "solution_add_project", "undo_last_change", "write_text", "xaml_add_element", "xaml_set_property",
];
}
