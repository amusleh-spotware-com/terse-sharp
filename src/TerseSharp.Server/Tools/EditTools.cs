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
        [Description("Alias for body.")] string? declaration = null,
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
        var supplied = body is { Length: > 0 } ? body : declaration ?? string.Empty;
        var text = held is null ? supplied : Preferred(supplied, held.Payloads);
        var imports = Kept(usings, held?.Usings);

        return Supplied(workspace, target, text, "body", (loaded, resolved) => SymbolEditService.ReplaceBodyAsync(
                loaded, resolved, text, Options("replace_symbol_body", dryRun, allowErrors, verbose, imports, allowPolicy: allowPolicy), cancellationToken),
                cancellationToken,
                new Carry("replace_symbol_body", [target ?? string.Empty], [text], Usings: imports),
                held?.Root);
    }

    [McpServerTool(Name = "replace_symbol")]
    [Description("Replace a whole member declaration - signature, attributes and doc comment - by symbol id; usings= adds the namespaces it needs in the same compile-gated edit. Several declarations in one call replace the target with all of them - how a member splits into overloads. A type's bodiless header re-heads it. symbolIds= with declarations= edits members across SEVERAL files as ONE compile-gated edit. Replaces one call per member, and is how a signature change lands with the callers it breaks. add= adds the private helpers the declaration calls, placed by addBefore=/addAfter=/addPosition=. A rollback names a retryWith token holding what was rejected.")]
    public Task<string> ReplaceSymbol(
                        [Description("Symbol id of the member.")] string? symbolId = null,
                        [Description("One complete member declaration, or several in sequence.")] string declaration = "",
                        [Description(AddHelp)] string[]? add = null,
                        [Description("Type add= lands in - any workspace type; comma-separated routes each add= entry to its own.")] string? addTo = null,
                        [Description("Diff only, write nothing.")] bool dryRun = false,
                        [Description("Apply even if it introduces compile errors.")] bool allowErrors = false,
                        [Description(PolicyHelp)] bool allowPolicy = false,
                        [Description(VerboseHelp)] bool verbose = false,
                        [Description("Workspace or worktree name.")] string? workspace = null,
                        [Description("Alias for symbolId.")] string? symbol = null,
                        [Description("Symbol ids to replace together, paired positionally with declarations. Beside retryWith= it corrects the held ids.")] string[]? symbolIds = null,
                        [Description("One complete declaration per symbolIds entry, in order, applied as ONE compile-gated edit across their files.")] string[]? declarations = null,
                        [Description(UsingsHelp)] string[]? usings = null,
                        [Description("Apply a declaration whose name differs from its paired symbol. References are NOT rewritten; rename_symbol makes them follow.")] bool rename = false,
                    [Description(FixHelp)] string[]? fix = null,
                    [Description("Beside retryWith=, ADD the pairs you pass to the held batch; an add_member token's members become add=. Refused without a token.")] bool append = false,
                    [Description(RetryHelp)] string? retryWith = null,
                [Description("Member to land the add= helpers ABOVE, by short name or documentation id. Only with add=.")] string? addBefore = null,
                [Description("Member to land them BELOW, addressed as addBefore= is.")] string? addAfter = null,
                [Description("Coarse slot instead of an anchor: first, afterFields or last. Default last.")] string? addPosition = null,
                        CancellationToken cancellationToken = default)
    {
        var placement = Placement(addBefore, addAfter, addPosition, PlacementNames.Added);

        if (Refused(usings, add, placement) is { } refusal)
            return Task.FromResult(refusal);

        var held = Held(retryWith, "replace_symbol");
        var adopted = append && held is null ? Held(retryWith, "add_member") : null;

        if (retryWith is { Length: > 0 } token && held is null && adopted is null)
            return Task.FromResult(Unknown(token, "replace_symbol"));

        if (RejectedFix(fix, retryWith, held?.Payloads.Count ?? 0, held?.Add.Count ?? 0) is { } misfit)
            return Task.FromResult(misfit);

        if (RejectedAppend(append, held ?? adopted, declaration, symbolId ?? symbol) is { } misused)
            return Task.FromResult(misused);

        if (RejectedClash(fix, declaration, declarations, append) is { } clash)
            return Task.FromResult(clash);

        if (adopted is not null)
            return Adopting(workspace, adopted, new AdoptedEdit(symbolIds ?? [], declarations ?? [], add, addTo, usings, placement.Value), new EditFlags(dryRun, allowErrors, verbose, rename, allowPolicy), cancellationToken);

        var imports = Kept(usings, held?.Usings);
        var helpers = Kept(add, held is null ? null : Patched(held.Add, fix, add: true));
        var container = addTo ?? held?.AddTo;

        if (RejectedPlacement(placement.Value, helpers) is { } misplaced)
            return Task.FromResult(misplaced);

        var options = Options("replace_symbol", dryRun, allowErrors, verbose, imports, helpers, container, rename, allowPolicy, placement.Value);

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
            return Batched(workspace, Corrected(symbolIds, held.Targets), Corrected(declarations, Patched(held.Payloads, fix)), options, cancellationToken, held.Root, helpers, container, imports);

        if (held is null && (symbolIds, declarations) is not (null, null))
            return Batched(workspace, symbolIds ?? [], declarations ?? [], options, cancellationToken, null, helpers, container, imports);

        var target = symbolId ?? symbol ?? (held is null ? null : Slot(held.Targets, 0));
        var text = held is null ? declaration : Preferred(declaration, Patched(held.Payloads, fix));

        return Supplied(workspace, target, text, "declaration", (loaded, resolved) => SymbolEditService.ReplaceDeclarationAsync(
            loaded, resolved, text, options, cancellationToken),
            cancellationToken,
            new Carry("replace_symbol", [target ?? string.Empty], [text], helpers, container, imports),
            held?.Root);
    }
    [McpServerTool(Name = "add_member")]
    [Description("Add one or more members to a type, addressed by the type's symbol id, with usings= adding the namespaces they need in the same compile-gated edit - or, with path=, add namespace-level types to an existing .cs file. typeSymbolIds= paired with declarations= adds a member to SEVERAL types as ONE compile-gated edit - an interface member and every implementation, with no uncompilable step. Replaces one call per implementation. before= and after= place the new members above or below a member the type declares, and position=first|afterFields|last picks a coarse slot; the default appends at the end, above a trailing #region the type closes. An enum symbol id takes enum members. Several declarations in one call land as one edit, so members that reference each other need no dependency ordering. A rollback names a retryWith token holding the rejected declarations, so the retry costs a token, not the payload; an unresolved typeSymbolId too. A successful edit answers in one line per changed file; pass verbose=true for the diff.")]
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
                [Description("Alias for declaration; entries join into the one edit - or, beside typeSymbolIds, one per id in order.")] string[]? declarations = null,
                [Description("Type ids to add to together, paired positionally with declarations and applied as ONE compile-gated edit across their files. Not with declaration= or path=.")] string[]? typeSymbolIds = null,
                [Description("Member of this type to land the new members ABOVE, by short name or documentation id. Not with after= or position=, and not held by a retryWith token.")] string? before = null,
                [Description("Member of this type to land the new members BELOW, addressed as before= is. Not with before= or position=.")] string? after = null,
                [Description("Coarse slot instead of an anchor: first, afterFields (after the last field) or last. Default last. Not with before= or after=.")] string? position = null,
                CancellationToken cancellationToken = default)
    {
        if (Malformed(usings, declarations, typeSymbolIds, declaration, path) is { } refusal)
            return Task.FromResult(refusal);

        var placement = Placement(before, after, position);

        if (!placement.IsOk)
            return Task.FromResult(placement.Error!.Render());

        var held = Held(retryWith, "add_member");

        if (retryWith is { Length: > 0 } token && held is null)
            return Task.FromResult(Unknown(token, "add_member"));

        var imports = Kept(usings, held?.Usings);
        var options = Options("add_member", dryRun, allowErrors, verbose, imports, allowPolicy: allowPolicy, placement: placement.Value);
        var ids = PairedIds(typeSymbolIds, held);

        if (ids.Length > 0)
            return Paired(workspace, ids, PairedDeclarations(declarations, held), options, cancellationToken, held?.Root, imports);

        var sent = Merged(declaration, declarations);

        return Added(
            workspace,
            typeSymbolId ?? symbol ?? symbolId ?? (held is null ? null : Slot(held.Targets, 0)),
            path ?? (held is null ? null : Slot(held.Targets, 1)),
            held is null ? sent : Preferred(sent, held.Payloads),
            options,
            cancellationToken,
            held?.Root,
            imports);
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
    [Description("Safe-delete a member, an enum member or a type. Refuses while references exist unless force is set, and lists them; allowErrors=true applies it anyway. symbolIds= removes up to 20 across files in ONE compile-gated edit. Replaces one call per member, and a reference sitting inside another removed member does not block it. A successful delete answers in one line per changed file; pass verbose=true for the diff.")]
    public Task<string> DeleteSymbol(
        [Description("Symbol id to delete.")] string? symbolId = null,
        [Description("Delete even when references exist. Default false.")] bool force = false,
        [Description("Diff only, write nothing.")] bool dryRun = false,
        [Description("Apply even if it introduces compile errors. Default false.")] bool allowErrors = false,
        [Description(PolicyHelp)] bool allowPolicy = false,
        [Description(VerboseHelp)] bool verbose = false,
        [Description("Workspace or worktree name.")] string? workspace = null,
        [Description("Alias for symbolId.")] string? symbol = null,
        [Description("Symbol ids deleted together as ONE compile-gated edit, at most 20; a reference inside another listed member does not count. Not with symbolId=.")] string[]? symbolIds = null,
        CancellationToken cancellationToken = default)
    {
        var options = Options("delete_symbol", dryRun, allowErrors, verbose, allowPolicy: allowPolicy);

        return symbolIds is { Length: > 0 }
            ? DeletedMany(workspace, symbolId ?? symbol, symbolIds, force, options, cancellationToken)
            : Guarded(workspace, symbolId ?? symbol, (loaded, resolved) => SymbolEditService.DeleteAsync(loaded, resolved, force, options, cancellationToken), cancellationToken);
    }

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

    private Task<string> DeletedMany(
        string? workspace,
        string? single,
        string[] symbolIds,
        bool force,
        EditOptions options,
        CancellationToken cancellationToken)
    {
        var rejection = context.RejectWrite() ?? (single is { Length: > 0 } ? BothIds() : null);

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(
                workspace,
                null,
                async loaded => Carried(await SymbolEditService.DeleteManyAsync(loaded, symbolIds, force, options, cancellationToken).ConfigureAwait(false), default, loaded.Root),
                cancellationToken: cancellationToken);
    }

    private static string BothIds() => Errors.Invalid(
        "symbolId= and symbolIds= were both passed",
        "put every id in symbolIds=, or pass symbolId= alone").Render();

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

    private const string RetryHelp = "Token from a previous rejection, e.g. r3, printed alone on its LAST line. It holds the rejected declaration with its add= and usings=, so the retry names the token instead of re-sending them; anything passed again - symbolId, the corrected text, usings= - OUTRANKS the held value; allowErrors=true may ride beside it.";

    internal readonly record struct Carry(
        string? Tool,
        string[]? Targets,
        string[]? Payloads,
        string[]? Add = null,
        string? AddTo = null,
        string[]? Usings = null);

    internal static string Carried(Result<string> result, Carry carry, string root) =>
        result.IsOk ? result.Value! : Rejected(result.Error!, carry, root);

    internal static string? Elsewhere(string? held, string root) => held is { Length: > 0 } origin && !PathBoundary.SameFile(origin, root)
        ? Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"the held rejection belongs to {origin}, and this call resolved to {root}"),
            "replay the token against the workspace it was rejected in, or re-send the declaration to edit this one").Render()
        : null;

    internal static string Unknown(string token, string tool) => Errors.Invalid(
        RejectedEdits.Recall(token) is { } issued
            ? string.Create(CultureInfo.InvariantCulture, $"retryWith={token} was issued by {issued.Tool}, not by {tool}")
            : string.Create(CultureInfo.InvariantCulture, $"retryWith={token} names no held rejection of {tool}"),
        RejectedEdits.Recall(token) is { } held
            ? string.Create(CultureInfo.InvariantCulture, $"replay it with {held.Tool}, which is the tool that can apply what it holds")
            : "re-send the text; the server holds only the last 8 rejected edits of this process").Render();

    internal static RejectedEdit? Held(string? retryWith, string tool) =>
        retryWith is { Length: > 0 } token && RejectedEdits.Recall(token) is { } edit
        && string.Equals(edit.Tool, tool, StringComparison.Ordinal)
            ? edit
            : null;

    private static string First(IReadOnlyList<string> values, string fallback) =>
        values is [var only, ..] ? only : fallback;

    private static string Preferred(string supplied, IReadOnlyList<string> held) =>
        supplied is { Length: > 0 } ? supplied : First(held, supplied);

    private static string? Slot(IReadOnlyList<string> targets, int index) =>
        index < targets.Count && targets[index] is { Length: > 0 } value ? value : null;

    private const string UsingsHelp = "Namespaces this declaration needs, added in the SAME compile-gated edit - the one-call answer to a CS0246 rollback. One already present is ignored, a non-namespace entry is refused by name, and usings=[] on a retryWith replay DROPS what the token holds.";

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

    private const string AddHelp = "New members added to the replaced member's type in the SAME compile-gated edit - the answer to the callee-after-caller rollback. They land at the END unless addBefore=/addAfter=/addPosition= places them; addTo= sends them to any other type in the workspace, so an interface member lands beside its implementations.";

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
                ? error.Render() + "\n" + RetryNote.For(tool, error.Code, carry.Payloads?.Length ?? 0) + "\nretryWith=" + RejectedEdits.Remember(
                    root, tool, carry.Targets ?? [], carry.Payloads ?? [], carry.Add, carry.AddTo, carry.Usings)
                : error.Render();

    private static bool Holdable(TerseErrorCode code) =>
            code is TerseErrorCode.CompileRegression or TerseErrorCode.PolicyViolation or TerseErrorCode.SymbolNotFound or TerseErrorCode.AmbiguousSymbol or TerseErrorCode.InvalidArgument;

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

    private const string FixHelp = "Correct held entries on a retryWith replay instead of re-sending the batch. Each entry is '<index>=<declaration>', or 'add:<index>=...' for a held helper; index is the 0-based position the rejection printed.";

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

    private static Result<MemberPosition> PlacementSlot(string? position, PlacementNames names)
    {
        if (position is null or "" || string.Equals(position, "last", StringComparison.OrdinalIgnoreCase))
            return Result.Ok(MemberPosition.Last);

        if (string.Equals(position, "first", StringComparison.OrdinalIgnoreCase))
            return Result.Ok(MemberPosition.First);

        return string.Equals(position, "afterFields", StringComparison.OrdinalIgnoreCase)
            ? Result.Ok(MemberPosition.AfterFields)
            : Result.Fail<MemberPosition>(Errors.Invalid(
                names.Position + "=" + position + " is not a slot this tool declares",
                string.Create(CultureInfo.InvariantCulture, $"pass {names.Position}=first, {names.Position}=afterFields or {names.Position}=last, or {names.Before}=/{names.After}= to anchor on a member this type declares")));
    }

    private static Result<MemberPlacement?> Anchored(string? before, string? after, string? position, PlacementNames names)
    {
        var slot = PlacementSlot(position, names);

        return slot.IsOk
            ? Result.Ok<MemberPlacement?>(new MemberPlacement(before, after, slot.Value, names.Before, names.After))
            : Result.Fail<MemberPlacement?>(slot.Error!);
    }

    private static Result<MemberPlacement?> Placement(string? before, string? after, string? position, PlacementNames names) => (before, after, position) switch
    {
        ({ Length: > 0 }, { Length: > 0 }, _) => Result.Fail<MemberPlacement?>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"{names.Before}= and {names.After}= both name an anchor, and one member cannot land in two places"),
            string.Create(CultureInfo.InvariantCulture, $"pass {names.Before}= to land the new members above that member, or {names.After}= to land them below it - not both"))),
        ({ Length: > 0 }, _, { Length: > 0 }) or (_, { Length: > 0 }, { Length: > 0 }) => Result.Fail<MemberPlacement?>(Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"{names.Position}= names a coarse slot and {names.Before}=/{names.After}= names an anchor, so the two describe different insertion points"),
            string.Create(CultureInfo.InvariantCulture, $"pass {names.Before}= or {names.After}= to anchor on a member, or {names.Position}=first, afterFields or last - not both"))),
        (null or "", null or "", null or "") => Result.Ok<MemberPlacement?>(null),
        _ => Anchored(before, after, position, names),
    };

    private Task<string> Paired(
            string? workspace,
            string[] typeSymbolIds,
            string[] declarations,
            EditOptions options,
            CancellationToken cancellationToken,
            string? heldRoot = null,
            string[]? usings = null)
    {
        var rejection = context.RejectWrite();
        var carry = new Carry("add_member", typeSymbolIds, declarations, Usings: usings);

        return rejection is not null
            ? Task.FromResult(rejection)
            : context.WithWorkspaceAsync(
                workspace,
                null,
                async loaded => Elsewhere(heldRoot, loaded.Root) ?? Carried(await SymbolEditService.AddMembersAsync(
                    loaded, typeSymbolIds, declarations, options, cancellationToken).ConfigureAwait(false), carry, loaded.Root),
                cancellationToken: cancellationToken);
    }

    private static string? Malformed(string[]? usings, string[]? declarations, string[]? typeSymbolIds, string declaration, string? path) =>
            RejectedUsings(usings)
            ?? RejectedDeclarations(declarations)
            ?? RejectedIds(typeSymbolIds)
            ?? PairedRefusal(typeSymbolIds, declaration, path);

    private static string? RejectedIds(string[]? typeSymbolIds)
    {
        foreach (var id in typeSymbolIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(id))
                return Errors.Blank("typeSymbolIds").Render();
        }

        return null;
    }

    private static string? PairedRefusal(string[]? typeSymbolIds, string declaration, string? path)
    {
        if (typeSymbolIds is not { Length: > 0 })
            return null;

        if (declaration is { Length: > 0 })
        {
            return Errors.Invalid(
                "typeSymbolIds pairs each id with the declarations entry at the same index, and a singular declaration= was passed beside it - it would be silently dropped",
                "send every member as declarations=[...], one per typeSymbolIds entry, in the same order").Render();
        }

        return path is { Length: > 0 }
            ? Errors.Invalid(
                "typeSymbolIds addresses types and path= appends namespace-level types to a file, so the two name different containers",
                "pass typeSymbolIds with declarations to add members to several types, or path to append types to one file - not both").Render()
            : null;
    }

    private static string[] PairedIds(string[]? typeSymbolIds, RejectedEdit? held) =>
            typeSymbolIds ?? (held is { Payloads.Count: > 1 } ? [.. held.Targets] : []);

    private static string[] PairedDeclarations(string[]? declarations, RejectedEdit? held) =>
            declarations ?? (held is { Payloads.Count: > 1 } ? [.. held.Payloads] : []);

    internal readonly record struct PlacementNames(string Before, string After, string Position)
    {
        public static readonly PlacementNames Member = new("before", "after", "position");

        public static readonly PlacementNames Added = new("addBefore", "addAfter", "addPosition");
    }

    private static Result<MemberPlacement?> Placement(string? before, string? after, string? position) =>
            Placement(before, after, position, PlacementNames.Member);

    private static string? RejectedPlacement(MemberPlacement? placement, string[]? helpers) =>
            placement is not null && helpers is null or { Length: 0 }
                ? Errors.Invalid(
                    "addBefore=, addAfter= and addPosition= place the members add= appends, and no add= was passed",
                    "pass the helpers as add=[...], or place members on their own with add_member before=/after=/position=").Render()
                : null;

    private static string? Refused(string[]? usings, string[]? add, Result<MemberPlacement?> placement) =>
            RejectedUsings(usings) ?? RejectedAdd(add) ?? (placement.IsOk ? null : placement.Error!.Render());

    private static string? RejectedAppend(bool append, RejectedEdit? held, string declaration, string? singular)
    {
        if (!append)
            return null;

        if (held is null)
        {
            return Errors.Invalid(
                "'append' adds to the batch a retryWith token holds, and no token was passed",
                "pass the retryWith token the rejection printed beside it, or send the whole batch as symbolIds= and declarations=").Render();
        }

        return declaration is { Length: > 0 } || singular is { Length: > 0 }
            ? Errors.Invalid(
                "'append' adds symbolIds= and declarations= pairs to the held batch, and a singular symbolId= or declaration= was passed beside it - it would be silently dropped",
                "send the pair you are adding as symbolIds=[...] and declarations=[...], or drop append= to correct the held batch instead").Render()
            : null;
    }

    private static string? RejectedClash(string[]? fix, string declaration, string[]? declarations, bool append) =>
        fix is { Length: > 0 } && fix.Any(entry => Correction(entry) is { Add: false }) && Replaces(declaration, declarations, append)
            ? Errors.Invalid(
                "fix= corrects held declarations by index and declaration=/declarations= replaces them, so one of the two would be silently dropped",
                "pass fix= alone to correct the held entries it names, or the corrected declaration(s) alone").Render()
            : null;

    private static bool Replaces(string declaration, string[]? declarations, bool append) =>
        declaration is { Length: > 0 } || (!append && declarations is { Length: > 0 });

    private Task<string> Adopting(string? workspace, RejectedEdit adopted, AdoptedEdit sent, EditFlags flags, CancellationToken cancellationToken)
    {
        if (AdoptedContainers(adopted) is not { } containers)
        {
            return Task.FromResult(Errors.Invalid(
                "the add_member token holds namespace-level types added to a file, which have no containing type to land in as add=",
                "replay it with add_member, which is the tool that can apply what it holds").Render());
        }

        string[] helpers = [.. adopted.Payloads, .. sent.Add ?? []];
        var container = sent.AddTo ?? Routed(containers, sent.Add?.Length ?? 0);
        var imports = Kept(sent.Usings, adopted.Usings);
        var options = Options("replace_symbol", flags.DryRun, flags.AllowErrors, flags.Verbose, imports, helpers, container, flags.Rename, flags.AllowPolicy, sent.Placement);

        return Batched(workspace, sent.SymbolIds, sent.Declarations, options, cancellationToken, adopted.Root, helpers, container, imports);
    }

    private static string Routed(string containers, int extra) => extra is 0
        ? containers
        : string.Join(',', [containers, .. Enumerable.Repeat(containers[(containers.LastIndexOf(',') + 1)..], extra)]);

    private static string? AdoptedContainers(RejectedEdit adopted) => adopted switch
    {
        { Targets: [{ Length: > 0 } type, { Length: 0 }], Payloads.Count: 1 } => TypeLeaf(type),
        { Targets.Count: > 0 } when adopted.Targets.Count == adopted.Payloads.Count && adopted.Targets.All(target => target.Length > 0) =>
            string.Join(',', adopted.Targets.Select(TypeLeaf)),
        _ => null,
    };

    private static string TypeLeaf(string type)
    {
        var name = type.AsSpan();

        if (name.StartsWith("T:", StringComparison.Ordinal))
            name = name[2..];

        var generic = name.IndexOfAny('`', '<');

        if (generic >= 0)
            name = name[..generic];

        return new string(name[(name.LastIndexOf('.') + 1)..]);
    }

    private readonly record struct AdoptedEdit(string[] SymbolIds, string[] Declarations, string[]? Add, string? AddTo, string[]? Usings, MemberPlacement? Placement);

    private readonly record struct EditFlags(bool DryRun, bool AllowErrors, bool Verbose, bool Rename, bool AllowPolicy);
}
