namespace TerseSharp.Server;

internal readonly record struct CallTiming(TimeSpan Load, TimeSpan Call)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"timing loadMs={(long)Load.TotalMilliseconds} callMs={(long)Call.TotalMilliseconds}");
}
