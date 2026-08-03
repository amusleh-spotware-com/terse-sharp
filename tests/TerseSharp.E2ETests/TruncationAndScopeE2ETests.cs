namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class TruncationAndScopeE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task SearchSymbols_WhenTheCapIsHit_SaysItTruncatedAndNamesTheRealTotal()
    {
        var capped = await server.CallAsync("search_symbols", new()
        {
            ["query"] = "Order",
            ["maxResults"] = 1,
        });

        var full = await server.CallAsync("search_symbols", new()
        {
            ["query"] = "Order",
            ["maxResults"] = 200,
        });

        Assert.Contains("truncated=true", capped, StringComparison.Ordinal);
        Assert.Contains("narrow with", capped, StringComparison.Ordinal);
        Assert.Equal(Total(full), Total(capped));
        Assert.Contains("\n1 symbols", capped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSymbols_WhenEverythingFits_SaysItDidNotTruncate()
    {
        var text = await server.CallAsync("search_symbols", new()
        {
            ["query"] = "OrderService",
            ["maxResults"] = 200,
        });

        Assert.Contains("truncated=false", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFilesAndSearch_SkipANestedAgentWorktree()
    {
        var nested = Path.Combine(TerseServerFixture.FixtureRoot, ".claude", "worktrees", "probe");

        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(
            Path.Combine(nested, "ShadowCopy.cs"),
            "namespace Shadow; public sealed class ShadowMarkerType { }\n",
            TestContext.Current.CancellationToken);

        try
        {
            var files = await server.CallAsync("find_files", new() { ["glob"] = "**/*.cs", ["maxResults"] = 500 });
            var text = await server.CallAsync("search_text", new() { ["query"] = "ShadowMarkerType" });

            Assert.DoesNotContain("ShadowCopy.cs", files, StringComparison.Ordinal);
            Assert.Contains("0 matches", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.Combine(TerseServerFixture.FixtureRoot, ".claude"), recursive: true);
        }
    }

    [Fact]
    public async Task SearchText_AcceptsQueryAsWellAsPattern()
    {
        var byQuery = await server.CallAsync("search_text", new()
        {
            ["query"] = "namespace",
            ["glob"] = "**/*.cs",
        });

        Assert.StartsWith("search_text", byQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", byQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchText_WithNeitherQueryNorPattern_ReportsAStructuredErrorWithARemedy()
    {
        var text = await server.CallAsync("search_text", []);

        Assert.StartsWith("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
        Assert.Contains("query", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_WithUsings_ListsTheFilesOwnUsingDirectives()
    {
        var text = await server.CallAsync("get_file_outline", new()
        {
            ["path"] = "src/Fixture.Trading/Localization.cs",
            ["usings"] = true,
        });

        Assert.Contains("usings: System.Globalization", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileOutline_WithoutUsings_StaysSilentAboutThem()
    {
        var text = await server.CallAsync("get_file_outline", new()
        {
            ["path"] = "src/Fixture.Trading/Localization.cs",
        });

        Assert.DoesNotContain("usings:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithHeadings_PrintsTheGitHubAnchorSlug()
    {
        var file = Path.Combine(Path.GetTempPath(), "terse-headings.md");

        await File.WriteAllTextAsync(file, "# 🚫 Where TerseSharp sits\n\nbody\n", TestContext.Current.CancellationToken);

        try
        {
            var text = await server.CallAsync("read_text", new()
            {
                ["path"] = file,
                ["headings"] = true,
            });

            Assert.Contains("#-where-tersesharp-sits", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Analyze_WithAGlob_ScopesToTheMatchingFiles()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/*.cs",
            ["includeDeadCode"] = false,
        });

        Assert.StartsWith("analyze", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithAGlob_StillFindsDeadCodeInsideTheScope()
    {
        var scoped = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/*.cs",
            ["ids"] = "TERSE001",
        });

        Assert.Contains("TERSE001", scoped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithChanged_DoesNotLeakDeadCodeFromUntouchedFiles()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/Order.cs",
            ["ids"] = "TERSE001",
        });

        Assert.DoesNotContain("OrderService.cs", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WithAGlobThatMatchesNothing_ReportsAStructuredError()
    {
        var text = await server.CallAsync("analyze", new()
        {
            ["path"] = "src/Fixture.Trading/*.nothing",
        });

        Assert.StartsWith("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_SinceLastWithChanged_KeepsTheSolutionBaselineIntact()
    {
        var touched = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Order.cs");
        var written = File.GetLastWriteTimeUtc(touched);

        await server.CallAsync("analyze", new() { ["sinceLast"] = true });
        File.SetLastWriteTimeUtc(touched, DateTime.UtcNow);

        try
        {
            var scoped = await server.CallAsync("analyze", new() { ["sinceLast"] = true, ["changed"] = true });
            var again = await server.CallAsync("analyze", new() { ["sinceLast"] = true });

            Assert.DoesNotContain("FIXED", scoped, StringComparison.Ordinal);
            Assert.Contains("0 new diagnostics", again, StringComparison.Ordinal);
        }
        finally
        {
            File.SetLastWriteTimeUtc(touched, written);
        }
    }

    [Fact]
    public async Task Analyze_WithChanged_ScopesToTheModifiedFiles()
    {
        var touched = Path.Combine(TerseServerFixture.FixtureRoot, "src", "Fixture.Trading", "Order.cs");
        var written = File.GetLastWriteTimeUtc(touched);

        File.SetLastWriteTimeUtc(touched, DateTime.UtcNow);

        try
        {
            var text = await server.CallAsync("analyze", new() { ["changed"] = true, ["ids"] = "TERSE001" });

            Assert.DoesNotContain("OrderService.cs", text, StringComparison.Ordinal);
        }
        finally
        {
            File.SetLastWriteTimeUtc(touched, written);
        }
    }

    [Fact]
    public async Task Analyze_WithChangedAndNothingModified_ReportsAStructuredError()
    {
        await using var solution = await TerseTempSolution.StartAsync(watch: false, TestContext.Current.CancellationToken);

        var text = await solution.CallAsync("analyze", new() { ["changed"] = true, ["path"] = "src/Fixture.Trading/Awkward.cs" });

        Assert.StartsWith("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithRepeatedHeadings_NumbersTheAnchorsLikeGitHub()
    {
        var file = Path.Combine(Path.GetTempPath(), "terse-repeated-headings.md");

        await File.WriteAllTextAsync(file, "## Added\n\na\n\n## Added\n\nb\n\n## Added\n\nc\n", TestContext.Current.CancellationToken);

        try
        {
            var text = await server.CallAsync("read_text", new() { ["path"] = file, ["headings"] = true });

            Assert.Contains("#added\n", text, StringComparison.Ordinal);
            Assert.Contains("#added-1", text, StringComparison.Ordinal);
            Assert.Contains("#added-2", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task SearchRegex_AnchorsEachLineNotTheWholeFile()
    {
        var text = await server.CallAsync("search_regex", new()
        {
            ["query"] = "^namespace Fixture",
            ["glob"] = "**/*.cs",
        });

        Assert.DoesNotContain("0 matches", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadWorkspace_WithDiscover_ListsCandidatesWithoutLoading()
    {
        var text = await server.CallAsync("load_workspace", new()
        {
            ["path"] = TerseServerFixture.FixtureRoot,
            ["discover"] = true,
        });

        Assert.Contains("candidates", text, StringComparison.Ordinal);
        Assert.Contains(".csproj", text, StringComparison.Ordinal);
        Assert.DoesNotContain("elapsedMs=", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSymbols_NeverEmitsMoreRecordsThanMaxResults()
    {
        var text = await server.CallAsync("search_symbols", new() { ["query"] = "Order", ["maxResults"] = 3 });
        var records = text.Split('\n').Count(line => line.Contains("  EXACT  ", StringComparison.Ordinal));

        Assert.True(records <= 3, text);
    }

    [Fact]
    public async Task LoadWorkspace_ReportsWarningsSeparatelyFromFailures()
    {
        var text = await server.CallAsync("workspace_status", []);

        Assert.Contains("warnings=", text, StringComparison.Ordinal);
    }

    private static string Total(string response)
    {
        var marker = response.IndexOf("total=", StringComparison.Ordinal);
        var tail = response[(marker + 6)..];
        var end = tail.IndexOf(')', StringComparison.Ordinal);

        return tail[..end];
    }
}
