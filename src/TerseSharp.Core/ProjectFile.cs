using System.Xml.Linq;

namespace TerseSharp.Core;

public static class ProjectFile
{
    public static async Task<Result<string>> Create(string projectPath, string kind, string? targetFramework, bool dryRun)
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

        return Rendered("project_create", projectPath, string.Empty, document.ToString(), dryRun);
    }

    public static Task<Result<string>> AddReference(string projectPath, string targetProject, bool dryRun) =>
        AddItem(projectPath, "ProjectReference", Relative(projectPath, targetProject), dryRun, "project_add_reference");

    public static Task<Result<string>> RemoveReference(string projectPath, string targetProject, bool dryRun) =>
        RemoveItem(projectPath, "ProjectReference", Relative(projectPath, targetProject), dryRun, "project_remove_reference");

    public static Task<Result<string>> AddPackage(string root, string projectPath, string package, string? version, bool dryRun)
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
            ? AddItem(projectPath, "PackageReference", package, dryRun, "package_add", version)
            : AddCentralPackage(projectPath, central, package, version, dryRun);
    }

    public static Task<Result<string>> RemovePackage(string projectPath, string package, bool dryRun) =>
        string.IsNullOrWhiteSpace(package)
            ? Task.FromResult(Result.Fail<string>(Errors.Blank("package")))
            : RemoveItem(projectPath, "PackageReference", package, dryRun, "package_remove");

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

    public static async Task<Result<string>> SetProperty(string projectPath, string name, string value, bool dryRun)
    {
        var document = Load(projectPath);

        if (document is null)
            return Result.Fail<string>(Errors.DocumentNotFound(projectPath));

        var before = document.ToString();
        var existing = document.Descendants(name).FirstOrDefault();

        if (existing is null)
            Group(document).Add(new XElement(name, value));
        else
            existing.Value = value;

        return await Save(projectPath, document, before, dryRun, "project_set_property", name + "=" + value).ConfigureAwait(false);
    }

    private static async Task<Result<string>> AddCentralPackage(
        string projectPath,
        string centralPath,
        string package,
        string? version,
        bool dryRun)
    {
        if (version is null)
        {
            return Result.Fail<string>(Errors.Invalid(
                "this solution uses central package management, so a version is required",
                "pass version=<x.y.z>; it is written to Directory.Packages.props"));
        }

        var central = XDocument.Load(centralPath);
        var before = central.ToString();

        if (central.Descendants("PackageVersion").All(element => !Named(element, package)))
            Group(central).Add(new XElement("PackageVersion", new XAttribute("Include", package), new XAttribute("Version", version)));

        if (!dryRun)
            await AtomicWrite.TextAsync(centralPath, central.ToString() + Environment.NewLine).ConfigureAwait(false);

        var added = await AddItem(projectPath, "PackageReference", package, dryRun, "package_add").ConfigureAwait(false);

        return added.IsOk
            ? Result.Ok(added.Value + "\n" + UnifiedDiff.Between(centralPath, before, central.ToString()))
            : added;
    }

    private static async Task<Result<string>> AddItem(
        string projectPath,
        string itemName,
        string include,
        bool dryRun,
        string tool,
        string? version = null)
    {
        var document = Load(projectPath);

        if (document is null)
            return Result.Fail<string>(Errors.DocumentNotFound(projectPath));

        if (Items(document, itemName).Contains(include, StringComparer.OrdinalIgnoreCase))
            return Result.Fail<string>(Errors.Invalid($"'{include}' is already referenced", "nothing to add"));

        var before = document.ToString();
        var element = new XElement(itemName, new XAttribute("Include", include));

        if (version is not null)
            element.Add(new XAttribute("Version", version));

        ItemGroup(document, itemName).Add(element);

        return await Save(projectPath, document, before, dryRun, tool, include).ConfigureAwait(false);
    }

    private static async Task<Result<string>> RemoveItem(string projectPath, string itemName, string include, bool dryRun, string tool)
    {
        var document = Load(projectPath);

        if (document is null)
            return Result.Fail<string>(Errors.DocumentNotFound(projectPath));

        var before = document.ToString();
        var element = document.Descendants(itemName).FirstOrDefault(candidate => Named(candidate, include));

        if (element is null)
            return Result.Fail<string>(Errors.Invalid($"'{include}' is not referenced", "check package_list"));

        element.Remove();

        return await Save(projectPath, document, before, dryRun, tool, include).ConfigureAwait(false);
    }

    private static bool Named(XElement element, string include) =>
        (element.Attribute("Include")?.Value ?? string.Empty)
            .Replace('\\', '/')
            .Equals(include.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string[] Items(XDocument document, string itemName) =>
        [.. document.Descendants(itemName).Select(element => element.Attribute("Include")?.Value).OfType<string>()];

    private static XElement ItemGroup(XDocument document, string itemName)
    {
        var existing = document.Descendants(itemName).FirstOrDefault()?.Parent;

        if (existing is not null)
            return existing;

        var created = new XElement("ItemGroup");

        document.Root!.Add(created);

        return created;
    }

    private static XElement Group(XDocument document)
    {
        var existing = document.Descendants("PropertyGroup").FirstOrDefault();

        if (existing is not null)
            return existing;

        var created = new XElement("PropertyGroup");

        document.Root!.Add(created);

        return created;
    }

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
        string tool,
        string argument)
    {
        var after = document.ToString() + Environment.NewLine;

        if (!dryRun)
            await AtomicWrite.TextAsync(Path.GetFullPath(projectPath), after).ConfigureAwait(false);

        return Rendered(tool, argument, before, after, dryRun);
    }

    private static Result<string> Rendered(string tool, string argument, string before, string after, bool dryRun)
    {
        var response = new ResponseBuilder(tool, argument);

        response.Summary(1, 1, "files changed");
        response.Note(dryRun ? "dryRun" : "applied");
        response.Line(UnifiedDiff.Between(argument, before, after));

        return Result.Ok(response.ToString());
    }

    private static string Relative(string projectPath, string target)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var full = Path.IsPathRooted(target) ? target : Path.Combine(directory, target);

        return Path.GetRelativePath(directory, Path.GetFullPath(full)).Replace('\\', '/');
    }

    private static XDocument? Load(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);

        return File.Exists(full) ? XDocument.Load(full) : null;
    }
}
