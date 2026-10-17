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

    internal IReadOnlySet<string> Claim(IReadOnlyList<string> rules, bool showUnrecorded, bool showLost = false, bool hold = false)
    {
        var recordable = Prepared();
        var shown = new HashSet<string>(rules.Count, PathBoundary.Comparer);

        foreach (var rule in rules)
        {
            var won = recordable ? Won(Marker(rule), hold) : null;

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

    private bool? Won(string marker, bool hold)
    {
        if (hold)
            Hold(marker);

        try
        {
            File.OpenHandle(marker, FileMode.CreateNew, FileAccess.Write).Dispose();
            created.Add(marker);

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
            {
                try
                {
                    stale.Delete(recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string Marker(string rule) => Path.Join(SessionFolder, Digest(rule));

    private static string Digest(string rule)
    {
        var folded = rule.Length <= MaxStackChars ? stackalloc char[MaxStackChars] : new char[rule.Length];
        var length = PathBoundary.Comparison is StringComparison.OrdinalIgnoreCase ? rule.AsSpan().ToUpperInvariant(folded) : rule.AsSpan().TryCopyTo(folded) ? rule.Length : 0;
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(MemoryMarshal.AsBytes(folded[..length]), hash);

        return Convert.ToHexString(hash);
    }

    internal void Release()
    {
        foreach (var marker in created)
        {
            File.Delete(marker);
            File.Delete(marker + PendingSuffix);
        }
    }

    private static string FileName(string session) => string.Create(session.Length, session, static (name, source) =>
    {
        for (var index = 0; index < source.Length; index++)
            name[index] = char.IsAsciiLetterOrDigit(source[index]) || source[index] is '-' or '_' ? source[index] : '_';
    });

    private readonly List<string> created = [];
    internal const string PendingPattern = "*" + PendingSuffix;
    private const string PendingSuffix = ".pending";
    private static readonly TimeSpan Unsettled = TimeSpan.FromSeconds(30);

    internal bool Settled(string rule) => Seen(rule) && !Pending(Marker(rule));

    internal void Settle()
    {
        if (!Directory.Exists(SessionFolder))
            return;

        foreach (var pending in Directory.EnumerateFiles(SessionFolder, PendingPattern))
            File.Delete(pending);
    }

    private static bool Pending(string marker) => DateTime.UtcNow - File.GetLastWriteTimeUtc(marker + PendingSuffix) <= Unsettled;

    private static void Hold(string marker)
    {
        try
        {
            if (!File.Exists(marker))
                File.OpenHandle(marker + PendingSuffix, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
