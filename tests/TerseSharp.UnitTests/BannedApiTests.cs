namespace TerseSharp.UnitTests;

public sealed class BannedApiTests
{
    private const int MaxSuppressions = 18;

    private static readonly string Source = Path.Combine(Fixtures.RepositoryRoot, "src");

    private static readonly string Banned = File.ReadAllText(Path.Combine(Source, "BannedSymbols.txt"));

    private static readonly string Props = File.ReadAllText(Path.Combine(Source, "Directory.Build.props"));

    [Fact]
    public void TheBanList_CarriesEveryApiTheHardGatesForbid()
    {
        string[] required =
        [
            "P:System.Threading.Tasks.Task`1.Result",
            "P:System.Threading.Tasks.ValueTask`1.Result",
            "M:System.Threading.Tasks.Task.Wait()",
            "M:System.Threading.Tasks.Task.WaitAll(System.Threading.Tasks.Task[])",
            "M:System.Threading.Tasks.Task.WaitAny(System.Threading.Tasks.Task[])",
            "M:System.Runtime.CompilerServices.TaskAwaiter.GetResult()",
            "M:System.Runtime.CompilerServices.ValueTaskAwaiter.GetResult()",
            "M:System.Threading.Thread.Sleep(System.Int32)",
            "M:System.IO.File.ReadAllText(System.String)",
            "M:System.IO.File.ReadAllLines(System.String)",
            "M:System.IO.File.ReadLines(System.String)",
            "M:System.IO.File.ReadAllBytes(System.String)",
            "M:System.IO.File.WriteAllText(System.String,System.String)",
            "M:System.IO.File.WriteAllBytes(System.String,System.Byte[])",
            "M:System.IO.StreamReader.ReadToEnd()",
            "M:System.Xml.Linq.XDocument.Load(System.String)",
        ];

        Assert.All(required, symbol => Assert.Contains(symbol + ";", Banned, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryBannedSymbol_NamesTheReplacementToUseInstead()
    {
        var entries = Banned.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.True(entry.Split(';') is [{ Length: > 10 }, { Length: > 20 }], entry));
    }

    [Fact]
    public void TheProductionProjects_ReferenceTheAnalyzerAndItsBanList()
    {
        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", Props, StringComparison.Ordinal);
        Assert.Contains("<AdditionalFiles Include=\"$(MSBuildThisFileDirectory)BannedSymbols.txt\" />", Props, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySuppressionOfTheBan_CarriesAJustificationAndTheSetOnlyEverShrinks()
    {
        var suppressed = Suppressions();

        Assert.NotEmpty(suppressed);
        Assert.All(suppressed, line => Assert.Contains("Justification = \"", line, StringComparison.Ordinal));
        Assert.True(
            suppressed.Count <= MaxSuppressions,
            $"{suppressed.Count} suppressions of RS0030 against a ratchet of {MaxSuppressions}; the set may only shrink");
    }

    private static List<string> Suppressions()
    {
        var found = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Source, "*.cs", SearchOption.AllDirectories))
        {
            if (Generated(file))
                continue;

            found.AddRange(File.ReadLines(file).Where(line => line.Contains("RS0030:Do not use banned APIs", StringComparison.Ordinal)));
        }

        return found;
    }

    private static bool Generated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
