using System.CommandLine;
using TerseSharp.Server;

var workspaceOption = new Option<string?>("--workspace") { Description = "Solution or project to load at startup." };
var readOnlyOption = new Option<bool>("--read-only") { Description = "Expose read tools only." };
var clientOption = new Option<string?>("--client") { Description = "claude-code, cursor, vscode or windsurf. Omit to detect." };

var serve = new Command("serve", "Run the MCP server over stdio.") { workspaceOption, readOnlyOption };
var install = new Command("install", "Register TerseSharp with your MCP clients.") { clientOption, workspaceOption };
var uninstall = new Command("uninstall", "Remove TerseSharp from your MCP clients.") { clientOption };
var doctor = new Command("doctor", "Verify SDK, MSBuild, client registration and workspace load.") { workspaceOption };

serve.SetAction((result, cancellationToken) =>
    McpHost.RunAsync(result.GetValue(workspaceOption), result.GetValue(readOnlyOption), cancellationToken));

install.SetAction(result =>
    Console.WriteLine(ClientRegistrar.Register(result.GetValue(clientOption), result.GetValue(workspaceOption))));

uninstall.SetAction(result => Console.WriteLine(ClientRegistrar.Unregister(result.GetValue(clientOption))));

doctor.SetAction(async (result, cancellationToken) =>
    Console.WriteLine(await Doctor.RunAsync(result.GetValue(workspaceOption), cancellationToken).ConfigureAwait(false)));

var root = new RootCommand("TerseSharp - token-efficient Roslyn MCP server for C# and .NET.")
{
    serve,
    install,
    uninstall,
    doctor,
};

return await root.Parse(args.Length is 0 ? ["serve"] : args).InvokeAsync().ConfigureAwait(false);
