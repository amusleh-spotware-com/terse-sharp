using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ResxDocumentTests
{
    private const string Mixed = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <data name="Alpha" xml:space="preserve">
            <value>First</value>
            <comment>note</comment>
          </data>
          <data name="Beta" type="System.Drawing.Color, System.Drawing">Blue</data>
          <data name="Gamma" mimetype="application/x-microsoft.net.object.bytearray.base64">
            <value>AAEC</value>
          </data>
          <data name="Alpha" xml:space="preserve">
            <value>Duplicate</value>
          </data>
        </root>
        """;

    private const string Sorted = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <data name="Apple" xml:space="preserve">
            <value>Apple</value>
          </data>
          <data name="Cherry" xml:space="preserve">
            <value>Cherry</value>
          </data>
        </root>
        """;

    private const string Unsorted = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <data name="Zulu" xml:space="preserve">
            <value>Zulu</value>
          </data>
          <data name="Alpha">
            <value>Alpha</value>
          </data>
        </root>
        """;

    [Fact]
    public void Parse_ReadsTheNameValueAndComment()
    {
        var entry = Parse(Mixed).Find("Alpha")!;

        Assert.Equal("First", entry.Value);
        Assert.Equal("note", entry.Comment);
        Assert.True(entry.Preserved);
    }

    [Fact]
    public void Parse_TagsATypedEntryAndKeepsItOutOfTheTranslatableSet()
    {
        var document = Parse(Mixed);

        Assert.Equal(ResxEntryKind.Typed, document.Find("Beta")!.Kind);
        Assert.DoesNotContain(document.Translatable, entry => entry.Name is "Beta");
    }

    [Fact]
    public void Parse_TagsABinaryEntry() =>
        Assert.Equal(ResxEntryKind.Binary, Parse(Mixed).Find("Gamma")!.Kind);

    [Fact]
    public void Parse_ReportsBothOccurrencesOfADuplicateName() =>
        Assert.Equal(2, Parse(Mixed).All("Alpha").Count);

    [Fact]
    public void Span_RoundTripsTheOriginalDeclaration()
    {
        var document = Parse(Mixed);
        var entry = document.Find("Alpha")!;

        Assert.Equal(
            "<data name=\"Alpha\" xml:space=\"preserve\">\n    <value>First</value>\n    <comment>note</comment>\n  </data>",
            document.Text[entry.Start..entry.End].ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Span_OfAnElementWrittenOnOneLine_StopsAtItsOwnClosingTag()
    {
        var document = Parse(Mixed);
        var entry = document.Find("Beta")!;

        Assert.Equal("<data name=\"Beta\" type=\"System.Drawing.Color, System.Drawing\">Blue</data>", document.Text[entry.Start..entry.End]);
    }

    [Fact]
    public void Parse_OnMalformedXml_FailsWithARemedy()
    {
        var parsed = ResxDocument.Parse("broken.resx", "<root><data name=\"A\">");

        Assert.False(parsed.IsOk);
        Assert.Equal(TerseErrorCode.InvalidArgument, parsed.Error!.Code);
        Assert.NotEmpty(parsed.Error!.Remedy);
    }

    [Fact]
    public void Parse_ToleratesAFileWithNoSchemaHeader() =>
        Assert.Equal(2, Parse(Sorted).Entries.Count);

    [Fact]
    public void IsSorted_IsTrueOnlyWhenTheKeysAreInOrdinalOrder()
    {
        Assert.True(Parse(Sorted).IsSorted);
        Assert.False(Parse(Unsorted).IsSorted);
    }

    [Fact]
    public void Preserved_IsFalseWhenTheEntryHasNoXmlSpaceAttribute() =>
        Assert.False(Parse(Unsorted).Find("Alpha")!.Preserved);

    [Fact]
    public void InsertionPoint_InASortedFile_LandsBeforeTheFirstLaterKey()
    {
        var document = Parse(Sorted);

        Assert.Equal(document.Find("Cherry")!.Line, LineOf(document.Text, document.InsertionPoint("Banana")));
    }

    [Fact]
    public void InsertionPoint_InAnUnsortedFile_LandsAfterTheLastEntry()
    {
        var document = Parse(Unsorted);

        Assert.True(document.InsertionPoint("Banana") > document.Find("Alpha")!.End);
    }

    private static int LineOf(string text, int offset) => text[..offset].Count(character => character is '\n') + 1;

    private static ResxDocument Parse(string text) => ResxDocument.Parse("Strings.resx", text).Value!;
}
