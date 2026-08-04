namespace Fixture.Warning;

public sealed class Calculator
{
    private readonly int neverUsedField;

    private int assignedButNeverRead = 1;

    public int Total()
    {
        var neverUsedLocal = 3;

        return 1 + 1;
    }
}
