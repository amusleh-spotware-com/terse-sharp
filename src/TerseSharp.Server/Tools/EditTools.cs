using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class EditTools(ToolContext context)
{
    [McpServerTool(Name = "replace_symbol_body")]
    [Description("Replace a method, constructor or accessor body, addressed by symbol id. No line numbers and no surrounding context needed. Rolled back if it introduces a compile error.")]
    public Task<string> ReplaceSymbolBody(
        [Description("Symbol id of the member.")] string symbolId,
        [Description("New body, with or without the surrounding braces.")] string body,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, symbolId, (loaded, symbol) => SymbolEditService.ReplaceBodyAsync(
            loaded, symbol, body, Options("replace_symbol_body", dryRun, allowErrors), cancellationToken), cancellationToken);

    [McpServerTool(Name = "replace_symbol")]
    [Description("Replace a whole member declaration including its signature, attributes and doc comment, addressed by symbol id.")]
    public Task<string> ReplaceSymbol(
        [Description("Symbol id of the member.")] string symbolId,
        [Description("Complete new member declaration.")] string declaration,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, symbolId, (loaded, symbol) => SymbolEditService.ReplaceDeclarationAsync(
            loaded, symbol, declaration, Options("replace_symbol", dryRun, allowErrors), cancellationToken), cancellationToken);

    [McpServerTool(Name = "add_member")]
    [Description("Add a member to a type, addressed by the type's symbol id.")]
    public Task<string> AddMember(
        [Description("Symbol id of the containing type.")] string typeSymbolId,
        [Description("Complete member declaration to add.")] string declaration,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, typeSymbolId, (loaded, symbol) => SymbolEditService.AddMemberAsync(
            loaded, symbol, declaration, Options("add_member", dryRun, allowErrors), cancellationToken), cancellationToken);

    [McpServerTool(Name = "delete_symbol")]
    [Description("Safe-delete a member or type. Refuses while references exist unless force is set, and lists them.")]
    public Task<string> DeleteSymbol(
        [Description("Symbol id to delete.")] string symbolId,
        [Description("Delete even when references exist. Default false.")] bool force = false,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, symbolId, (loaded, symbol) => SymbolEditService.DeleteAsync(
            loaded, symbol, force, Options("delete_symbol", dryRun, allowErrors: false), cancellationToken), cancellationToken);

    [McpServerTool(Name = "rename_symbol")]
    [Description("Rename a symbol across the whole solution, including interface implementations, overrides and XML doc crefs. Use instead of a find-and-replace sweep.")]
    public Task<string> RenameSymbol(
        [Description("Symbol id to rename.")] string symbolId,
        [Description("New identifier.")] string newName,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, symbolId, (loaded, symbol) => RenameService.RenameAsync(
            loaded, symbol, newName, Options("rename_symbol", dryRun, allowErrors: false), cancellationToken), cancellationToken);

    private static EditOptions Options(string tool, bool dryRun, bool allowErrors) => new(tool, dryRun, allowErrors);

    private Task<string> Guarded(
        string? workspace,
        string symbolId,
        Func<LoadedWorkspace, Microsoft.CodeAnalysis.ISymbol, Task<Result<string>>> action,
        CancellationToken cancellationToken)
    {
        var rejection = context.RejectWrite();

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithSymbolAsync(workspace, symbolId, async (loaded, symbol) =>
                NavigationTools.Unwrap(await action(loaded, symbol).ConfigureAwait(false)), cancellationToken);
    }
}
