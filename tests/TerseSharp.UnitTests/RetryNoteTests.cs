using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using TerseSharp.Core;
using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed partial class RetryNoteTests
{
    private static readonly TerseErrorCode[] Holdable =
    [
        TerseErrorCode.CompileRegression,
        TerseErrorCode.PolicyViolation,
        TerseErrorCode.SymbolNotFound,
        TerseErrorCode.AmbiguousSymbol,
        TerseErrorCode.InvalidArgument,
    ];

    [Fact]
    public void EveryRetryNote_NamesOnlyParametersItsToolDeclares()
    {
        var declared = Advertised();

        Assert.NotEmpty(RetryNote.Tools);

        foreach (var tool in RetryNote.Tools)
        {
            Assert.True(declared.ContainsKey(tool), tool + " is not an advertised tool");

            foreach (var note in Notes(tool))
            {
                foreach (var named in Named(note))
                    Assert.True(declared[tool].Contains(named), tool + " names " + named + "=, which it does not declare: " + note);
            }
        }
    }

    [Fact]
    public void EveryToolThatCanHoldARejection_HasItsOwnRetryNote()
    {
        var advertised = Advertised();

        Assert.NotEmpty(advertised);

        foreach (var (tool, parameters) in advertised)
        {
            if (parameters.Contains("retryWith"))
                Assert.Contains(tool, RetryNote.Tools);
        }
    }

    [Fact]
    public void EveryRetryNote_IsDistinctPerToolWhereTheRetryFormDiffers()
    {
        Assert.NotEqual(
            RetryNote.For("add_member", TerseErrorCode.SymbolNotFound, 1),
            RetryNote.For("replace_symbol", TerseErrorCode.SymbolNotFound, 1));

        Assert.NotEqual(
            RetryNote.For("write_text", TerseErrorCode.SymbolNotFound, 1),
            RetryNote.For("replace_symbol_body", TerseErrorCode.SymbolNotFound, 1));
    }

    private static IEnumerable<string> Notes(string tool)
    {
        foreach (var code in Holdable)
        {
            yield return RetryNote.For(tool, code, 1);
            yield return RetryNote.For(tool, code, 2);
        }
    }

    private static IEnumerable<string> Named(string note)
    {
        foreach (var match in NameBeforeEquals().Matches(note).Cast<Match>())
            yield return match.Groups[1].Value;
    }

    [GeneratedRegex(@"(?<![<\w])([a-z][A-Za-z0-9]*)=")]
    private static partial Regex NameBeforeEquals();

    private static Dictionary<string, HashSet<string>> Advertised()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var type in typeof(RetryNote).Assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is { Name: { Length: > 0 } name })
                    map[name] = [.. method.GetParameters().Select(parameter => parameter.Name!)];
            }
        }

        return map;
    }
}
