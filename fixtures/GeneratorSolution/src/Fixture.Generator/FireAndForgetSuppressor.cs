using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Fixture.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FireAndForgetSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor Rule = new("FIXSUP1", "CS4014", "a fire-and-forget call is intended here");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(Rule);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
            context.ReportSuppression(Suppression.Create(Rule, diagnostic));
    }
}
