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

    [Fact]
    public async Task ReplaceSymbol_AddToNamingATypeNoTargetLivesIn_LandsTheInterfaceMemberBesideItsImplementationsAsOneEdit()
    {
        var alone = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.IOrderRepository",
            ["declaration"] = "int Capacity { get; }",
            ["dryRun"] = true,
        });

        var together = await server.CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "InMemoryOrderRepository.PendingCount", "NullOrderRepository.PendingCount" },
            ["declarations"] = new[]
            {
            "public int PendingCount => orders.Count;\n\npublic int Capacity => 64;",
            "public int PendingCount => 0;\n\npublic int Capacity => 0;",
        },
            ["add"] = new[] { "int Capacity { get; }" },
            ["addTo"] = "IOrderRepository",
            ["dryRun"] = true,
        });

        Assert.Contains("would be rolled back", alone, StringComparison.Ordinal);
        Assert.Contains("CS0535", alone, StringComparison.Ordinal);

        Assert.DoesNotContain("ERROR", together, StringComparison.Ordinal);
        Assert.DoesNotContain("would be rolled back", together, StringComparison.Ordinal);
        Assert.Contains("3 files changed", together, StringComparison.Ordinal);
        Assert.Contains("IOrderRepository.cs", together, StringComparison.Ordinal);
        Assert.Contains("InMemoryOrderRepository.cs", together, StringComparison.Ordinal);
        Assert.Contains("NullOrderRepository.cs", together, StringComparison.Ordinal);
        Assert.Contains("+    int Capacity { get; }", together, StringComparison.Ordinal);
        Assert.Matches(@"errors=\d+ \(\+0\)", together);
    }

    [Fact]
    public async Task ReplaceSymbol_AddToNamingNoTypeInTheWorkspace_IsRefusedNamingTheContainer()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = new[] { "InMemoryOrderRepository.PendingCount", "NullOrderRepository.PendingCount" },
            ["declarations"] = new[] { "public int PendingCount => orders.Count;", "public int PendingCount => 0;" },
            ["add"] = new[] { "int Capacity { get; }" },
            ["addTo"] = "NoSuchContainer",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("addTo=NoSuchContainer names no type add= can land in", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithTypeSymbolIdsAndUsings_LandsEachUsingOnlyInTheFilesThatNeedIt()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.TwinAlpha", "T:Fixture.Trading.TwinBravo" },
            ["declarations"] = new[]
            {
            "public string Described() => new StringBuilder(\"alpha\").ToString();",
            "public int Doubled() => Count() * 2;",
        },
            ["usings"] = new[] { "System.Text" },
            ["dryRun"] = true,
        });

        var bravo = text[text.IndexOf("TwinBravo.cs", StringComparison.Ordinal)..];

        Assert.Contains("2 files changed", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split("+using System.Text;").Length - 1);
        Assert.DoesNotContain("+using System.Text;", bravo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithTypeSymbolIdsAndAUsingNoFileNeeds_StillLandsItInEveryFile()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.TwinAlpha", "T:Fixture.Trading.TwinBravo" },
            ["declarations"] = new[] { "public int Doubled() => Count() * 2;", "public int Doubled() => Count() * 2;" },
            ["usings"] = new[] { "System.Text" },
            ["dryRun"] = true,
        });

        Assert.Equal(2, text.Split("+using System.Text;").Length - 1);
    }
}
