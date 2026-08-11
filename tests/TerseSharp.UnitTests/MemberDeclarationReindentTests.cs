using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class MemberDeclarationReindentTests
{
    [Fact]
    public void Reindented_RestoresTheContinuationLinesOfADedentedDeclaration()
    {
        const string Dedented = "public static string Render() => string.Create(\n    CultureInfo.InvariantCulture,\n    $\"a\");";

        Assert.Equal(
            "public static string Render() => string.Create(\n        CultureInfo.InvariantCulture,\n        $\"a\");",
            MemberDeclaration.Reindented(Dedented, 4));
    }

    [Fact]
    public void Reindented_LeavesADeclarationThatIsAlreadyIndentedAlone()
    {
        const string Indented = "    public int Value =>\n        1;";

        Assert.Equal(Indented, MemberDeclaration.Reindented(Indented, 4));
    }

    [Fact]
    public void Reindented_LeavesTheTopLevelColumnAlone()
    {
        const string Declaration = "public int Value =>\n    1;";

        Assert.Equal(Declaration, MemberDeclaration.Reindented(Declaration, 0));
    }

    [Fact]
    public void Reindented_NeverTouchesTheInteriorOfAMultiLineRawStringLiteral()
    {
        const string Declaration = "public const string Json = \"\"\"\n{\n  \"a\": 1\n}\n\"\"\";";

        Assert.Equal(Declaration, MemberDeclaration.Reindented(Declaration, 4));
    }

    [Fact]
    public void Reindented_NeverTouchesTheInteriorOfAVerbatimStringLiteral()
    {
        const string Declaration = "public string Text() =>\n    @\"first\nsecond\";";

        Assert.Equal(
            "public string Text() =>\n        @\"first\nsecond\";",
            MemberDeclaration.Reindented(Declaration, 4));
    }

    [Fact]
    public void Reindented_LeavesBlankLinesEmpty()
    {
        const string Declaration = "public int Value()\n{\n\n    return 1;\n}";

        Assert.Equal("public int Value()\n    {\n\n        return 1;\n    }", MemberDeclaration.Reindented(Declaration, 4));
    }

    [Fact]
    public void Reindented_KeepsCarriageReturnsWhereTheyWere()
    {
        const string Declaration = "public int Value()\r\n{\r\n    return 1;\r\n}";

        Assert.Equal("public int Value()\r\n    {\r\n        return 1;\r\n    }", MemberDeclaration.Reindented(Declaration, 4));
    }

    [Fact]
    public void Reindented_LeavesASingleLineDeclarationAlone() =>
        Assert.Equal("public int Value => 1;", MemberDeclaration.Reindented("public int Value => 1;", 4));

    [Fact]
    public void Reindented_NeverTouchesAVerbatimLiteralInTheSecondOfTwoMembers()
    {
        const string Declaration = "public int Value() => 1;\n\npublic string Sql() => @\"SELECT\nFROM t\";";

        Assert.Equal(
            "public int Value() => 1;\n\n    public string Sql() => @\"SELECT\nFROM t\";",
            MemberDeclaration.Reindented(Declaration, 4));
    }

    [Fact]
    public void Reindented_NeverTouchesARawLiteralInTheSecondOfTwoMembers()
    {
        const string Declaration = "public int Value() => 1;\n\npublic const string Json = \"\"\"\n{\n  \"a\": 1\n}\n\"\"\";";

        Assert.Equal(
            "public int Value() => 1;\n\n    public const string Json = \"\"\"\n{\n  \"a\": 1\n}\n\"\"\";",
            MemberDeclaration.Reindented(Declaration, 4));
    }

    [Fact]
    public void Reindented_IndentsTheSecondMemberOfAPlainPair()
    {
        const string Declaration = "public int First()\n{\n    return 1;\n}\n\npublic int Second()\n{\n    return 2;\n}";

        Assert.Equal(
            "public int First()\n    {\n        return 1;\n    }\n\n    public int Second()\n    {\n        return 2;\n    }",
            MemberDeclaration.Reindented(Declaration, 4));
    }
}
