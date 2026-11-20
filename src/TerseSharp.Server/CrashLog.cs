using System.Text;

namespace TerseSharp.Server;

internal static class CrashLog
{
    private const string FileName = "crash.log";

    private const long BytesPerMegabyte = 1024 * 1024;

    private static readonly TimeSpan Recent = TimeSpan.FromDays(7);

    public static void Install(string home) =>
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Append(home, Record(DateTime.UtcNow, UpdateSettings.Version(), args.IsTerminating, Environment.WorkingSet, args.ExceptionObject));

    public static string? Warning()
    {
        var log = new FileInfo(PathIn(ClientRegistrar.Home()));

        return Describe(log.FullName, log.Exists ? log.LastWriteTimeUtc : null, DateTime.UtcNow);
    }

    internal static string PathIn(string home) => Path.Join(Folder(home), FileName);

    internal static string? Describe(string path, DateTime? writtenUtc, DateTime now) =>
        writtenUtc is { } written && now - written <= Recent
            ? string.Create(CultureInfo.InvariantCulture, $"WARNING terse crashed at {written:s}Z - read_text {path} for the exception, the memory and the version it ran")
            : null;

    internal static string Record(DateTime utc, string version, bool terminating, long workingSet, object exception) =>
        string.Create(CultureInfo.InvariantCulture, $"{utc:O} pid={Environment.ProcessId} terse={version} terminating={terminating} workingSetMB={workingSet / BytesPerMegabyte}\n{exception}\n\n");

    internal static void Append(string home, string record)
    {
        try
        {
            Directory.CreateDirectory(Folder(home));

            using var stream = new FileStream(PathIn(home), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

            stream.Write(Encoding.UTF8.GetBytes(record));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.Write(record);
        }
    }

    private static string Folder(string home) => Path.Join(home, ".terse");
}
