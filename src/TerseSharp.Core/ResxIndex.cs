using System.Diagnostics.CodeAnalysis;

namespace TerseSharp.Core;

public sealed class ResxIndex
{
    private static readonly string[] Extensions = [".resx", ".resw"];

    private readonly ParsedDocumentCache documents;
    private readonly Dictionary<string, IReadOnlyList<string>> names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock namesGate = new();

    private ResxIndex(string root, ParsedDocumentCache documents)
    {
        Root = root;
        this.documents = documents;
        Families = Group(root, WorkspaceFiles.Enumerate(root, IsResource));
    }

    public string Root { get; }

    public IReadOnlyList<ResxFamily> Families { get; }

    public static ResxIndex Of(string root, ParsedDocumentCache documents) => new(root, documents);

    public static bool IsResource(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public ResxFamily? FamilyOf(string fullPath) => Families.FirstOrDefault(family => family
        .Files
        .Any(file => string.Equals(file.Path, fullPath, StringComparison.OrdinalIgnoreCase)));

    public Result<ResxDocument> Read(string path) =>
        documents.Load(path, ParsedDocumentCache.ResourceFactor, Parse);

    public IReadOnlyList<ResxEntry> Entries(ResxFile file) =>
        Read(file.Path) is { IsOk: true, Value: { } document } ? document.Entries : [];

    public IReadOnlyList<ResxEntry> Translatable(ResxFile file) =>
        [.. Entries(file).Where(entry => entry.IsTranslatable)];

    public void Forget(string path)
    {
        documents.Forget(path);

        lock (namesGate)
            names.Remove(path);
    }

    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "Synchronous .resx index leaf, called from the parsed-document cache that every resx tool shares. Removing it means an async index, not a local change.")]
    private static Result<ResxDocument> Parse(string path)
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

        return ResxDocument.Parse(path, text);
    }

    private IReadOnlyList<ResxFamily> Group(string root, IEnumerable<string> files) =>
    [
        .. files
            .Select(file => Describe(root, file))
            .GroupBy(descriptor => descriptor.Key, StringComparer.OrdinalIgnoreCase)
            .Select(Family)
            .OrderBy(family => family.Relative, StringComparer.Ordinal),
    ];

    private ResxFamily Family(IGrouping<string, Descriptor> group)
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

    private ResxKind KindOf(Descriptor neutral) => Path.GetExtension(neutral.File.Path) switch
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

    public IReadOnlyList<string> TranslatableNames(ResxFile file)
    {
        lock (namesGate)
        {
            if (names.TryGetValue(file.Path, out var cached))
                return cached;
        }

        IReadOnlyList<string> computed = [.. Translatable(file).Select(entry => entry.Name)];

        lock (namesGate)
            names[file.Path] = computed;

        return computed;
    }
}
