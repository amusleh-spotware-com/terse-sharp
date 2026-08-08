using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class EditTools(ToolContext context)
{
    private const string VerboseHelp = "Return the full diff instead of the one-line summary. Default false.";

    [McpServerTool(Name = "replace_symbol_body")]
    [Description("Replace a method, constructor or accessor body, addressed by symbol id. No line numbers and no surrounding context needed. Rolled back if it introduces a compile error. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> ReplaceSymbolBody(
        [Description("Symbol id of the member.")] string? symbolId = null,
        [Description("New body: statements with or without the surrounding braces, or an expression body as '=> expr'. On a member that is already expression-bodied, a bare expression is accepted and stays expression-bodied.")] string body = "",
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
        [Description(VerboseHelp)] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        Supplied(workspace, symbolId ?? symbol, body, "body", (loaded, resolved) => SymbolEditService.ReplaceBodyAsync(
            loaded, resolved, body, Options("replace_symbol_body", dryRun, allowErrors, verbose), cancellationToken), cancellationToken);

    [McpServerTool(Name = "replace_symbol")]
    [Description("Replace a whole member declaration including its signature, attributes and doc comment, addressed by symbol id. An enum member id takes enum member declarations. Several declarations in one call replace the target with all of them - the way to split a member into overloads in one compile-gated edit. Pass symbolIds and declarations instead to replace members in several files as ONE compile-gated edit, which is how a signature change lands together with the callers it breaks. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> ReplaceSymbol(
    [Description("Symbol id of the member.")] string? symbolId = null,
    [Description("One complete member declaration, or several in sequence to replace the target with all of them.")] string declaration = "",
    [Description("Diff only, write nothing.")] bool dryRun = false,
    [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
    [Description(VerboseHelp)] bool verbose = false,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Alias for symbolId.")] string? symbol = null,
    [Description("Symbol ids of the members to replace together, paired positionally with declarations. Several entries per file are allowed; two entries where one declaration contains the other are refused.")] string[]? symbolIds = null,
    [Description("One complete declaration per entry of symbolIds, in the same order, applied as a single compile-gated edit across every file they live in.")] string[]? declarations = null,
    CancellationToken cancellationToken = default) =>
    (symbolIds, declarations) is (null, null)
        ? Supplied(workspace, symbolId ?? symbol, declaration, "declaration", (loaded, resolved) => SymbolEditService.ReplaceDeclarationAsync(
            loaded, resolved, declaration, Options("replace_symbol", dryRun, allowErrors, verbose), cancellationToken), cancellationToken)
        : Batched(workspace, symbolIds ?? [], declarations ?? [], Options("replace_symbol", dryRun, allowErrors, verbose), cancellationToken);
    [McpServerTool(Name = "add_member")]
    [Description("Add one or more members to a type, addressed by the type's symbol id - or, with path=, add namespace-level types to an existing .cs file. An enum symbol id takes enum members. Several declarations in one call land as a single compile-gated edit, so a set of members that reference each other needs no dependency ordering. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> AddMember(
        [Description("Symbol id of the containing type, or of an enum when adding enum members. Cannot be combined with path.")] string? typeSymbolId = null,
        [Description("One complete member declaration, or several in sequence; they are added together as one edit. With an enum container, one or more enum member names.")] string declaration = "",
        [Description("Path of a .cs file to append namespace-level type declarations to, instead of a type symbol id.")] string? path = null,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
        [Description(VerboseHelp)] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for typeSymbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        Added(workspace, typeSymbolId ?? symbol, path, declaration, Options("add_member", dryRun, allowErrors, verbose), cancellationToken);

    private Task<string> Added(
        string? workspace,
        string? typeSymbolId,
        string? path,
        string declaration,
        EditOptions options,
        CancellationToken cancellationToken) => (typeSymbolId, path) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => Task.FromResult(Errors.Invalid(
                "both a type symbol id and a path were passed, and they name different containers",
                "pass typeSymbolId to add members to a type, or path to add namespace-level types to a file - not both").Render()),
            (_, { Length: > 0 } file) when declaration is { Length: > 0 } => AddToFile(workspace, file, declaration, options, cancellationToken),
            (_, { Length: > 0 }) => Task.FromResult(Errors.Blank("declaration").Render()),
            _ => Supplied(workspace, typeSymbolId, declaration, "declaration", (loaded, resolved) => SymbolEditService.AddMemberAsync(
                loaded, resolved, declaration, options, cancellationToken), cancellationToken),
        };

    private Task<string> AddToFile(string? workspace, string path, string declaration, EditOptions options, CancellationToken cancellationToken)
    {
        var rejection = context.RejectWrite();

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(
                workspace,
                path,
                async loaded => NavigationTools.Unwrap(
                    await SymbolEditService.AddToFileAsync(loaded, path, declaration, options, cancellationToken).ConfigureAwait(false)),
                cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "delete_symbol")]
    [Description("Safe-delete a member, an enum member or a type. Refuses while references exist unless force is set, and lists them. A successful delete answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> DeleteSymbol(
        [Description("Symbol id to delete.")] string? symbolId = null,
        [Description("Delete even when references exist. Default false.")] bool force = false,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description(VerboseHelp)] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        Guarded(workspace, symbolId ?? symbol, (loaded, resolved) => SymbolEditService.DeleteAsync(
            loaded, resolved, force, Options("delete_symbol", dryRun, allowErrors: false, verbose), cancellationToken), cancellationToken);

    [McpServerTool(Name = "rename_symbol")]
    [Description("Rename a symbol across the whole solution, including interface implementations, overrides and XML doc crefs. Use instead of a find-and-replace sweep. A successful rename answers in one line per changed file - plus every XAML or Razor site it could NOT rewrite; pass verbose=true for the diff.")]
    public Task<string> RenameSymbol(
        [Description("Symbol id to rename.")] string? symbolId = null,
        [Description("New identifier.")] string newName = "",
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description(VerboseHelp)] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for symbolId.")] string? symbol = null,
        CancellationToken cancellationToken = default) =>
        Supplied(workspace, symbolId ?? symbol, newName, "newName", (loaded, resolved) => RenameService.RenameAsync(
            loaded, resolved, newName, Options("rename_symbol", dryRun, allowErrors: false, verbose), cancellationToken), cancellationToken);

    private static EditOptions Options(string tool, bool dryRun, bool allowErrors, bool verbose) =>
        new(tool, dryRun, allowErrors, verbose);

    private Task<string> Guarded(
        string? workspace,
        string? symbolId,
        Func<LoadedWorkspace, Microsoft.CodeAnalysis.ISymbol, Task<Result<string>>> action,
        CancellationToken cancellationToken)
    {
        var rejection = context.RejectWrite();

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithSymbolAsync(workspace, symbolId, async (loaded, resolved) =>
                NavigationTools.Unwrap(await action(loaded, resolved).ConfigureAwait(false)), cancellationToken);
    }

    private Task<string> Supplied(
        string? workspace,
        string? symbolId,
        string text,
        string name,
        Func<LoadedWorkspace, Microsoft.CodeAnalysis.ISymbol, Task<Result<string>>> action,
        CancellationToken cancellationToken) => text is { Length: > 0 }
        ? Guarded(workspace, symbolId, action, cancellationToken)
        : Task.FromResult(Errors.Blank(name).Render());

    private Task<string> Batched(
        string? workspace,
        string[] symbolIds,
        string[] declarations,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var rejection = context.RejectWrite();
        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(
                workspace,
                null,
                async loaded => NavigationTools.Unwrap(await SymbolEditService.ReplaceDeclarationsAsync(
                    loaded, symbolIds, declarations, options, cancellationToken).ConfigureAwait(false)),
                cancellationToken: cancellationToken);
    }
}
