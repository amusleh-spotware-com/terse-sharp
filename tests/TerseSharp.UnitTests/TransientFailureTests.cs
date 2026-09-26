using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TransientFailureTests
{
    [Fact]
    public void IsBuildHostFailure_RecognisesTheRpcTypesMsBuildsOutOfProcessHostThrows()
    {
        Assert.True(Errors.IsBuildHostFailure(new RemoteInvocationException("the pipe is broken")));
        Assert.True(Errors.IsBuildHostFailure(new InvalidOperationException("apply failed", new RemoteInvocationException("gone"))));
        Assert.True(Errors.IsBuildHostFailure(new ConnectionLostException("gone")));
    }

    [Fact]
    public void IsBuildHostFailure_LeavesEveryOtherFailureAlone()
    {
        Assert.False(Errors.IsBuildHostFailure(new InvalidOperationException("plain")));
        Assert.False(Errors.IsBuildHostFailure(new IOException("locked", new UnauthorizedAccessException())));
    }

    [Fact]
    public void Transient_RendersItsOwnCodeAndTellsTheCallerToRetry()
    {
        var rendered = Errors.Transient(new RemoteInvocationException("the pipe is broken")).Render();

        Assert.StartsWith("ERROR Transient: RemoteInvocationException: the pipe is broken", rendered, StringComparison.Ordinal);
        Assert.Contains("the project file was restored", rendered, StringComparison.Ordinal);
        Assert.Contains("may already be on disk", rendered, StringComparison.Ordinal);
        Assert.Contains("Retry the same call", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("server defect", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a\r\nb\r\n", "a\nb\n", true)]
    [InlineData("a\nb\n", "a\r\nb\r\n", true)]
    [InlineData("", "", true)]
    [InlineData("a\nb", "a\nb\n", false)]
    [InlineData("a\nc\n", "a\nb\n", false)]
    public void Same_IgnoresOnlyTheLineEndingTheWriteAdopted(string disk, string requested, bool expected) =>
        Assert.Equal(expected, LandedWrites.Same(disk, requested));

    [Fact]
    public async Task LandingAsync_NamesOnlyTheTargetsWhoseDiskTextIsTheRequestedContent()
    {
        var root = Root();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Adopted.cs"), "class A\r\n{\r\n}\r\n", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "Stale.cs"), "class B\n{\n}\n", TestContext.Current.CancellationToken);

            var landing = await LandedWrites.LandingAsync(
                root,
                [new("Adopted.cs", "class A\n{\n}\n"), new("Stale.cs", "class B2\n{\n}\n"), new("Absent.cs", "class C;\n")],
                CancellationToken.None);

            Assert.Equal("Adopted.cs", Assert.Single(landing.Landed));
            Assert.Equal("Stale.cs,Absent.cs", string.Join(',', landing.Missing));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GuardedAsync_AWriteTheBuildHostDroppedAfterItLanded_AnswersLandedAndSteersToAnalyzeInsteadOfARetry()
    {
        const string Content = "namespace Probe;\n\npublic sealed class Landed;\n";
        var root = Root();

        try
        {
            var result = await LandedWrites.GuardedAsync(
                root,
                [new("Landed.cs", Content)],
                false,
                async () =>
                {
                    await File.WriteAllTextAsync(Path.Combine(root, "Landed.cs"), Content.ReplaceLineEndings("\r\n"), TestContext.Current.CancellationToken);

                    throw new InvalidOperationException("apply failed", new RemoteInvocationException("the pipe is broken"));
                },
                CancellationToken.None);

            Assert.False(result.IsOk);

            var rendered = result.Error!.Render();

            Assert.StartsWith("ERROR Transient: InvalidOperationException: apply failed  landed=Landed.cs  retry would be a no-op\n", rendered, StringComparison.Ordinal);
            Assert.Contains("Do not retry - call analyze path=Landed.cs for the diagnostics", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("Retry the same call", rendered, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GuardedAsync_ABatchTheBuildHostDroppedHalfway_NamesWhatLandedAndWhatToRetry()
    {
        var root = Root();

        try
        {
            var result = await LandedWrites.GuardedAsync(
                root,
                [new("First.cs", "class First;\n"), new("Second.cs", "class Second;\n")],
                false,
                async () =>
                {
                    await File.WriteAllTextAsync(Path.Combine(root, "First.cs"), "class First;\n", TestContext.Current.CancellationToken);

                    throw new ConnectionLostException("gone");
                },
                CancellationToken.None);

            var rendered = result.Error!.Render();

            Assert.StartsWith("ERROR Transient: ConnectionLostException: gone  landed=First.cs  not landed=Second.cs\n", rendered, StringComparison.Ordinal);
            Assert.Contains("Retry with only the files not landed, then call analyze path=First.cs", rendered, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GuardedAsync_AWriteTheBuildHostDroppedBeforeItLanded_RethrowsSoTheBoundaryStillPrescribesTheRetry()
    {
        var root = Root();

        try
        {
            await Assert.ThrowsAsync<RemoteInvocationException>(() => LandedWrites.GuardedAsync(
                root, [new("Never.cs", "class Never;\n")], false, () => throw new RemoteInvocationException("the pipe is broken"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GuardedAsync_ADryRunOrAnotherFailure_IsNeverAnsweredAsLanded()
    {
        var root = Root();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Present.cs"), "class Present;\n", TestContext.Current.CancellationToken);
            FileService.FileWrite[] targets = [new("Present.cs", "class Present;\n")];

            await Assert.ThrowsAsync<RemoteInvocationException>(() => LandedWrites.GuardedAsync(
                root, targets, true, () => throw new RemoteInvocationException("dropped"), CancellationToken.None));
            await Assert.ThrowsAsync<IOException>(() => LandedWrites.GuardedAsync(
                root, targets, false, () => throw new IOException("locked"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string Root() => Directory.CreateTempSubdirectory("terse-landing-").FullName;

    private sealed class RemoteInvocationException(string message) : Exception(message);

    private sealed class ConnectionLostException(string message) : Exception(message);
}
