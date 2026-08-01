using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace TerseSharp.Core;

public sealed record ResxUsage(string Relative, int Line, string Form, Confidence Confidence);

public static class ResxUsageService
{
    private static readonly string[] Extensions = [".cs", ".xaml", ".axaml", ".paml", ".cshtml", ".razor"];

    private static readonly Regex Composed = new(
        @"(GetString\s*\(\s*[^""\s)]|GetString\s*\([^)]*\+|ocalizer\s*\[\s*[^""\]]|ocalizer\s*\[[^\]]*\+)",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    public static async Task<Result<string>> UsagesAsync(
        LoadedWorkspace workspace,
        string key,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (key is not { Length: > 0 })
        {
            return Result.Fail<string>(Errors.Blank("key"));
        }

        var all = await AllAsync(workspace, key, cancellationToken).ConfigureAwait(false);

        return Result.Ok(Render(key, all, ComposedLookups(workspace.Root), maxResults));
    }

    public static IReadOnlyList<ResxUsage> Textual(string root, string key) =>
    [
        .. WorkspaceFiles
            .Enumerate(root, IsScannable)
            .SelectMany(file => Scan(root, file, key)),
    ];

    public static int ComposedLookups(string root) => WorkspaceFiles
        .Enumerate(root, IsScannable)
        .Sum(file => Lines(file).Count(line => Composed.IsMatch(line)));

    public static bool IsScannable(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
        && !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ResxUsage> Scan(string root, string file, string key)
    {
        var quoted = string.Create(CultureInfo.InvariantCulture, $"\"{key}\"");
        var member = MemberPattern(key);
        var relative = PositionFormat.Relative(root, file);
        var line = 0;

        foreach (var text in Lines(file))
        {
            line++;

            if (text.Contains(quoted, StringComparison.Ordinal) || member.IsMatch(text))
                yield return new ResxUsage(relative, line, Form(text, quoted), Confidence.Heuristic);
        }
    }

    private static Regex MemberPattern(string key) => new(
        @"\b[A-Za-z_]\w*\s*\.\s*" + Regex.Escape(key) + @"\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    private static string Form(string line, string quoted) => line switch
    {
        _ when line.Contains("x:Uid=", StringComparison.Ordinal) => "x:Uid",
        _ when line.Contains("GetString", StringComparison.Ordinal) => "GetString",
        _ when line.Contains("ocalizer[", StringComparison.Ordinal) => "localizer[]",
        _ when line.Contains(quoted, StringComparison.Ordinal) => "literal",
        _ => "member",
    };

    private static IEnumerable<string> Lines(string file)
    {
        try
        {
            return File.ReadLines(file);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<ResxUsage>> ExactAsync(
        LoadedWorkspace workspace,
        string key,
        CancellationToken cancellationToken)
    {
        var usages = new List<ResxUsage>();

        foreach (var symbol in await MembersAsync(workspace, key, cancellationToken).ConfigureAwait(false))
            usages.AddRange(await ReferencesAsync(workspace, symbol, cancellationToken).ConfigureAwait(false));

        return usages;
    }

    private static async Task<IReadOnlyList<ISymbol>> MembersAsync(
        LoadedWorkspace workspace,
        string key,
        CancellationToken cancellationToken)
    {
        var found = new List<ISymbol>();

        foreach (var project in workspace.Solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);

            found.AddRange(Members(compilation, key, cancellationToken));
        }

        return found;
    }

    private static IEnumerable<ISymbol> Members(Compilation? compilation, string key, CancellationToken cancellationToken) =>
        compilation is null
            ? []
            : compilation
                .GetSymbolsWithName(name => string.Equals(name, key, StringComparison.Ordinal), SymbolFilter.Member, cancellationToken)
                .Where(symbol => symbol is IPropertySymbol or IFieldSymbol);

    private static async Task<IEnumerable<ResxUsage>> ReferencesAsync(
        LoadedWorkspace workspace,
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        var references = await SymbolFinder
            .FindReferencesAsync(symbol, workspace.Solution, cancellationToken)
            .ConfigureAwait(false);

        return references
            .SelectMany(reference => reference.Locations)
            .Where(location => location.Location.IsInSource)
            .Select(location => Usage(workspace.Root, symbol, location));
    }

    private static ResxUsage Usage(string root, ISymbol symbol, ReferenceLocation location)
    {
        var span = location.Location.GetLineSpan();

        return new ResxUsage(
            PositionFormat.Relative(root, span.Path),
            span.StartLinePosition.Line + 1,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{TestScope.Of(location.Document.Project)}  {symbol.ContainingType?.Name}.{symbol.Name}{Container(location)}"),
            Confidence.Exact);
    }

    private static bool Same(ResxUsage left, ResxUsage right) =>
        left.Line == right.Line && string.Equals(left.Relative, right.Relative, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ResxUsage> Ordered(IEnumerable<ResxUsage> usages) =>
    [
        .. usages
            .OrderBy(usage => usage.Relative, StringComparer.Ordinal)
            .ThenBy(usage => usage.Line),
    ];

    private static string Render(string key, IReadOnlyList<ResxUsage> usages, int composed, int maxResults)
    {
        var response = new ResponseBuilder("resx_usages", key);

        response.Summary(Math.Min(maxResults, usages.Count), usages.Count, "usages", "maxResults=");
        response.Note(string.Create(CultureInfo.InvariantCulture, $"composedLookups={composed}{Advisory(usages.Count, composed)}"));

        foreach (var usage in usages.Take(maxResults))
            response.Line(Describe(usage));

        return response.ToString();
    }

    private static string Advisory(int usages, int composed) => usages is 0 && composed > 0
        ? " - the solution builds resource keys at runtime, so 'no usages' is advisory, not proof"
        : string.Empty;

    private static string Describe(ResxUsage usage) => string.Create(
        CultureInfo.InvariantCulture,
        $"{usage.Relative}:{usage.Line}  {ConfidenceTag.Of(usage.Confidence)}  {usage.Form}");
    public static HashSet<string> Tokens(string root)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in WorkspaceFiles.Enumerate(root, IsScannable))
        {
            foreach (var line in Lines(file))
            {
                Collect(tokens, line);
            }
        }

        return tokens;
    }
    private static void Collect(HashSet<string> tokens, string line)
    {
        foreach (var match in Identifier.Matches(line).Cast<Match>())
        {
            tokens.Add(match.Value);

            foreach (var part in match.Value.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                tokens.Add(part);
            }
        }
    }
    private static readonly Regex Identifier = new(
        @"[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    public static async Task<IReadOnlyList<ResxUsage>> AllAsync(
        LoadedWorkspace workspace,
        string key,
        CancellationToken cancellationToken)
    {
        var exact = await ExactAsync(workspace, key, cancellationToken).ConfigureAwait(false);
        var textual = Textual(workspace.Root, key).Where(usage => !exact.Any(hit => Same(hit, usage)));

        return Ordered(exact.Concat(textual));
    }
    private static string Container(ReferenceLocation location)
    {
        var node = location.Location.SourceTree?.GetRoot().FindNode(location.Location.SourceSpan);
        var method = node?.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();

        return method is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"  in {Owner(method)}.{method.Identifier.ValueText}");
    }
    private static string Owner(SyntaxNode member) =>
        member.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "?";
}
