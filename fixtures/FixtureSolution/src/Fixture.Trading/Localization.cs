using System.Globalization;
using System.Resources;

namespace Fixture.Trading;

public static class Localization
{
    private static readonly ResourceManager Manager = new("Fixture.Trading.Strings", typeof(Localization).Assembly);

    public static string SubmitCaption() => Strings.Caption_Submit;

    public static string CountCaption(int shown, int total) =>
        string.Format(CultureInfo.CurrentUICulture, Strings.Caption_Count, shown, total);

    public static string TotalCaption() =>
        Manager.GetString("Caption_Total", CultureInfo.CurrentUICulture) ?? string.Empty;

    public static string ComposedCaption(string suffix) =>
        Manager.GetString("Caption_" + suffix, CultureInfo.CurrentUICulture) ?? string.Empty;
}
