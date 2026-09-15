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
    [Description("Replace a whole member declaration including its signature, attributes and doc comment, addressed by symbol id, with usings= adding the namespaces it needs in the same compile-gated edit. An enum member id takes enum member declarations. Several declarations in one call replace the target with all of them - the way to split a member into overloads in one compile-gated edit. Pass symbolIds and declarations to replace members in several files as ONE compile-gated edit. Replaces one call per file, and is how a signature change lands together with the callers it breaks. Pass add to append the new private helpers the declaration calls, in that same edit, and addTo to name which containing type takes them - comma-separated, one per add entry, when they differ. rename=true accepts a declaration whose NAME differs from the symbol it is paired with, so a member is renamed and rewritten in one edit. A rollback names a retryWith token that holds the rejected declarations, so the retry costs a token instead of the whole payload, as is a batch refused for ONE unresolvable id; fix=[\"2=<corrected>\"] replaces only the held declarations that were wrong and fix=[\"add:1=...\"] the held add= helpers, while append=true ADDS the symbolIds= and declarations= you pass to the held batch - how a CS7036 rollback's callers land with the member. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
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
                [Description(FixHelp)] string[]? fix = null,
                [Description("Beside retryWith=, ADD the pairs you pass to the held batch instead of correcting it. Refused without a token. Default false.")] bool append = false,
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

        if (RejectedFix(fix, retryWith, held?.Payloads.Count ?? 0, held?.Add.Count ?? 0) is { } misfit)
            return Task.FromResult(misfit);

        if (append && held is null)
        {
            return Task.FromResult(Errors.Invalid(
                "'append' adds to the batch a retryWith token holds, and no token was passed",
                "pass the retryWith token the rejection printed beside it, or send the whole batch as symbolIds= and declarations=").Render());
        }

        if (append && (declaration is { Length: > 0 } || symbolId is { Length: > 0 } || symbol is { Length: > 0 }))
        {
            return Task.FromResult(Errors.Invalid(
                "'append' adds symbolIds= and declarations= pairs to the held batch, and a singular symbolId= or declaration= was passed beside it - it would be silently dropped",
                "send the pair you are adding as symbolIds=[...] and declarations=[...], or drop append= to correct the held batch instead").Render());
        }

        var imports = Kept(usings, held?.Usings);
        var helpers = Kept(add, held is null ? null : Patched(held.Add, fix, add: true));
        var container = addTo ?? held?.AddTo;
        var options = Options("replace_symbol", dryRun, allowErrors, verbose, imports, helpers, container, rename, allowPolicy);

        if (held is not null && append)
        {
            return Batched(
                workspace,
                [.. held.Targets, .. symbolIds ?? []],
                [.. Patched(held.Payloads, fix), .. declarations ?? []],
                options,
                cancellationToken,
                held.Root,
                helpers,
                container,
                imports);
        }

        if (held is { Targets.Count: > 1 })
            return Batched(workspace, Corrected(symbolIds, held.Targets), Patched(held.Payloads, fix), options, cancellationToken, held.Root, helpers, container, imports);

        if (held is null && (symbolIds, declarations) is not (null, null))
            return Batched(workspace, symbolIds ?? [], declarations ?? [], options, cancellationToken, null, helpers, container, imports);

        var target = symbolId ?? symbol ?? (held is null ? null : Slot(held.Targets, 0));
        var text = held is null ? declaration : First(Patched(held.Payloads, fix), declaration);

        return Supplied(workspace, target, text, "declaration", (loaded, resolved) => SymbolEditService.ReplaceDeclarationAsync(
            loaded, resolved, text, options, cancellationToken),
            cancellationToken,
            new Carry("replace_symbol", [target ?? string.Empty], [text], helpers, container, imports),
            held?.Root);
    }
    [McpServerTool(Name = "add_member")]
    [Description("Add one or more members to a type, addressed by the type's symbol id, with usings= adding the namespaces they need in the same compile-gated edit - or, with path=, add namespace-level types to an existing .cs file. before= and after= place the new members above or below a member the type declares, and position=first|afterFields|last picks a coarse slot; the default appends at the end, above a trailing #region the type closes. An enum symbol id takes enum members. Several declarations in one call land as one edit, so members that reference each other need no dependency ordering. Replaces one call per missing import. A rollback names a retryWith token that holds the rejected declarations, so the retry costs a token instead of the whole payload; an unresolved typeSymbolId is held the same way. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
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
            [Description("Member of this type to land the new members ABOVE, by short name or documentation id. Not with after= or position=, and not held by a retryWith token.")] string? before = null,
            [Description("Member of this type to land the new members BELOW, addressed as before= is. Not with before= or position=.")] string? after = null,
            [Description("Coarse slot instead of an anchor: first, afterFields (after the last field) or last. Default last. Not with before= or after=.")] string? position = null,
            CancellationToken cancellationToken = default)
    {
        if (RejectedUsings(usings) is { } rejected)
            return Task.FromResult(rejected);

        if (RejectedDeclarations(declarations) is { } malformed)
            return Task.FromResult(malformed);

        var placement = Placement(before, after, position);

        if (!placement.IsOk)
            return Task.FromResult(placement.Error!.Render());

        var held = Held(retryWith, "add_member");

        if (retryWith is { Length: > 0 } token && held is null)
            return Task.FromResult(Unknown(token, "add_member"));

        var container = typeSymbolId ?? symbol ?? symbolId ?? (held is null ? null : Slot(held.Targets, 0));
        var file = path ?? (held is null ? null : Slot(held.Targets, 1));
        var sent = Merged(declaration, declarations);
        var text = held is null ? sent : First(held.Payloads, sent);
        var imports = Kept(usings, held?.Usings);
        var options = Options("add_member", dryRun, allowErrors, verbose, imports, allowPolicy: allowPolicy, placement: placement.Value);

        return Added(workspace, container, file, text, options, cancellationToken, held?.Root, imports);
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
            (_, { Length: > 0 }) when options.Placement is not null => Task.FromResult(Errors.Invalid(
                "before=, after= and position= place a member inside a type, and path= appends namespace-level types to a file, which has no member list to place them in",
                "drop the placement to append the types to that file, or pass typeSymbolId to place members inside a type").Render()),
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
    [Description("Safe-delete a member, an enum member or a type. Refuses while references exist unless force is set, and lists them; allowErrors=true applies it anyway. A successful delete answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> DeleteSymbol(
            [Description("Symbol id to delete.")] string? symbolId = null,
            [Description("Delete even when references exist. Default false.")] bool force = false,
            [Description("Diff only, write nothing.")] bool dryRun = false,
            [Description("Apply even if it introduces compile errors. Default false.")] bool allowErrors = false,
            [Description(PolicyHelp)] bool allowPolicy = false,
            [Description(VerboseHelp)] bool verbose = false,
            [Description("Workspace or worktree name.")] string? workspace = null,
            [Description("Alias for symbolId.")] string? symbol = null,
            CancellationToken cancellationToken = default) =>
            Guarded(workspace, symbolId ?? symbol, (loaded, resolved) => SymbolEditService.DeleteAsync(
                loaded, resolved, force, Options("delete_symbol", dryRun, allowErrors, verbose, allowPolicy: allowPolicy), cancellationToken), cancellationToken);

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

    private static EditOptions Options(string tool, bool dryRun, bool allowErrors, bool verbose, string[]? usings = null, string[]? add = null, string? addTo = null, bool rename = false, bool allowPolicy = false, MemberPlacement? placement = null) =>
            new(tool, dryRun, allowErrors, verbose, usings is null ? default : [.. usings], add is null ? default : [.. add], addTo, rename, allowPolicy, placement);

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
            code is TerseErrorCode.CompileRegression or TerseErrorCode.PolicyViolation or TerseErrorCode.SymbolNotFound or TerseErrorCode.AmbiguousSymbol or TerseErrorCode.InvalidArgument;

    private static string Note(TerseErrorCode code, Carry carry) => (code, carry.Targets) switch
    {
        (TerseErrorCode.CompileRegression, { Length: > 1 }) => "the rejected declarations, their add= and their usings= are held, so the retry names the token, and fix=[\"<index>=<corrected declaration>\"] replaces only the entries that were wrong",
        (TerseErrorCode.CompileRegression, _) => "the rejected text, its add= and its usings= are held, so the retry names the token instead of re-sending them",
        (_, { Length: > 1 }) => "the declarations are held, so the retry is the token plus a corrected symbolIds= - one entry per held declaration - or fix=[\"<index>=<corrected declaration>\"] to replace only the entries that were wrong",
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

    private const string FixHelp = "Correct held entries on a retryWith replay instead of re-sending the batch. Each entry is '<index>=<declaration>' for a held declaration, or 'add:<index>=<declaration>' for a held add= helper, index being the 0-based position the rejection printed; every held entry fix does not name replays unchanged. Only with retryWith, and an index the batch does not carry is refused naming the range.";

    private static (int Index, string Text, bool Add)? Correction(string entry)
    {
        var span = entry.AsSpan();
        var add = span.StartsWith("add:", StringComparison.Ordinal);

        if (add)
            span = span[4..];

        var separator = span.IndexOf('=');

        if (separator <= 0 || !int.TryParse(span[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return null;

        var text = span[(separator + 1)..].Trim();

        return text.IsEmpty ? null : (index, new string(text), add);
    }

    private static string Detached() => Errors.Invalid(
        "fix was passed without retryWith, so there is no held batch to correct",
        "pass the retryWith token the rejection printed, or send the corrected batch as declarations=").Render();

    private static string? Misfit(string entry, int position, int held, List<int> named, int addHeld, List<int> addNamed) => Correction(entry) switch
    {
        null => Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"fix[{position}] is not '<index>=<declaration>' or 'add:<index>=<declaration>'"),
            "each fix entry names the 0-based index the rejection printed and the corrected declaration, e.g. 2=public int Count() => 1; or add:1=private int Helper() => 2;").Render(),
        { Add: var add, Index: var index } when index >= (add ? addHeld : held) => OutOfRange(position, index, add, add ? addHeld : held),
        { Add: true, Index: var index } when addNamed.Contains(index) => Repeat(position, string.Create(CultureInfo.InvariantCulture, $"add:{index}")),
        { Add: false, Index: var index } when named.Contains(index) => Repeat(position, string.Create(CultureInfo.InvariantCulture, $"index {index}")),
        { Add: var add, Index: var index } => Remembered(add ? addNamed : named, index),
    };

    private static string? RejectedFix(string[]? fix, string? retryWith, int held, int addHeld = 0)
    {
        if (fix is null || fix.Length is 0)
            return null;

        if (retryWith is not { Length: > 0 })
            return Detached();

        var named = new List<int>(fix.Length);
        var addNamed = new List<int>(fix.Length);

        for (var entry = 0; entry < fix.Length; entry++)
        {
            if (Misfit(fix[entry], entry, held, named, addHeld, addNamed) is { } refusal)
                return refusal;
        }

        return null;
    }

    private static string[] Patched(IReadOnlyList<string> held, string[]? fix, bool add = false)
    {
        string[] payloads = [.. held];

        foreach (var entry in fix ?? [])
        {
            if (Correction(entry) is { } slot && slot.Add == add)
                payloads[slot.Index] = slot.Text;
        }

        return payloads;
    }

    private static string? Remembered(List<int> named, int index)
    {
        named.Add(index);

        return null;
    }

    private static string OutOfRange(int position, int index, bool add, int held) => Errors.Invalid(
        add
            ? string.Create(CultureInfo.InvariantCulture, $"fix[{position}] names add:{index}, and the held batch carries {held} add= entries")
            : string.Create(CultureInfo.InvariantCulture, $"fix[{position}] names index {index}, and the held batch carries {held} declaration(s)"),
        held is 0
            ? (add ? "the token holds no add= entry to correct - re-send them as add=" : "the token holds no declaration to correct - re-send the batch as declarations=")
            : string.Create(CultureInfo.InvariantCulture, $"name an index between 0 and {held - 1}")).Render();

    private static string Repeat(int position, string named) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"fix[{position}] names {named}, which an earlier entry already corrected"),
        "name each held entry at most once - two corrections of one entry cannot both land").Render();

    private static Result<MemberPosition> PlacementSlot(string? position)
    {
        if (position is null or "" || string.Equals(position, "last", StringComparison.OrdinalIgnoreCase))
            return Result.Ok(MemberPosition.Last);

        if (string.Equals(position, "first", StringComparison.OrdinalIgnoreCase))
            return Result.Ok(MemberPosition.First);

        return string.Equals(position, "afterFields", StringComparison.OrdinalIgnoreCase)
            ? Result.Ok(MemberPosition.AfterFields)
            : Result.Fail<MemberPosition>(Errors.Invalid(
                "position=" + position + " is not a slot this tool declares",
                "pass position=first, position=afterFields or position=last, or before=/after= to anchor on a member this type declares"));
    }

    private static Result<MemberPlacement?> Anchored(string? before, string? after, string? position)
    {
        var slot = PlacementSlot(position);

        return slot.IsOk
            ? Result.Ok<MemberPlacement?>(new MemberPlacement(before, after, slot.Value))
            : Result.Fail<MemberPlacement?>(slot.Error!);
    }

    private static Result<MemberPlacement?> Placement(string? before, string? after, string? position) => (before, after, position) switch
    {
        ({ Length: > 0 }, { Length: > 0 }, _) => Result.Fail<MemberPlacement?>(Errors.Invalid(
            "before= and after= both name an anchor, and one member cannot land in two places",
            "pass before= to land the new members above that member, or after= to land them below it - not both")),
        ({ Length: > 0 }, _, { Length: > 0 }) or (_, { Length: > 0 }, { Length: > 0 }) => Result.Fail<MemberPlacement?>(Errors.Invalid(
            "position= names a coarse slot and before=/after= names an anchor, so the two describe different insertion points",
            "pass before= or after= to anchor on a member, or position=first, afterFields or last - not both")),
        (null or "", null or "", null or "") => Result.Ok<MemberPlacement?>(null),
        _ => Anchored(before, after, position),
    };
}
