using System.Globalization;
using System.Resources;

namespace Multi.Core;

public static class Strings
{
    private static readonly ResourceManager Manager = new ResourceManager("Multi.Core.Strings", typeof(Strings).Assembly);

    public static string Probe_Caption => Manager.GetString("Probe_Caption", CultureInfo.CurrentUICulture) ?? string.Empty;
}
