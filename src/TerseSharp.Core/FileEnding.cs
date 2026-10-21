using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class FileEnding
{
    private const char ByteOrderMark = (char)0xFEFF;
    private const string EditorConfig = ".editorconfig";
    private const string InsertFinalNewline = "insert_final_newline";

    public static async Task<(string Text, string? Note)> KeptAsync(string full, bool existed, string before, string content, CancellationToken cancellationToken)
    {
        if (!existed || before.Length is 0)
            return (content, null);

        var (marked, bomNote) = Marked(full, content);
        var insert = await InsertFinalNewlineAsync(full, cancellationToken).ConfigureAwait(false);
        var (ended, newlineNote) = Ended(marked, insert ?? Terminated(before), insert, Ending(before));

        return (ended, (bomNote, newlineNote) switch
        {
            (null, null) => null,
            ({ } bom, null) => bom,
            (null, { } newline) => newline,
            ({ } bom, { } newline) => bom + "\n" + newline,
        });
    }

    private static (string Text, string? Note) Marked(string full, string content)
    {
        if (content is not [ByteOrderMark, ..])
            return (content, null);

        return AtomicWrite.EncodingOf(full).Preamble.Length > 0
            ? (content[1..], null)
            : (content, "NOTE byte order mark added - the file had none; send the content without a leading U+FEFF to keep it that way");
    }

    private static string Ending(string before) => before.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static (string Text, string? Note) Ended(string content, bool wanted, bool? insert, string ending)
    {
        if (content.Length is 0 || Terminated(content) == wanted)
            return (content, null);

        var reason = insert is null
            ? "to keep the file's own ending - no .editorconfig sets insert_final_newline; edit_text changes it deliberately"
            : "because .editorconfig sets " + InsertFinalNewline + "=" + (wanted ? "true" : "false");

        return wanted
            ? (content + ending, "NOTE final newline added " + reason)
            : (content[..^(content.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : 1)], "NOTE final newline removed " + reason);
    }

    private static bool Terminated(string text) => text is [.., '\n'];

    private static async Task<bool?> InsertFinalNewlineAsync(string full, CancellationToken cancellationToken)
    {
        var configs = new List<AnalyzerConfig>(4);

        for (var directory = Path.GetDirectoryName(full); directory is { Length: > 0 }; directory = Path.GetDirectoryName(directory))
        {
            var candidate = Path.Join(directory, EditorConfig);

            if (!File.Exists(candidate))
                continue;

            var text = await File.ReadAllTextAsync(candidate, cancellationToken).ConfigureAwait(false);

            configs.Add(AnalyzerConfig.Parse(text, candidate));

            if (IsRoot(text))
                break;
        }

        return configs.Count > 0
            && AnalyzerConfigSet.Create(configs).GetOptionsForSourcePath(full).AnalyzerOptions.TryGetValue(InsertFinalNewline, out var value)
            && bool.TryParse(value, out var insert)
                ? insert
                : null;
    }

    private static bool IsRoot(ReadOnlySpan<char> text)
    {
        foreach (var line in text.EnumerateLines())
        {
            var trimmed = line.Trim();

            if (trimmed is ['[', ..])
                return false;

            if (trimmed.StartsWith("root", StringComparison.OrdinalIgnoreCase) && trimmed[4..].TrimStart() is ['=', .. var value])
                return value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
