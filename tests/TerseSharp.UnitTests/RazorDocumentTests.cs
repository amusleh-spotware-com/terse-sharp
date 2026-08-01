using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class RazorDocumentTests
{
    private const string Component = """
        @page "/orders"
        @using Fixture.Blazor.Models
        @inject IOrderService Orders
        @implements IDisposable

        <div class="card">
            <h3>@Title</h3>
            <Badge Kind="warning" Count="@Items.Count" />
            <button @onclick="Toggle">Toggle</button>
        </div>

        @code {
            [Parameter]
            public string? Title { get; set; }

            private void Toggle() { }
        }
        """;

    [Fact]
    public void Parse_ReadsEveryFileLevelDirective()
    {
        var document = RazorDocument.Parse("Card.razor", Component);

        Assert.Equal(["page", "using", "inject", "implements"], document.Directives.Select(directive => directive.Name));
        Assert.Equal("\"/orders\"", document.Value("page"));
        Assert.Equal(["/orders"], document.Routes);
        Assert.Equal(["Fixture.Blazor.Models"], document.Usings);
    }

    [Fact]
    public void Parse_BuildsElementPathsFromTheMarkupNesting()
    {
        var document = RazorDocument.Parse("Card.razor", Component);

        Assert.Equal(["div", "div/h3", "div/Badge", "div/button"], document.Elements.Select(element => element.Path));
        Assert.Equal(6, document.Elements[0].Line);
    }

    [Fact]
    public void Parse_KeepsAttributeNamesValuesAndSelfClosing()
    {
        var badge = RazorDocument.Parse("Card.razor", Component).Locate("div/Badge");

        Assert.NotNull(badge);
        Assert.True(badge.SelfClosing);
        Assert.Equal("warning", badge.Attribute("Kind")?.Value);
        Assert.Equal("@Items.Count", badge.Attribute("Count")?.Value);
        Assert.True(badge.Attribute("Count")?.IsExpression);
        Assert.Equal("Items.Count", badge.Attribute("Count")?.Expression);
    }

    [Fact]
    public void Parse_TreatsACapitalisedTagAsAComponentCandidate()
    {
        var document = RazorDocument.Parse("Card.razor", Component);

        Assert.True(document.Locate("div/Badge")!.LooksLikeComponent);
        Assert.False(document.Locate("div/h3")!.LooksLikeComponent);
    }

    [Fact]
    public void Parse_RecordsTheCodeBlockAndItsBody()
    {
        var document = RazorDocument.Parse("Card.razor", Component);

        var block = Assert.Single(document.CodeBlocks);

        Assert.Equal("code", block.Keyword);
        Assert.Contains("private void Toggle()", Component.Substring(block.BodySpan.Start, block.BodySpan.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReportsAnUnclosedElement()
    {
        var document = RazorDocument.Parse("Broken.razor", "<div>\n  <span>text\n");

        Assert.False(document.WellFormed);
        Assert.Contains(document.Issues, issue => issue.Contains("<div> is never closed", StringComparison.Ordinal));
        Assert.Contains(document.Issues, issue => issue.Contains("<span> is never closed", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ReportsACloseTagThatMatchesNothing()
    {
        var document = RazorDocument.Parse("Broken.razor", "<p>text</p></section>");

        Assert.Contains(document.Issues, issue => issue.Contains("</section> closes nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_AcceptsAFileThatStartsWithAByteOrderMark()
    {
        var document = RazorDocument.Parse("Home.razor", "﻿@page \"/\"\n<div></div>");

        Assert.Equal(["/"], document.Routes);
        Assert.True(document.WellFormed);
    }

    [Fact]
    public void Parse_KeepsScanningMarkupInsideAControlFlowBlock()
    {
        var document = RazorDocument.Parse("Home.razor", "@if (visible)\n{\n    <Card />\n}\n");

        Assert.Equal(["Card"], document.Elements.Select(element => element.TagName));
        Assert.Empty(document.Directives);
    }

    [Fact]
    public void Parse_DoesNotTreatAUsingStatementAsADirective()
    {
        var document = RazorDocument.Parse("Home.razor", "@using (var scope = Open())\n{\n    <Card />\n}\n");

        Assert.Empty(document.Directives);
    }

    [Fact]
    public void Parse_SkipsRazorComments()
    {
        var document = RazorDocument.Parse("Home.razor", "@* <Card /> *@\n<Badge />");

        Assert.Equal(["Badge"], document.Elements.Select(element => element.TagName));
    }

    [Fact]
    public void Parse_NumbersRepeatedSiblings()
    {
        var document = RazorDocument.Parse("Home.razor", "<div>\n<Card />\n<Card />\n</div>");

        Assert.Equal(["div", "div/Card", "div/Card[1]"], document.Elements.Select(element => element.Path));
        Assert.Equal(2, document.Matches("Card"));
    }

    [Fact]
    public void Locate_FindsAnElementByItsCapturedReference()
    {
        var document = RazorDocument.Parse("Home.razor", "<div>\n<Card @ref=\"card\" />\n</div>");

        Assert.Equal("div/Card", document.Locate("#card")?.Path);
    }

    [Theory]
    [InlineData("Card.razor", RazorFileKind.Component)]
    [InlineData("Index.cshtml", RazorFileKind.View)]
    [InlineData("_Imports.razor", RazorFileKind.Imports)]
    [InlineData("_ViewImports.cshtml", RazorFileKind.Imports)]
    [InlineData("_ViewStart.cshtml", RazorFileKind.ViewStart)]
    [InlineData("MainLayout.razor", RazorFileKind.Layout)]
    public void Parse_ClassifiesTheFileKindFromItsName(string name, RazorFileKind expected) =>
        Assert.Equal(expected, RazorDocument.Parse(name, "<div></div>").Kind);

    [Fact]
    public void IsRazor_AcceptsRazorAndCshtmlOnly()
    {
        Assert.True(RazorDocument.IsRazor("Card.razor"));
        Assert.True(RazorDocument.IsRazor("Index.cshtml"));
        Assert.False(RazorDocument.IsRazor("Card.razor.cs"));
        Assert.False(RazorDocument.IsRazor("styles.css"));
    }

    [Fact]
    public void IsGenerated_RecognisesTheRazorGeneratorOutput()
    {
        Assert.True(RazorFiles.IsGenerated("Components/Card_razor.g.cs"));
        Assert.True(RazorFiles.IsGenerated("Pages/Index_cshtml.g.cs"));
        Assert.False(RazorFiles.IsGenerated("Components/Card.razor.cs"));
    }

    [Fact]
    public void LineOf_MapsAnOffsetToItsOneBasedLine()
    {
        var document = RazorDocument.Parse("Home.razor", "<a>\n<b>\n<c>");

        Assert.Equal(1, document.LineOf(0));
        Assert.Equal(2, document.LineOf(4));
        Assert.Equal(3, document.LineOf(8));
    }
}
