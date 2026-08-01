namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class DocsCoverageE2ETests(TerseServerFixture server)
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("NUGET_README.md")]
    [InlineData("src/TerseSharp.Server/Assets/SKILL.md")]
    public async Task EveryAdvertisedTool_IsNamedInTheShippedDocumentation(string document)
    {
        var path = Path.Combine(TerseServerFixture.RepositoryRoot, document.Replace('/', Path.DirectorySeparatorChar));
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        var advertised = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var missing = advertised.Select(tool => tool.Name).Where(name => !Names(text, name)).ToArray();

        Assert.True(missing.Length is 0, string.Create(CultureInfo.InvariantCulture, $"{document} does not name: {string.Join(", ", missing)}"));
    }

    private static bool Names(string text, string tool)
    {
        var quoted = string.Concat("`", tool);

        for (var index = text.IndexOf(quoted, StringComparison.Ordinal); index >= 0; index = text.IndexOf(quoted, index + 1, StringComparison.Ordinal))
        {
            var following = index + quoted.Length;

            if (following >= text.Length || (!char.IsLetterOrDigit(text[following]) && text[following] is not '_'))
                return true;
        }

        return false;
    }
}
