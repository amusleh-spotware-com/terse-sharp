using System.Collections.Concurrent;

namespace TerseSharp.Core;

public static class RazorIndex
{
    private static readonly ConcurrentDictionary<string, Cached> Parsed = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<RazorDocument> Build(string root)
    {
        var files = RazorFiles.Enumerate(root).ToArray();
        var documents = new RazorDocument?[files.Length];

        Parallel.For(0, files.Length, index => documents[index] = Of(files[index]));

        return [.. documents.OfType<RazorDocument>()];
    }

    public static RazorDocument? Of(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);

            if (Parsed.TryGetValue(fullPath, out var cached) && cached.Matches(info))
                return cached.Document;

            var document = RazorDocument.Load(fullPath);

            if (!document.IsOk)
                return null;

            Parsed[fullPath] = new Cached(info.LastWriteTimeUtc, info.Length, document.Value!);

            return document.Value;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Invalidate(string fullPath) => Parsed.TryRemove(fullPath, out _);

    private readonly record struct Cached(DateTime WriteUtc, long Length, RazorDocument Document)
    {
        public bool Matches(FileInfo info) => info.LastWriteTimeUtc == WriteUtc && info.Length == Length;
    }
}
