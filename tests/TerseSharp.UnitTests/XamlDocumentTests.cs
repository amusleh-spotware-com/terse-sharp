using System.Xml.Linq;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class XamlDocumentTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-xaml-");

    public void Dispose() => directory.Delete(recursive: true);

    [Theory]
    [InlineData("https://github.com/avaloniaui", ".axaml", "avalonia")]
    [InlineData("http://schemas.microsoft.com/dotnet/2021/maui", ".xaml", "maui")]
    [InlineData("http://schemas.microsoft.com/winfx/2006/xaml/presentation", ".xaml", "wpf")]
    public void Load_DetectsTheDialectFromTheRootNamespace(string markup, string extension, string expected)
    {
        var path = Write(extension, "<Root xmlns=\"" + markup + "\" />");

        Assert.Equal(expected, XamlDocument.Load(path).Value!.Dialect);
    }

    [Fact]
    public void Load_ForAWinUiUsingPrefix_DetectsWinUi()
    {
        var path = Write(
            ".xaml",
            "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:local=\"using:App.Views\" />");

        Assert.Equal("winui", XamlDocument.Load(path).Value!.Dialect);
    }

    [Fact]
    public void Load_ForAnAxamlFileWithNoRecognisedNamespace_FallsBackToAvalonia()
    {
        var path = Write(".axaml", "<Root />");

        Assert.Equal("avalonia", XamlDocument.Load(path).Value!.Dialect);
    }

    [Theory]
    [InlineData("clr-namespace:App.Views;assembly=App", "App.Views")]
    [InlineData("clr-namespace:App.Views", "App.Views")]
    [InlineData("using:App.Views", "App.Views")]
    [InlineData("http://schemas.microsoft.com/winfx/2006/xaml", null)]
    public void ClrNamespace_ReadsBothPrefixForms(string declaration, string? expected) =>
        Assert.Equal(expected, XamlDocument.ClrNamespace(declaration));

    [Fact]
    public void ClrNamespaceOf_ResolvesAPrefixDeclaredOnTheRoot()
    {
        var path = Write(".xaml", "<Root xmlns:vm=\"clr-namespace:App.ViewModels;assembly=App\" />");

        Assert.Equal("App.ViewModels", XamlDocument.Load(path).Value!.ClrNamespaceOf("vm"));
    }

    [Fact]
    public void Uid_IsReadFromTheElement()
    {
        var element = XElement.Parse("<TextBlock xmlns:x=\"http://x\" x:Uid=\"Greeting\" />");

        Assert.Equal("Greeting", new XamlElementInfo(element, "TextBlock", 1).Uid);
    }

    private string Write(string extension, string content)
    {
        var path = Path.Combine(directory.FullName, Guid.NewGuid().ToString("N") + extension);

        File.WriteAllText(path, content);

        return path;
    }
}
