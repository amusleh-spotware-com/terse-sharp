namespace Fixture.Trading;

public sealed class StyleSample
{
    public List<int> Quantities { get; } = new List<int>();

    public int Doubled(int quantity) => quantity * 2;
}
