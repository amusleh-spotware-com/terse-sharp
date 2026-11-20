using System.Text.Json.Nodes;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class ToolGuardBindingTests
{
    private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "terse-guard-scratch").Replace('\\', '/');

    [Theory]
    [InlineData("cat {/tmp/terse-x,/tmp/terse-y/Leak.cs}")]
    [InlineData("head -n 3 /tmp/terse-x.{txt,cs}")]
    [InlineData("cat /tmp/terse-{a,b}/{notes.md,Leak.cs}")]
    public void Guard_ForABraceListOneOfWhoseAlternativesIsDotNetSourceOutsideTheTree_DeniesItAsSource(string command)
    {
        var verdict = Judge(command);

        Assert.True(verdict.Denied, command);
        Assert.Contains("is C#/.NET source", verdict.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Glob", "pattern", "**/*.{cs,razor}")]
    [InlineData("Grep", "glob", "*.{json,cs}")]
    public void Inspect_ForABuiltInWhoseBraceGlobListsDotNetSource_Denies(string tool, string key, string value) =>
        Assert.True(ToolGuard.Inspect(tool, new JsonObject { [key] = value }).Denied, value);

    [Theory]
    [InlineData("cat {/tmp/terse-x,/tmp/terse-y/notes.md}")]
    [InlineData("ls /tmp/terse-{a,b}")]
    [InlineData("echo {a,b}.cs")]
    public void Guard_ForABraceListNoReadAlternativeOfWhichIsDotNetSourceOutsideTheTree_StillAllowsIt(string command) =>
        Assert.False(Judge(command).Denied, command);

    [Fact]
    public void Inspect_ForAGlobWhoseBraceListNamesNoDotNetSource_StillAllows() =>
        Assert.False(ToolGuard.Inspect("Glob", new JsonObject { ["pattern"] = "**/*.{json,yml}" }).Denied);

    [Theory]
    [InlineData("printf -v f README.md; cat \"$f\"")]
    [InlineData("cat \"$f\"")]
    [InlineData("cat \"$f\" /tmp/terse-x")]
    [InlineData("grep -n FAIL \"$f\"")]
    public void Guard_ForATextReadOfABareVariableNoLiteralBinds_DeniesItNamingTheVariable(string command)
    {
        var verdict = Judge(command);

        Assert.True(verdict.Denied, command);
        Assert.True(ToolGuard.Unjudged(verdict), verdict.Reason);
    }

    [Theory]
    [InlineData("read f; cat \"$f\"")]
    [InlineData(". /tmp/terse-set.sh; cat \"$f\"")]
    public void Guard_ForABatchWhoseTextReadUsesAVariableABuiltinBinds_StripsTheReadFromWhatRuns(string command)
    {
        var verdict = Judge(command);

        Assert.True(verdict.Denied, command);
        Assert.DoesNotContain("cat", verdict.Rewrite ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_ForATextReadOfABracedVariableNoLiteralBinds_DeniesIt() =>
        Assert.True(Judge("cat \"${f}\"").Denied);

    [Fact]
    public void Guard_ForATextReadOfAVariableBoundToALiteralInsideTheTree_DeniesItLikeItsLiteral() =>
        Assert.True(Judge("LOG=build.log; tail -5 \"$LOG\"").Denied);

    [Theory]
    [InlineData("F=/tmp/terse-x.log; cat \"$F\"")]
    [InlineData("F=SCRATCH/x.log; tail -5 \"$F\"")]
    [InlineData("grep -n \"$pattern\" /tmp/terse-x.log")]
    [InlineData("for f in /tmp/terse-loop-a /tmp/terse-loop-b; do cat \"$f\"; done")]
    public void Guard_ForATextReadWhoseVariableIsALiteralOutsideTheTreeAPatternOrALoopWord_StillAllowsIt(string command) =>
        Assert.False(Judge(command).Denied, command);

    [Theory]
    [InlineData("L=SCRATCH/ci-windows.log; for f in \"$L\" SCRATCH/ci-ubuntu.log; do grep -n error \"$f\"; done")]
    [InlineData("L=SCRATCH; for f in \"$L/a.log\" \"$L/b.log\"; do tail -5 \"$f\"; done")]
    [InlineData("L=/tmp/terse-loop-a; for f in \"$L/a.log\" \"$L/b.log\"; do tail -5 \"$f\"; done")]
    public void Guard_ForALoopOverAVariableAssignedALiteralOutsideTheTreeBeforeIt_AllowsTheRead(string template)
    {
        var verdict = Judge(template);

        Assert.False(verdict.Denied, verdict.Reason);
    }

    [Theory]
    [InlineData("L=src; for f in \"$L/notes.md\"; do cat \"$f\"; done")]
    [InlineData("L=/tmp/terse-loop-a; L=src; for f in \"$L/notes.md\"; do cat \"$f\"; done")]
    [InlineData("L=/tmp/terse-loop-a; . /tmp/terse-set.sh; for f in \"$L/notes.md\"; do cat \"$f\"; done")]
    [InlineData("L=SCRATCH; for f in \"$L/a.log\" src/notes.md; do tail -5 \"$f\"; done")]
    public void Guard_ForALoopOverAVariableReassignedSourcedOrLandingInTheTree_StillDeniesTheRead(string template) =>
        Assert.True(Judge(template).Denied, template);

    [Theory]
    [InlineData("S=SCRATCH; for i in 1 2; do sed -i 's/a/b/' \"$S/r$i.py\"; done")]
    [InlineData("S=SCRATCH; for i in 1 2; do cat \"$S/r$i.log\"; done; python x & B=$!")]
    public void Guard_ForALoopOverAScratchVariableTheCommandAssignsBesideABackgroundJob_AllowsItInsteadOfCallingItUnassigned(string template)
    {
        var verdict = Judge(template);

        Assert.False(verdict.Denied, verdict.Reason);
    }

    [Fact]
    public void Guard_ForALoopOverAnAssignedVariableLandingInTheTree_DeniesItNamingTheLiteralNotAnUnassignedVariable()
    {
        var verdict = Judge("S=src; for i in 1 2; do sed -i 's/a/b/' \"$S/r$i.md\"; done; python x & B=$!");

        Assert.True(verdict.Denied);
        Assert.False(ToolGuard.Unjudged(verdict), verdict.Reason);
    }

    [Fact]
    public void Guard_ForAStatementAfterACdIntoADirectoryThatDoesNotExist_StillJudgesItInTheTree() =>
        Assert.True(Judge("cd SCRATCH/terse-no-such-dir && python scan.py > smoke.out; grep -n x smoke.out").Denied);

    private static GuardVerdict Judge(string template) =>
        ToolGuard.Inspect("Bash", new JsonObject { ["command"] = template.Replace("SCRATCH", Scratch, StringComparison.Ordinal) }, Fixtures.RepositoryRoot);
}
