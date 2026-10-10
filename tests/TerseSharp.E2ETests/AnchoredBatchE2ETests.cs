
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
    public async Task AddMember_WithTypeSymbolIdsPairingAnEnumWithAClass_LandsTheEnumValueAndItsFirstUseAsOneEdit()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.OrderSide", "T:Fixture.Trading.TwinAlpha" },
            ["declarations"] = new[] { "Hold", "public OrderSide Held() => OrderSide.Hold;" },
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rolled back", text, StringComparison.Ordinal);
        Assert.Contains("2 files changed", text, StringComparison.Ordinal);
        Assert.Contains("OrderSide.cs", text, StringComparison.Ordinal);
        Assert.Contains("TwinAlpha.cs", text, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\+\s+Hold,?\r?$", text);
        Assert.Contains("OrderSide.Hold", text, StringComparison.Ordinal);
        Assert.Contains("errors=0 (+0)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithTypeSymbolIdsSendingAMethodToAnEnum_IsRefusedAsNotEnumMembersAtItsIndex()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.TwinAlpha", "T:Fixture.Trading.OrderSide" },
            ["declarations"] = new[] { "public int Twice() => Count() * 2;", "public int Twice() => 2;" },
            ["dryRun"] = true,
        });

        Assert.StartsWith("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("declarations[1]:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not a type declaration", text, StringComparison.Ordinal);
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
    public async Task WorkspaceStatus_WithTools_InAWorkspaceThatDeclaresNoTool_EstimatesNothingFromSource()
    {
        var text = await server.CallAsync("workspace_status", new() { ["tools"] = true });

        Assert.Contains("perTool=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tools declared in the working tree", text, StringComparison.Ordinal);
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

    private static int InsertedAt(string diff)
    {
        var hunk = diff.AsSpan(diff.IndexOf("@@ -", StringComparison.Ordinal));
        var start = hunk[(hunk.IndexOf(" +", StringComparison.Ordinal) + 2)..];

        return int.Parse(start[..start.IndexOfAny(',', ' ')], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task AddMember_AnchoredOnTheQualifiedSpellingAnOutlinePrints_PicksThatOverloadAmongScatteredOnes()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.Scattered",
            ["declaration"] = "public int Qualified() => 1;",
            ["after"] = "Scattered.Pick(int)",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Qualified", text, StringComparison.Ordinal);
        Assert.InRange(InsertedAt(text), 43, 45);
    }

    [Fact]
    public async Task AddMember_AnchoredOnANamespaceQualifiedParameterList_LandsBesideThatOverloadNotTheFirst()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.Awkward",
            ["declaration"] = "public int Suffixed() => 1;",
            ["before"] = "Weigh(Fixture.Trading.Boxed<Fixture.Trading.IHandler>)",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Suffixed", text, StringComparison.Ordinal);
        Assert.InRange(InsertedAt(text), 21, 22);
    }

    [Fact]
    public async Task AddMember_WithTypeSymbolIdsNamingOneTypeTwice_JoinsItsDeclarationsIntoOneInsertion()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.TwinAlpha", "T:Fixture.Trading.TwinAlpha", "T:Fixture.Trading.TwinBravo" },
            ["declarations"] = new[] { "public int Doubled() => Count() * 2;", "public int Tripled() => Count() * 3;", "public int Doubled() => Count() * 2;" },
            ["dryRun"] = true,
        });

        var doubled = text.IndexOf("+    public int Doubled() => Count() * 2;", StringComparison.Ordinal);
        var tripled = text.IndexOf("+    public int Tripled() => Count() * 3;", StringComparison.Ordinal);

        Assert.DoesNotContain("overlap", text, StringComparison.Ordinal);
        Assert.Contains("2 files changed", text, StringComparison.Ordinal);
        Assert.Contains("errors=0 (+0)", text, StringComparison.Ordinal);
        Assert.InRange(doubled, 0, tripled - 1);
    }

    [Fact]
    public async Task AddMember_WithTypeSymbolIdsNamingOneEnumInTwoSpellings_JoinsItsValuesIntoOneInsertion()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.OrderSide", "OrderSide" },
            ["declarations"] = new[] { "Hold", "Hedge" },
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\+\s+Hold,?\r?$", text);
        Assert.Matches(@"(?m)^\+\s+Hedge,?\r?$", text);
        Assert.InRange(text.IndexOf("Hold", StringComparison.Ordinal), 0, text.IndexOf("Hedge", StringComparison.Ordinal) - 1);
    }

    [Fact]
    public async Task AddMember_WithTypeSymbolIdsAndAUsingTheImplicitGlobalUsingsImport_LandsNoDirective()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolIds"] = new[] { "T:Fixture.Trading.TwinAlpha", "T:Fixture.Trading.TwinBravo" },
            ["declarations"] = new[]
            {
                "public int Summed() => Enumerable.Range(0, Count()).Sum();",
                "public int Doubled() => Count() * 2;",
            },
            ["usings"] = new[] { "System.Linq" },
            ["dryRun"] = true,
        });

        Assert.Contains("2 files changed", text, StringComparison.Ordinal);
        Assert.Contains("errors=0 (+0)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("+using System.Linq;", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithAGloballyImportedUsingBesideOneTheFileNeeds_LandsOnlyTheNeededOne()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.TwinAlpha",
            ["declaration"] = "public int Summed() => new StringBuilder(\"ab\").Length + Enumerable.Range(0, Count()).Sum();",
            ["usings"] = new[] { "System.Linq", "System.Text" },
            ["dryRun"] = true,
        });

        Assert.Contains("+using System.Text;", text, StringComparison.Ordinal);
        Assert.Contains("errors=0 (+0)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("+using System.Linq;", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("T:Fixture.Trading.Scattered", "before", "M:Fixture.Trading.Scattered.Pick(System.String)~System.Int32", 44, 46)]
    [InlineData("T:Fixture.Trading.Scattered", "after", "M:Fixture.Trading.Scattered.Pick(System.Int32)~System.Int32", 43, 45)]
    [InlineData("T:Fixture.Trading.Awkward", "before", "M:Fixture.Trading.Awkward.Weigh(Fixture.Trading.Boxed{Fixture.Trading.IHandler})~System.Int32", 21, 22)]
    public async Task AddMember_AnchoredOnADocumentationIdWithKeywordAliasesOrBraces_LandsBesideThatOverload(string type, string side, string anchor, int low, int high)
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = type,
            ["declaration"] = "public int Respelled() => 1;",
            [side] = anchor,
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Respelled", text, StringComparison.Ordinal);
        Assert.InRange(InsertedAt(text), low, high);
    }
}
