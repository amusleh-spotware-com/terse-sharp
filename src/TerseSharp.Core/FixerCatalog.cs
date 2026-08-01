using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TerseSharp.Core;

public sealed class FixerCatalog
{
    private const int MaxCatalogs = 32;

    private static readonly ConcurrentDictionary<string, FixerCatalog> Catalogs = new(StringComparer.Ordinal);

    private static readonly string[] BundledFixerAssemblies =
        ["Microsoft.CodeAnalysis.CSharp.Features", "Microsoft.CodeAnalysis.Features"];

    private readonly Dictionary<string, CodeFixProvider> providers;

    private FixerCatalog(Dictionary<string, CodeFixProvider> providers) => this.providers = providers;

    public int Count => providers.Count;

    public bool HasStyleFixers => providers.Keys.Any(id => id.StartsWith("IDE", StringComparison.Ordinal));

    public CodeFixProvider? For(string diagnosticId) =>
        providers.TryGetValue(diagnosticId, out var provider) ? provider : null;

    public static FixerCatalog For(Project project)
    {
        if (Catalogs.Count >= MaxCatalogs)
            Catalogs.Clear();

        return Catalogs.GetOrAdd(Key(project), _ => Build(project));
    }

    private static string Key(Project project) => string.Join(
        '|',
        project.Language,
        string.Join(';', project.AnalyzerReferences.Select(Stamped).Order(StringComparer.Ordinal)));

    private static FixerCatalog Build(Project project)
    {
        var providers = new Dictionary<string, CodeFixProvider>(StringComparer.Ordinal);

        foreach (var assembly in BundledFixerAssemblies.Select(Bundled).OfType<Assembly>())
            Collect(providers, Types(assembly), project.Language);

        foreach (var reference in project.AnalyzerReferences.OfType<AnalyzerFileReference>())
            Collect(providers, Types(reference), project.Language);

        return new FixerCatalog(providers);
    }

    private static Assembly? Bundled(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }

    private static void Collect(Dictionary<string, CodeFixProvider> providers, Type[] types, string language)
    {
        foreach (var provider in Providers(types, language))
        {
            foreach (var id in Fixable(provider))
                providers.TryAdd(id, provider);
        }
    }

    private static string[] Fixable(CodeFixProvider provider)
    {
        try
        {
            return [.. provider.FixableDiagnosticIds];
        }
        catch (Exception exception) when (exception is TypeLoadException or MissingMemberException or FileNotFoundException)
        {
            return [];
        }
    }

    private static IEnumerable<CodeFixProvider> Providers(Type[] types, string language)
    {
        foreach (var type in types)
        {
            if (!Exports(type, language))
                continue;

            if (Instantiate(type) is { } provider)
                yield return provider;
        }
    }

    private static bool Exports(Type type, string language) =>
        !type.IsAbstract
        && typeof(CodeFixProvider).IsAssignableFrom(type)
        && Attribute(type) is { } export
        && export.Languages.Contains(language, StringComparer.Ordinal);

    private static ExportCodeFixProviderAttribute? Attribute(Type type)
    {
        try
        {
            return type.GetCustomAttribute<ExportCodeFixProviderAttribute>();
        }
        catch (Exception exception) when (exception is TypeLoadException or FileNotFoundException or CustomAttributeFormatException)
        {
            return null;
        }
    }

    private static Type[] Types(AnalyzerFileReference reference)
    {
        try
        {
            return Types(reference.GetAssembly());
        }
        catch (Exception exception) when (exception is FileNotFoundException or BadImageFormatException or NotSupportedException or IOException)
        {
            return [];
        }
    }

    private static Type[] Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException loadFailure)
        {
            return [.. loadFailure.Types.OfType<Type>()];
        }
        catch (Exception exception) when (exception is FileNotFoundException or BadImageFormatException or NotSupportedException or IOException)
        {
            return [];
        }
    }

    private static CodeFixProvider? Instantiate(Type type)
    {
        try
        {
            return Activator.CreateInstance(type) as CodeFixProvider;
        }
        catch (Exception exception) when (exception is MissingMethodException or TargetInvocationException or TypeLoadException or MemberAccessException)
        {
            return null;
        }
    }
    private static string Stamped(AnalyzerReference reference)
    {
        var path = reference.FullPath ?? reference.Display ?? string.Empty;

        try
        {
            var file = new FileInfo(path);

            return file.Exists
                ? string.Create(CultureInfo.InvariantCulture, $"{path}@{file.LastWriteTimeUtc.Ticks}:{file.Length}")
                : path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }
}
