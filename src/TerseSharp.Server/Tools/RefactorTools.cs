using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class RefactorTools(ToolContext context)
{
    [McpServerTool(Name = "extract_interface")]
    [Description("Create an interface beside a type containing its public instance methods and properties. The new file is added to the same project.")]
    public Task<string> ExtractInterface(
        [Description("Symbol id of the type, e.g. T:Trading.OrderService.")] string typeSymbolId,
        [Description("Name of the interface to create, e.g. IOrderService.")] string interfaceName,
        [Description("Return the diff without writing. Default false.")] bool dryRun = false,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, typeSymbolId, (loaded, symbol) => RefactorService.ExtractInterfaceAsync(
            loaded, symbol, interfaceName, Options("extract_interface", dryRun), cancellationToken), cancellationToken);

    [McpServerTool(Name = "move_type_to_file")]
    [Description("Move a type out of a shared file into its own file named after it, keeping the usings and namespace.")]
    public Task<string> MoveTypeToFile(
        [Description("Symbol id of the type to move.")] string typeSymbolId,
        [Description("Return the diff without writing. Default false.")] bool dryRun = false,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, typeSymbolId, (loaded, symbol) => RefactorService.MoveTypeToFileAsync(
            loaded, symbol, Options("move_type_to_file", dryRun), cancellationToken), cancellationToken);

    [McpServerTool(Name = "move_type_to_namespace")]
    [Description("Change the namespace declared in the file containing a type.")]
    public Task<string> MoveTypeToNamespace(
        [Description("Symbol id of the type to move.")] string typeSymbolId,
        [Description("Target namespace, e.g. Trading.Orders.")] string targetNamespace,
        [Description("Return the diff without writing. Default false.")] bool dryRun = false,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, typeSymbolId, (loaded, symbol) => RefactorService.MoveTypeToNamespaceAsync(
            loaded, symbol, targetNamespace, Options("move_type_to_namespace", dryRun), cancellationToken), cancellationToken);

    [McpServerTool(Name = "change_signature")]
    [Description("Replace a method's parameter list. The compile gate reports every call site the change breaks, so run it with dryRun first.")]
    public Task<string> ChangeSignature(
        [Description("Symbol id of the method.")] string symbolId,
        [Description("New parameter list without the parentheses, e.g. 'int count, string name'.")] string parameters,
        [Description("Return the diff without writing. Default false.")] bool dryRun = false,
        [Description("Apply even when call sites stop compiling. Default false.")] bool allowErrors = false,
        [Description("Optional workspace path or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, symbolId, (loaded, symbol) => RefactorService.ChangeSignatureAsync(
            loaded, symbol, parameters, new EditOptions("change_signature", dryRun, allowErrors), cancellationToken), cancellationToken);

    [McpServerTool(Name = "undo_last_change")]
    [Description("Revert the most recent mutation applied by this server. Keeps the last 10 snapshots.")]
    public string UndoLastChange([Description("Optional workspace path or worktree name.")] string? workspace = null)
    {
        var rejection = context.RejectWrite();

        return rejection ?? context.WithWorkspace(workspace, null, loaded => loaded.Undo());
    }

    private static EditOptions Options(string tool, bool dryRun) => new(tool, dryRun, AllowErrors: false);

    private Task<string> Guarded(
        string? workspace,
        string symbolId,
        Func<LoadedWorkspace, ISymbol, Task<Result<string>>> action,
        CancellationToken cancellationToken)
    {
        var rejection = context.RejectWrite();

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithSymbolAsync(workspace, symbolId, async (loaded, symbol) =>
                NavigationTools.Unwrap(await action(loaded, symbol).ConfigureAwait(false)), cancellationToken);
    }
}
