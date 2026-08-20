using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TestReporterProbeTests
{
    [Fact]
    public async Task Detect_WhenGlobalJsonDeclaresNoRunner_StaysOnTheVsTestLogger() =>
        Assert.Equal(TestReporter.VsTestLogger, await DetectAsync("""{ "sdk": { "version": "10.0.300" } }""", XunitProject));

    [Fact]
    public async Task Detect_WhenGlobalJsonNamesVsTest_StaysOnTheVsTestLogger() =>
        Assert.Equal(TestReporter.VsTestLogger, await DetectAsync("""{ "test": { "runner": "VSTest" } }""", XunitProject));

    [Fact]
    public async Task Detect_WhenGlobalJsonIsNotAnObject_StaysOnTheVsTestLoggerRatherThanThrowing() =>
        Assert.Equal(TestReporter.VsTestLogger, await DetectAsync("[]", XunitProject));

    [Fact]
    public async Task Detect_WhenGlobalJsonIsMalformed_StaysOnTheVsTestLoggerRatherThanThrowing() =>
        Assert.Equal(TestReporter.VsTestLogger, await DetectAsync("{ not json", XunitProject));

    [Fact]
    public async Task Detect_WhenGlobalJsonNamesThePlatformRunnerForAnXunitProject_PicksTheXunitReport() =>
        Assert.Equal(TestReporter.XunitTrx, await DetectAsync(PlatformRunner, XunitProject));

    [Fact]
    public async Task Detect_WhenTheProjectReferencesTheTrxExtension_PicksThePlatformReport() =>
        Assert.Equal(
            TestReporter.TestingPlatformTrx,
            await DetectAsync(PlatformRunner, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="xunit.v3" />
                    <PackageReference Include="Microsoft.Testing.Extensions.TrxReport" />
                  </ItemGroup>
                </Project>
                """));

    [Fact]
    public async Task Detect_WhenTheProjectOnlyReferencesTheTrxAbstractions_StillPicksTheXunitReport() =>
        Assert.Equal(
            TestReporter.XunitTrx,
            await DetectAsync(PlatformRunner, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="xunit.v3" />
                    <PackageReference Include="Microsoft.Testing.Extensions.TrxReport.Abstractions" />
                  </ItemGroup>
                </Project>
                """));

    [Fact]
    public async Task Detect_ForAnMsTestSdkProjectUnderThePlatformRunner_PicksThePlatformReport() =>
        Assert.Equal(
            TestReporter.TestingPlatformTrx,
            await DetectAsync(PlatformRunner, """
                <Project Sdk="MSTest.Sdk">
                </Project>
                """));

    [Fact]
    public async Task Detect_WhenNoProjectDeclaresAnyTrxReporter_SaysSoRatherThanGuessingOne() =>
        Assert.Equal(
            TestReporter.Unknown,
            await DetectAsync(PlatformRunner, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="NUnit" />
                  </ItemGroup>
                </Project>
                """));

    [Fact]
    public async Task Detect_ForTheSplitXunitPackageLayout_StillPicksTheXunitReport() =>
        Assert.Equal(
            TestReporter.XunitTrx,
            await DetectAsync(PlatformRunner, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="xunit.v3.core" />
                    <PackageReference Include="xunit.v3.runner.inproc.console" />
                  </ItemGroup>
                </Project>
                """));

    [Fact]
    public async Task Detect_WhenTheTrxExtensionIsSingleQuoted_StillPicksThePlatformReport() =>
        Assert.Equal(
            TestReporter.TestingPlatformTrx,
            await DetectAsync(PlatformRunner, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include='Microsoft.Testing.Extensions.TrxReport' />
                  </ItemGroup>
                </Project>
                """));

    private const string PlatformRunner = """{ "test": { "runner": "Microsoft.Testing.Platform" } }""";

    private const string XunitProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="xunit.v3" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public async Task UsesTestingPlatform_ReadsTheGlobalJsonAboveTheWorkingDirectory()
    {
        var root = Directory.CreateTempSubdirectory("terse-runner-");

        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "deep"));

            await File.WriteAllTextAsync(Path.Combine(root.FullName, "global.json"), PlatformRunner, TestContext.Current.CancellationToken);

            Assert.True(await TestReporterProbe.UsesTestingPlatformAsync(nested.FullName, TestContext.Current.CancellationToken));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UsesTestingPlatform_StopsAtTheNearestGlobalJsonRatherThanWalkingPastIt()
    {
        var root = Directory.CreateTempSubdirectory("terse-runner-");

        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root.FullName, "src"));

            await File.WriteAllTextAsync(Path.Combine(root.FullName, "global.json"), PlatformRunner, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(nested.FullName, "global.json"), "{ }", TestContext.Current.CancellationToken);

            Assert.False(await TestReporterProbe.UsesTestingPlatformAsync(nested.FullName, TestContext.Current.CancellationToken));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static async Task<TestReporter> DetectAsync(string globalJson, string project)
    {
        var root = Directory.CreateTempSubdirectory("terse-runner-");

        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "global.json"), globalJson, TestContext.Current.CancellationToken);

            var path = Path.Combine(root.FullName, "Probe.csproj");

            await File.WriteAllTextAsync(path, project, TestContext.Current.CancellationToken);

            return await TestReporterProbe.DetectAsync(root.FullName, path, TestContext.Current.CancellationToken);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
