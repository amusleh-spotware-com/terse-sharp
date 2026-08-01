namespace TerseSharp.Core;

public readonly record struct FileStamp(long Ticks, long Length)
{
    public static readonly FileStamp Missing = new(-1, -1);

    public static FileStamp Of(string path) => Of(new FileInfo(path));

    public static FileStamp Of(FileInfo info) =>
        info.Exists ? new FileStamp(info.LastWriteTimeUtc.Ticks, info.Length) : Missing;
}
