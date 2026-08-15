using Selection.Core;

namespace Selection.CoreTests;

internal static class AdderProbe
{
    public static int Twice(int value) => Adder.Add(value, value);
}
