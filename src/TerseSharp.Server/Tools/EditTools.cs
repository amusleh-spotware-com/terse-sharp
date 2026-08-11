using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class EditTools(ToolContext context)
{
    private const string VerboseHelp = "Return the full diff instead of the one-line summary. Default false.";

    [McpServerTool(Name = "replace_symbol_body")]
    [Description("Replace a method, constructor or accessor body, addressed by symbol id. No line numbers and no surrounding context needed. Pass usings to add the namespaces the new body needs in the same compile-gated edit. Replaces one call per missing import. Rolled back if it introduces a compile error, and the rejection then names a retryWith token that holds the body, so the retry costs a token instead of the whole payload. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> ReplaceSymbolBody(
    [Description("Symbol id of the member.")] string? symbolId = null,
    [Description("New body: statements with or without the surrounding braces, or an expression body as '=> expr'. On a member that is already expression-bodied, a bare expression is accepted and stays expression-bodied.")] string body = "",
    [Description("Diff only, write nothing.")] bool dryRun = false,
    [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
    [Description(VerboseHelp)] bool verbose = false,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Alias for symbolId.")] string? symbol = null,
    [Description(UsingsHelp)] string[]? usings = null,
    [Description(RetryHelp)] string? retryWith = null,
    CancellationToken cancellationToken = default)
    {
        if (RejectedUsings(usings) is { } rejected)
            return Task.FromResult(rejected);

        var held = Held(retryWith, "replace_symbol_body");

        if (retryWith is { Length: > 0 } token && held is null)
            return Task.FromResult(Unknown(token, "replace_symbol_body"));

        var target = held is null ? symbolId ?? symbol : Slot(held.Targets, 0);
        var text = held is null ? body : First(held.Payloads, body);

        return Supplied(workspace, target, text, "body", (loaded, resolved) => SymbolEditService.ReplaceBodyAsync(
            loaded, resolved, text, Options("replace_symbol_body", dryRun, allowErrors, verbose, usings), cancellationToken),
            cancellationToken,
            new Carry("replace_symbol_body", [target ?? string.Empty], [text]),
            held?.Root);
    }

    [McpServerTool(Name = "replace_symbol")]
    [Description("Replace a whole member declaration including its signature, attributes and doc comment, addressed by symbol id. An enum member id takes enum member declarations. Several declarations in one call replace the target with all of them - the way to split a member into overloads in one compile-gated edit. Pass symbolIds and declarations to replace members in several files as ONE compile-gated edit. Replaces one call per file, and is how a signature change lands together with the callers it breaks. Pass usings to add the namespaces the new declarations need in the same edit. A rollback names a retryWith token that holds the rejected declarations, so the retry costs a token instead of the whole payload. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> ReplaceSymbol(
    [Description("Symbol id of the member.")] string? symbolId = null,
    [Description("One complete member declaration, or several in sequence to replace the target with all of them.")] string declaration = "",
    [Description("Diff only, write nothing.")] bool dryRun = false,
    [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
    [Description(VerboseHelp)] bool verbose = false,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Alias for symbolId.")] string? symbol = null,
    [Description("Symbol ids of the members to replace together, paired positionally with declarations. Replaces one call per member. Several entries per file are allowed; two entries where one declaration contains the other are refused.")] string[]? symbolIds = null,
    [Description("One complete declaration per entry of symbolIds, in the same order, applied as a single compile-gated edit across every file they live in.")] string[]? declarations = null,
    [Description(UsingsHelp)] string[]? usings = null,
    [Description(RetryHelp)] string? retryWith = null,
    CancellationToken cancellationToken = default)
    {
        if (RejectedUsings(usings) is { } rejected)
            return Task.FromResult(rejected);

        var held = Held(retryWith, "replace_symbol");

        if (retryWith is { Length: > 0 } token && held is null)
            return Task.FromResult(Unknown(token, "replace_symbol"));

        var options = Options("replace_symbol", dryRun, allowErrors, verbose, usings);

        if (held is { Targets.Count: > 1 })
            return Batched(workspace, [.. held.Targets], [.. held.Payloads], options, cancellationToken, held.Root);

        if (held is null && (symbolIds, declarations) is not (null, null))
            return Batched(workspace, symbolIds ?? [], declarations ?? [], options, cancellationToken);

        var target = held is null ? symbolId ?? symbol : Slot(held.Targets, 0);
        var text = held is null ? declaration : First(held.Payloads, declaration);

        return Supplied(workspace, target, text, "declaration", (loaded, resolved) => SymbolEditService.ReplaceDeclarationAsync(
            loaded, resolved, text, options, cancellationToken),
            cancellationToken,
            new Carry("replace_symbol", [target ?? string.Empty], [text]),
            held?.Root);
    }
    [McpServerTool(Name = "add_member")]
    [Description("Add one or more members to a type, addressed by the type's symbol id - or, with path=, add namespace-level types to an existing .cs file. An enum symbol id takes enum members. Several declarations in one call land as a single compile-gated edit, so a set of members that reference each other needs no dependency ordering. Pass usings to add the namespaces they need in that same edit. Replaces one call per missing import. A rollback names a retryWith token that holds the rejected declarations, so the retry costs a token instead of the whole payload. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> AddMember(
    [Description("Symbol id of the containing type, or of an enum when adding enum members. Cannot be combined with path.")] string? typeSymbolId = null,
    [Description("One complete member declaration, or several in sequence; they are added together as one edit. With an enum container, one or more enum member names.")] string declaration = "",
    [Description("Path of a .cs file to append namespace-level type declarations to, instead of a type symbol id.")] string? path = null,
    [Description("Diff only, write nothing.")] bool dryRun = false,
    [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
    [Description(VerboseHelp)] bool verbose = false,
    [Description("Workspace or worktree name.")] string? workspace = null,
    [Description("Alias for typeSymbolId.")] string? symbol = null,
    [Description(UsingsHelp)] string[]? usings = null,
    [Description(RetryHelp)] string? retryWith = null,
    CancellationToken cancellationToken = default)
    {
        if (RejectedUsings(usings) is { } rejected)
            return Task.FromResult(rejected);

        var held = Held(retryWith, "add_member");

        if (retryWith is { Length: > 0 } token && held is null)
            return Task.FromResult(Unknown(token, "add_member"));

        var container = held is null ? typeSymbolId ?? symbol : Slot(held.Targets, 0);
        var file = held is null ? path : Slot(held.Targets, 1);
        var text = held is null ? declaration : First(held.Payloads, declaration);

        return Added(workspace, container, file, text, Options("add_member", dryRun, allowErrors, verbose, usings), cancellationToken, held?.Root);
    }

    private Task<string> Added(
        string? workspace,
        string? typeSymbolId,
        string? path,
        string declaration,
        EditOptions options,
        CancellationToken cancellationToken,
        string? heldRoot = null) => (typeSymbolId, path) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => Task.FromResult(Errors.Invalid(
                "both a type symbol id and a path were passed, and they name different containers",
                "pass typeSymbolId to add members to a type, or path to add namespace-level types to a file - not both").Render()),
            (_, { Length: > 0 } file) when declaration is { Length: > 0 } => AddToFile(workspace, file, declaration, options, cancellationToken, heldRoot),
            (_, { Length: > 0 }) => Task.FromResult(Errors.Blank("declaration").Render()),
            _ => Supplied(workspace, typeSymbolId, declaration, "declaration", (loaded, resolved) => SymbolEditService.AddMemberAsync(
                loaded, resolved, declaration, options, cancellationToken), cancellationToken, new Carry("add_member", [typeSymbolId ?? string.Empty, string.Empty], [declaration]), heldRoot, typesOnly: true),
        };

    private Task<string> AddToFile(
        string? workspace,
        string path,
        string declaration,
        EditOptions options,
        CancellationToken cancellationToken,
        string? heldRoot = null)
    {
        var rejection = context.RejectWrite();
        var carry = new Carry("add_member", [string.Empty, path], [declaration]);

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(
                workspace,
                path,
                async loaded => Elsewhere(heldRoot, loaded.Root) ?? Carried(
                    await SymbolEditService.AddToFileAsync(loaded, path, declaration, options, cancellationToken).ConfigureAwait(false),
                    carry,
                    loaded.Root),
                cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "delete_symbol", Destructive = true)]
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

    private static EditOptions Options(string tool, bool dryRun, bool allowErrors, bool verbose, string[]? usings = null) =>
    new(tool, dryRun, allowErrors, verbose, usings is null ? default : [.. usings]);

    private Task<string> Guarded(
    string? workspace,
    string? symbolId,
    Func<LoadedWorkspace, Microsoft.CodeAnalysis.ISymbol, Task<Result<string>>> action,
    CancellationToken cancellationToken,
    Carry carry = default,
    string? heldRoot = null,
    bool typesOnly = false)
    {
        var rejection = context.RejectWrite();

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithSymbolAsync(
                workspace,
                symbolId,
                async (loaded, resolved) => Carried(await action(loaded, resolved).ConfigureAwait(false), carry, loaded.Root),
                cancellationToken,
                guard: loaded => Elsewhere(heldRoot, loaded.Root),
                typesOnly: typesOnly);
    }

    private Task<string> Supplied(
    string? workspace,
    string? symbolId,
    string text,
    string name,
    Func<LoadedWorkspace, Microsoft.CodeAnalysis.ISymbol, Task<Result<string>>> action,
    CancellationToken cancellationToken,
    Carry carry = default,
    string? heldRoot = null,
    bool typesOnly = false) => text is { Length: > 0 }
    ? Guarded(workspace, symbolId, action, cancellationToken, carry, heldRoot, typesOnly)
    : Task.FromResult(Errors.Blank(name).Render());

    private Task<string> Batched(
        string? workspace,
        string[] symbolIds,
        string[] declarations,
        EditOptions options,
        CancellationToken cancellationToken,
        string? heldRoot = null)
    {
        var rejection = context.RejectWrite();
        var carry = new Carry("replace_symbol", symbolIds, declarations);

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(
                workspace,
                null,
                async loaded => Elsewhere(heldRoot, loaded.Root) ?? Carried(await SymbolEditService.ReplaceDeclarationsAsync(
                    loaded, symbolIds, declarations, options, cancellationToken).ConfigureAwait(false), carry, loaded.Root),
                cancellationToken: cancellationToken);
    }

    private const string RetryHelp = "Token from a previous CompileRegression, e.g. r3. The rejected declaration is held by the server, so a retry names the token instead of re-sending the text; combine it with allowErrors=true, or send the missing callee first and then retry. usings= is NOT held with it - pass it again on the retry. The token is bound to the workspace the edit was rejected in: a replay that resolves to another one is refused instead of landing there.";

    private readonly record struct Carry(string? Tool, string[]? Targets, string[]? Payloads);

    private static string Carried(Result<string> result, Carry carry, string root)
    {
        if (result.IsOk)
            return result.Value!;

        var error = result.Error!;

        return carry.Tool is { Length: > 0 } tool && error.Code is TerseErrorCode.CompileRegression
            ? error.Render() + "\nretryWith=" + RejectedEdits.Remember(root, tool, carry.Targets ?? [], carry.Payloads ?? [])
                + "  the rejected text is held, so the retry names the token instead of re-sending it"
            : error.Render();
    }

    private static string? Elsewhere(string? held, string root) => held is { Length: > 0 } origin && !PathBoundary.SameFile(origin, root)
        ? Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"the held rejection belongs to {origin}, and this call resolved to {root}"),
            "replay the token against the workspace it was rejected in, or re-send the declaration to edit this one").Render()
        : null;

    private static string Unknown(string token, string tool) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"retryWith={token} names no held rejection of {tool}"),
        "re-send the text; the server holds only the last 8 rejected edits of this process").Render();


    private static RejectedEdit? Held(string? retryWith, string tool) =>
        retryWith is { Length: > 0 } token && RejectedEdits.Recall(token) is { } edit
        && string.Equals(edit.Tool, tool, StringComparison.Ordinal)
            ? edit
            : null;


    private static string First(IReadOnlyList<string> values, string fallback) =>
        values is [var only, ..] ? only : fallback;

    private static string? Slot(IReadOnlyList<string> targets, int index) =>
        index < targets.Count && targets[index] is { Length: > 0 } value ? value : null;

    private const string UsingsHelp = "Pass usings to add the namespaces this declaration needs in the SAME compile-gated edit. Replaces one edit_text force=true on the file header plus one retryWith after a CS0246 rollback. Each entry is a namespace such as System.Collections.Immutable; one already present is ignored, an entry that is not a namespace is refused by name, and a new directive is inserted at its sorted position without reordering the ones already there. It is not carried by a retryWith token - pass it again on the retry.";

    private static string? RejectedUsings(string[]? usings)
    {
        if (usings is null)
            return null;

        foreach (var name in usings)
        {
            if (!UsingDirectives.IsNamespace(name))
            {
                return Errors.Invalid(
                    string.Create(CultureInfo.InvariantCulture, $"'{name}' is not a namespace"),
                    "each usings entry is a namespace such as System.Collections.Immutable").Render();
            }
        }

        return null;
    }
}
