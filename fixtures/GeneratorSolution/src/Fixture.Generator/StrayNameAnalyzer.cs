using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Fixture.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StrayNameAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "FIX001",
        "Stray name",
        "'{0}' is a stray name",
        "Naming",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Inspect, SymbolKind.NamedType);
    }

    private static void Inspect(SymbolAnalysisContext context)
    {
        if (context.Symbol.Name.StartsWith("Stray", System.StringComparison.Ordinal))
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Symbol.Locations[0], context.Symbol.Name));
    }
}
