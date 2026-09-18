namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class AnchoredBatchE2ETests(TerseServerFixture server)
{
    private const string Reconciler = "T:Fixture.Trading.Reconciler";

    [Fact]
    public async Task AddMember_AnchoredOnAnOverloadedNameWhoseOverloadsSitTogether_PlacesItInsteadOfRefusing()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = Reconciler,
            ["declaration"] = "public int Anchored() => 1;",
            ["after"] = "Reconcile",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Anchored", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_AnchoredOnTheSpellingAnOutlinePrints_ResolvesTheOverloadWithoutTheExactSignature()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = Reconciler,
            ["declaration"] = "public int Spelled() => 1;",
            ["after"] = "Reconcile(Order, decimal)",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Spelled", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_AnchoredOnANameTheTypeDoesNotDeclare_IsStillRefusedByName()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = Reconciler,
            ["declaration"] = "public int Absent() => 1;",
            ["after"] = "NoSuchMember",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("names no member of Reconciler", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithTypeSymbolIdsPairedWithDeclarations_LandsInEveryTypeAsOneEdit()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.TwinAlpha", "T:Fixture.Trading.TwinBravo" },
            ["declarations"] = new[] { "public int Doubled() => Count() * 2;", "public int Doubled() => Count() * 2;" },
            ["dryRun"] = true,
        });

        Assert.Contains("2 files changed", text, StringComparison.Ordinal);
        Assert.Contains("TwinAlpha.cs", text, StringComparison.Ordinal);
        Assert.Contains("TwinBravo.cs", text, StringComparison.Ordinal);
        Assert.Contains("Doubled", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithUnpairedTypeSymbolIdsAndDeclarations_IsRefusedNamingBothCounts()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.TwinAlpha", "T:Fixture.Trading.TwinBravo" },
            ["declarations"] = new[] { "public int Doubled() => 1;" },
            ["dryRun"] = true,
        });

        Assert.Contains("typeSymbolIds has 2 entries and declarations has 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithTypeSymbolIdsBesideASingularDeclaration_IsRefusedRatherThanDroppingIt()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.TwinAlpha" },
            ["declarations"] = new[] { "public int Doubled() => 1;" },
            ["declaration"] = "public int Other() => 1;",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("would be silently dropped", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_WithTools_PricesEveryAdvertisedToolSoADescriptionEditNeedsNoBuildRound()
    {
        var advertised = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var text = await server.CallAsync("workspace_status", new() { ["tools"] = true });

        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"perTool={advertised.Count} advertised"), text, StringComparison.Ordinal);
        Assert.Contains("read_text ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceStatus_WithoutTools_PricesNothingPerTool()
    {
        var text = await server.CallAsync("workspace_status", new() { ["verbose"] = true });

        Assert.DoesNotContain("perTool=", text, StringComparison.Ordinal);
    }
}
