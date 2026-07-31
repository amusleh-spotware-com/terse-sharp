using System.Xml.Linq;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class XamlBindingServiceTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-binding-");

    public void Dispose() => directory.Delete(recursive: true);

    [Theory]
    [InlineData("{Binding Symbol}", "Symbol")]
    [InlineData("{Binding Symbol, Mode=OneWay}", "Symbol")]
    [InlineData("{Binding Path=Selected.Symbol}", "Selected.Symbol")]
    [InlineData("{Binding Path=Symbol, Mode=TwoWay}", "Symbol")]
    [InlineData("{CompiledBinding Volume}", "Volume")]
    [InlineData("{x:Bind ViewModel.Symbol}", "ViewModel.Symbol")]
    [InlineData("{Binding}", "")]
    public void PathOf_ReadsThePropertyPath(string expression, string expected) =>
        Assert.Equal(expected, XamlBindingService.PathOf(expression));

    [Theory]
    [InlineData("{Binding ElementName=Other}")]
    [InlineData("{Binding RelativeSource={RelativeSource Self}}")]
    public void PathOf_ForABindingWithNoPropertyPath_ReturnsNull(string expression) =>
        Assert.Null(XamlBindingService.PathOf(expression));

    [Theory]
    [InlineData("{Binding .}")]
    [InlineData("{Binding Items[0]}")]
    [InlineData("{Binding Orders/Symbol}")]
    [InlineData("{Binding (local:Attached.Prop)}")]
    [InlineData("{Binding Selected.}")]
    public void PathOf_ForAPathItCannotResolveMemberByMember_ReturnsNullRatherThanAFalseError(string expression) =>
        Assert.Null(XamlBindingService.PathOf(expression));

    [Fact]
    public void PathOf_TrimsTheSpaceBeforeAnExplicitPathSeparator() =>
        Assert.Equal("Symbol", XamlBindingService.PathOf("{Binding Path=Symbol , Mode=OneWay}"));

    [Fact]
    public void ContextTypeName_ReadsXDataTypeFromTheElement()
    {
        var root = XElement.Parse("<Window xmlns:x=\"http://x\" x:DataType=\"vm:OrderViewModel\"><Text /></Window>");

        Assert.Equal("vm:OrderViewModel", XamlBindingService.ContextTypeName(Info(root)));
    }

    [Fact]
    public void ContextTypeName_InheritsXDataTypeFromAnAncestor()
    {
        var root = XElement.Parse("<Window xmlns:x=\"http://x\" x:DataType=\"vm:OrderViewModel\"><Text /></Window>");

        Assert.Equal("vm:OrderViewModel", XamlBindingService.ContextTypeName(Info(root.Elements().First())));
    }

    [Fact]
    public void ContextTypeName_ReadsADesignInstanceDataContext()
    {
        var root = XElement.Parse(
            "<UserControl xmlns:d=\"http://d\" d:DataContext=\"{d:DesignInstance Type=vm:OrderViewModel}\" />");

        Assert.Equal("vm:OrderViewModel", XamlBindingService.ContextTypeName(Info(root)));
    }

    [Fact]
    public void ContextTypeName_WithNoDeclaredContext_ReturnsNull()
    {
        var root = XElement.Parse("<Window><Text /></Window>");

        Assert.Null(XamlBindingService.ContextTypeName(Info(root)));
    }

    [Fact]
    public void Sites_FindsEveryBindingAttribute()
    {
        var path = Path.Combine(directory.FullName, Guid.NewGuid().ToString("N") + ".xaml");

        File.WriteAllText(path, "<Root><Text A=\"{Binding One}\" B=\"literal\" C=\"{Binding Two}\" /></Root>");

        var sites = XamlBindingService.Sites(XamlDocument.Load(path).Value!).ToArray();

        Assert.Equal(["{Binding One}", "{Binding Two}"], sites.Select(site => site.Expression));
    }

    private static XamlElementInfo Info(XElement element) => new(element, element.Name.LocalName, 1);
}
