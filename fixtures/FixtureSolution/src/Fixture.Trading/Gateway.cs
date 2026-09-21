using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Fixture.Trading;

public abstract class Gateway
{
    public abstract bool TryQuote(int volume, [NotNullWhen(true)] out string? quote);
}

public class FixGateway : Gateway
{
    public override bool TryQuote(int volume, [NotNullWhen(true)] out string? quote)
    {
        quote = volume.ToString(CultureInfo.InvariantCulture);

        return volume > 0;
    }
}

public sealed class RestGateway : FixGateway
{
    public int Retries { get; init; }

    public bool TryRoute(
        [Description("route \"fast\" or \"slow\", and never an empty string, so this outline has a long argument to elide")] string mode,
        [Description("a\"b")] int tag) => mode.Length > tag;
}
