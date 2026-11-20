using System.Collections.Frozen;

namespace TerseSharp.Core;

public static class HostSystem
{
    public const string Windows = "windows";

    public const string MacOs = "macos";

    public const string Linux = "linux";

    private static readonly FrozenDictionary<string, string> Tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["windows"] = Windows,
        ["win"] = Windows,
        ["win32"] = Windows,
        ["win64"] = Windows,
        ["mac"] = MacOs,
        ["macos"] = MacOs,
        ["osx"] = MacOs,
        ["darwin"] = MacOs,
        ["linux"] = Linux,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> ByToken = Tokens.GetAlternateLookup<ReadOnlySpan<char>>();

    public static string Name { get; } = OperatingSystem.IsWindows() ? Windows : OperatingSystem.IsMacOS() ? MacOs : Linux;

    public static int Rank(string solution, string host) => Named(Path.GetFileNameWithoutExtension(solution.AsSpan())) switch
    {
        null => 1,
        var named when named == host => 0,
        _ => 2,
    };

    private static string? Named(ReadOnlySpan<char> stem)
    {
        foreach (var range in stem.SplitAny('.', '-', '_', ' '))
        {
            if (ByToken.TryGetValue(stem[range], out var system))
                return system;
        }

        return null;
    }
}
