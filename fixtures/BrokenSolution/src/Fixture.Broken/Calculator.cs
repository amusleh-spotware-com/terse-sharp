namespace Fixture.Broken;

public sealed class Calculator
{
    public int Healthy() => 1;

    public int PreExistingError() => "this does not compile";
}
