using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerseSharp.Core;

public sealed record CodeSmell(string Name, string Member, double Penalty);

public sealed record CodeHealthScore(double Score, ImmutableArray<CodeSmell> Smells)
{
    private const int Named = 3;

    public string Describe()
    {
        var worst = Smells.OrderByDescending(smell => smell.Penalty).Take(Named).Select(smell => smell.Name + " " + smell.Member);
        var rest = Smells.Length > Named ? string.Create(CultureInfo.InvariantCulture, $", +{Smells.Length - Named} more") : "";

        return string.Create(CultureInfo.InvariantCulture, $"code health {Score:0.0} ({string.Join(", ", worst)}{rest})");
    }
}

public static class CodeHealth
{
    public const double Perfect = 10.0;
    public const double Floor = 1.0;
    public const int ComplexMethodBranches = 10;
    public const int LargeMethodLines = 70;
    public const int ExcessArguments = 4;
    public const int DeepNestingLevels = 4;
    public const int BumpyRoadBumps = 2;
    public const int LargeFileLines = 1000;

    private const double LargeFilePenalty = 1.0;

    private sealed record Biomarker(string Name, double Penalty, Func<FunctionMetrics, bool> Applies);

    private sealed class FunctionMetrics(SyntaxNode function, SyntaxNode body)
    {
        public int Branches { get; } = 1 + body.DescendantNodesAndSelf().Count(SyntaxShape.IsBranch);

        public int Lines { get; } = SyntaxShape.Lines(body);

        public int Arguments { get; } = function is BaseMethodDeclarationSyntax method ? method.ParameterList.Parameters.Count : 0;

        public int Nesting { get; } = SyntaxShape.Depth(body, function);

        public int Bumps { get; } = SyntaxShape.Bumps(body);

        public bool ComplexConditional { get; } = SyntaxShape.HasComplexConditional(body);
    }

    private static readonly Biomarker[] Biomarkers =
    [
        new("Brain Method", 1.5, static metrics => metrics.Branches >= ComplexMethodBranches && metrics.Lines > LargeMethodLines),
        new("Deep Nested Complexity", 0.75, static metrics => metrics.Nesting >= DeepNestingLevels),
        new("Complex Method", 0.5, static metrics => metrics.Branches >= ComplexMethodBranches),
        new("Large Method", 0.5, static metrics => metrics.Lines > LargeMethodLines),
        new("Bumpy Road", 0.5, static metrics => metrics.Bumps >= BumpyRoadBumps),
        new("Excess Function Arguments", 0.25, static metrics => metrics.Arguments > ExcessArguments),
        new("Complex Conditional", 0.25, static metrics => metrics.ComplexConditional),
    ];

    public static CodeHealthScore Assess(SyntaxNode root)
    {
        var smells = root.DescendantNodes().Where(IsFunction).SelectMany(Smells)
            .Concat(Cohesion.Smells(root))
            .Concat(Duplication.Smells(root))
            .ToList();

        if (SyntaxShape.Lines(root) > LargeFileLines)
            smells.Add(new CodeSmell("Large File", "<file>", LargeFilePenalty));

        var score = Math.Max(Floor, Perfect - smells.Sum(smell => smell.Penalty));

        return new CodeHealthScore(Math.Round(score, 1, MidpointRounding.AwayFromZero), [.. smells]);
    }

    internal static bool IsFunction(SyntaxNode node) => node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax;

    private static IEnumerable<CodeSmell> Smells(SyntaxNode function)
    {
        if (Body(function) is not { } body)
            return [];

        var metrics = new FunctionMetrics(function, body);

        return Biomarkers.Where(biomarker => biomarker.Applies(metrics)).Select(biomarker => new CodeSmell(biomarker.Name, Member(function), biomarker.Penalty));
    }

    internal static SyntaxNode? Body(SyntaxNode function) => function switch
    {
        BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
        AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
        _ => null,
    };

    internal static string Member(SyntaxNode function) => string.Join(
        '.',
        function.AncestorsAndSelf().Select(Label).OfType<string>().Reverse());

    private static string? Label(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        EventDeclarationSyntax declared => declared.Identifier.ValueText,
        IndexerDeclarationSyntax => "this",
        OperatorDeclarationSyntax declared => "operator " + declared.OperatorToken.ValueText,
        AccessorDeclarationSyntax accessor => accessor.Keyword.ValueText,
        _ => null,
    };
}
