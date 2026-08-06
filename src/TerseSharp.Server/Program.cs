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

var serve = new Command("serve", "Run the MCP server over stdio.") { workspaceOption, readOnlyOption, noWatchOption, maxWorkspacesOption, idleMinutesOption };
var guard = new Command("guard", "Hook entry point: reads a Claude Code PreToolUse payload on stdin and denies built-in tools on C#/.NET source.");
var install = new Command("install", "Register TerseSharp with your MCP clients.") { clientOption, workspaceOption, skillOption, guardOption };
var uninstall = new Command("uninstall", "Remove TerseSharp from your MCP clients.") { clientOption };
var doctor = new Command("doctor", "Verify SDK, MSBuild, client registration and workspace load.") { workspaceOption };

serve.SetAction((result, cancellationToken) =>
    McpHost.RunAsync(
        result.GetValue(workspaceOption),
        result.GetValue(readOnlyOption),
        Watching(result.GetValue(noWatchOption)),
        WorkspaceLimit.Resolve(result.GetValue(maxWorkspacesOption)),
        IdleLimit.Resolve(result.GetValue(idleMinutesOption)),
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

doctor.SetAction(async (result, cancellationToken) =>
    Console.WriteLine(await Doctor.RunAsync(result.GetValue(workspaceOption), cancellationToken).ConfigureAwait(false)));

var root = new RootCommand("TerseSharp - token-efficient Roslyn MCP server for C# and .NET.")
{
    serve,
    guard,
    install,
    uninstall,
    doctor,
};

string[] rootOptions = ["--version", "--help", "-h", "-?", "/?"];

var invocation = args is [] || (args[0].StartsWith('-') && !rootOptions.Contains(args[0], StringComparer.Ordinal))
    ? ["serve", .. args]
    : args;

return await root.Parse(invocation).InvokeAsync().ConfigureAwait(false);
