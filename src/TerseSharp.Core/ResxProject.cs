using System.Xml.Linq;

namespace TerseSharp.Core;

public static class ResxProject
{
    public static string Wiring(string neutralPath, string newFile)
    {
        var project = Nearest(Path.GetDirectoryName(newFile) ?? Path.GetDirectoryName(neutralPath));

        return project is null
            ? "csprojWiring=unknown - no project file was found above the new file"
            : Describe(project);
    }

    public static string? Nearest(string? directory)
    {
        var current = directory;

        while (current is { Length: > 0 })
        {
            var project = Projects(current).FirstOrDefault();

            if (project is not null)
                return project;

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static string Describe(string project)
    {
        var document = Load(project);

        return document is null || !IsSdk(document) || Excluded(document)
            ? string.Create(CultureInfo.InvariantCulture, $"csprojWiring=required - add an EmbeddedResource item for the new file to {Path.GetFileName(project)}")
            : "csprojWiring=automatic";
    }

    private static bool IsSdk(XDocument document) => document.Root?.Attribute("Sdk") is not null;

    private static bool Excluded(XDocument document) => Disabled(document) || Removed(document);

    private static bool Disabled(XDocument document) => document
        .Descendants()
        .Any(element => string.Equals(element.Name.LocalName, "EnableDefaultEmbeddedResourceItems", StringComparison.Ordinal)
            && string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

    private static bool Removed(XDocument document) => document
        .Descendants()
        .Any(element => string.Equals(element.Name.LocalName, "EmbeddedResource", StringComparison.Ordinal)
            && element.Attribute("Remove") is not null);

    private static string[] Projects(string directory)
    {
        try
        {
            return [.. Directory.EnumerateFiles(directory, "*.*proj").Where(file => file.EndsWith("proj", StringComparison.OrdinalIgnoreCase))];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static XDocument? Load(string project)
    {
        try
        {
            return XDocument.Load(project);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
