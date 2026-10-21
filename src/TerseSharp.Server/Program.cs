using System.CommandLine;
using TerseSharp.Server;

return GuardEntry.Route(args) is { } hook
    ? await GuardEntry.RunAsync(hook, Console.In, Console.Out, Console.Error, CancellationToken.None).ConfigureAwait(false)
    : await CommandLineAsync(args).ConfigureAwait(false);

static async Task<int> CommandLineAsync(string[] arguments)
{
    var workspaceOption = new Option<string?>("--workspace") { Description = "Solution or project to load at startup." };
    var readOnlyOption = new Option<bool>("--read-only") { Description = "Expose read tools only." };
    var noWatchOption = new Option<bool>("--no-watch") { Description = "Do not watch the workspace for external file changes. TERSE_WATCH=0 does the same." };
    var clientOption = new Option<string?>("--client") { Description = "claude-code, cursor, vscode or windsurf. Omit to detect." };

    var skillOption = new Option<bool>("--skill") { Description = "Also install the agent skill that teaches which TerseSharp tool replaces which built-in." };

    var guardOption = new Option<bool>("--guard") { Description = "Also install a Claude Code PreToolUse hook that blocks Read/Grep/Edit on C#/.NET and XAML files, a PostToolBatch hook that asks for independent calls in one response after a lone read-only terse call, and PreToolUse, PostToolUse and SessionStart hooks that hand a terse call the nested CLAUDE.md files above the paths it touched." };

    var maxWorkspacesOption = new Option<int?>("--max-workspaces") { Description = "How many solutions stay loaded at once; the least recently used is unloaded beyond that. Default 4. TERSE_MAX_WORKSPACES does the same. A large solution costs gigabytes, so set 1 when you only ever work in one." };

    var idleMinutesOption = new Option<int?>("--idle-minutes") { Description = "Drop a workspace's Roslyn compilations after it has been idle this long, and return the memory. Default 60; 0 keeps them for the life of the process. TERSE_IDLE_MINUTES does the same, and .terse.json workspace.idleMinutes sets it below both. The next semantic call re-realizes what it needs." };

    var toolsOption = new Option<string?>("--tools") { Description = TerseSharp.Server.ToolProfile.ToolsOptionDescription };

    var serve = new Command("serve", "Run the MCP server over stdio.") { workspaceOption, readOnlyOption, noWatchOption, maxWorkspacesOption, idleMinutesOption, toolsOption };
    var postBatchOption = new Option<bool>("--post-batch") { Description = "Read a Claude Code PostToolBatch payload instead, and nudge the next response to batch its independent calls when the batch was one read-only terse call; TERSE_NUDGE_LOG=1 also appends its tool_use id to ~/.terse/batch-nudge.log." };

    var folderRulesOption = new Option<bool>("--folder-rules") { Description = "Read a Claude Code PreToolUse, PostToolUse or SessionStart payload for a terse call instead, and inject the nested CLAUDE.md files above the paths it touched once per session - Claude Code loads them only for its own Read, Edit and Write. The first terse write that names a path below rules the session has not seen is denied with them; a compaction forgets what was shown." };

    var guard = new Command("guard", "Hook entry point: reads a Claude Code PreToolUse payload on stdin and denies built-in tools on C#/.NET source; --post-batch and --folder-rules read the payloads of the other hooks install --guard registers.") { postBatchOption, folderRulesOption };
    var install = new Command("install", "Register TerseSharp with your MCP clients.") { clientOption, workspaceOption, skillOption, guardOption };
    var uninstall = new Command("uninstall", "Remove TerseSharp from your MCP clients.") { clientOption };
    var doctor = new Command("doctor", "Verify SDK, MSBuild, client registration and workspace load.") { workspaceOption };
    var toolArgument = new Argument<string>("tool") { Description = "MCP tool name, e.g. get_file_outline." };
    var jsonOption = new Option<string?>("--json") { Description = "Tool arguments as a JSON object, e.g. '{\"path\": \"src/App.cs\"}'. Omit for none." };
    var call = new Command("call", "Call one MCP tool of this binary from the shell and print its response, so a claim about a freshly built terse can be tested without hand-writing JSON-RPC.") { toolArgument, workspaceOption, jsonOption };

    serve.SetAction(async (result, cancellationToken) =>
        await McpHost.RunAsync(
            result.GetValue(workspaceOption),
            result.GetValue(readOnlyOption),
            Watching(result.GetValue(noWatchOption)),
            WorkspaceLimit.Resolve(result.GetValue(maxWorkspacesOption)),
            await IdleLimit.ResolveAsync(result.GetValue(idleMinutesOption), Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false),
            result.GetValue(toolsOption),
            cancellationToken).ConfigureAwait(false));

    static bool Watching(bool disabled) =>
        !disabled && !string.Equals(Environment.GetEnvironmentVariable("TERSE_WATCH"), "0", StringComparison.Ordinal);

    install.SetAction(async result =>
    {
        Console.WriteLine(await ClientRegistrar.Register(result.GetValue(clientOption), result.GetValue(workspaceOption)).ConfigureAwait(false));
        Console.WriteLine(await ClientRegistrar.InstallConfig().ConfigureAwait(false));

        if (result.GetValue(skillOption))
            Console.WriteLine(await ClientRegistrar.InstallSkill().ConfigureAwait(false));

        if (result.GetValue(guardOption))
            Console.WriteLine(await ClientRegistrar.InstallGuard().ConfigureAwait(false));
    });

    guard.SetAction((result, cancellationToken) =>
        GuardEntry.DispatchAsync(GuardEntry.Hook(result.GetValue(postBatchOption), result.GetValue(folderRulesOption)), Console.In, Console.Out, cancellationToken));

    uninstall.SetAction(async result =>
        Console.WriteLine(await ClientRegistrar.Unregister(result.GetValue(clientOption)).ConfigureAwait(false)));

    call.SetAction((result, cancellationToken) =>
        ToolCall.RunAsync(
            result.GetValue(toolArgument)!,
            result.GetValue(workspaceOption),
            result.GetValue(jsonOption),
            Console.Out,
            Console.Error,
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

    var invocation = arguments is [] || (arguments[0].StartsWith('-') && !rootOptions.Contains(arguments[0], StringComparer.Ordinal))
        ? ["serve", .. arguments]
        : arguments;

    return await root.Parse(invocation).InvokeAsync().ConfigureAwait(false);
}
