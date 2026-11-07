namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class OfferMemoE2ETests
{
    [Fact]
    public async Task EveryStandingOffer_IsPrintedOncePerToolPerProcess_AndEveryTruncationSteerOnEveryCall()
    {
        var server = await TerseServerProcess.StartAsync(
            TerseServerFixture.RepositoryRoot,
            [TerseServerFixture.ServerAssemblyPath(), "serve", "--workspace", Path.Combine(TerseServerFixture.FixtureRoot, "FixtureSolution.slnx")],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["TERSE_OFFERS"] = "once" },
            TestContext.Current.CancellationToken);

        try
        {
            await SearchTextOffersContainersOnceAndSteersEveryTruncationAsync(server);
            await ReadTextSaysCondensedOnEveryCondensedRangeAsync(server);
            await GetFileOutlineSteersWideOutlineOnceAsync(server);
            await FindFilesAdvertisesNarrowingOnceAsync(server);
            await CSharpTextEditNotesTheCompileGateOncePerToolAsync(server);
            await ChangedFilesOffersTheUnscopedDiffSymbolsStepOnceAsync(server);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static Task<string> CallAsync(TerseServerProcess server, string tool, Dictionary<string, object?> arguments) =>
        server.CallAsync(tool, arguments, TestContext.Current.CancellationToken);

    private static async Task SearchTextOffersContainersOnceAndSteersEveryTruncationAsync(TerseServerProcess server)
    {
        var first = await CallAsync(server, "search_text", new() { ["query"] = "OrderService", ["glob"] = "src/**/*.cs" });
        var second = await CallAsync(server, "search_text", new() { ["query"] = "Submit", ["glob"] = "src/**/*.cs" });
        var clipped = await CallAsync(server, "search_text", new() { ["query"] = "public", ["glob"] = "src/**/*.cs", ["maxResults"] = 1 });
        var again = await CallAsync(server, "search_text", new() { ["query"] = "class", ["glob"] = "src/**/*.cs", ["maxResults"] = 1 });

        Assert.Contains("containers=true names the C# declaration each hit sits in", first, StringComparison.Ordinal);
        Assert.DoesNotContain("containers=true names", second, StringComparison.Ordinal);
        Assert.Contains("truncated - ", clipped.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains(" - narrow with glob=", clipped.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains(" - narrow with glob=", again.Split('\n')[0], StringComparison.Ordinal);
    }

    private static async Task ReadTextSaysCondensedOnEveryCondensedRangeAsync(TerseServerProcess server)
    {
        var first = await CallAsync(server, "read_text", new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["lines"] = "1-17" });
        var second = await CallAsync(server, "read_text", new() { ["path"] = "src/Fixture.Trading/OrderService.cs", ["lines"] = "1-16" });

        Assert.Equal("condensed=true - blank lines dropped, a number shown only after a gap; verbose=true numbers every line", first.Split('\n')[1]);
        Assert.True(second.Split('\n').Length < 17, second);
        Assert.Contains("condensed=true", second, StringComparison.Ordinal);
    }

    private static async Task GetFileOutlineSteersWideOutlineOnceAsync(TerseServerProcess server)
    {
        var first = await CallAsync(server, "get_file_outline", new() { ["path"] = "src/Fixture.Trading/WideSurface.cs", ["all"] = true });
        var second = await CallAsync(server, "get_file_outline", new() { ["path"] = "src/Fixture.Trading/WideSurface.cs", ["all"] = true, ["signatures"] = false });

        Assert.Contains("45 members - narrow with contains=", first, StringComparison.Ordinal);
        Assert.Contains("WideSurface", second, StringComparison.Ordinal);
        Assert.DoesNotContain("narrow with contains=", second, StringComparison.Ordinal);
    }

    private static async Task FindFilesAdvertisesNarrowingOnceAsync(TerseServerProcess server)
    {
        var first = await CallAsync(server, "find_files", new() { ["glob"] = "src/**/*.cs" });
        var second = await CallAsync(server, "find_files", new() { ["glob"] = "**/*.cs" });

        Assert.Matches(@"^\d+ files - narrow with ", first.Split('\n')[0]);
        Assert.Matches(@"^\d+ files$", second.Split('\n')[0]);
    }

    private static async Task CSharpTextEditNotesTheCompileGateOncePerToolAsync(TerseServerProcess server)
    {
        const string Probe = "src/Fixture.Trading/OfferMemoEditProbe.cs";

        await CallAsync(server, "write_text", new() { ["path"] = Probe, ["content"] = "namespace Fixture.Trading;\n\npublic sealed class OfferMemoEditProbe\n{\n    public int Value => 0;\n}\n", ["force"] = true });

        try
        {
            var first = await CallAsync(server, "edit_text", new() { ["path"] = Probe, ["oldText"] = "public int Value => 0;", ["newText"] = "public int Value => 1;" });
            var second = await CallAsync(server, "edit_text", new() { ["path"] = Probe, ["oldText"] = "public int Value => 1;", ["newText"] = "public int Value => 2;" });
            var verbose = await CallAsync(server, "edit_text", new() { ["path"] = Probe, ["oldText"] = "public int Value => 2;", ["newText"] = "public int Value => 3;", ["verbose"] = true });
            var written = await CallAsync(server, "write_text", new() { ["path"] = Probe, ["content"] = "namespace Fixture.Trading;\n\npublic sealed class OfferMemoEditProbe\n{\n    public int Value => 4;\n}\n" });
            var after = await CallAsync(server, "read_text", new() { ["path"] = Probe, ["verbose"] = true });

            Assert.Contains("NOTE a .cs text edit is compile-gated without force=true", first, StringComparison.Ordinal);
            Assert.Contains("replace_symbol_body", first, StringComparison.Ordinal);
            Assert.DoesNotContain("NOTE a .cs text edit", second, StringComparison.Ordinal);
            Assert.Contains("NOTE a .cs text edit is compile-gated", verbose, StringComparison.Ordinal);
            Assert.Contains("NOTE a .cs text edit is compile-gated", written, StringComparison.Ordinal);
            Assert.Contains("public int Value => 4;", after, StringComparison.Ordinal);
        }
        finally
        {
            await CallAsync(server, "write_text", new() { ["path"] = Probe, ["delete"] = true, ["force"] = true });
        }
    }

    private static async Task ChangedFilesOffersTheUnscopedDiffSymbolsStepOnceAsync(TerseServerProcess server)
    {
        try
        {
            await CallAsync(server, "edit_text", new() { ["path"] = "notes.md", ["oldText"] = "# Fixture notes", ["newText"] = "# Fixture notes, momentarily changed" });

            var first = await CallAsync(server, "changed_files", []);
            var second = await CallAsync(server, "changed_files", new() { ["maxResults"] = 50 });
            var scoped = await CallAsync(server, "changed_files", new() { ["path"] = "notes.md" });

            Assert.Contains("next: diff_symbols - maps each hunk onto the declaration that contains it", first, StringComparison.Ordinal);
            Assert.Contains("notes.md", second, StringComparison.Ordinal);
            Assert.DoesNotContain("next: diff_symbols", second, StringComparison.Ordinal);
            Assert.Contains("next: diff_symbols path=\"notes.md\"", scoped, StringComparison.Ordinal);
        }
        finally
        {
            await CallAsync(server, "write_text", new() { ["path"] = "notes.md", ["ref"] = "HEAD" });
        }
    }
}
