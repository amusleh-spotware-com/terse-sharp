using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public enum NamingKind
{
    Namespace,
    Type,
    Interface,
    TypeParameter,
    Method,
    Property,
    Event,
    Field,
    Constant,
    Parameter,
    Local,
    EnumMember
}

public sealed record NamingPattern(NamingKind Kind, string Expression, Regex Matcher)
{
    public static NamingPattern? Create(NamingKind kind, string expression)
    {
        try
        {
            return new(kind, expression, new Regex(expression, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

public static class NamingDefaults
{
    private const string Pascal = "^[A-Z][A-Za-z0-9]*$";
    private const string Camel = "^[a-z][A-Za-z0-9]*$";

    public static FrozenDictionary<NamingKind, string> Expressions { get; } = new Dictionary<NamingKind, string>
    {
        [NamingKind.Namespace] = "^[A-Z][A-Za-z0-9]*(\\.[A-Z][A-Za-z0-9]*)*$",
        [NamingKind.Type] = Pascal,
        [NamingKind.Interface] = "^I[A-Z][A-Za-z0-9]*$",
        [NamingKind.TypeParameter] = "^T$|^T[A-Z][A-Za-z0-9]*$",
        [NamingKind.Method] = Pascal,
        [NamingKind.Property] = Pascal,
        [NamingKind.Event] = Pascal,
        [NamingKind.Field] = Camel,
        [NamingKind.Constant] = Pascal,
        [NamingKind.Parameter] = Camel,
        [NamingKind.Local] = Camel,
        [NamingKind.EnumMember] = Pascal,
    }.ToFrozenDictionary();

    public static FrozenDictionary<NamingKind, NamingPattern> Patterns { get; } = Expressions
        .Select(entry => NamingPattern.Create(entry.Key, entry.Value))
        .OfType<NamingPattern>()
        .ToFrozenDictionary(pattern => pattern.Kind);

    public static string Keys() => string.Join(", ", Expressions.Keys.Select(kind => Name(kind)));

    public static string Name(NamingKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];

    public static NamingKind? Parse(string key) => Expressions.Keys
        .Cast<NamingKind?>()
        .FirstOrDefault(kind => string.Equals(Name(kind!.Value), key, StringComparison.OrdinalIgnoreCase));
}
