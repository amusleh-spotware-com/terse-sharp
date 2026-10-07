using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class DotSettingsFormatTests
{
    private const string Settings = """
        <wpf:ResourceDictionary xml:space="preserve" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:s="clr-namespace:System;assembly=mscorlib" xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <s:Boolean x:Key="/Default/CodeStyle/CSharp/FormatSettings/USE_TABS_ONLY/@EntryValue">True</s:Boolean>
          <s:Int64 x:Key="/Default/CodeStyle/CSharp/FormatSettings/INDENT_SIZE/@EntryValue">2</s:Int64>
        </wpf:ResourceDictionary>
        """;

    [Fact]
    public async Task FoundAsync_ReadsTheIndentStyleAndSizeOutOfTheNearestDotSettings()
    {
        var root = Rooted();
        var nested = Path.Combine(root, "src", "App");

        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(root, "App.sln.DotSettings"), Settings, TestContext.Current.CancellationToken);
        try
        {
            DotSettingsFormat.Forget();

            var convention = await DotSettingsFormat.FoundAsync(Path.Combine(nested, "Program.cs"), TestContext.Current.CancellationToken);

            Assert.True(convention.Governs);
            Assert.True(convention.UseTabs);
            Assert.Equal(2, convention.IndentSize);
            Assert.EndsWith("App.sln.DotSettings", convention.Path!, StringComparison.Ordinal);
        }
        finally
        {
            DotSettingsFormat.Forget();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FoundAsync_ForADotSettingsThatSetsNeitherValue_GovernsNothing()
    {
        var root = Rooted();

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "App.sln.DotSettings"),
            """<wpf:ResourceDictionary xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />""",
            TestContext.Current.CancellationToken);
        try
        {
            DotSettingsFormat.Forget();

            var convention = await DotSettingsFormat.FoundAsync(Path.Combine(root, "Program.cs"), TestContext.Current.CancellationToken);

            Assert.False(convention.Governs);
            Assert.Null(convention.UseTabs);
            Assert.Null(convention.IndentSize);
        }
        finally
        {
            DotSettingsFormat.Forget();
            Directory.Delete(root, recursive: true);
        }
    }

    private const string SpacedCast = """
        <wpf:ResourceDictionary xml:space="preserve" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:s="clr-namespace:System;assembly=mscorlib" xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <s:Boolean x:Key="/Default/CodeStyle/CodeFormatting/CSharpFormat/SPACE_AFTER_TYPECAST_PARENTHESES/@EntryValue">True</s:Boolean>
        </wpf:ResourceDictionary>
        """;

    [Fact]
    public async Task FoundAsync_ReadsTheCastSpacingOutOfTheDotSettings_WithoutClaimingToGovernIndentation()
    {
        var root = Rooted();

        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "App.sln.DotSettings"), SpacedCast, TestContext.Current.CancellationToken);
        try
        {
            DotSettingsFormat.Forget();

            var convention = await DotSettingsFormat.FoundAsync(Path.Combine(root, "Program.cs"), TestContext.Current.CancellationToken);

            Assert.True(convention.SpaceAfterCast);
            Assert.False(convention.Governs);
        }
        finally
        {
            DotSettingsFormat.Forget();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, "var y = (long) x;")]
    [InlineData(false, "var y = (long)x;")]
    public void Applied_WithACastSpacingConvention_LaysTheCastOutTheWayTheDotSettingsSays(bool spaced, string expected)
    {
        using var workspace = new AdhocWorkspace();
        var statement = SyntaxFactory.ParseStatement("var y = (long)  x;");
        var convention = new DotSettingsConvention("App.sln.DotSettings", UseTabs: null, IndentSize: null, SpaceAfterCast: spaced);

        var formatted = Formatter.Format(statement, workspace, FormatService.Applied(workspace.Options, convention), TestContext.Current.CancellationToken);

        Assert.Equal(expected, formatted.ToFullString());
    }

    private static string Rooted() =>
        Path.Combine(Path.GetTempPath(), "terse-dotsettings-" + Guid.NewGuid().ToString("N"));
}
