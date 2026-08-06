namespace Fixture.Trading;

public sealed partial class SplitHandler
{
    public const string SampleTag = "  EXACT  ";

    public int Dispatch(int value) => Route(value) + Route(SampleTag);

    public int Route(int value) => value + 1;
}
