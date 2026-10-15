using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace TerseSharp.Server;

internal sealed class FolderRuleLedger(string home, string session)
{
    internal const string DirectoryName = "folder-rules";

    private const int MaxStackChars = 512;

    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private string Root => Path.Join(home, ".terse", DirectoryName);

    private string SessionFolder => Path.Join(Root, FileName(session));

    internal bool Seen(string rule) => File.Exists(Marker(rule));

    internal IReadOnlySet<string> Claim(IReadOnlyList<string> rules, bool showUnrecorded, bool showLost = false)
    {
        var recordable = Prepared();
        var shown = new HashSet<string>(rules.Count, PathBoundary.Comparer);

        foreach (var rule in rules)
        {
            var won = recordable ? Won(Marker(rule)) : null;

            if (won is false && !showLost)
                break;

            if (won switch { true => true, false => showLost, null => showUnrecorded })
                shown.Add(rule);
        }

        return shown;
    }

    internal void Forget()
    {
        if (Directory.Exists(SessionFolder))
            Directory.Delete(SessionFolder, recursive: true);
    }

    private static bool? Won(string marker)
    {
        try
        {
            File.OpenHandle(marker, FileMode.CreateNew, FileAccess.Write).Dispose();

            return true;
        }
        catch (IOException) when (File.Exists(marker))
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool Prepared()
    {
        if (!Directory.Exists(SessionFolder))
            Prune();

        try
        {
            Directory.CreateDirectory(SessionFolder);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Prune()
    {
        try
        {
            foreach (var stale in new DirectoryInfo(Root).EnumerateDirectories().Where(folder => DateTime.UtcNow - folder.LastWriteTimeUtc > Retention))
                stale.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Marker(string rule) => Path.Join(SessionFolder, Digest(rule));

    private static string Digest(string rule)
    {
        var folded = rule.Length <= MaxStackChars ? stackalloc char[MaxStackChars] : new char[rule.Length];
        var length = Folded(rule, folded);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(MemoryMarshal.AsBytes(folded[..length]), hash);

        return Convert.ToHexString(hash);
    }

    private static int Folded(ReadOnlySpan<char> rule, Span<char> destination)
    {
        if (PathBoundary.Comparison is StringComparison.OrdinalIgnoreCase)
            return rule.ToUpperInvariant(destination);

        rule.CopyTo(destination);

        return rule.Length;
    }

    private static string FileName(string session) => string.Create(session.Length, session, static (name, source) =>
    {
        for (var index = 0; index < source.Length; index++)
            name[index] = char.IsAsciiLetterOrDigit(source[index]) || source[index] is '-' or '_' ? source[index] : '_';
    });
}
