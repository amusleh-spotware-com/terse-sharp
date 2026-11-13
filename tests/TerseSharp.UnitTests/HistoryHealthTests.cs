using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class HistoryHealthTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "terse-history-root");

    [Fact]
    public void Findings_ForAFileFourAuthorsChanged_ReportsDeveloperCongestionAsAHeuristicCodeHealthLine() =>
        Assert.Equal(
            "TERSE114 warning Policy " + Path.Combine("src", "A.cs") + ":1:1: <file> - developer congestion HEURISTIC: 4 authors in the last 200 commits exceeds 3",
            Assert.Single(Findings(Log(("a", ["src/A.cs"]), ("b", ["src/A.cs"]), ("c", ["src/A.cs"]), ("d", ["src/A.cs"])), "src/A.cs")));

    [Fact]
    public void Findings_ForAFileThreeAuthorsChanged_ReportsNothing() =>
        Assert.Empty(Findings(Log(("a", ["src/A.cs"]), ("b", ["src/A.cs"]), ("c", ["src/A.cs"])), "src/A.cs"));

    [Fact]
    public void Findings_ForACSharpFileChangedWithItInThreeOfFourCommits_ReportsChangeCouplingNamingThePartner() =>
        Assert.Equal(
            "TERSE114 warning Policy " + Path.Combine("src", "A.cs") + ":1:1: <file> - change coupling HEURISTIC: co-changed with " + Path.Combine("src", "B.cs") + " in 3 of its 4 commits reaches 50%",
            Assert.Single(Findings(Log(("a", ["src/A.cs", "src/B.cs"]), ("a", ["src/A.cs", "src/B.cs"]), ("a", ["src/A.cs", "src/B.cs"]), ("a", ["src/A.cs"])), "src/A.cs")));

    [Fact]
    public void Findings_ForAPartnerBelowThreeCommitsOrHalfTheFilesCommits_ReportsNothing()
    {
        var twice = Log(("a", ["src/A.cs", "src/B.cs"]), ("a", ["src/A.cs", "src/B.cs"]));
        var rarely = Log(("a", ["src/A.cs", "src/B.cs"]), ("a", ["src/A.cs", "src/B.cs"]), ("a", ["src/A.cs", "src/B.cs"]), ("a", ["src/A.cs"]), ("a", ["src/A.cs"]), ("a", ["src/A.cs"]), ("a", ["src/A.cs"]));

        Assert.Empty(Findings(twice, "src/A.cs"));
        Assert.Empty(Findings(rarely, "src/A.cs"));
    }

    [Fact]
    public void Findings_ForANonCSharpFileChangedInEveryCommit_ReportsNoCoupling() =>
        Assert.Empty(Findings(Log(("a", ["src/A.cs", "CHANGELOG.md"]), ("a", ["src/A.cs", "CHANGELOG.md"]), ("a", ["src/A.cs", "CHANGELOG.md"])), "src/A.cs"));

    [Fact]
    public void Findings_ForCrlfOutputAndAFileTheLogNeverNames_MatchesOnlyTheNamedFile()
    {
        var log = Log(("a", ["src/A.cs"]), ("b", ["src/A.cs"]), ("c", ["src/A.cs"]), ("d", ["src/A.cs"])).Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.Single(HistoryHealth.Findings(log, Root, [Full("src/A.cs"), Full("src/Other.cs")], PolicyAction.Reject));
        Assert.Empty(HistoryHealth.Findings(string.Empty, Root, [Full("src/A.cs")], PolicyAction.Reject));
    }

    private static IReadOnlyList<string> Findings(string log, string file) =>
        HistoryHealth.Findings(log, Root, [Full(file)], PolicyAction.Reject);

    private static string Full(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Log(params (string Author, string[] Paths)[] commits) =>
        string.Concat(commits.Select(commit => "\0" + commit.Author + "\n\n" + string.Join('\n', commit.Paths) + "\n"));
}
