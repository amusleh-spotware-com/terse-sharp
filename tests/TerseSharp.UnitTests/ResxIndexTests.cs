using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ResxIndexTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-resx-index-");

    public void Dispose() => directory.Delete(recursive: true);

    [Fact]
    public void Build_GroupsTheCultureFilesUnderTheirNeutralFile()
    {
        Write("Strings.resx", Entry("Alpha", "First"));
        Write("Strings.fr.resx", Entry("Alpha", "Premier"));
        Write("Strings.de.resx", Entry("Alpha", "Erste"));

        var family = Single();

        Assert.Equal("Strings", family.Name);
        Assert.Equal(["de", "fr"], family.Cultures.Select(file => file.Culture).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Build_DoesNotReadANonCultureSegmentAsACulture()
    {
        Write("Order.Web.resx", Entry("Alpha", "First"));

        var family = Single();

        Assert.Equal("Order.Web", family.Name);
        Assert.Empty(family.Cultures);
    }

    [Fact]
    public void Build_MarksAWinFormsDesignerFile()
    {
        Write("Form1.resx", Entry("$this.Text", "Form"));

        Assert.Equal(ResxKind.WinForms, Single().Kind);
    }

    [Fact]
    public void Build_MarksAReswFile()
    {
        Write("Resources.resw", Entry("MainPage.Text", "Orders"));

        Assert.Equal(ResxKind.Resw, Single().Kind);
    }

    [Fact]
    public void Build_FindsTheDesignerBesideTheNeutralFile()
    {
        Write("Strings.resx", Entry("Alpha", "First"));
        File.WriteAllText(Path.Combine(directory.FullName, "Strings.Designer.cs"), "class Strings { }");

        Assert.Equal("Strings.Designer.cs", Single().Designer);
    }

    [Fact]
    public void Build_SkipsExcludedDirectories()
    {
        Directory.CreateDirectory(Path.Combine(directory.FullName, "obj"));
        File.WriteAllText(Path.Combine(directory.FullName, "obj", "Generated.resx"), Document(Entry("Alpha", "First")));
        Write("Strings.resx", Entry("Alpha", "First"));

        Assert.Equal("Strings", Single().Name);
    }

    [Fact]
    public void Read_ReturnsTheCachedDocumentWhileTheFileIsUnchanged()
    {
        var path = Write("Strings.resx", Entry("Alpha", "First"));

        Assert.Same(ResxIndex.Read(path).Value, ResxIndex.Read(path).Value);
    }

    [Fact]
    public void Read_OnAMissingFile_ReportsDocumentNotFound()
    {
        var parsed = ResxIndex.Read(Path.Combine(directory.FullName, "Absent.resx"));

        Assert.False(parsed.IsOk);
        Assert.Equal(TerseErrorCode.DocumentNotFound, parsed.Error!.Code);
    }

    [Theory]
    [InlineData("fr", true)]
    [InlineData("fr-FR", true)]
    [InlineData("Web", false)]
    [InlineData("dev", false)]
    [InlineData("api", false)]
    [InlineData("Designer", false)]
    public void IsCulture_AcceptsOnlyRealCultures(string token, bool expected) =>
        Assert.Equal(expected, ResxCulture.IsCulture(token));

    private ResxFamily Single() => Assert.Single(ResxIndex.Build(directory.FullName).Families);

    private string Write(string name, string entries)
    {
        var path = Path.Combine(directory.FullName, name);

        File.WriteAllText(path, Document(entries));

        return path;
    }

    private static string Document(string entries) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n" + entries + "</root>\n";

    private static string Entry(string name, string value) =>
        "  <data name=\"" + name + "\" xml:space=\"preserve\">\n    <value>" + value + "</value>\n  </data>\n";
}
