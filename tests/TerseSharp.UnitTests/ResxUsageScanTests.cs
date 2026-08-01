using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ResxUsageScanTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-resx-usage-");

    public void Dispose() => directory.Delete(recursive: true);

    [Fact]
    public void Textual_FindsTheQuotedLiteralAndTheMemberAccess()
    {
        Write("Caller.cs", "var caption = Strings.Caption_Submit;\nvar other = Manager.GetString(\"Caption_Submit\");");

        var usages = ResxUsageService.Textual(directory.FullName, "Caption_Submit");

        Assert.Equal(2, usages.Count);
        Assert.Contains(usages, usage => usage.Form is "member");
        Assert.Contains(usages, usage => usage.Form is "GetString");
    }

    [Fact]
    public void Textual_ClassifiesAUidAndALocalizerIndexer()
    {
        Write("View.xaml", "<TextBlock x:Uid=\"Caption_Submit\" />");
        Write("Index.cshtml", "<h1>@Localizer[\"Caption_Submit\"]</h1>");

        var usages = ResxUsageService.Textual(directory.FullName, "Caption_Submit");

        Assert.Contains(usages, usage => usage.Form is "x:Uid");
        Assert.Contains(usages, usage => usage.Form is "localizer[]");
    }

    [Fact]
    public void Textual_IgnoresTheGeneratedDesignerFile()
    {
        Write("Strings.Designer.cs", "public static string Caption_Submit => Manager.GetString(\"Caption_Submit\");");

        Assert.Empty(ResxUsageService.Textual(directory.FullName, "Caption_Submit"));
    }

    [Fact]
    public void Textual_IsEveryUsageHeuristic()
    {
        Write("Caller.cs", "var caption = Strings.Caption_Submit;");

        Assert.All(ResxUsageService.Textual(directory.FullName, "Caption_Submit"), usage => Assert.Equal(Confidence.Heuristic, usage.Confidence));
    }

    [Fact]
    public void ComposedLookups_CountsAKeyBuiltAtRuntimeButNotALiteralOne()
    {
        Write("Caller.cs", "Manager.GetString(\"Caption_Total\");\nManager.GetString(\"Caption_\" + suffix);\nLocalizer[$\"Msg_{kind}\"];");

        Assert.Equal(2, ResxUsageService.ComposedLookups(directory.FullName));
    }

    [Fact]
    public void IsScannable_SkipsResourceFilesThemselves()
    {
        Assert.False(ResxUsageService.IsScannable("Strings.resx"));
        Assert.True(ResxUsageService.IsScannable("Caller.cs"));
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(directory.FullName, name), content);
}
