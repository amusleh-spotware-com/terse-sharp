using Multi.Core.Support;
using System.Globalization;

namespace Multi.Core;

public static class UsingOrder
{
    public static string Caption() => Marker.Value.ToString(CultureInfo.InvariantCulture);
}
