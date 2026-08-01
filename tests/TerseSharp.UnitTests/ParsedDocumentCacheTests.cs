using System.Globalization;
using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ParsedDocumentCacheTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("terse-document-cache-");
    private readonly ParsedDocumentCache cache = new();

    public void Dispose() => directory.Delete(recursive: true);

    [Fact]
    public void Load_ForAnUnchangedFile_ParsesOnce()
    {
        var path = Write("one", "alpha");

        var first = cache.Load(path, ParsedDocumentCache.ResourceFactor, Parse);
        var second = cache.Load(path, ParsedDocumentCache.ResourceFactor, Parse);

        Assert.Same(first.Value, second.Value);
        Assert.Equal(1, cache.Parses);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Load_AfterTheFileChanges_Reparses()
    {
        var path = Write("one", "alpha");
        var first = cache.Load(path, ParsedDocumentCache.ResourceFactor, Parse);

        Write("one", "alpha and omega");

        var second = cache.Load(path, ParsedDocumentCache.ResourceFactor, Parse);

        Assert.NotSame(first.Value, second.Value);
        Assert.Equal("alpha and omega", second.Value!.Text);
        Assert.Equal(2, cache.Parses);
    }

    [Fact]
    public void Load_ForAMissingFile_ReportsDocumentNotFoundWithoutParsing()
    {
        var parsed = cache.Load(Path.Combine(directory.FullName, "absent.txt"), ParsedDocumentCache.ResourceFactor, Parse);

        Assert.False(parsed.IsOk);
        Assert.Equal(TerseErrorCode.DocumentNotFound, parsed.Error!.Code);
        Assert.Equal(0, cache.Parses);
    }

    [Fact]
    public void Forget_DropsTheEntrySoTheNextLoadReparses()
    {
        var path = Write("one", "alpha");
        var first = cache.Load(path, ParsedDocumentCache.ResourceFactor, Parse);

        cache.Forget(path);

        Assert.Equal(0, cache.Count);
        Assert.NotSame(first.Value, cache.Load(path, ParsedDocumentCache.ResourceFactor, Parse).Value);
        Assert.Equal(2, cache.Parses);
    }

    [Fact]
    public void Load_BeyondTheDocumentCap_StaysAtTheCapAndEvictsTheLeastRecentlyUsed()
    {
        var paths = Written(ParsedDocumentCache.MaxDocuments + 20);

        foreach (var path in paths)
        {
            cache.Load(path, ParsedDocumentCache.ResourceFactor, Parse);

            Assert.True(cache.Count <= ParsedDocumentCache.MaxDocuments, Overflow(cache.Count));
        }

        Assert.Equal(ParsedDocumentCache.MaxDocuments, cache.Count);
        Assert.True(cache.Bytes <= ParsedDocumentCache.MaxBytes, Overflow(cache.Count));

        cache.Load(paths[^1], ParsedDocumentCache.ResourceFactor, Parse);

        Assert.Equal(paths.Length, cache.Parses);

        cache.Load(paths[0], ParsedDocumentCache.ResourceFactor, Parse);

        Assert.Equal(paths.Length + 1, cache.Parses);
    }

    private string[] Written(int count, string? content = null) =>
    [
        .. Enumerable
            .Range(0, count)
            .Select(index => index.ToString(CultureInfo.InvariantCulture))
            .Select(name => Write(name, content ?? name)),
    ];

    private static string Overflow(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"the cache held {count} documents");

    private static Result<TextDocument> Parse(string path) => Result.Ok(new TextDocument(File.ReadAllText(path)));

    private string Write(string name, string text)
    {
        var path = Path.Combine(directory.FullName, name + ".txt");

        File.WriteAllText(path, text);

        return path;
    }

    private sealed record TextDocument(string Text);
    [Fact]
    public void Load_BeyondTheByteBudget_EvictsEvenWhileTheDocumentCapIsUntouched()
    {
        var budgeted = new ParsedDocumentCache(maxDocuments: 1_000, maxBytes: 240);
        var paths = Written(20, new string('x', 40));

        foreach (var path in paths)
            budgeted.Load(path, ParsedDocumentCache.ResourceFactor, Parse);

        Assert.True(budgeted.Bytes <= 240, Overflow(budgeted.Count));
        Assert.Equal(2, budgeted.Count);

        foreach (var path in paths)
            budgeted.Forget(path);

        Assert.Equal(0, budgeted.Count);
        Assert.Equal(0, budgeted.Bytes);
    }
}
