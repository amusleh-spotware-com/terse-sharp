using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace TerseSharp.Core;

public static class ProjectFile
{
    public static async Task<Result<string>> Create(string projectPath, string kind, string? targetFramework, bool dryRun, bool verbose)
    {
        var full = Path.GetFullPath(projectPath);

        if (File.Exists(full))
            return Result.Fail<string>(Errors.Invalid($"'{projectPath}' already exists", "pick another path"));

        var document = new XElement("Project", new XAttribute("Sdk", Sdk(kind)));
        var properties = Properties(kind, targetFramework);

        if (properties.HasElements)
            document.Add(properties);

        if (!dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await AtomicWrite.TextAsync(full, document.ToString() + Environment.NewLine).ConfigureAwait(false);
        }

        return Rendered("project_create", projectPath, projectPath, string.Empty, document.ToString(), dryRun, verbose);
    }

    public static Task<Result<string>> AddReference(string projectPath, string targetProject, bool dryRun, bool verbose) =>
        AddItem(projectPath, "ProjectReference", Relative(projectPath, targetProject), dryRun, verbose, "project_add_reference");

    public static Task<Result<string>> RemoveReference(string projectPath, string targetProject, bool dryRun, bool verbose) =>
        RemoveItem(projectPath, "ProjectReference", Relative(projectPath, targetProject), dryRun, verbose, "project_remove_reference");

    public static Task<Result<string>> AddPackage(
        string root,
        string projectPath,
        string package,
        string? version,
        bool dryRun,
        bool verbose = false)
    {
        if (string.IsNullOrWhiteSpace(package))
            return Task.FromResult(Result.Fail<string>(Errors.Blank("package")));

        var central = CentralVersionsFile(root, projectPath);

        if (central is null && CentralVersionsFile(null, projectPath) is not null)
        {
            return Task.FromResult(Result.Fail<string>(Errors.Invalid(
                "this project's Directory.Packages.props sits above the workspace root",
                "load the workspace at the repository root, or edit Directory.Packages.props directly")));
        }

        return central is null
            ? AddItem(projectPath, "PackageReference", package, dryRun, verbose, "package_add", version)
            : AddCentralPackage(projectPath, central, package, version, dryRun, verbose);
    }

    public static Task<Result<string>> RemovePackage(string projectPath, string package, bool dryRun, bool verbose) =>
        string.IsNullOrWhiteSpace(package)
            ? Task.FromResult(Result.Fail<string>(Errors.Blank("package")))
            : RemoveItem(projectPath, "PackageReference", package, dryRun, verbose, "package_remove");

    public static Result<string> ListPackages(string projectPath)
    {
        var document = Load(projectPath);

        if (document is null)
            return Result.Fail<string>(Errors.DocumentNotFound(projectPath));

        var packages = Items(document, "PackageReference");
        var references = Items(document, "ProjectReference");
        var response = new ResponseBuilder("package_list", projectPath);

        response.Summary(packages.Length + references.Length, packages.Length + references.Length, "references");

        foreach (var package in packages)
            response.Line("package  " + package);

        foreach (var reference in references)
            response.Line("project  " + reference);

        return Result.Ok(response.ToString());
    }

    public static Result<string> GetProperties(string projectPath, string? name)
    {
        var document = Load(projectPath);

        if (document is null)
            return Result.Fail<string>(Errors.DocumentNotFound(projectPath));

        var properties = document
            .Descendants("PropertyGroup")
            .SelectMany(group => group.Elements())
            .Where(element => name is null || element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var response = new ResponseBuilder("project_properties", projectPath);

        response.Summary(properties.Length, properties.Length, "properties");

        foreach (var property in properties)
            response.Line(property.Name.LocalName + " = " + property.Value);

        return Result.Ok(response.ToString());
    }

    public static async Task<Result<string>> SetProperty(string projectPath, string name, string value, bool dryRun, bool verbose)
    {
        if (await SourceAsync(projectPath).ConfigureAwait(false) is not { } source)
            return Result.Fail<string>(Errors.DocumentNotFound(projectPath));

        var existing = source.Document.Descendants(name).FirstOrDefault();

        if (existing is null)
            Append(Group(source.Document), new XElement(name, value));
        else
            existing.Value = value;

        return await Save(projectPath, source.Document, source.Text, dryRun, verbose, "project_set_property", name + "=" + value).ConfigureAwait(false);
    }

    private static async Task<Result<string>> AddCentralPackage(
        string projectPath,
        string centralPath,
        string package,
        string? version,
        bool dryRun,
        bool verbose)
    {
        if (version is null)
        {
            return Result.Fail<string>(Errors.Invalid(
                "this solution uses central package management, so a version is required",
                "pass version=<x.y.z>; it is written to Directory.Packages.props"));
        }

        if (await SourceAsync(centralPath).ConfigureAwait(false) is not { } central)
            return Result.Fail<string>(Errors.DocumentNotFound(centralPath));

        if (central.Document.Descendants("PackageVersion").All(element => !Named(element, package)))
            Append(ItemGroup(central.Document, "PackageVersion"), new XElement("PackageVersion", new XAttribute("Include", package), new XAttribute("Version", version)));

        var after = Serialized(central.Document, central.Text);

        if (!dryRun)
            await AtomicWrite.TextAsync(centralPath, after).ConfigureAwait(false);

        var added = await AddItem(projectPath, "PackageReference", package, dryRun, verbose, "package_add").ConfigureAwait(false);

        return added.IsOk
            ? Result.Ok(added.Value + "\n" + Central(projectPath, centralPath, central.Text, after, dryRun, verbose))
            : added;
    }

    private static async Task<Result<string>> AddItem(
        string projectPath,
        string itemName,
        string include,
        bool dryRun,
        bool verbose,
        string tool,
        string? version = null)
    {
        if (await SourceAsync(projectPath).ConfigureAwait(false) is not { } source)
            return Result.Fail<string>(Errors.DocumentNotFound(projectPath));

        if (Items(source.Document, itemName).Contains(include, StringComparer.OrdinalIgnoreCase))
            return Result.Fail<string>(Errors.Invalid($"'{include}' is already referenced", "nothing to add"));

        var element = new XElement(itemName, new XAttribute("Include", include));

        if (version is not null)
            element.Add(new XAttribute("Version", version));

        Append(ItemGroup(source.Document, itemName), element);

        return await Save(projectPath, source.Document, source.Text, dryRun, verbose, tool, include).ConfigureAwait(false);
    }

    private static async Task<Result<string>> RemoveItem(
        string projectPath,
        string itemName,
        string include,
        bool dryRun,
        bool verbose,
        string tool)
    {
        if (await SourceAsync(projectPath).ConfigureAwait(false) is not { } source)
            return Result.Fail<string>(Errors.DocumentNotFound(projectPath));

        var element = source.Document.Descendants(itemName).FirstOrDefault(candidate => Named(candidate, include));

        if (element is null)
            return Result.Fail<string>(Errors.Invalid($"'{include}' is not referenced", "check package_list"));

        Detach(element);

        return await Save(projectPath, source.Document, source.Text, dryRun, verbose, tool, include).ConfigureAwait(false);
    }

    private static bool Named(XElement element, string include) =>
        (element.Attribute("Include")?.Value ?? string.Empty)
            .Replace('\\', '/')
            .Equals(include.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string[] Items(XDocument document, string itemName) =>
        [.. document.Descendants(itemName).Select(element => element.Attribute("Include")?.Value).OfType<string>()];

    private static XElement ItemGroup(XDocument document, string itemName)
    {
        var existing = document.Descendants(itemName)
            .Select(element => element.Parent)
            .FirstOrDefault(parent => parent is not null && parent.Attribute("Condition") is null);

        return existing ?? Created(document.Root!, "ItemGroup");
    }

    private static XElement Group(XDocument document) =>
        document.Descendants("PropertyGroup").FirstOrDefault() ?? Created(document.Root!, "PropertyGroup");

    private static XElement Properties(string kind, string? targetFramework)
    {
        var group = new XElement("PropertyGroup");

        if (targetFramework is not null)
            group.Add(new XElement("TargetFramework", targetFramework));

        if (kind.Equals("console", StringComparison.OrdinalIgnoreCase))
            group.Add(new XElement("OutputType", "Exe"));

        return group;
    }

    private static string Sdk(string kind) => kind.ToLowerInvariant() switch
    {
        "web" => "Microsoft.NET.Sdk.Web",
        "razor" or "blazor" => "Microsoft.NET.Sdk.Razor",
        _ => "Microsoft.NET.Sdk",
    };

    private static string? CentralVersionsFile(string? root, string projectPath)
    {
        var nearest = NearestVersionsFile(root, projectPath);

        return nearest is not null && ManagesVersionsCentrally(root, projectPath, nearest) ? nearest : null;
    }

    private static string? NearestVersionsFile(string? root, string projectPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(projectPath))!);

        while (directory is not null && (root is null || PathBoundary.Contains(root, directory.FullName)))
        {
            var candidate = Path.Combine(directory.FullName, "Directory.Packages.props");

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    private static bool ManagesVersionsCentrally(string? root, string projectPath, string versionsFile) =>
        PropertySources(root, projectPath, versionsFile)
            .Select(CentralManagementSetting)
            .OfType<bool>()
            .Any(enabled => enabled);

    private static IEnumerable<string> PropertySources(string? root, string projectPath, string versionsFile)
    {
        yield return versionsFile;
        yield return Path.GetFullPath(projectPath);

        foreach (var file in BuildPropertyFiles(root, projectPath))
            yield return file;
    }

    private static IEnumerable<string> BuildPropertyFiles(string? root, string projectPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(projectPath))!);

        while (directory is not null && (root is null || PathBoundary.Contains(root, directory.FullName)))
        {
            var candidate = Path.Combine(directory.FullName, "Directory.Build.props");

            if (File.Exists(candidate))
                yield return candidate;

            directory = directory.Parent;
        }
    }

    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "Synchronous probe of one MSBuild props file from a synchronous walk up the directory tree; converting it means an async walk, not a local change.")]
    private static bool? CentralManagementSetting(string file)
    {
        try
        {
            var value = XDocument.Load(file)
                .Descendants("ManagePackageVersionsCentrally")
                .Select(element => element.Value.Trim())
                .LastOrDefault();

            return value is null ? null : !bool.TryParse(value, out var enabled) || enabled;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static async Task<Result<string>> Save(
        string projectPath,
        XDocument document,
        string before,
        bool dryRun,
        bool verbose,
        string tool,
        string argument)
    {
        var after = Serialized(document, before);

        if (!dryRun)
            await AtomicWrite.TextAsync(Path.GetFullPath(projectPath), after).ConfigureAwait(false);

        return Rendered(tool, argument, projectPath, before, after, dryRun, verbose);
    }

    private static Result<string> Rendered(
        string tool,
        string argument,
        string file,
        string before,
        string after,
        bool dryRun,
        bool verbose)
    {
        var response = new ResponseBuilder(tool, argument).Verbose(verbose);

        if (!dryRun && !verbose)
        {
            return Result.Ok(response
                .Line(string.Create(CultureInfo.InvariantCulture, $"{file}  changedLines={UnifiedDiff.ChangedLines(before, after)}"))
                .ToString());
        }

        response.Summary(1, 1, "files changed");
        response.Note(dryRun ? "dryRun" : "applied");
        response.Line(UnifiedDiff.Between(file, before, after));

        return Result.Ok(response.ToString());
    }
    private static string Relative(string projectPath, string target)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var full = Path.IsPathRooted(target) ? target : Path.Combine(directory, target);

        return Path.GetRelativePath(directory, Path.GetFullPath(full)).Replace('\\', '/');
    }

    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "Synchronous project-file leaf shared by every project_* reader; converting it means an async XML layer, not a local change.")]
    private static XDocument? Load(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);

        return File.Exists(full) ? XDocument.Load(full) : null;
    }

    private readonly record struct ProjectSource(XDocument Document, string Text);

    private static async Task<ProjectSource?> SourceAsync(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);

        if (!File.Exists(full))
            return null;

        var text = await File.ReadAllTextAsync(full).ConfigureAwait(false);

        return new ProjectSource(XDocument.Parse(text, LoadOptions.PreserveWhitespace), text);
    }

    internal static string Serialized(XDocument document, string before)
    {
        var declaration = document.Declaration is { } head ? head.ToString() : string.Empty;
        var body = declaration + document.ToString(SaveOptions.DisableFormatting);

        return before.Length is 0
            ? body + Environment.NewLine
            : LineEndings.Adopt(Compacted(body, before), LineEndings.Dominant(before));
    }

    private static string Compacted(string after, string before) =>
        before.Contains(" />", StringComparison.Ordinal) || !before.Contains("/>", StringComparison.Ordinal)
            ? after
            : after.Replace(" />", "/>", StringComparison.Ordinal);

    private static void Detach(XElement element)
    {
        if (element.PreviousNode is XText whitespace && whitespace.Value.AsSpan().IsWhiteSpace())
            whitespace.Remove();

        element.Remove();
    }

    private static string Central(string projectPath, string centralPath, string before, string after, bool dryRun, bool verbose)
    {
        var relative = PositionFormat.Relative(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, centralPath);

        return dryRun || verbose
            ? UnifiedDiff.Between(relative, before, after)
            : string.Create(CultureInfo.InvariantCulture, $"{relative}  changedLines={UnifiedDiff.ChangedLines(before, after)}");
    }

    private static void Append(XElement parent, XElement element)
    {
        var indent = (parent.Elements().LastOrDefault()?.PreviousNode as XText)?.Value;

        if (parent.LastNode is XText tail && tail.Value.AsSpan().IsWhiteSpace())
            tail.AddBeforeSelf(new XText(indent ?? tail.Value + "  "), element);
        else
            parent.Add(element);
    }

    private static XElement Created(XElement root, string name)
    {
        var group = new XElement(name);

        Append(root, group);
        group.Add(new XText(Environment.NewLine + "  "));

        return group;
    }
}
