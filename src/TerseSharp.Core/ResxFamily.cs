using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public enum ResxKind
{
    Localization,
    WinForms,
    Resw,
}

public sealed record ResxFile(string Path, string Relative, string? Culture);

public sealed record ResxFamily(
    string Name,
    string Relative,
    ResxFile Neutral,
    IReadOnlyList<ResxFile> Cultures,
    string? Designer,
    ResxKind Kind)
{
    public IEnumerable<ResxFile> Files => [Neutral, .. Cultures];

    public ResxFile? Culture(string culture) => Cultures
        .FirstOrDefault(file => string.Equals(file.Culture, culture, StringComparison.OrdinalIgnoreCase));

    public string CulturePath(string culture) => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Neutral.Path) ?? string.Empty,
        string.Create(CultureInfo.InvariantCulture, $"{Name}.{culture}{System.IO.Path.GetExtension(Neutral.Path)}"));
}

public static class ResxCulture
{
    private static readonly Regex Shape = new(
        @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    private static readonly HashSet<string> Known = Cultures();

    public static bool IsCulture(string token) => Shape.IsMatch(token)
        && (Known.Count is 0 ? token.Length is 2 || token.Contains('-', StringComparison.Ordinal) : Known.Contains(token));

    private static HashSet<string> Cultures()
    {
        try
        {
            return CultureInfo
                .GetCultures(CultureTypes.AllCultures)
                .Select(culture => culture.Name)
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (CultureNotFoundException)
        {
            return [];
        }
    }
}
