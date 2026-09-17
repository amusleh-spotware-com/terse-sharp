using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TerseSharp.Core;

public readonly record struct ProjectSnapshot(string ProjectPath, byte[] Bytes, IReadOnlyList<string> AddedFiles);

public static class ProjectFileGuard
{
    public static async Task<ProjectSnapshot?> CaptureAsync(
        string? projectPath,
        IReadOnlyList<string> addedFiles,
        CancellationToken cancellationToken)
    {
        if (projectPath is not { Length: > 0 } path || addedFiles.Count is 0 || !File.Exists(path))
            return null;

        if (!Globs(path))
            return null;

        return new ProjectSnapshot(path, await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), addedFiles);
    }

    private static bool Globs(string path) => ProjectGlobs.Memoized(path);

    public static async Task<bool> RestoreAsync(ProjectSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!File.Exists(snapshot.ProjectPath))
            return false;

        var current = await File.ReadAllBytesAsync(snapshot.ProjectPath, cancellationToken).ConfigureAwait(false);

        if (current.AsSpan().SequenceEqual(snapshot.Bytes))
            return false;

        if (!OnlyRedundantCompileItems(Text(snapshot.Bytes), Text(current), snapshot.AddedFiles))
            return false;

        await AtomicWrite.BytesAsync(snapshot.ProjectPath, snapshot.Bytes, cancellationToken).ConfigureAwait(false);

        return true;
    }

    internal static bool OnlyRedundantCompileItems(string before, string after, IReadOnlyList<string> addedFiles)
    {
        if (Parsed(before) is not { } original || Parsed(after) is not { } rewritten)
            return false;

        Strip(original, addedFiles);
        Strip(rewritten, addedFiles);

        return XNode.DeepEquals(original, rewritten);
    }

    private static XElement? Parsed(string text)
    {
        try
        {
            if (XDocument.Parse(text).Root is not { } root)
                return null;

            Condense(root);

            return root;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static void Condense(XElement element)
    {
        foreach (var node in element.Nodes().OfType<XText>().Where(IsBlank).ToArray())
            node.Remove();

        foreach (var child in element.Elements())
            Condense(child);
    }

    private static bool IsBlank(XText node) => node.Value.AsSpan().IsWhiteSpace();

    private static void Strip(XElement root, IReadOnlyList<string> addedFiles)
    {
        foreach (var item in root.Descendants().Where(element => IsAddedItem(element, addedFiles)).ToArray())
        {
            if (item.Parent is not { } group)
                continue;

            item.Remove();

            if (group is { HasAttributes: false, HasElements: false, Name.LocalName: "ItemGroup", Parent: not null })
                group.Remove();
        }
    }

    private static bool IsAddedItem(XElement element, IReadOnlyList<string> addedFiles)
    {
        if (element.Name.LocalName is not "Compile" || element.HasElements)
            return false;

        if (element.Attribute("Include") is not { NextAttribute: null, PreviousAttribute: null } include)
            return false;

        var name = Path.GetFileName(include.Value.AsSpan());

        foreach (var file in addedFiles)
        {
            if (name.Equals(Path.GetFileName(file.AsSpan()), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('﻿');
}
