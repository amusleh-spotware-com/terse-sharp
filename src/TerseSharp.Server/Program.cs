using System.CommandLine;
using TerseSharp.Server;

var workspaceOption = new Option<string?>("--workspace") { Description = "Solution or project to load at startup." };
var readOnlyOption = new Option<bool>("--read-only") { Description = "Expose read tools only." };
var noWatchOption = new Option<bool>("--no-watch") { Description = "Do not watch the workspace for external file changes. TERSE_WATCH=0 does the same." };
var clientOption = new Option<string?>("--client") { Description = "claude-code, cursor, vscode or windsurf. Omit to detect." };

var skillOption = new Option<bool>("--skill") { Description = "Also install the agent skill that teaches which TerseSharp tool replaces which built-in." };

var guardOption = new Option<bool>("--guard") { Description = "Also install a Claude Code PreToolUse hook that blocks Read/Grep/Edit on C#/.NET and XAML files." };

var maxWorkspacesOption = new Option<int?>("--max-workspaces") { Description = "How many solutions stay loaded at once; the least recently used is unloaded beyond that. Default 4. TERSE_MAX_WORKSPACES does the same. A large solution costs gigabytes, so set 1 when you only ever work in one." };

var idleMinutesOption = new Option<int?>("--idle-minutes") { Description = "Drop a workspace's Roslyn compilations after it has been idle this long, and return the memory. Default 15; 0 keeps them for the life of the process. TERSE_IDLE_MINUTES does the same. The next semantic call re-realizes what it needs." };

var toolsOption = new Option<string?>("--tools") { Description = "Which tools to advertise: core (default), a ~20-tool subset, or all. Every other tool still answers when called by name; only the advertised list shrinks, which is the measured lever on tool-selection accuracy. Pass all to advertise the whole surface. TERSE_TOOLS does the same, and an unrecognised value falls back to core." };

var serve = new Command("serve", "Run the MCP server over stdio.") { workspaceOption, readOnlyOption, noWatchOption, maxWorkspacesOption, idleMinutesOption, toolsOption };
var guard = new Command("guard", "Hook entry point: reads a Claude Code PreToolUse payload on stdin and denies built-in tools on C#/.NET source.");
var install = new Command("install", "Register TerseSharp with your MCP clients.") { clientOption, workspaceOption, skillOption, guardOption };
var uninstall = new Command("uninstall", "Remove TerseSharp from your MCP clients.") { clientOption };
var doctor = new Command("doctor", "Verify SDK, MSBuild, client registration and workspace load.") { workspaceOption };
var toolArgument = new Argument<string>("tool") { Description = "MCP tool name, e.g. get_file_outline." };
var jsonOption = new Option<string?>("--json") { Description = "Tool arguments as a JSON object, e.g. '{\"path\": \"src/App.cs\"}'. Omit for none." };
var call = new Command("call", "Call one MCP tool of this binary from the shell and print its response, so a claim about a freshly built terse can be tested without hand-writing JSON-RPC.") { toolArgument, workspaceOption, jsonOption };

serve.SetAction((result, cancellationToken) =>
    McpHost.RunAsync(
        result.GetValue(workspaceOption),
        result.GetValue(readOnlyOption),
        Watching(result.GetValue(noWatchOption)),
        WorkspaceLimit.Resolve(result.GetValue(maxWorkspacesOption)),
        IdleLimit.Resolve(result.GetValue(idleMinutesOption)),
        result.GetValue(toolsOption),
        cancellationToken));

static bool Watching(bool disabled) =>
    !disabled && !string.Equals(Environment.GetEnvironmentVariable("TERSE_WATCH"), "0", StringComparison.Ordinal);

install.SetAction(async result =>
{
    Console.WriteLine(await ClientRegistrar.Register(result.GetValue(clientOption), result.GetValue(workspaceOption)).ConfigureAwait(false));

    if (result.GetValue(skillOption))
        Console.WriteLine(await ClientRegistrar.InstallSkill().ConfigureAwait(false));

    if (result.GetValue(guardOption))
        Console.WriteLine(await ClientRegistrar.InstallGuard().ConfigureAwait(false));
});

guard.SetAction((_, cancellationToken) =>
    ToolGuard.RunAsync(Console.In, Console.Out, cancellationToken));

uninstall.SetAction(async result =>
    Console.WriteLine(await ClientRegistrar.Unregister(result.GetValue(clientOption)).ConfigureAwait(false)));

call.SetAction((result, cancellationToken) =>
    ToolCall.RunAsync(
        result.GetValue(toolArgument)!,
        result.GetValue(workspaceOption),
        result.GetValue(jsonOption),
        Console.Out,
        cancellationToken));

doctor.SetAction(async (result, cancellationToken) =>
    Console.WriteLine(await Doctor.RunAsync(result.GetValue(workspaceOption), cancellationToken).ConfigureAwait(false)));

var root = new RootCommand("TerseSharp - token-efficient Roslyn MCP server for C# and .NET.")
{
    serve,
    guard,
    install,
    uninstall,
    doctor,
    call,
};

string[] rootOptions = ["--version", "--help", "-h", "-?", "/?"];

var invocation = args is [] || (args[0].StartsWith('-') && !rootOptions.Contains(args[0], StringComparer.Ordinal))
    ? ["serve", .. args]
    : args;

return await root.Parse(invocation).InvokeAsync().ConfigureAwait(false);
