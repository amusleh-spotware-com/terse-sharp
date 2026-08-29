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
        new("run_tests", new() { ["project"] = TestProject, ["force"] = true }),
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
            new() { ["project"] = TestProject, ["test"] = "Fixture.Trading.Tests.DeliberateOutcomesTests.Passes", ["force"] = true },
            "the quiet green line is a verdict, not a request echo; it only appears on a green run, so the probe selects the one fixture test that passes and forces it past the unchanged-run memo"),
    ];

    public static ToolBudget[] BudgetOverrides =>
    [
        new(
            "search_text",
            1050,
            "returns a full default page of 100 matches, each a path:line and the matched source line; the response is bounded by maxResults=, not by the read-tool cap, and a .cs-scoped page carries the one-line containers=true steer on top of it"),
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
        "changed_files", "diff_symbols", "diff_text", "history",
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

    public static string WithoutSteer(string text) => string.Join(
            '\n',
            text.Split('\n').Where(line => !line.Contains(" calls in a row", StringComparison.Ordinal)));

    public const double RedundancyThreshold = 0.45;
    public const int MaxSimilarByDesignPairs = 7;
    private static readonly string[] Stopwords =
    [
        "that", "this", "with", "from", "into", "when", "which", "they", "them", "than", "then",
        "instead", "every", "each", "call", "calls", "tool", "tools", "pass", "passed", "returns",
        "return", "response", "answer", "answers", "path", "paths", "file", "files", "workspace",
        "default", "false", "true", "only", "also", "same", "onto", "over", "under", "does", "one",
        "replaces", "bash", "name", "names", "line", "lines", "what", "have", "been", "because",
    ];

    public static ToolPair[] SimilarByDesignPairs =>
        [
            new(
                "search_text",
                "search_regex",
                "literal versus pattern is the whole difference and both are named in every routing table; the descriptions are symmetric because the parameters are, and the pair carried 615 and 191 calls in the measured corpus, so neither is the unused half of a redundant pair"),
            new(
                "xaml_set_property",
                "xaml_remove_element",
                "opposite verbs on the same addressing scheme; the descriptions overlap on how an element is addressed, which is the part that must read identically"),
            new(
                "razor_add_element",
                "razor_remove_element",
                "opposite verbs on the same addressing scheme, as above"),
            new(
                "project_add_reference",
                "project_remove_reference",
                "opposite verbs on the same target; add versus remove is unambiguous from the verb, which is not the selection hazard ToolScope measured"),
            new(
                "project_remove_reference",
                "package_remove",
                "project reference versus PackageReference; both remove a reference from a project file, and the shared wording is the containment and central-package-management rule they must state the same way"),
            new(
                "solution_add_project",
                "solution_remove_project",
                "opposite verbs on the same solution file, as above"),
            new(
                "xaml_names",
                "xaml_resources",
                "x:Name declarations versus x:Key declarations, from the same index; the descriptions share the index and dialect wording every xaml_* tool must repeat"),
        ];

    public static bool SimilarByDesign(string first, string second) => Array.Exists(
        SimilarByDesignPairs,
        pair => (string.Equals(pair.First, first, StringComparison.Ordinal) && string.Equals(pair.Second, second, StringComparison.Ordinal))
            || (string.Equals(pair.First, second, StringComparison.Ordinal) && string.Equals(pair.Second, first, StringComparison.Ordinal)));

    public static HashSet<string> Words(string description)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var current = new System.Text.StringBuilder(32);

        foreach (var character in description)
        {
            if (char.IsAsciiLetter(character))
            {
                current.Append(char.ToLowerInvariant(character));
                continue;
            }

            Keep(words, current);
        }

        Keep(words, current);

        return words;
    }

    private static void Keep(HashSet<string> words, System.Text.StringBuilder current)
    {
        if (current.Length >= 4)
        {
            var word = current.ToString();

            if (!Array.Exists(Stopwords, stopword => string.Equals(stopword, word, StringComparison.Ordinal)))
                words.Add(word);
        }

        current.Clear();
    }

    public static double Overlap(HashSet<string> first, HashSet<string> second)
    {
        if (first.Count is 0 || second.Count is 0)
            return 0;

        var shared = first.Count(word => second.Contains(word));

        return (double)shared / (first.Count + second.Count - shared);
    }

    public const string BudgetProbe =
        "measure the next iteration in ~3 s instead of a ~90 s build+test cycle: "
        + "dotnet src/TerseSharp.Server/bin/<Configuration>/net10.0/terse.dll call <tool> "
        + "--workspace <solution> --json '{...}' answers from the freshly built binary. "
        + "The advertised tools/list cost is a session number: workspace_status prints it as "
        + "advertised=<n> tools <t> tokens - split by verbose=true into toolDescriptions, "
        + "parameterDescriptions, schemaFrame and names - inside an MCP session, not from that one-shot probe";
    public const int MaxPolicyExemptions = 23;

    public static ToolExemption[] PolicyExempt =>
    [
        new("format", "the whitespace formatter is one of the policy's own fixers and constructs its EditOptions with AllowPolicy: true, so it can never be blocked by it"),
        new("cleanup", "the code-fix pass is one of the policy's own fixers and constructs its EditOptions with AllowPolicy: true"),
        new("gate", "the analyze-format-cleanup composite runs those same exempt fixers and authors no declaration of its own"),
        new("clean", "deletes bin and obj; it never edits a document, so no EditGate call and no policy evaluation exists to bypass"),
        new("edit_text", "writes the file directly and never reaches EditGate.ApplyAsync, so the policy does not evaluate it; write_text force=true is the gated path"),
        new("xaml_set_property", "XamlEditService writes markup through its own path, not EditGate.ApplyAsync"),
        new("xaml_add_element", "XamlEditService writes markup through its own path, not EditGate.ApplyAsync"),
        new("xaml_remove_element", "XamlEditService writes markup through its own path, not EditGate.ApplyAsync"),
        new("razor_set_attribute", "RazorEditGate is a separate gate over .razor markup and evaluates no C# code policy"),
        new("razor_add_element", "RazorEditGate is a separate gate over .razor markup and evaluates no C# code policy"),
        new("razor_remove_element", "RazorEditGate is a separate gate over .razor markup and evaluates no C# code policy"),
        new("razor_set_directive", "RazorEditGate is a separate gate over .razor markup and evaluates no C# code policy"),
        new("resx_set", "ResxEditService writes XML resources, which carry no C# declaration for the policy to judge"),
        new("resx_remove", "ResxEditService writes XML resources, which carry no C# declaration for the policy to judge"),
        new("resx_rename", "ResxEditService writes XML resources, which carry no C# declaration for the policy to judge"),
        new("project_create", "writes a .csproj and a solution entry, not a C# declaration the policy judges"),
        new("project_add_reference", "edits MSBuild XML, which the policy does not evaluate"),
        new("project_remove_reference", "edits MSBuild XML, which the policy does not evaluate"),
        new("project_set_property", "edits MSBuild XML, which the policy does not evaluate"),
        new("package_add", "edits MSBuild XML and Directory.Packages.props, which the policy does not evaluate"),
        new("package_remove", "edits MSBuild XML and Directory.Packages.props, which the policy does not evaluate"),
        new("solution_add_project", "edits the .slnx solution file, which the policy does not evaluate"),
        new("solution_remove_project", "edits the .slnx solution file, which the policy does not evaluate"),
    ];

    public const int MaxReadOnlyExclusions = 7;
    public static readonly ToolExemption[] RunsUnderReadOnly =
    [
        new("analyze", "reads diagnostics; the only state it mutates is this server's own diagnostic history, never the tree"),
    new("build", "shells out to dotnet build, which writes to obj/ and bin/ and never to source"),
    new("list_tests", "builds to discover test names and writes no source"),
    new("load_workspace", "mutates only this server's in-memory workspace registry"),
    new("rerun_failed", "replays the previous run's failures and writes no source"),
    new("run_tests", "builds and runs tests, writing only to obj/, bin/ and TestResults"),
    new("unload_workspace", "releases this server's in-memory registry and the MSBuild file locks it holds"),
];

    public static bool IsFraming(string line) =>
        line.Length is 0
        || line.StartsWith("repeat #", StringComparison.Ordinal)
        || line.Contains("calls in a row", StringComparison.Ordinal);
}

internal sealed record ToolPair(string First, string Second, string Reason);
