using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public sealed class ShadowCopyAnalyzerLoader : IAnalyzerAssemblyLoader
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, Assembly> assemblies = new(PathBoundary.Comparer);
    private readonly ConcurrentDictionary<string, string> shadows = new(PathBoundary.Comparer);
    private readonly ConcurrentDictionary<string, byte> dependencies = new(PathBoundary.Comparer);

    private ShadowCopyAnalyzerLoader() => AssemblyLoadContext.Default.Resolving += (_, name) => Probe(name);

    public static ShadowCopyAnalyzerLoader Shared { get; } = new();

    public static string Root { get; } = Path.Combine(Base(), "terse-analyzers");

    public void AddDependencyLocation(string fullPath)
    {
        if (Containing(fullPath) is { Length: > 0 } directory)
            dependencies.TryAdd(directory, 0);
    }

    public Assembly LoadFromPath(string fullPath)
    {
        AddDependencyLocation(fullPath);

        return assemblies.GetOrAdd(Path.GetFullPath(fullPath), Load);
    }

    public static void Sweep()
    {
        var cutoff = DateTime.UtcNow - Lifetime;

        foreach (var directory in Stale(cutoff))
            Discard(directory);
    }

    private static string[] Stale(DateTime cutoff)
    {
        try
        {
            var root = new DirectoryInfo(Root);

            return root.Exists
                ? [.. root.EnumerateDirectories().Where(entry => entry.LastWriteTimeUtc < cutoff).Select(entry => entry.FullName)]
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? Containing(string path)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private Assembly? Probe(AssemblyName name)
    {
        if (name.Name is not { Length: > 0 } simple)
            return null;

        var fileName = simple + ".dll";

        foreach (var directory in dependencies.Keys)
        {
            var candidate = Path.Combine(directory, fileName);

            if (File.Exists(candidate) && Matches(candidate, name))
                return LoadFromPath(candidate);
        }

        return null;
    }
    private Assembly Load(string original)
    {
        if (Path.GetDirectoryName(original) is not { Length: > 0 } directory)
        {
            InPlace.TryAdd(original, 0);

            return Map(original);
        }

        var copy = Path.Combine(Shadow(directory), Path.GetFileName(original));

        if (!File.Exists(copy))
        {
            InPlace.TryAdd(directory, 0);

            return Map(original);
        }

        InPlace.TryRemove(directory, out _);

        return Map(copy);
    }

    private string Shadow(string directory)
    {
        if (shadows.TryGetValue(directory, out var known))
            return known;

        var copied = Copy(directory);

        if (!PathBoundary.SameFile(copied, directory))
            shadows.TryAdd(directory, copied);

        return copied;
    }

    private static string Copy(string directory)
    {
        var files = Files(directory);

        if (files.Length is 0)
            return directory;

        var destination = Path.Combine(Root, Stamp(directory, files));

        if (!Directory.Exists(destination))
            Publish(directory, files, destination);

        return Directory.Exists(destination) ? destination : directory;
    }
    private static FileInfo[] Files(string directory)
    {
        try
        {
            var found = new DirectoryInfo(directory).GetFiles("*", SearchOption.AllDirectories);

            Array.Sort(found, (left, right) => string.CompareOrdinal(left.FullName, right.FullName));

            return found;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Stamp(string directory, FileInfo[] files)
    {
        var text = new StringBuilder(directory);

        foreach (var file in files)
        {
            text.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"|{Path.GetRelativePath(directory, file.FullName)}@{file.LastWriteTimeUtc.Ticks}:{file.Length}"));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())).AsSpan(0, 8));
    }

    private static void Publish(string directory, FileInfo[] files, string destination)
    {
        var staging = destination + ".tmp-" + Guid.NewGuid().ToString("n");

        try
        {
            EnsureRoot();

            foreach (var file in files)
                Place(directory, file, staging);

            Directory.Move(staging, destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(staging);
        }
    }
    private static void Place(string directory, FileInfo file, string staging)
    {
        var target = Path.Combine(staging, Path.GetRelativePath(directory, file.FullName));

        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? staging);
        file.CopyTo(target, overwrite: true);
    }

    private static void Discard(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }

    private static Assembly Map(string path)
    {
        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
        catch (FileLoadException)
        {
            if (Existing(path) is { } loaded)
                return loaded;

            throw;
        }
    }
    private static Assembly? Existing(string path)
    {
        var name = AssemblyName.GetAssemblyName(path).Name;

        return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureRoot()
    {
        if (Directory.Exists(Root))
            return;

        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(Root);
        else
            Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static string Base() =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } local
            ? local
            : Path.GetTempPath();

    private static bool Matches(string candidate, AssemblyName requested)
    {
        try
        {
            var found = AssemblyName.GetAssemblyName(candidate);

            return string.Equals(found.Name, requested.Name, StringComparison.OrdinalIgnoreCase)
                && (requested.Version is null || found.Version >= requested.Version);
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileNotFoundException or IOException)
        {
            return false;
        }
    }

    private static readonly ConcurrentDictionary<string, byte> InPlace = new(StringComparer.OrdinalIgnoreCase);

    public static string[] InPlaceDirectories => [.. InPlace.Keys];
}
