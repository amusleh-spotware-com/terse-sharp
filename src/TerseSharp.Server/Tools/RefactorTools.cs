using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class RefactorTools(ToolContext context)
{
    [McpServerTool(Name = "extract_interface")]
    [Description("Create an interface beside a type containing its public instance methods and properties. The new file is added to the same project. A successful refactor answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> ExtractInterface(
        [Description("Type id, e.g. T:Trading.OrderService.")] string? typeSymbolId = null,
        [Description("Name of the interface to create, e.g. IOrderService.")] string interfaceName = "",
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for typeSymbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, typeSymbolId ?? symbol, (loaded, resolved) => RefactorService.ExtractInterfaceAsync(
            loaded, resolved, interfaceName, Options("extract_interface", dryRun, verbose), cancellationToken), cancellationToken, typesOnly: true);

    [McpServerTool(Name = "move_type_to_file")]
    [Description("Move a type out of a shared file into its own file named after it, keeping the usings and namespace. A successful refactor answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> MoveTypeToFile(
        [Description("Symbol id of the type to move.")] string? typeSymbolId = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for typeSymbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, typeSymbolId ?? symbol, (loaded, resolved) => RefactorService.MoveTypeToFileAsync(
            loaded, resolved, Options("move_type_to_file", dryRun, verbose), cancellationToken), cancellationToken, typesOnly: true);

    [McpServerTool(Name = "move_type_to_namespace")]
    [Description("Change the namespace declared in the file containing a type. A successful refactor answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> MoveTypeToNamespace(
        [Description("Symbol id of the type to move.")] string? typeSymbolId = null,
        [Description("Target namespace, e.g. Trading.Orders.")] string targetNamespace = "",
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for typeSymbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, typeSymbolId ?? symbol, (loaded, resolved) => RefactorService.MoveTypeToNamespaceAsync(
            loaded, resolved, targetNamespace, Options("move_type_to_namespace", dryRun, verbose), cancellationToken), cancellationToken, typesOnly: true);

    [McpServerTool(Name = "change_signature")]
    [Description("Replace a method's parameter list. The compile gate reports every call site the change breaks, so run it with dryRun first. A successful change answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> ChangeSignature(
        [Description("Symbol id of the method.")] string? symbolId = null,
        [Description("New parameter list without the parentheses, e.g. 'int count, string name'.")] string parameters = "",
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Apply even when call sites stop compiling. Default false.")] bool allowErrors = false,
        [Description("Return the full diff instead of the one-line summary. Default false.")] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, symbolId ?? symbol, (loaded, resolved) => RefactorService.ChangeSignatureAsync(
            loaded, resolved, parameters, new EditOptions("change_signature", dryRun, allowErrors, verbose), cancellationToken), cancellationToken);

    [McpServerTool(Name = "undo_last_change")]
    [Description("Revert the most recent mutation applied by this server. Keeps the last 10 snapshots.")]
    public Task<string> UndoLastChange(
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        context.RejectWrite() is { } rejection
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(
                workspace,
                null,
                loaded => UndoneAsync(loaded, cancellationToken),
                cancellationToken: cancellationToken);

    private static async Task<string> UndoneAsync(LoadedWorkspace workspace, CancellationToken cancellationToken)
    {
        var response = new ResponseBuilder("undo_last_change", workspace.Git.WorktreeName);

        response.Note(await workspace.UndoAsync(cancellationToken).ConfigureAwait(false));

        return response.ToString();
    }

    private static EditOptions Options(string tool, bool dryRun, bool verbose) =>
        new(tool, dryRun, AllowErrors: false, verbose);

    private Task<string> Guarded(
    string? workspace,
    string? symbolId,
    Func<LoadedWorkspace, ISymbol, Task<Result<string>>> action,
    CancellationToken cancellationToken,
    bool typesOnly = false)
    {
        var rejection = context.RejectWrite();

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithSymbolAsync(workspace, symbolId, async (loaded, resolved) =>
                NavigationTools.Unwrap(await action(loaded, resolved).ConfigureAwait(false)), cancellationToken, typesOnly: typesOnly);
    }
}
