using System.Globalization;
using System.Resources;

namespace Fixture.Trading;

public static class Strings
{
    private static readonly ResourceManager Manager = new("Fixture.Trading.Strings", typeof(Strings).Assembly);

    public static string Caption_Submit => Manager.GetString("Caption_Submit", CultureInfo.CurrentUICulture) ?? string.Empty;

    public static string Caption_Count => Manager.GetString("Caption_Count", CultureInfo.CurrentUICulture) ?? string.Empty;
}
