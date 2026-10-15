using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TerseSharp.Server;

internal static partial class ResponsePaths
{
    internal static List<string> Mentioned(JsonNode? response)
    {
        var found = new List<string>();

        foreach (var text in Texts(response))
            Collect(text, found);

        return found;
    }

    private static IEnumerable<string> Texts(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue(out string? text) => [text],
        JsonArray array => array.SelectMany(Texts),
        JsonObject members => members.SelectMany(member => Texts(member.Value)),
        _ => [],
    };

    private static void Collect(ReadOnlySpan<char> text, List<string> found)
    {
        foreach (var match in MentionedPath().EnumerateMatches(text))
            found.Add(text.Slice(match.Index, match.Length).ToString());
    }

    [GeneratedRegex(@"(?:[A-Za-z]:)?[\\/]?(?:[\w.\-]+[\\/])+[\w.\-]+\.(?:cs|razor|cshtml|xaml|axaml|resx|resw|csproj|props|targets|slnx|sln|slnf|json|md)(?!\w)")]
    private static partial Regex MentionedPath();
}
