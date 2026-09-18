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

    private static string Rooted() =>
        Path.Combine(Path.GetTempPath(), "terse-dotsettings-" + Guid.NewGuid().ToString("N"));
}
