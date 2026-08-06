namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class ResxToolsE2ETests(TerseServerFixture server)
{
    private const string Strings = "src/Fixture.Trading/Strings.resx";
    private const string Scratch = "src/Fixture.Trading/Scratch.resx";

    private static string ScratchPath => Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Scratch.resx");

    private static string ScratchFrenchPath => Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Scratch.fr.resx");

    [Fact]
    public async Task ResxFiles_ListsEveryFamilyWithItsCulturesAndDesigner()
    {
        var text = Slashes(await server.CallAsync("resx_files", []));

        Assert.StartsWith("6 families", text, StringComparison.Ordinal);
        Assert.Contains("src/Fixture.Trading/Strings.resx  localization  neutral=7  de=2 fr=5  missing=8", text, StringComparison.Ordinal);
        Assert.Contains("designer=src/Fixture.Trading/Strings.Designer.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxFiles_MarksAWinFormsFileAndAResw()
    {
        var text = Slashes(await server.CallAsync("resx_files", []));

        Assert.Contains("src/Fixture.Trading/Legacy.resx  winforms", text, StringComparison.Ordinal);
        Assert.Contains("src/Fixture.Trading/Views/WinUi/Resources.resw  resw", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxFiles_DoesNotReadANonCultureSegmentAsACulture()
    {
        var text = Slashes(await server.CallAsync("resx_files", new() { ["filter"] = "Order.Web" }));

        Assert.Contains("src/Fixture.Trading/Order.Web.resx  localization  neutral=1  -  missing=0", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxGet_ShowsAMissingTranslationInsteadOfOmittingTheKey()
    {
        var text = await server.CallAsync("resx_get", new()
        {
            ["path"] = Strings,
            ["cultures"] = "all",
            ["key"] = "Caption_Submit",
        });

        Assert.Contains("Caption_Submit  EXACT  neutral=\"Submit order\"  de=MISSING  fr=\"Envoyer l'ordre\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxGet_WithValuesFalse_ListsTheKeysWithoutTheirValues()
    {
        var text = await server.CallAsync("resx_get", new() { ["path"] = Strings, ["values"] = false });

        Assert.Contains("Caption_Submit  EXACT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Submit order", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxGet_MarksATypedAndABinaryEntry()
    {
        var text = await server.CallAsync("resx_get", new() { ["path"] = Strings });

        Assert.Contains("Caption_Icon  TYPED", text, StringComparison.Ordinal);
        Assert.Contains("Caption_Logo  BINARY", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxGet_TruncationNamesTheParameterThatNarrowsIt()
    {
        var text = await server.CallAsync("resx_get", new() { ["path"] = Strings, ["maxResults"] = 2 });

        Assert.Contains(" truncated", text, StringComparison.Ordinal);
        Assert.Contains("narrow with prefix=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxGet_OnAFileThatIsNotAResource_SaysSoWithARemedy()
    {
        var text = await server.CallAsync("resx_get", new() { ["path"] = "src/Fixture.Trading/Order.cs" });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxFind_ByKey_ListsEveryFileThatDeclaresIt()
    {
        var text = Slashes(await server.CallAsync("resx_find", new() { ["query"] = "Caption_Count" }));

        Assert.StartsWith("3 entries", text, StringComparison.Ordinal);
        Assert.Contains("src/Fixture.Trading/Strings.fr.resx#Caption_Count", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxFind_ByValue_FindsTheTranslatedText()
    {
        var text = Slashes(await server.CallAsync("resx_find", new()
        {
            ["query"] = "Envoyer",
            ["scope"] = "value",
        }));

        Assert.Contains("src/Fixture.Trading/Strings.fr.resx#Caption_Submit", text, StringComparison.Ordinal);
        Assert.Contains("value=\"Envoyer l'ordre\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxFind_RestrictedToACulture_SkipsTheOtherFiles()
    {
        var text = Slashes(await server.CallAsync("resx_find", new()
        {
            ["query"] = "Caption_Total",
            ["culture"] = "de",
        }));

        Assert.StartsWith("1 entries", text, StringComparison.Ordinal);
        Assert.Contains("src/Fixture.Trading/Strings.de.resx#Caption_Total", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxUsages_SeparatesTheRoslynResolvedUsageFromTheTextualOnes()
    {
        var text = Slashes(await server.CallAsync("resx_usages", new() { ["key"] = "Caption_Submit" }));

        Assert.Contains("src/Fixture.Trading/Localization.cs:10  EXACT  src  Strings.Caption_Submit  in Localization.SubmitCaption", text, StringComparison.Ordinal);
        Assert.Contains("src/Fixture.Trading/Pages/Index.cshtml:3  HEURISTIC  localizer[]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxUsages_ReportsComposedLookupsSoAnEmptyAnswerIsNotClaimedAsProof()
    {
        var text = await server.CallAsync("resx_usages", new() { ["key"] = "Unused_Key" });

        Assert.StartsWith("0 usages", text, StringComparison.Ordinal);
        Assert.Contains("composedLookups=1", text, StringComparison.Ordinal);
        Assert.Contains("advisory", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxSet_WithDryRun_ReturnsTheDiffAndLeavesTheFileUntouched()
    {
        var before = await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken);

        var text = await server.CallAsync("resx_set", new()
        {
            ["path"] = Scratch,
            ["key"] = "Scratch_Two",
            ["value"] = "Two",
            ["dryRun"] = true,
        });

        Assert.Contains("dryRun", text, StringComparison.Ordinal);
        Assert.Contains("+  <data name=\"Scratch_Two\" xml:space=\"preserve\">", text, StringComparison.Ordinal);
        Assert.Contains("changedLines=3", text, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResxSet_Applied_InsertsTheEntryAndKeepsTheRestOfTheFile()
    {
        var before = await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken);

        try
        {
            var text = await server.CallAsync("resx_set", new()
            {
                ["path"] = Scratch,
                ["key"] = "Scratch_Two",
                ["value"] = "Two",
                ["comment"] = "second",
            });

            var after = await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken);

            Assert.Contains("1 files changed", text, StringComparison.Ordinal);
            Assert.Contains("<value>Two</value>", after, StringComparison.Ordinal);
            Assert.Contains("<comment>second</comment>", after, StringComparison.Ordinal);
            Assert.Contains("<data name=\"Scratch_One\" xml:space=\"preserve\">", after, StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(ScratchPath, before, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ResxSet_WithEntries_AddsSeveralKeysInOneCall()
    {
        var before = await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken);

        try
        {
            await server.CallAsync("resx_set", new()
            {
                ["path"] = Scratch,
                ["entries"] = "Scratch_Alpha=A\nScratch_Beta=B",
            });

            var after = await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken);

            Assert.Contains("<value>A</value>", after, StringComparison.Ordinal);
            Assert.Contains("<value>B</value>", after, StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(ScratchPath, before, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ResxSet_ForAMissingCultureFile_CreatesItAndReportsTheProjectWiring()
    {
        var created = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Scratch.de.resx");

        try
        {
            var text = await server.CallAsync("resx_set", new()
            {
                ["path"] = Scratch,
                ["key"] = "Scratch_One",
                ["value"] = "Eins",
                ["culture"] = "de",
            });

            Assert.Contains("csprojWiring=required", text, StringComparison.Ordinal);
            Assert.Contains("<value>Eins</value>", await File.ReadAllTextAsync(created, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(created);
        }
    }

    [Fact]
    public async Task ResxSet_OnAFamilyWithADesigner_WarnsThatTheDesignerIsStale()
    {
        var text = await server.CallAsync("resx_set", new()
        {
            ["path"] = Strings,
            ["key"] = "Caption_New",
            ["value"] = "New",
            ["dryRun"] = true,
        });

        Assert.Contains("designerStale=true", text, StringComparison.Ordinal);
        Assert.Contains("Strings.Designer.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxSet_OnABinaryEntry_IsRefusedWithARemedy()
    {
        var text = await server.CallAsync("resx_set", new()
        {
            ["path"] = Strings,
            ["key"] = "Caption_Logo",
            ["value"] = "text",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("binary resource", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxSet_OnADuplicateKey_UpdatesTheFirstAndWarns()
    {
        var text = await server.CallAsync("resx_set", new()
        {
            ["path"] = Strings,
            ["key"] = "Caption_Dup",
            ["value"] = "Updated",
            ["dryRun"] = true,
        });

        Assert.Contains("WARNING 'Caption_Dup' is declared 2 times", text, StringComparison.Ordinal);
        Assert.Contains("RESX004", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxSet_EscapesAValueThatWouldOtherwiseBreakTheXml()
    {
        var before = await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken);

        try
        {
            await server.CallAsync("resx_set", new()
            {
                ["path"] = Scratch,
                ["key"] = "Scratch_Markup",
                ["value"] = "a</value><broken & raw",
            });

            var after = await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken);

            Assert.Contains("&lt;/value&gt;", after, StringComparison.Ordinal);
            Assert.Contains("&amp;", after, StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(ScratchPath, before, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ResxSet_WithNeitherKeyNorEntries_SaysWhatToPass()
    {
        var text = await server.CallAsync("resx_set", new() { ["path"] = Scratch });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("neither key nor entries", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxRemove_WhileTheKeyIsStillReferenced_IsRefusedAndListsTheUsages()
    {
        var text = await server.CallAsync("resx_remove", new()
        {
            ["path"] = Strings,
            ["key"] = "Caption_Submit",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("still referenced", text, StringComparison.Ordinal);
        Assert.Contains("force=true", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxRemove_FromEveryFileOfTheFamily_ReportsBothFiles()
    {
        var text = await server.CallAsync("resx_remove", new()
        {
            ["path"] = Scratch,
            ["key"] = "Scratch_One",
            ["dryRun"] = true,
        });

        Assert.Contains("2 files changed", text, StringComparison.Ordinal);
        Assert.Contains("-  <data name=\"Scratch_One\" xml:space=\"preserve\">", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxRemove_Applied_DeletesTheWholeElement()
    {
        var before = await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken);
        var french = await File.ReadAllTextAsync(ScratchFrenchPath, TestContext.Current.CancellationToken);

        try
        {
            await server.CallAsync("resx_remove", new() { ["path"] = Scratch, ["key"] = "Scratch_One" });

            Assert.DoesNotContain("Scratch_One", await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
            Assert.DoesNotContain("Scratch_One", await File.ReadAllTextAsync(ScratchFrenchPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(ScratchPath, before, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(ScratchFrenchPath, french, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ResxRemove_ForAKeyTheFamilyDoesNotDeclare_SaysSo()
    {
        var text = await server.CallAsync("resx_remove", new()
        {
            ["path"] = Scratch,
            ["key"] = "No_Such_Key",
            ["force"] = true,
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("is not declared in", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxRename_RewritesTheFamilyAndTheReferencesItCanProve()
    {
        var text = Slashes(await server.CallAsync("resx_rename", new()
        {
            ["path"] = Strings,
            ["key"] = "Caption_Total",
            ["newKey"] = "Caption_Sum",
            ["dryRun"] = true,
        }));

        Assert.Contains("5 files changed", text, StringComparison.Ordinal);
        Assert.Contains("references=2", text, StringComparison.Ordinal);
        Assert.Contains("src/Fixture.Trading/Pages/Index.cshtml", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxRename_WithUpdateReferencesFalse_TouchesOnlyTheResourceFiles()
    {
        var text = await server.CallAsync("resx_rename", new()
        {
            ["path"] = Strings,
            ["key"] = "Caption_Total",
            ["newKey"] = "Caption_Sum",
            ["updateReferences"] = false,
            ["dryRun"] = true,
        });

        Assert.Contains("3 files changed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Index.cshtml", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxRename_ToAKeyThatAlreadyExists_IsRefused()
    {
        var text = await server.CallAsync("resx_rename", new()
        {
            ["path"] = Strings,
            ["key"] = "Caption_Total",
            ["newKey"] = "Caption_Submit",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("already exists in this family", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxRename_Applied_RenamesEveryFileOfTheFamily()
    {
        var before = await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken);
        var french = await File.ReadAllTextAsync(ScratchFrenchPath, TestContext.Current.CancellationToken);

        try
        {
            await server.CallAsync("resx_rename", new()
            {
                ["path"] = Scratch,
                ["key"] = "Scratch_One",
                ["newKey"] = "Scratch_Renamed",
            });

            Assert.Contains("Scratch_Renamed", await File.ReadAllTextAsync(ScratchPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
            Assert.Contains("Scratch_Renamed", await File.ReadAllTextAsync(ScratchFrenchPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            await File.WriteAllTextAsync(ScratchPath, before, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(ScratchFrenchPath, french, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ResxValidate_ReportsTheMissingGermanTranslation()
    {
        var text = Slashes(await server.CallAsync("resx_validate", new() { ["rules"] = "RESX001" }));

        Assert.Contains("RESX001  src/Fixture.Trading/Strings.de.resx  Caption_Submit  MISSING  no de value; neutral=\"Submit order\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxValidate_ReportsBothDirectionsOfAPlaceholderMismatch()
    {
        var text = Slashes(await server.CallAsync("resx_validate", new() { ["rules"] = "RESX002" }));

        Assert.Contains("RESX002  src/Fixture.Trading/Strings.de.resx  Caption_Count  PLACEHOLDER", text, StringComparison.Ordinal);
        Assert.Contains("{1} is never filled in", text, StringComparison.Ordinal);
        Assert.Contains("string.Format throws FormatException", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxValidate_ReportsTheDuplicateNameWithBothLines()
    {
        var text = Slashes(await server.CallAsync("resx_validate", new() { ["rules"] = "RESX004" }));

        Assert.Contains("RESX004  src/Fixture.Trading/Strings.resx  Caption_Dup  DUPLICATE  declared at lines 22, 25", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxValidate_ReportsAnOrphanAnEmptyValueAndTrimmedWhitespace()
    {
        var text = Slashes(await server.CallAsync("resx_validate", new() { ["rules"] = "RESX005,RESX006,RESX007" }));

        Assert.Contains("RESX005  src/Fixture.Trading/Strings.fr.resx  Legacy_Header  ORPHAN", text, StringComparison.Ordinal);
        Assert.Contains("RESX006  src/Fixture.Trading/Strings.fr.resx  Caption_Trim  EMPTY", text, StringComparison.Ordinal);
        Assert.Contains("RESX007  src/Fixture.Trading/Strings.resx  Caption_Trim  WHITESPACE", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxValidate_ReportsAKeyTheGeneratedDesignerDoesNotExpose()
    {
        var text = Slashes(await server.CallAsync("resx_validate", new() { ["rules"] = "RESX009" }));

        Assert.Contains("RESX009  src/Fixture.Trading/Strings.Designer.cs  Caption_Total  DESIGNER", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Caption_Submit  DESIGNER", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxValidate_DoesNotLintAWinFormsResourceFile()
    {
        var text = Slashes(await server.CallAsync("resx_validate", []));

        Assert.DoesNotContain("Legacy.resx", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxValidate_WithoutIncludeUnused_OmitsTheUnusedRule()
    {
        var without = await server.CallAsync("resx_validate", new() { ["path"] = Scratch });
        var with = await server.CallAsync("resx_validate", new() { ["path"] = Scratch, ["includeUnused"] = true });

        Assert.StartsWith("0 findings", without, StringComparison.Ordinal);
        Assert.Contains("RESX003", with, StringComparison.Ordinal);
        Assert.Contains("Scratch_One  UNUSED", with, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxSet_WithACultureThatEscapesTheWorkspace_IsRefusedAndWritesNothing()
    {
        var escaped = Path.GetFullPath(Path.Combine(TerseServerFixture.FixtureRoot, "..", "Scratch.escape.resx"));

        var text = await server.CallAsync("resx_set", new()
        {
            ["path"] = Scratch,
            ["key"] = "Scratch_Escape",
            ["value"] = "x",
            ["culture"] = "../../../../Scratch.escape",
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("is not a culture name", text, StringComparison.Ordinal);
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public async Task ResxRename_ToAKeyThatIsNotAResourceKey_IsRefused()
    {
        var text = await server.CallAsync("resx_rename", new()
        {
            ["path"] = Scratch,
            ["key"] = "Scratch_One",
            ["newKey"] = "not a key\"",
            ["dryRun"] = true,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("is not a resource key", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxValidate_WithRulesNamingTheUnusedRule_RunsTheScanInsteadOfReportingZero()
    {
        var text = await server.CallAsync("resx_validate", new()
        {
            ["path"] = Scratch,
            ["rules"] = "RESX003",
        });

        Assert.Contains("RESX003", text, StringComparison.Ordinal);
        Assert.Contains("Scratch_One  UNUSED", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxFiles_NamesTheOwningProject()
    {
        var text = Slashes(await server.CallAsync("resx_files", new() { ["filter"] = "Strings.resx" }));

        Assert.Contains("project=Fixture.Trading", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResxValidate_ScopedToOneFamily_ChecksThatFamilyAlone()
    {
        var text = Slashes(await server.CallAsync("resx_validate", new() { ["path"] = Scratch }));

        Assert.Contains("checked=1 family(ies)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Strings.resx", text, StringComparison.Ordinal);
    }

    private static string Slashes(string text) => text.Replace(Path.DirectorySeparatorChar, '/');
}
