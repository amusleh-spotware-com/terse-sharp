using Microsoft.CodeAnalysis;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class TestScopeTests
{
    [Fact]
    public void Of_ForAProjectReferencingATestFramework_ReportsTest() =>
        Assert.Equal("test", TestScope.Of(Project(typeof(FactAttribute).Assembly.Location)));

    [Fact]
    public void Of_ForAProjectWithNoTestFramework_ReportsSrc() =>
        Assert.Equal("src", TestScope.Of(Project(typeof(object).Assembly.Location)));

    [Fact]
    public void Of_ForAProjectWithNoReferences_ReportsSrc() =>
        Assert.Equal("src", TestScope.Of(Project(null)));

    private static Project Project(string? reference)
    {
        using var workspace = new AdhocWorkspace();

        var info = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "Sample",
            "Sample",
            LanguageNames.CSharp,
            metadataReferences: reference is null ? [] : [MetadataReference.CreateFromFile(reference)]);

        return workspace.AddProject(info);
    }
}
