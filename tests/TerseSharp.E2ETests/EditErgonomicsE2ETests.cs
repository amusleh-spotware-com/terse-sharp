namespace TerseSharp.E2ETests;

[Collection(nameof(TerseServerCollection))]
public sealed class EditErgonomicsE2ETests(TerseServerFixture server)
{
    [Fact]
    public async Task AddMember_WithTwoMutuallyReferencingMembers_LandsThemInOneEdit()
    {
        var text = await server.CallAsync("add_member", new()
        {
            ["typeSymbolId"] = "T:Fixture.Trading.OrderService",
            ["declaration"] = "private static int Doubled(int value) => Halved(value) * 4;\n\nprivate static int Halved(int value) => value / 2;",
            ["dryRun"] = true,
        });

        Assert.Contains("Doubled", text, StringComparison.Ordinal);
        Assert.Contains("Halved", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbol_WithTwoOverloads_ReplacesTheTargetWithBoth()
    {
        var text = await server.CallAsync("replace_symbol", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["declaration"] = "public int Unused() => 7;\n\npublic int Unused(int extra) => 7 + extra;",
            ["dryRun"] = true,
            ["allowErrors"] = true,
        });

        Assert.DoesNotContain("is not exactly one member", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceSymbolBody_OnAnExpressionBodiedMember_AcceptsABareExpression()
    {
        var text = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["body"] = "42",
            ["dryRun"] = true,
        });

        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CS0161", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task XamlStyles_CapsItsResultsAndSaysSo()
    {
        var text = await server.CallAsync("xaml_styles", new()
        {
            ["typeName"] = "Button",
            ["maxResults"] = 1,
        });

        Assert.StartsWith("1/3 styles truncated", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadText_WithAnAbsolutePathOutsideTheWorkspace_ReadsItAndSaysSo()
    {
        var outside = Path.Combine(Path.GetTempPath(), "terse-outside-workspace.md");

        await File.WriteAllTextAsync(outside, "# Outside\n\nbody\n", TestContext.Current.CancellationToken);

        try
        {
            var text = await server.CallAsync("read_text", new() { ["path"] = outside });

            Assert.Contains("outside-workspace", text, StringComparison.Ordinal);
            Assert.Contains("# Outside", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AmbiguousWorkspace", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task WriteText_OutsideTheWorkspace_IsStillRefused()
    {
        var outside = Path.Combine(Path.GetTempPath(), "terse-outside-write.md");
        var text = await server.CallAsync("write_text", new()
        {
            ["path"] = outside,
            ["content"] = "no",
        });

        Assert.StartsWith("ERROR", text, StringComparison.Ordinal);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task EditText_WithSeveralEdits_AppliesThemAllAsOneWrite()
    {
        const string Probe = "terse-batch-edit-probe.md";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "alpha\nbeta\ngamma\n" });
        try
        {
            var applied = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["edits"] = new object[]
                {
                new Dictionary<string, object?> { ["oldText"] = "alpha", ["newText"] = "ALPHA" },
                new Dictionary<string, object?> { ["oldText"] = "gamma", ["newText"] = "GAMMA" },
                },
            });
            var after = await server.CallAsync("read_text", new() { ["path"] = Probe });

            Assert.Contains("edits=2/2", applied, StringComparison.Ordinal);
            Assert.DoesNotContain("FAILED", applied, StringComparison.Ordinal);
            Assert.Contains("ALPHA", after, StringComparison.Ordinal);
            Assert.Contains("GAMMA", after, StringComparison.Ordinal);
            Assert.Contains("beta", after, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WhenOneEditOfABatchDoesNotMatch_LandsTheRestAndNamesTheFailure()
    {
        const string Probe = "terse-batch-edit-partial-probe.md";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "alpha\nbeta\n" });
        try
        {
            var applied = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["edits"] = new object[]
                {
                new Dictionary<string, object?> { ["oldText"] = "alpha", ["newText"] = "ALPHA" },
                new Dictionary<string, object?> { ["oldText"] = "nowhere", ["newText"] = "x" },
                },
            });
            var after = await server.CallAsync("read_text", new() { ["path"] = Probe });

            Assert.Contains("edits=1/2", applied, StringComparison.Ordinal);
            Assert.Contains("FAILED edit 2", applied, StringComparison.Ordinal);
            Assert.Contains("matched 0 times", applied, StringComparison.Ordinal);
            Assert.Contains("remedy:", applied, StringComparison.Ordinal);
            Assert.Contains("ALPHA", after, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WithMoreEditsThanTheCap_RefusesInsteadOfApplyingSome()
    {
        const string Probe = "terse-batch-cap-probe.md";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "alpha0\nalpha1\nalpha2\n" });
        try
        {
            var edits = Enumerable
                .Range(0, 11)
                .Select(index => (object)new Dictionary<string, object?>
                {
                    ["oldText"] = "alpha" + index.ToString(CultureInfo.InvariantCulture),
                    ["newText"] = "beta",
                })
                .ToArray();

            var refused = await server.CallAsync("edit_text", new() { ["path"] = Probe, ["edits"] = edits });
            var after = await server.CallAsync("read_text", new() { ["path"] = Probe });

            Assert.Contains("ERROR", refused, StringComparison.Ordinal);
            Assert.Contains("at most 10", refused, StringComparison.Ordinal);
            Assert.Contains("remedy:", refused, StringComparison.Ordinal);
            Assert.Contains("alpha0", after, StringComparison.Ordinal);
            Assert.DoesNotContain("beta", after, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WithNeitherNewTextNorEdits_NamesBothSpellings()
    {
        var text = await server.CallAsync("edit_text", new() { ["path"] = "appsettings.json", ["oldText"] = "100" });

        Assert.Contains("ERROR", text, StringComparison.Ordinal);
        Assert.Contains("newText", text, StringComparison.Ordinal);
        Assert.Contains("edits", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WhenTheAnchorMatchesSeveralTimes_ShowsTheCandidateLines()
    {
        const string Probe = "terse-occurrence-probe.md";
        await server.CallAsync("write_text", new()
        {
            ["path"] = Probe,
            ["content"] = "| row |\n| keep |\n| row |\n| keep |\n| row |\n",
        });
        try
        {
            var refused = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["oldText"] = "| row |",
                ["newText"] = "| picked |",
            });

            Assert.Contains("matched 3 times", refused, StringComparison.Ordinal);
            Assert.Contains("occurrence=1  line 1:", refused, StringComparison.Ordinal);
            Assert.Contains("occurrence=3  line 5:", refused, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task ASuccessfulEdit_DoesNotRepeatTheFileCountAboveThePerFileLine()
    {
        var applied = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["body"] = "=> 8;",
        });
        var restored = await server.CallAsync("replace_symbol_body", new()
        {
            ["symbolId"] = "M:Fixture.Trading.OrderService.Unused",
            ["body"] = "=> 7;",
        });

        Assert.DoesNotContain("files changed", applied, StringComparison.Ordinal);
        Assert.Contains("OrderService.cs  changedLines=", applied, StringComparison.Ordinal);
        Assert.Contains("changedLines=", restored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithBothATopLevelEditAndABatch_RefusesInsteadOfDroppingOne()
    {
        const string Probe = "terse-batch-plus-single-probe.md";
        await server.CallAsync("write_text", new() { ["path"] = Probe, ["content"] = "alpha\nbeta\n" });
        try
        {
            var refused = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["oldText"] = "alpha",
                ["newText"] = "ALPHA",
                ["edits"] = new object[]
                {
                new Dictionary<string, object?> { ["oldText"] = "beta", ["newText"] = "BETA" },
                },
            });
            var after = await server.CallAsync("read_text", new() { ["path"] = Probe });

            Assert.Contains("ERROR", refused, StringComparison.Ordinal);
            Assert.Contains("silently dropped", refused, StringComparison.Ordinal);
            Assert.Contains("alpha", after, StringComparison.Ordinal);
            Assert.Contains("beta", after, StringComparison.Ordinal);
            Assert.DoesNotContain("BETA", after, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task Section_WhenTheHeadingRepeats_IsPickedWithOccurrenceRatherThanAnAnchor()
    {
        const string Probe = "terse-section-occurrence-probe.md";
        await server.CallAsync("write_text", new()
        {
            ["path"] = Probe,
            ["content"] = "# Changelog\n\n## [Unreleased]\n\n### Added\n\n- one\n\n## [1.0.0]\n\n### Added\n\n- two\n",
        });
        try
        {
            var refused = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["section"] = "### Added",
                ["place"] = "prepend",
                ["newText"] = "- fresh\n",
            });

            Assert.Contains("'### Added' names 2 sections", refused, StringComparison.Ordinal);
            Assert.Contains("occurrence=1..2", refused, StringComparison.Ordinal);
            Assert.Contains("1:line 5", refused, StringComparison.Ordinal);

            var applied = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["section"] = "### Added",
                ["occurrence"] = 1,
                ["place"] = "prepend",
                ["newText"] = "- fresh\n",
            });

            Assert.Contains("changedLines=", applied, StringComparison.Ordinal);

            var second = await server.CallAsync("read_text", new()
            {
                ["path"] = Probe,
                ["section"] = "### Added",
                ["occurrence"] = 2,
                ["verbose"] = true,
            });

            Assert.Contains("- two", second, StringComparison.Ordinal);
            Assert.DoesNotContain("- fresh", second, StringComparison.Ordinal);

            var first = await server.CallAsync("read_text", new()
            {
                ["path"] = Probe,
                ["section"] = "### Added",
                ["occurrence"] = 1,
                ["verbose"] = true,
            });

            Assert.Contains("- fresh", first, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task ReadText_WithAnOccurrenceAndNoSection_RefusesInsteadOfIgnoringIt()
    {
        var text = await server.CallAsync("read_text", new()
        {
            ["path"] = "notes.md",
            ["occurrence"] = 2,
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("no section was passed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WhereEveryEntryCarriesItsOwnPath_NeedsNoTopLevelPath()
    {
        const string First = "terse-i282-first.md";
        const string Second = "terse-i282-second.md";

        await server.CallAsync("write_text", new()
        {
            ["files"] = new[]
            {
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = First, ["content"] = "alpha\n" },
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = Second, ["content"] = "beta\n" },
        },
        });

        try
        {
            var applied = await server.CallAsync("edit_text", new()
            {
                ["edits"] = new[]
                {
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = First, ["oldText"] = "alpha", ["newText"] = "ALPHA" },
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = Second, ["oldText"] = "beta", ["newText"] = "BETA" },
            },
            });

            Assert.DoesNotContain("ERROR", applied, StringComparison.Ordinal);
            Assert.Contains(First, applied, StringComparison.Ordinal);
            Assert.Contains(Second, applied, StringComparison.Ordinal);

            var read = await server.CallAsync("read_text", new() { ["paths"] = new[] { First, Second }, ["verbose"] = true });

            Assert.Contains("ALPHA", read, StringComparison.Ordinal);
            Assert.Contains("BETA", read, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = First, ["delete"] = true });
            await server.CallAsync("write_text", new() { ["path"] = Second, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WithAPathlessEntryAndNoTopLevelPath_NamesTheEntryItCannotPlace()
    {
        var text = await server.CallAsync("edit_text", new()
        {
            ["edits"] = new[]
            {
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = "notes.md", ["oldText"] = "a", ["newText"] = "b" },
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["oldText"] = "c", ["newText"] = "d" },
        },
        });

        Assert.Contains("edits[1] carries no path", text, StringComparison.Ordinal);
        Assert.Contains("remedy:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithNeitherAPathNorEdits_StillNamesPath()
    {
        var text = await server.CallAsync("edit_text", new() { ["oldText"] = "a", ["newText"] = "b" });

        Assert.Contains("'path' is required", text, StringComparison.Ordinal);
        Assert.Contains("every entry declares its own path", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithToPath_MovesTheSectionIntoTheOtherFileInOneWrite()
    {
        const string Source = "terse-i285-open.md";
        const string Target = "terse-i285-archive.md";

        await server.CallAsync("write_text", new()
        {
            ["files"] = new[]
            {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = Source,
                ["content"] = "# Backlog\n\n## Open\n\n| Finding |\n|---|\n| one |\n\n## Notes\n\nkeep me\n",
            },
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = Target,
                ["content"] = "# Archive\n\n## Closed\n\n| Finding |\n|---|\n",
            },
        },
        });

        try
        {
            var moved = await server.CallAsync("edit_text", new()
            {
                ["path"] = Source,
                ["section"] = "## Open",
                ["toPath"] = Target,
            });

            Assert.DoesNotContain("ERROR", moved, StringComparison.Ordinal);
            Assert.DoesNotContain("@@", moved, StringComparison.Ordinal);
            Assert.Contains(Source, moved, StringComparison.Ordinal);
            Assert.Contains(Target, moved, StringComparison.Ordinal);
            Assert.Contains("changedLines=", moved, StringComparison.Ordinal);

            var after = await server.CallAsync("read_text", new() { ["paths"] = new[] { Source, Target }, ["verbose"] = true });

            Assert.Contains("## Notes", after, StringComparison.Ordinal);
            Assert.Contains("keep me", after, StringComparison.Ordinal);
            Assert.Contains("## Closed", after, StringComparison.Ordinal);
            Assert.Contains("## Open", after, StringComparison.Ordinal);
            Assert.Contains("| one |", after, StringComparison.Ordinal);

            var source = await server.CallAsync("read_text", new() { ["path"] = Source, ["verbose"] = true });

            Assert.DoesNotContain("## Open", source, StringComparison.Ordinal);
            Assert.DoesNotContain("| one |", source, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Source, ["delete"] = true });
            await server.CallAsync("write_text", new() { ["path"] = Target, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WithToPathAndNoSection_SaysWhatItIsMissing()
    {
        var missing = await server.CallAsync("edit_text", new()
        {
            ["path"] = "notes.md",
            ["toPath"] = "other.md",
        });
        var combined = await server.CallAsync("edit_text", new()
        {
            ["path"] = "notes.md",
            ["toPath"] = "other.md",
            ["oldText"] = "a",
            ["newText"] = "b",
        });

        Assert.Contains("ERROR InvalidArgument", missing, StringComparison.Ordinal);
        Assert.Contains("no section= was passed", missing, StringComparison.Ordinal);

        Assert.Contains("ERROR InvalidArgument", combined, StringComparison.Ordinal);
        Assert.Contains("cannot be combined with newText, oldText or edits", combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithToPathNamingTheSameFile_IsRefusedRatherThanDuplicatingTheSection()
    {
        const string Probe = "terse-i285-same.md";

        await server.CallAsync("write_text", new()
        {
            ["path"] = Probe,
            ["content"] = "# Backlog\n\n## Open\n\n| one |\n",
        });

        try
        {
            var refused = await server.CallAsync("edit_text", new()
            {
                ["path"] = Probe,
                ["section"] = "## Open",
                ["toPath"] = Probe,
            });

            Assert.Contains("ERROR InvalidArgument", refused, StringComparison.Ordinal);
            Assert.Contains("names the same file as path", refused, StringComparison.Ordinal);

            var after = await server.CallAsync("read_text", new() { ["path"] = Probe, ["verbose"] = true });

            Assert.Equal(1, after.Split("## Open", StringSplitOptions.None).Length - 1);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Probe, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WithToPathThatIsNotMarkdown_IsRefused()
    {
        var text = await server.CallAsync("edit_text", new()
        {
            ["path"] = "notes.md",
            ["section"] = "## Open",
            ["toPath"] = "appsettings.json",
        });

        Assert.Contains("ERROR InvalidArgument", text, StringComparison.Ordinal);
        Assert.Contains("is not markdown", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditText_WithRowAndToPath_MovesThatRowAndRewritesItWithoutSendingItsOldText()
    {
        const string Source = "terse-i289-open.md";
        const string Target = "terse-i289-archive.md";

        await server.CallAsync("write_text", new()
        {
            ["files"] = new[]
            {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = Source,
                ["content"] = "# Backlog\n\n## Open\n\n| Finding | Tool | Proposed change |\n|---|---|---|\n| **I900** first | build | do a thing |\n| **I901** second | format | do another |\n",
            },
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = Target,
                ["content"] = "# Archive\n\n## Closed\n\n| Finding | Tool | Change | Outcome |\n|---|---|---|---|\n| **I899** older | clean | shipped | measured |\n",
            },
        },
        });

        try
        {
            var moved = await server.CallAsync("edit_text", new()
            {
                ["path"] = Source,
                ["row"] = "I900",
                ["toPath"] = Target,
                ["newText"] = "| **I900** first | build | shipped the thing | 1 200 tokens per call |",
            });

            Assert.DoesNotContain("ERROR", moved, StringComparison.Ordinal);
            Assert.Contains("changedLines=", moved, StringComparison.Ordinal);

            var source = await server.CallAsync("read_text", new() { ["path"] = Source, ["verbose"] = true });
            var target = await server.CallAsync("read_text", new() { ["path"] = Target, ["verbose"] = true });

            Assert.DoesNotContain("I900", source, StringComparison.Ordinal);
            Assert.Contains("| **I901** second | format | do another |", source, StringComparison.Ordinal);
            Assert.Contains("| **I899** older | clean | shipped | measured |", target, StringComparison.Ordinal);
            Assert.Contains("| **I900** first | build | shipped the thing | 1 200 tokens per call |", target, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Source, ["delete"] = true });
            await server.CallAsync("write_text", new() { ["path"] = Target, ["delete"] = true });
        }
    }

    [Fact]
    public async Task EditText_WithARowIdentifierThatMatchesNothingOrTooMuch_SaysWhichInsteadOfMovingTheWrongRow()
    {
        const string Source = "terse-i289-miss.md";
        const string Target = "terse-i289-miss-archive.md";

        await server.CallAsync("write_text", new()
        {
            ["files"] = new[]
            {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = Source,
                ["content"] = "# Backlog\n\n## Open\n\n| Finding |\n|---|\n| **I910** one |\n| **I911** two |\n",
            },
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = Target,
                ["content"] = "# Archive\n\n## Closed\n\n| Finding |\n|---|\n| **I800** old |\n",
            },
        },
        });

        try
        {
            var absent = await server.CallAsync("edit_text", new() { ["path"] = Source, ["row"] = "I999", ["toPath"] = Target });
            var many = await server.CallAsync("edit_text", new() { ["path"] = Source, ["row"] = "I91", ["toPath"] = Target });
            var unrouted = await server.CallAsync("edit_text", new() { ["path"] = Source, ["row"] = "I910" });

            Assert.Contains("no markdown table row's first cell contains 'I999'", absent, StringComparison.Ordinal);
            Assert.Contains("matches the first cell of 2 table rows", many, StringComparison.Ordinal);
            Assert.Contains("no toPath= was passed", unrouted, StringComparison.Ordinal);
        }
        finally
        {
            await server.CallAsync("write_text", new() { ["path"] = Source, ["delete"] = true });
            await server.CallAsync("write_text", new() { ["path"] = Target, ["delete"] = true });
        }
    }
}
