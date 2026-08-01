using System.Collections.Concurrent;

namespace TerseSharp.Core;

public sealed class ResxIndex
{
    private static readonly ConcurrentDictionary<string, CachedDocument> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] Extensions = [".resx", ".resw"];

    private ResxIndex(string root, IReadOnlyList<ResxFamily> families)
    {
        Root = root;
        Families = families;
    }

    public string Root { get; }

    public IReadOnlyList<ResxFamily> Families { get; }

    public static ResxIndex Build(string root) => new(root, Group(root, WorkspaceFiles.Enumerate(root, IsResource)));

    public static bool IsResource(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public ResxFamily? FamilyOf(string fullPath) => Families.FirstOrDefault(family => family
        .Files
        .Any(file => string.Equals(file.Path, fullPath, StringComparison.OrdinalIgnoreCase)));

    public static Result<ResxDocument> Read(string path)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
            return Result.Fail<ResxDocument>(Errors.DocumentNotFound(path));

        if (Cache.TryGetValue(path, out var cached) && cached.Matches(info))
            return Result.Ok(cached.Document);

        return Parse(path, info);
    }

    public static IReadOnlyList<ResxEntry> Entries(ResxFile file) =>
        Read(file.Path) is { IsOk: true, Value: { } document } ? document.Entries : [];

    public static IReadOnlyList<ResxEntry> Translatable(ResxFile file) =>
        [.. Entries(file).Where(entry => entry.IsTranslatable)];

    internal static void Forget(string path) => Cache.TryRemove(path, out _);

    private static Result<ResxDocument> Parse(string path, FileInfo info)
    {
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException exception)
        {
            return Result.Fail<ResxDocument>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{path}' could not be read: {exception.Message}"),
                "close whatever holds the file open and retry"));
        }

        var parsed = ResxDocument.Parse(path, text);

        if (parsed.IsOk)
            Cache[path] = new CachedDocument(info.LastWriteTimeUtc, info.Length, parsed.Value!);

        return parsed;
    }

    private static IReadOnlyList<ResxFamily> Group(string root, IEnumerable<string> files) =>
    [
        .. files
            .Select(file => Describe(root, file))
            .GroupBy(descriptor => descriptor.Key, StringComparer.OrdinalIgnoreCase)
            .Select(Family)
            .OrderBy(family => family.Relative, StringComparer.Ordinal),
    ];

    private static ResxFamily Family(IGrouping<string, Descriptor> group)
    {
        var ordered = group.OrderBy(descriptor => descriptor.File.Culture ?? string.Empty, StringComparer.Ordinal).ToArray();
        var neutral = ordered.FirstOrDefault(descriptor => descriptor.File.Culture is null, ordered[0]);
        var cultures = ordered.Where(descriptor => descriptor != neutral).Select(descriptor => descriptor.File).ToArray();

        return new ResxFamily(neutral.Name, neutral.File.Relative, neutral.File, cultures, Designer(neutral), KindOf(neutral));
    }

    private static string? Designer(Descriptor neutral)
    {
        var path = Path.Combine(neutral.Directory, neutral.Name + ".Designer.cs");

        return File.Exists(path) ? PositionFormat.Relative(neutral.Root, path) : null;
    }

    private static ResxKind KindOf(Descriptor neutral) => Path.GetExtension(neutral.File.Path) switch
    {
        var extension when string.Equals(extension, ".resw", StringComparison.OrdinalIgnoreCase) => ResxKind.Resw,
        _ => Read(neutral.File.Path) is { IsOk: true, Value.HasDesignerState: true } ? ResxKind.WinForms : ResxKind.Localization,
    };

    private static Descriptor Describe(string root, string path)
    {
        var directory = Path.GetDirectoryName(path) ?? root;
        var stem = Path.GetFileNameWithoutExtension(path);
        var separator = stem.LastIndexOf('.');
        var candidate = separator < 0 ? null : stem[(separator + 1)..];
        var culture = candidate is not null && ResxCulture.IsCulture(candidate) ? candidate : null;
        var name = culture is null ? stem : stem[..separator];

        return new Descriptor(
            root,
            directory,
            name,
            new ResxFile(path, PositionFormat.Relative(root, path), culture),
            string.Create(CultureInfo.InvariantCulture, $"{directory}|{name}|{Path.GetExtension(path)}"));
    }

    private sealed record Descriptor(string Root, string Directory, string Name, ResxFile File, string Key);

    private sealed record CachedDocument(DateTime WriteTimeUtc, long Length, ResxDocument Document)
    {
        public bool Matches(FileInfo info) => info.LastWriteTimeUtc == WriteTimeUtc && info.Length == Length;
    }
}
