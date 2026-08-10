namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class EditToolsE2ETests(TerseServerFixture server)
{
    private const string UnusedMethod = "M:Fixture.Trading.OrderService.Unused";
    private static readonly string OrderServicePath =
        Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "OrderService.cs");

    [Fact]
    public async Task ReplaceSymbolBody_WithDryRun_ReportsTheDiagnosticCountsTheEditWouldLeave()
    {
        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = UnusedMethod,
            ["body"] = "{ return 9; }",
            ["dryRun"] = true,
        });

        Assert.Contains("errors=0 (+0)", text, StringComparison.Ordinal);
        Assert.Contains("warnings=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbolBody_WithDryRun_ReturnsDiffAndLeavesTheFileUntouched()
    {
        var before = await File.ReadAllTextAsync(OrderServicePath, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = UnusedMethod,
            ["body"] = "{ return 9; }",
            ["dryRun"] = true,
        });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Contains("+", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(OrderServicePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceSymbolBody_ThatWouldNotCompile_IsRolledBack()
    {
        var before = await File.ReadAllTextAsync(OrderServicePath, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = UnusedMethod,
            ["body"] = "{ return \"not an int\"; }",
            ["dryRun"] = false,
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(OrderServicePath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSymbol_WithLiveReferences_IsRefusedAndListsThem()
    {
        var text = await server.CallAsync("delete_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
            ["force"] = false,
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("usages", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameSymbol_WithDryRun_TouchesEveryReferencingFile()
    {
        var text = await server.CallAsync("rename_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
            ["newName"] = "SubmitAsync",
            ["dryRun"] = true,
        });

        Assert.Contains("OrderService.cs", text, StringComparison.Ordinal);
        Assert.Contains("OrderRouter.cs", text, StringComparison.Ordinal);
        Assert.Contains("2 files changed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameSymbol_WithAnInvalidIdentifier_IsRefused()
    {
        var text = await server.CallAsync("rename_symbol", new()
        {
            ["symbolId"] = UnusedMethod,
            ["newName"] = "9not valid",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithDryRun_ShowsTheInsertedMember()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["declaration"] = "public int Added() => 1;",
            ["dryRun"] = true,
        });

        Assert.Contains("+", text, StringComparison.Ordinal);
        Assert.Contains("Added", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_WithSeveralMembers_ReplacesTheTargetWithAllOfThem()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)",
            ["declaration"] = "public bool Submit(Order order) => repository.Submit(order);\npublic int Extra() => 1;",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("Extra", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithSeveralMembers_AddsThemInOneEdit()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["declaration"] = "public int One() => 1;\npublic int Two() => 2;",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("One", text, StringComparison.Ordinal);
        Assert.Contains("Two", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_WithAMalformedDeclaration_IsStillRefused()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["declaration"] = "public int Broken( => 1;",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithAnAmbiguousMatch_IsRefused()
    {
        var text = await server.CallAsync("edit_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["oldText"] = "order",
            ["newText"] = "request",
            ["dryRun"] = true,
            ["force"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("expected exactly 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_OnACsFileWithoutForce_PointsAtTheSemanticTools()
    {
        var text = await server.CallAsync("edit_text", new()
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["oldText"] = "Unused",
            ["newText"] = "Spare",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("replace_symbol_body", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteText_Applied_ChangesTheFileOnDiskAndIsReadableBack()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "scratch.json");

        try
        {
            var written = await server.CallAsync("write_text", new()
            {
                ["path"] = "scratch.json",
                ["content"] = "{ \"written\": true }",
                ["dryRun"] = false,
            });

            Assert.Equal("scratch.json  changedLines=1", written);
            Assert.Equal("{ \"written\": true }", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

            var read = await server.CallAsync("read_text", new() { ["path"] = "scratch.json" });

            Assert.Contains("\"written\": true", read, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WriteText_OutsideTheWorkspace_IsRefused()
    {
        var text = await server.CallAsync("write_text", new()
        {
            ["path"] = "../../escaped.txt",
            ["content"] = "no",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR OutOfWorkspace", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbolBody_OnSuccess_AnswersInOneLinePerFileAndKeepsTheDiffBehindVerbose()
    {
        var before = await File.ReadAllTextAsync(OrderServicePath, TestContext.Current.CancellationToken);

        try
        {
            var quiet = await server.CallAsync("replace_symbol_body", new()
            {
                ["symbolId"] = UnusedMethod,
                ["body"] = "{ return 7; }",
            });

            var loud = await server.CallAsync("replace_symbol_body", new()
            {
                ["symbolId"] = UnusedMethod,
                ["body"] = "{ return 8; }",
                ["verbose"] = true,
            });

            Assert.DoesNotContain("@@", quiet, StringComparison.Ordinal);
            Assert.DoesNotContain("verbose=true", quiet, StringComparison.Ordinal);
            Assert.Contains("changedLines=", quiet, StringComparison.Ordinal);
            Assert.Contains("@@", loud, StringComparison.Ordinal);
            Assert.True(quiet.Length < loud.Length, quiet);
        }
        finally
        {
            await File.WriteAllTextAsync(OrderServicePath, before, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task WriteText_WithTheContentTheFileAlreadyHas_SaysItChangedNothing()
    {
        var path = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "OrderService.cs");
        var before = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var written = File.GetLastWriteTimeUtc(path);
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = "src/Fixture.Trading/OrderService.cs",
            ["content"] = before,
            ["force"] = true,
        };

        try
        {
            await server.CallAsync("write_text", arguments);

            var text = await server.CallAsync("write_text", arguments);

            Assert.Equal(before, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Contains("0 files changed", text, StringComparison.Ordinal);
            Assert.Contains("no change - the result is identical to what is already there", text, StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(path, before, TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(path, written);
        }
    }

    private const string SubmitId = "M:Fixture.Trading.OrderService.Submit(Fixture.Trading.Order)";
    private static readonly string[] SignatureChangeIds =
    [
        SubmitId,
    "M:Fixture.Trading.OrderService.SubmitTwice(Fixture.Trading.Order)",
    "M:Fixture.Trading.OrderRouter.Route(Fixture.Trading.Order)",
    "M:Fixture.Trading.OrderRouter.Retry(Fixture.Trading.Order)",
];
    private static readonly string[] SignatureChangeDeclarations =
    [
        "public bool Submit(Order order, bool urgent) => repository.Submit(order) && urgent;",
    "public bool SubmitTwice(Order order) => Submit(order, true) && Submit(order, true);",
    "public bool Route(Order order) => service.Submit(order, false);",
    "public bool Retry(Order order) => service.Submit(order, true);",
];

    [Fact]
    public async Task ReplaceSymbol_ChangingASignatureAlone_IsReportedAsARollback()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = SubmitId,
            ["declaration"] = SignatureChangeDeclarations[0],
            ["dryRun"] = true,
        });

        Assert.Contains("would be rolled back", text, StringComparison.Ordinal);
        Assert.Contains("OrderRouter.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_WithABatchCarryingTheBrokenCallers_LandsAsOneCompileGatedEditAcrossBothFiles()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = SignatureChangeIds,
            ["declarations"] = SignatureChangeDeclarations,
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("would be rolled back", text, StringComparison.Ordinal);
        Assert.Contains("2 files changed", text, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs", text, StringComparison.Ordinal);
        Assert.Contains("OrderRouter.cs", text, StringComparison.Ordinal);
        Assert.Contains("errors=0 (+0)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_WithUnpairedBatchArrays_NamesBothCounts()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = SignatureChangeIds,
            ["declarations"] = new[] { SignatureChangeDeclarations[0] },
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("symbolIds has 4 entries and declarations has 1", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbolBody_WithTheBodyThatIsAlreadyThere_ChangesNothingInsteadOfEatingTheBlankLine()
    {
        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = SubmitId,
            ["body"] = "=> repository.Submit(order)",
            ["dryRun"] = true,
        });

        Assert.Contains("0 files changed", text, StringComparison.Ordinal);
        Assert.Contains("identical to what is already there", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbolBody_TurningAnExpressionIntoABlock_LeavesTheBlankLineAfterTheMember()
    {
        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = UnusedMethod,
            ["body"] = "{\n    return 7;\n}",
            ["dryRun"] = true,
        });

        Assert.Contains("@@ -15,1 +15,4 @@", text, StringComparison.Ordinal);
        Assert.Contains("changedLines=4", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplaceSymbol_WithOverlappingBatchedEdits_IsRefusedInEitherOrder(bool innerFirst)
    {
        string[] ids = ["T:Fixture.Trading.OrderRouter", "M:Fixture.Trading.OrderRouter.Route(Fixture.Trading.Order)"];
        string[] bodies =
        [
            "public sealed class OrderRouter\n{\n    private readonly OrderService service;\n\n    public OrderRouter(OrderService service) => this.service = service;\n\n    public bool Route(Order order) => service.Submit(order);\n\n    public bool Retry(Order order) => !service.Submit(order);\n}",
        "public bool Route(Order order) => !service.Submit(order);",
    ];

        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolIds"] = innerFirst ? [ids[1], ids[0]] : ids,
            ["declarations"] = innerFirst ? [bodies[1], bodies[0]] : bodies,
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("overlap", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1 files changed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_RolledBackForAMissingUsing_NamesTheNamespaceToImport()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderBook",
            ["declaration"] = "public ImmutableArray<string> TrackedTags() => [];",
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.Contains("CS0246", text, StringComparison.Ordinal);
        Assert.Contains("add: using System.Collections.Immutable;", text, StringComparison.Ordinal);
        Assert.Contains("retryWith=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_RolledBackForARegressionWithNoImport_NamesNoImportItCannotProve()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderBook",
            ["declaration"] = "public int Unresolvable() => NoSuchHelperAnywhere(1);",
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.DoesNotContain("add: using", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_RolledBackForANameInTwoNamespaces_NamesNeitherRatherThanGuessing()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderBook",
            ["declaration"] = "public DuplicatedName Ambiguous() => null!;",
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.Contains("CS0246", text, StringComparison.Ordinal);
        Assert.DoesNotContain("add: using", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMember_RolledBackForAMissingUsingBesideARealRegression_NamesNoImport()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderBook",
            ["declaration"] = "public ImmutableArray<int> Mixed() => NoSuchHelperAnywhere();",
        });

        Assert.Contains("ERROR CompileRegression", text, StringComparison.Ordinal);
        Assert.Contains("CS0246", text, StringComparison.Ordinal);
        Assert.DoesNotContain("add: using", text, StringComparison.Ordinal);
    }
}
