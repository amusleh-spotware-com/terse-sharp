using System.Text;
using ModelContextProtocol.Server;

namespace TerseSharp.Server.Tools;

[McpServerToolType]
public sealed class EditTools(ToolContext context)
{
    private const string VerboseHelp = "Return the full diff instead of the one-line summary. Default false.";

    [McpServerTool(Name = "replace_symbol_body")]
    [Description("Replace a method, constructor or accessor body, addressed by symbol id, with usings= adding the namespaces it needs in the same compile-gated edit. No line numbers and no surrounding context needed. Replaces one call per missing import. Rolled back if it introduces a compile error, and the rejection then names a retryWith token that holds the body, so the retry costs a token instead of the whole payload. An unresolved symbolId holds the body the same way. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> ReplaceSymbolBody(
            [Description("Symbol id of the member.")] string? symbolId = null,
            [Description("New body: statements with or without the surrounding braces, or an expression body as '=> expr'. On a member that is already expression-bodied, a bare expression is accepted and stays expression-bodied.")] string body = "",
            [Description("Diff only, write nothing.")] bool dryRun = false,
            [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
            [Description(PolicyHelp)] bool allowPolicy = false,
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

        var target = symbolId ?? symbol ?? (held is null ? null : Slot(held.Targets, 0));
        var text = held is null ? body : First(held.Payloads, body);
        var imports = Kept(usings, held?.Usings);

        return Supplied(workspace, target, text, "body", (loaded, resolved) => SymbolEditService.ReplaceBodyAsync(
            loaded, resolved, text, Options("replace_symbol_body", dryRun, allowErrors, verbose, imports, allowPolicy: allowPolicy), cancellationToken),
            cancellationToken,
            new Carry("replace_symbol_body", [target ?? string.Empty], [text], Usings: imports),
            held?.Root);
    }

    [McpServerTool(Name = "replace_symbol")]
    [Description("Replace a whole member declaration including its signature, attributes and doc comment, addressed by symbol id, with usings= adding the namespaces it needs in the same compile-gated edit. An enum member id takes enum member declarations. Several declarations in one call replace the target with all of them - the way to split a member into overloads in one compile-gated edit. Pass symbolIds and declarations to replace members in several files as ONE compile-gated edit. Replaces one call per file, and is how a signature change lands together with the callers it breaks. Pass add to append the new private helpers the declaration calls, in that same edit, and addTo to name which containing type takes them - comma-separated, one per add entry, when they differ. rename=true accepts a declaration whose NAME differs from the symbol it is paired with, so a member is renamed and rewritten in one edit. A rollback names a retryWith token that holds the rejected declarations, so the retry costs a token instead of the whole payload, as is a batch refused for ONE unresolvable id. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> ReplaceSymbol(
                    [Description("Symbol id of the member.")] string? symbolId = null,
                    [Description("One complete member declaration, or several in sequence to replace the target with all of them.")] string declaration = "",
                    [Description(AddHelp)] string[]? add = null,
                    [Description("Name of the containing type that add= lands in, e.g. ToolBoundary or T:TerseSharp.Server.ToolBoundary. Only needed when the targets do not share one container, and it must name one of theirs. Comma-separated routes each add= entry to its own container, in order.")] string? addTo = null,
                    [Description("Diff only, write nothing.")] bool dryRun = false,
                    [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
                    [Description(PolicyHelp)] bool allowPolicy = false,
                    [Description(VerboseHelp)] bool verbose = false,
                    [Description("Workspace or worktree name.")] string? workspace = null,
                    [Description("Alias for symbolId.")] string? symbol = null,
                    [Description("Symbol ids of the members to replace together, paired positionally with declarations. Replaces one call per member. Several entries per file are allowed; two entries where one declaration contains the other are refused. Beside retryWith= it corrects the held ids.")] string[]? symbolIds = null,
                    [Description("One complete declaration per entry of symbolIds, in the same order, applied as a single compile-gated edit across every file they live in.")] string[]? declarations = null,
                    [Description(UsingsHelp)] string[]? usings = null,
                    [Description("Apply a declaration whose name differs from the symbol it is paired with instead of refusing the batch. References are not rewritten, so the gate rolls it back when a caller breaks; rename_symbol makes them follow. Not held by a retryWith token. Default false.")] bool rename = false,
                    [Description(RetryHelp)] string? retryWith = null,
                    CancellationToken cancellationToken = default)
    {
        if (RejectedUsings(usings) is { } rejected)
            return Task.FromResult(rejected);

        if (RejectedAdd(add) is { } blank)
            return Task.FromResult(blank);

        var held = Held(retryWith, "replace_symbol");

        if (retryWith is { Length: > 0 } token && held is null)
            return Task.FromResult(Unknown(token, "replace_symbol"));

        var imports = Kept(usings, held?.Usings);
        var helpers = Kept(add, held?.Add);
        var container = addTo ?? held?.AddTo;
        var options = Options("replace_symbol", dryRun, allowErrors, verbose, imports, helpers, container, rename, allowPolicy);

        if (held is { Targets.Count: > 1 })
            return Batched(workspace, Corrected(symbolIds, held.Targets), [.. held.Payloads], options, cancellationToken, held.Root, helpers, container, imports);

        if (held is null && (symbolIds, declarations) is not (null, null))
            return Batched(workspace, symbolIds ?? [], declarations ?? [], options, cancellationToken, null, helpers, container, imports);

        var target = symbolId ?? symbol ?? (held is null ? null : Slot(held.Targets, 0));
        var text = held is null ? declaration : First(held.Payloads, declaration);

        return Supplied(workspace, target, text, "declaration", (loaded, resolved) => SymbolEditService.ReplaceDeclarationAsync(
            loaded, resolved, text, options, cancellationToken),
            cancellationToken,
            new Carry("replace_symbol", [target ?? string.Empty], [text], helpers, container, imports),
            held?.Root);
    }
    [McpServerTool(Name = "add_member")]
    [Description("Add one or more members to a type, addressed by the type's symbol id, with usings= adding the namespaces they need in the same compile-gated edit - or, with path=, add namespace-level types to an existing .cs file. An enum symbol id takes enum members. Several declarations in one call land as one edit, so members that reference each other need no dependency ordering. Replaces one call per missing import. A rollback names a retryWith token that holds the rejected declarations, so the retry costs a token instead of the whole payload; an unresolved typeSymbolId is held the same way. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> AddMember(
            [Description("Symbol id of the containing type, or of an enum when adding enum members. Cannot be combined with path.")] string? typeSymbolId = null,
            [Description("One complete member declaration, or several in sequence; they are added together as one edit. With an enum container, one or more enum member names.")] string declaration = "",
            [Description("Path of a .cs file to append namespace-level type declarations to, instead of a type symbol id.")] string? path = null,
            [Description("Diff only, write nothing.")] bool dryRun = false,
            [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
            [Description(PolicyHelp)] bool allowPolicy = false,
            [Description(VerboseHelp)] bool verbose = false,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Alias for typeSymbolId.")] string? symbol = null,
            [Description("Alias for typeSymbolId, so the name every other symbol-addressed tool takes resolves here too.")] string? symbolId = null,
            [Description(UsingsHelp)] string[]? usings = null,
            [Description(RetryHelp)] string? retryWith = null,
            [Description("Alias for declaration; entries join into the one edit.")] string[]? declarations = null,
            CancellationToken cancellationToken = default)
    {
        if (RejectedUsings(usings) is { } rejected)
            return Task.FromResult(rejected);

        if (RejectedDeclarations(declarations) is { } malformed)
            return Task.FromResult(malformed);

        var held = Held(retryWith, "add_member");

        if (retryWith is { Length: > 0 } token && held is null)
            return Task.FromResult(Unknown(token, "add_member"));

        var container = typeSymbolId ?? symbol ?? symbolId ?? (held is null ? null : Slot(held.Targets, 0));
        var file = path ?? (held is null ? null : Slot(held.Targets, 1));
        var sent = Merged(declaration, declarations);
        var text = held is null ? sent : First(held.Payloads, sent);
        var imports = Kept(usings, held?.Usings);

        return Added(workspace, container, file, text, Options("add_member", dryRun, allowErrors, verbose, imports, allowPolicy: allowPolicy), cancellationToken, held?.Root, imports);
    }

    private Task<string> Added(
        string? workspace,
        string? typeSymbolId,
        string? path,
        string declaration,
        EditOptions options,
        CancellationToken cancellationToken,
        string? heldRoot = null,
        string[]? usings = null) => (typeSymbolId, path) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => Task.FromResult(Errors.Invalid(
                "both a type symbol id and a path were passed, and they name different containers",
                "pass typeSymbolId to add members to a type, or path to add namespace-level types to a file - not both").Render()),
            (_, { Length: > 0 } file) when declaration is { Length: > 0 } => AddToFile(workspace, file, declaration, options, cancellationToken, heldRoot, usings),
            (_, { Length: > 0 }) => Task.FromResult(Errors.Blank("declaration").Render()),
            _ => Supplied(workspace, typeSymbolId, declaration, "declaration", (loaded, resolved) => SymbolEditService.AddMemberAsync(
                loaded, resolved, declaration, options, cancellationToken), cancellationToken, new Carry("add_member", [typeSymbolId ?? string.Empty, string.Empty], [declaration], Usings: usings), heldRoot, typesOnly: true),
        };

    private Task<string> AddToFile(
        string? workspace,
        string path,
        string declaration,
        EditOptions options,
        CancellationToken cancellationToken,
        string? heldRoot = null,
        string[]? usings = null)
    {
        var rejection = context.RejectWrite();
        var carry = new Carry("add_member", [string.Empty, path], [declaration], Usings: usings);

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
            [Description(PolicyHelp)] bool allowPolicy = false,
            [Description(VerboseHelp)] bool verbose = false,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Alias for symbolId.")] string? symbol = null,
            CancellationToken cancellationToken = default) =>
            Guarded(workspace, symbolId ?? symbol, (loaded, resolved) => SymbolEditService.DeleteAsync(
                loaded, resolved, force, Options("delete_symbol", dryRun, allowErrors: false, verbose, allowPolicy: allowPolicy), cancellationToken), cancellationToken);

    [McpServerTool(Name = "rename_symbol")]
    [Description("Rename a symbol across the whole solution, including interface implementations, overrides and XML doc crefs. Use instead of a find-and-replace sweep. A successful rename answers in one line per changed file - plus every XAML or Razor site it could NOT rewrite; pass verbose=true for the diff.")]
    public Task<string> RenameSymbol(
            [Description("Symbol id to rename.")] string? symbolId = null,
            [Description("New identifier.")] string newName = "",
            [Description("Diff only, write nothing.")] bool dryRun = false,
            [Description(PolicyHelp)] bool allowPolicy = false,
            [Description(VerboseHelp)] bool verbose = false,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Alias for symbolId.")] string? symbol = null,
            CancellationToken cancellationToken = default) =>
            Supplied(workspace, symbolId ?? symbol, newName, "newName", (loaded, resolved) => RenameService.RenameAsync(
                loaded, resolved, newName, Options("rename_symbol", dryRun, allowErrors: false, verbose, allowPolicy: allowPolicy), cancellationToken), cancellationToken);

    private static EditOptions Options(string tool, bool dryRun, bool allowErrors, bool verbose, string[]? usings = null, string[]? add = null, string? addTo = null, bool rename = false, bool allowPolicy = false) =>
            new(tool, dryRun, allowErrors, verbose, usings is null ? default : [.. usings], add is null ? default : [.. add], addTo, rename, allowPolicy);

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
                typesOnly: typesOnly,
                unresolved: (loaded, error) => Rejected(error, carry, loaded.Root));
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
        string? heldRoot = null,
        string[]? add = null,
        string? addTo = null,
        string[]? usings = null)
    {
        var rejection = context.RejectWrite();
        var carry = new Carry("replace_symbol", symbolIds, declarations, add, addTo, usings);

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(
                workspace,
                null,
                async loaded => Elsewhere(heldRoot, loaded.Root) ?? Carried(await SymbolEditService.ReplaceDeclarationsAsync(
                    loaded, symbolIds, declarations, options, cancellationToken).ConfigureAwait(false), carry, loaded.Root),
                cancellationToken: cancellationToken);
    }

    private const string RetryHelp = "Token from a previous CompileRegression or resolution failure, e.g. r3. The rejected declaration is held with its add= and usings=, so a retry names the token instead of re-sending any of them; pass either again to override what is held, pass usings=[] to DROP the imports it holds, combine it with allowErrors=true, or send the missing callee first and then retry. The token is printed alone on the LAST line of a rejection, so reading it to the end of the line is safe. A symbolId or symbolIds you pass OUTRANKS the held one, which is how a mis-typed id is corrected. The token is bound to the workspace the edit was rejected in and to the tool that issued it: a replay that resolves to another workspace is refused instead of landing there, and a replay by the wrong edit tool is refused naming the tool that can apply it.";

    private readonly record struct Carry(
        string? Tool,
        string[]? Targets,
        string[]? Payloads,
        string[]? Add = null,
        string? AddTo = null,
        string[]? Usings = null);

    private static string Carried(Result<string> result, Carry carry, string root) =>
        result.IsOk ? result.Value! : Rejected(result.Error!, carry, root);

    private static string? Elsewhere(string? held, string root) => held is { Length: > 0 } origin && !PathBoundary.SameFile(origin, root)
        ? Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"the held rejection belongs to {origin}, and this call resolved to {root}"),
            "replay the token against the workspace it was rejected in, or re-send the declaration to edit this one").Render()
        : null;

    private static string Unknown(string token, string tool) => Errors.Invalid(
        RejectedEdits.Recall(token) is { } issued
            ? string.Create(CultureInfo.InvariantCulture, $"retryWith={token} was issued by {issued.Tool}, not by {tool}")
            : string.Create(CultureInfo.InvariantCulture, $"retryWith={token} names no held rejection of {tool}"),
        RejectedEdits.Recall(token) is { } held
            ? string.Create(CultureInfo.InvariantCulture, $"replay it with {held.Tool}, which is the tool that can apply what it holds")
            : "re-send the text; the server holds only the last 8 rejected edits of this process").Render();

    private static RejectedEdit? Held(string? retryWith, string tool) =>
        retryWith is { Length: > 0 } token && RejectedEdits.Recall(token) is { } edit
        && string.Equals(edit.Tool, tool, StringComparison.Ordinal)
            ? edit
            : null;

    private static string First(IReadOnlyList<string> values, string fallback) =>
        values is [var only, ..] ? only : fallback;

    private static string? Slot(IReadOnlyList<string> targets, int index) =>
        index < targets.Count && targets[index] is { Length: > 0 } value ? value : null;

    private const string UsingsHelp = "Pass usings to add the namespaces this declaration needs in the SAME compile-gated edit. Replaces one edit_text force=true on the file header plus one retryWith after a CS0246 rollback. Each entry is a namespace such as System.Collections.Immutable; one already present is ignored, an entry that is not a namespace is refused by name, and a new directive is inserted at its sorted position without reordering the ones already there. It is carried by a retryWith token, so a retry need not re-send it - and usings=[] on that retry drops what the token holds, which is the fix when the import this edit added is what made a name ambiguous.";

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

    private const string AddHelp = "New members appended to the type that contains the replaced member, in the SAME compile-gated edit - the one-call answer to the callee-after-caller rollback. Every target must share one containing type. Not held by a retryWith token; pass it again on the retry.";

    private static string? RejectedAdd(string[]? add)
    {
        if (add is null)
            return null;

        foreach (var declaration in add)
        {
            if (string.IsNullOrWhiteSpace(declaration))
                return Errors.Blank("add").Render();
        }

        return null;
    }

    private static string[]? Kept(string[]? supplied, IReadOnlyList<string>? held) => supplied is not null
        ? (supplied.Length is 0 ? null : supplied)
        : held is { Count: > 0 } ? [.. held] : null;

    private static string Rejected(TerseError error, Carry carry, string root) =>
        carry.Tool is { Length: > 0 } tool && Holdable(error.Code) && Worth(carry)
            ? error.Render() + "\n" + Note(error.Code, carry) + "\nretryWith=" + RejectedEdits.Remember(
                root, tool, carry.Targets ?? [], carry.Payloads ?? [], carry.Add, carry.AddTo, carry.Usings)
            : error.Render();

    private static bool Holdable(TerseErrorCode code) =>
            code is TerseErrorCode.CompileRegression or TerseErrorCode.PolicyViolation or TerseErrorCode.SymbolNotFound or TerseErrorCode.AmbiguousSymbol;

    private static string Note(TerseErrorCode code, Carry carry) => code switch
    {
        TerseErrorCode.CompileRegression => "the rejected text, its add= and its usings= are held, so the retry names the token instead of re-sending them",
        _ when carry.Targets is { Length: > 1 } => "the declarations are held, so the retry is the token plus a corrected symbolIds= - one entry per held declaration, and nothing else",
        _ => "the declaration is held, so the retry is the token plus a corrected symbolId= and nothing else",
    };

    private static string[] Corrected(string[]? supplied, IReadOnlyList<string> held) =>
        supplied is { Length: > 0 } ? supplied : [.. held];

    private static bool Worth(Carry carry) =>
        carry.Payloads is { } payloads && Array.Exists(payloads, text => text is { Length: > 0 });

    private const string PolicyHelp = "Apply an edit the project's .terse.json code policy would reject; the response then names every rule it bypassed. Default false.";

    private static string Merged(string declaration, string[]? declarations)
    {
        if (declarations is null || declarations.Length is 0)
            return declaration;

        var builder = new StringBuilder(declaration);

        foreach (var entry in declarations)
        {
            if (builder.Length > 0)
                builder.Append("\n\n");

            builder.Append(entry);
        }

        return builder.ToString();
    }

    private const int DeclarationCap = 20;

    private static string? RejectedDeclarations(string[]? declarations)
    {
        if (declarations is null)
            return null;

        if (declarations.Length > DeclarationCap)
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"declarations carries {declarations.Length} entries, more than the {DeclarationCap} add_member accepts"),
                "send the remaining declarations in a second add_member call").Render();
        }

        for (var index = 0; index < declarations.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(declarations[index]))
            {
                return Errors.Invalid(
                    string.Create(CultureInfo.InvariantCulture, $"declarations[{index}] is blank"),
                    "every declarations entry is one complete member declaration").Render();
            }
        }

        return null;
    }
}
