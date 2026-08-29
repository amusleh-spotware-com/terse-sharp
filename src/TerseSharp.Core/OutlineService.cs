using System.Buffers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public static class OutlineService
{
    public static async Task<Result<string>> FileAsync(
        LoadedWorkspace workspace,
        string path,
        bool signatures,
        string ids,
        bool usings,
        CancellationToken cancellationToken,
        bool parameterNames = true,
        string? contains = null,
        bool all = false)
    {
        var document = DocumentLookup.Find(workspace, path);

        if (document is null)
            return await FromDiskAsync(workspace, path, signatures, ids, usings, parameterNames, contains, all, cancellationToken).ConfigureAwait(false);

        if (Rejected(ids) is { } refusal)
            return refusal;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root is null || model is null)
            return Result.Fail<string>(Errors.DocumentNotFound(path));

        var declarations = Declarations(root);
        var format = new OutlineFormat(signatures, ids, usings ? Usings(root) : null, parameterNames, contains, All: all);

        return Result.Ok(declarations.Length is 0 && TopLevel(root) is { } note
            ? note
            : Render("get_file_outline", path, declarations, model, format));
    }

    public static async Task<Result<string>> TypeAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        bool signatures,
        string ids,
        CancellationToken cancellationToken,
        bool parameterNames = true,
        string? contains = null,
        bool all = false)
    {
        if (Rejected(ids) is { } refusal)
            return refusal;

        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();

        if (reference is null)
        {
            return symbol is INamedTypeSymbol metadata && MetadataSearch.IsMetadata(symbol)
                ? Result.Ok(Metadata(metadata, parameterNames, contains))
                : Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []));
        }

        var node = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
        var document = workspace.Solution.GetDocument(node.SyntaxTree);
        var model = document is null
            ? null
            : await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        return model is null || node is not MemberDeclarationSyntax declaration || !IsTypeDeclaration(declaration)
            ? Result.Fail<string>(Errors.SymbolNotFound(SymbolId.From(symbol).Value, []))
            : Result.Ok(Render("get_type_outline", symbol.Name, [declaration], model, new OutlineFormat(signatures, ids, null, parameterNames, contains, All: all)));
    }

    private static Result<string>? Rejected(string ids) =>
        ids is "short" or "full"
            ? null
            : Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"ids='{ids}' is not a known value"),
                "pass ids=short for names every tool accepts, or ids=full for documentation ids"));

    private static MemberDeclarationSyntax[] Declarations(SyntaxNode root) =>
        [.. root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsTypeDeclaration)];

    private static bool IsTypeDeclaration(MemberDeclarationSyntax member) =>
        member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax;

    private static string Render(
        string tool,
        string argument,
        MemberDeclarationSyntax[] declarations,
        SemanticModel model,
        OutlineFormat format)
    {
        var response = new ResponseBuilder(tool, argument);

        response.Summary(declarations.Length, declarations.Length, "types");

        if (format.Usings is { Length: > 0 } usings)
            response.Note("usings: " + usings);

        var references = new List<string>();
        var members = 0;
        var omitted = 0;

        foreach (var declaration in declarations)
        {
            var tally = AppendType(response, declaration, model, format, references);

            members += tally.Total;
            omitted += tally.Omitted;
        }

        if (omitted is 0 && format.Contains is not { Length: > 0 } && members >= WideOutline)
            response.Note(string.Create(CultureInfo.InvariantCulture, $"{members} members - narrow with contains="));
        else if (format.Batchable && ArgumentLine.Ids(references) is { } batch)
            response.Note(batch);

        return response.ToString();
    }

    private const int WideOutline = 25;

    private static MemberTally AppendType(
        ResponseBuilder response,
        MemberDeclarationSyntax declaration,
        SemanticModel model,
        OutlineFormat format,
        List<string> references)
    {
        var symbol = model.GetDeclaredSymbol(declaration);

        if (symbol is null)
            return default;

        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"{Reference(symbol, format.Ids, Never)}  {SymbolFormat.Kind(symbol)} {SymbolFormat.Accessibility(symbol)}  :{PositionFormat.LineRange(declaration)}"));

        var overloaded = Overloaded(declaration, model);
        var filtered = format.Contains is { Length: > 0 };
        var capped = !filtered && !format.All;
        var total = 0;
        var shown = 0;
        var omitted = 0;

        foreach (var member in Members(declaration))
        {
            var counts = !IsTypeDeclaration(member);

            total += counts ? 1 : 0;

            if (capped && shown >= MaxListedMembers)
            {
                omitted += counts ? 1 : 0;
                continue;
            }

            if (AppendMember(response, member, model, format, overloaded) is { } reference)
            {
                references.Add(reference);
                shown++;
            }
        }

        if (filtered && shown < total)
            response.Line(string.Create(CultureInfo.InvariantCulture, $"  {shown} of {total} members"));
        else if (omitted > 0)
            response.Line(string.Create(CultureInfo.InvariantCulture, $"  {total - omitted} of {total} members - contains= or all=true"));

        return new MemberTally(total, omitted);
    }

    private static readonly IReadOnlySet<string> Never = new HashSet<string>(StringComparer.Ordinal);

    private static IEnumerable<MemberDeclarationSyntax> Members(MemberDeclarationSyntax declaration) => declaration switch
    {
        TypeDeclarationSyntax type => type.Members,
        EnumDeclarationSyntax enumeration => enumeration.Members,
        _ => [],
    };

    private static string? AppendMember(
            ResponseBuilder response,
            MemberDeclarationSyntax member,
            SemanticModel model,
            OutlineFormat format,
            IReadOnlySet<string> overloaded)
    {
        var symbol = model.GetDeclaredSymbol(member);

        if (symbol is null || IsTypeDeclaration(member))
            return null;

        if (format.Contains is { Length: > 0 } filter && !symbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return null;

        var reference = Reference(symbol, format.Ids, overloaded);

        response.Line(string.Create(
            CultureInfo.InvariantCulture,
            $"  {reference}  {Signature(symbol, format)} :{PositionFormat.LineRange(member)}"));

        return reference;
    }

    private static string Signature(ISymbol symbol, OutlineFormat format) => format.Signatures
        ? string.Create(CultureInfo.InvariantCulture, $"{SymbolFormat.Accessibility(symbol)} {SymbolFormat.Describe(symbol, format.ParameterNames)} ")
        : string.Create(CultureInfo.InvariantCulture, $"{SymbolFormat.Accessibility(symbol)} ");

    private static string Reference(ISymbol symbol, string ids, IReadOnlySet<string> overloaded)
    {
        if (string.Equals(ids, "full", StringComparison.OrdinalIgnoreCase) || !SymbolReference.RoundTrips(symbol))
            return SymbolId.From(symbol).Value;

        return overloaded.Contains(symbol.Name) ? SymbolReference.Brief(symbol) : SymbolReference.Simple(symbol);
    }

    private static HashSet<string> Overloaded(MemberDeclarationSyntax declaration, SemanticModel model)
    {
        var repeated = new HashSet<string>(StringComparer.Ordinal);

        if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type)
            return repeated;

        foreach (var member in type.GetMembers())
        {
            if (type.GetMembers(member.Name).Length > 1)
                repeated.Add(member.Name);
        }

        return repeated;
    }
    private static string Located(GlobalStatementSyntax statement)
    {
        var span = statement.SyntaxTree.GetLineSpan(statement.Span);
        var text = statement.Statement.ToString().ReplaceLineEndings(" ").Trim();

        return string.Create(
            CultureInfo.InvariantCulture,
            $":{span.StartLinePosition.Line + 1}-{span.EndLinePosition.Line + 1}  {(text.Length <= 100 ? text : text[..100] + "...")}");
    }

    private static string? TopLevel(SyntaxNode root)
    {
        var statements = root.ChildNodes().OfType<GlobalStatementSyntax>().ToArray();

        if (statements.Length is 0)
            return null;

        var lines = root.SyntaxTree.GetText().Lines.Count;
        var response = new ResponseBuilder("get_file_outline", "top-level statements");

        response.Summary(statements.Length, statements.Length, "statements");
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"this file declares no type: it is {lines} lines of top-level statements - read it with read_text"));

        foreach (var statement in statements)
            response.Line(Located(statement));

        return response.ToString();
    }

    private static string Usings(SyntaxNode root) => string.Join(
            ", ",
            root.DescendantNodes().OfType<UsingDirectiveSyntax>().Select(Describe));

    private static string Describe(UsingDirectiveSyntax directive) => string.Concat(
            directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) ? "global " : string.Empty,
            directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? "static " : string.Empty,
            directive.Alias is { } alias ? alias.Name.Identifier.ValueText + " = " : string.Empty,
            directive.NamespaceOrType?.ToString() ?? string.Empty);

    private readonly record struct OutlineFormat(bool Signatures, string Ids, string? Usings, bool ParameterNames = true, string? Contains = null, bool Batchable = true, bool All = false);

    private const string TextSteer =
        "NOTE this is the outline, not the file text - a whole-file .cs read costs about three times as much and is almost never the question."
        + " get_symbol_source symbolId=<an id above> for one member, get_symbol_source symbolIds=[...] for several, read_text verbose=true for the raw text.";

    public static async Task<Result<string>> SteeredAsync(
            LoadedWorkspace workspace,
            string path,
            long? bytes,
            int? characters,
            CancellationToken cancellationToken)
    {
        var outline = await FileAsync(workspace, path, signatures: true, "short", usings: false, cancellationToken).ConfigureAwait(false);

        if (!outline.IsOk)
            return outline;

        return Result.Ok(outline.Value! + "\n" + TextSteer
            + (bytes is { } length ? "\n" + FileService.Sized(length) : string.Empty)
            + (characters is { } count ? "\n" + FileService.Counted(count) : string.Empty));
    }

    public static async Task<Result<string>> OrTextAsync(
            LoadedWorkspace workspace,
            string path,
            FileService.ReadRequest request,
            CancellationToken cancellationToken)
    {
        if (DocumentLookup.Find(workspace, path) is not { } document)
            return await FileService.ReadTextAsync(workspace, path, request, cancellationToken).ConfigureAwait(false);

        var characters = request.Tokens
            ? await FileService.CharacterLengthAsync(document.FilePath, cancellationToken).ConfigureAwait(false)
                ?? (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).Length
            : (int?)null;

        return await SteeredAsync(
            workspace,
            path,
            request.Bytes ? FileService.ByteLength(document.FilePath) : null,
            characters,
            cancellationToken).ConfigureAwait(false);
    }

    public static Result<string> FromText(
        string path,
        string text,
        bool signatures,
        string ids,
        bool usings,
        bool parameterNames = true,
        string? contains = null,
        bool all = false)
    {
        if (Rejected(ids) is { } refusal)
            return refusal;

        var tree = CSharpSyntaxTree.ParseText(text, path: path);
        var root = tree.GetRoot();
        var model = CSharpCompilation.Create("terse-ref", [tree]).GetSemanticModel(tree);
        var declarations = Declarations(root);
        var format = new OutlineFormat(signatures, ids, usings ? Usings(root) : null, parameterNames, contains, Batchable: false, All: all);

        return Result.Ok(declarations.Length is 0 && TopLevel(root) is { } note
            ? note
            : Render("get_file_outline", path, declarations, model, format));
    }

    private const string ParsedFromText = "HEURISTIC parsed from the file's own text - it is not a document of this solution, so the members come from syntax and not from the compilation";

    private static async Task<Result<string>> FromDiskAsync(
        LoadedWorkspace workspace,
        string path,
        bool signatures,
        string ids,
        bool usings,
        bool parameterNames,
        string? contains,
        bool all,
        CancellationToken cancellationToken)
    {
        var resolved = PathGuard.Resolve(workspace.Root, path);

        if (!resolved.IsOk)
            return Result.Fail<string>(resolved.Error!);

        if (!SourceFile.IsCSharp(path) || !File.Exists(resolved.Value!))
            return Result.Fail<string>(await MissingDocument.ReadAsync(workspace, path, cancellationToken).ConfigureAwait(false));

        var text = await File.ReadAllTextAsync(resolved.Value!, cancellationToken).ConfigureAwait(false);
        var outline = FromText(path, text, signatures, ids, usings, parameterNames, contains, all);

        return outline.IsOk ? Result.Ok(outline.Value! + "\n" + ParsedFromText) : outline;
    }

    public static string? BatchFromText(string path, string text)
    {
        var tree = CSharpSyntaxTree.ParseText(text, path: path);
        var root = tree.GetRoot();
        var model = CSharpCompilation.Create("terse-batch", [tree]).GetSemanticModel(tree);
        var references = new List<string>();

        foreach (var declaration in Declarations(root))
        {
            var overloaded = Overloaded(declaration, model);

            foreach (var member in Members(declaration))
            {
                if (!IsTypeDeclaration(member) && model.GetDeclaredSymbol(member) is { } symbol)
                    references.Add(Reference(symbol, "short", overloaded));
            }
        }

        return ArgumentLine.Ids(references);
    }

    private const int MetadataMembers = 100;

    private static string Metadata(INamedTypeSymbol type, bool parameterNames, string? contains)
    {
        var members = Public(type, contains);
        var response = new ResponseBuilder("get_type_outline", type.Name);
        var shown = members.Capped(MetadataMembers).ToArray();

        response.Summary(shown.Length, members.Count, "members", "contains=");
        response.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"{SymbolId.From(type).Value}  {MetadataSearch.Origin(type)}  metadata - no source, so no line ranges"));

        foreach (var member in shown)
        {
            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{SymbolFormat.Kind(member)} {SymbolFormat.Accessibility(member)} {SymbolFormat.Describe(member, parameterNames)}"));
        }

        return response.ToString();
    }

    private static List<ISymbol> Public(INamedTypeSymbol type, string? contains)
    {
        var members = new List<ISymbol>();

        foreach (var member in type.GetMembers())
        {
            if (member.DeclaredAccessibility is Accessibility.Public && Listable(member) && Wanted(member, contains))
                members.Add(member);
        }

        return members;
    }

    private static bool Wanted(ISymbol member, string? contains) =>
        contains is not { Length: > 0 } text || member.Name.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool Listable(ISymbol member) =>
        !member.IsImplicitlyDeclared && member is not IMethodSymbol { AssociatedSymbol: not null };

    private const int MaxListedMembers = 40;

    private readonly record struct MemberTally(int Total, int Omitted);

    public readonly record struct TypeOutlineFormat(bool Signatures, string Ids, bool ParameterNames, string? Contains, bool All);

    public static async Task<string> TypesAsync(
        LoadedWorkspace workspace,
        IReadOnlyList<string> symbolIds,
        TypeOutlineFormat format,
        string? path,
        CancellationToken cancellationToken)
    {
        var response = new ResponseBuilder("get_type_outline", string.Join(", ", symbolIds));
        var answered = 0;

        foreach (var symbolId in symbolIds)
        {
            var outline = await OneTypeAsync(workspace, symbolId, format, path, cancellationToken).ConfigureAwait(false);

            if (Resolved(outline))
                answered++;

            response.Note(outline);
        }

        return response.Answered(answered, symbolIds.Count, "types").ToString();
    }

    private static async Task<string> OneTypeAsync(
        LoadedWorkspace workspace,
        string symbolId,
        TypeOutlineFormat format,
        string? path,
        CancellationToken cancellationToken)
    {
        var resolved = await SymbolLookup.ResolveAsync(workspace, symbolId, path, cancellationToken, referenced: true).ConfigureAwait(false);

        if (!resolved.IsOk)
            return "NOT_RESOLVED " + symbolId + "  " + resolved.Error!.Message;

        var outline = await TypeAsync(
            workspace,
            resolved.Value!,
            format.Signatures,
            format.Ids,
            cancellationToken,
            format.ParameterNames,
            format.Contains,
            format.All).ConfigureAwait(false);

        return outline.IsOk ? outline.Value!.TrimEnd('\n') : outline.Error!.Render();
    }

    private static bool Resolved(string outline) =>
        !outline.StartsWith("NOT_RESOLVED", StringComparison.Ordinal) && !outline.StartsWith("ERROR", StringComparison.Ordinal);
}
