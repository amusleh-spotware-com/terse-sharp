using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class FileEnding
{
    private const char ByteOrderMark = '﻿';
    private const string EditorConfig = ".editorconfig";
    private const string InsertFinalNewline = "insert_final_newline";

    public static async Task<(string Text, string? Note)> KeptAsync(string full, bool existed, string before, string content, CancellationToken cancellationToken)
    {
        var overwrites = existed && before.Length > 0;
        var (marked, bomNote) = Marked(full, overwrites, content);
        var insert = await InsertFinalNewlineAsync(full, cancellationToken).ConfigureAwait(false);
        var (ended, newlineNote) = Ended(marked, Wanted(insert, overwrites, before, marked), insert);

        return (ended, (bomNote, newlineNote) switch
        {
            (null, null) => null,
            ({ } bom, null) => bom,
            (null, { } newline) => newline,
            ({ } bom, { } newline) => bom + "\n" + newline,
        });
    }

    private static (string Text, string? Note) Marked(string full, bool overwrites, string content)
    {
        if (content is not [ByteOrderMark, ..])
            return (content, null);

        if (overwrites && AtomicWrite.EncodingOf(full).Preamble.Length > 0)
            return (content[1..], null);

        return (content, overwrites ? "NOTE byte order mark added - the file had none; send the content without a leading U+FEFF to keep it that way" : null);
    }

    private static bool Wanted(bool? insert, bool overwrites, string before, string content) => insert switch
    {
        { } configured => configured,
        null when overwrites => Terminated(before),
        null => Terminated(content),
    };

    private static (string Text, string? Note) Ended(string content, bool wanted, bool? insert)
    {
        if (content.Length is 0 || Terminated(content) == wanted)
            return (content, null);

        var reason = insert is null
            ? "to keep the file's own ending - no .editorconfig sets insert_final_newline; edit_text changes it deliberately"
            : "because .editorconfig sets " + InsertFinalNewline + "=" + (wanted ? "true" : "false");

        return wanted
            ? (content + (content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"), "NOTE final newline added " + reason)
            : (content.TrimEnd('\r', '\n'), "NOTE final newline removed " + reason);
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
