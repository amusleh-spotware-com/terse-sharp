namespace Multi.Core;

public static class Caller
{
    public static int Twice(int value) => Numbers.Doubled(value);

    public static string Caption() => Strings.Probe_Caption;
}
