using Microsoft.CodeAnalysis;

namespace Fixture.Generator;

[Generator]
public sealed class GreetingGenerator : IIncrementalGenerator
{
    private const string Source = """
        namespace Fixture.Generated;

        public static class Greeting
        {
            public const string Text = "generated";
        }

        """;

    public void Initialize(IncrementalGeneratorInitializationContext context) =>
        context.RegisterPostInitializationOutput(output => output.AddSource("Greeting.g.cs", Source));
}
