using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class RollbackHintsTests
{
    [Fact]
    public void CompileRegression_ForAFieldThisEditAddsWithNoWriter_NamesTheOneCallThatLandsBoth()
    {
        var error = Errors.CompileRegression(
            ["CS0649 src/OrderService.cs: Field 'Trading.OrderService.pending' is never assigned to"],
            new RollbackHints(Fields: ["Trading.OrderService.pending"]),
            "add_member");

        Assert.Contains("never assigned: Trading.OrderService.pending", error.Remedy, StringComparison.Ordinal);
        Assert.Contains("replace_symbol symbolId=", error.Remedy, StringComparison.Ordinal);
        Assert.Contains("add=[", error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileRegression_ForAFieldRejectedByAToolThatCannotBatchASymbolEdit_DoesNotNameReplaceSymbol()
    {
        var error = Errors.CompileRegression(
            ["CS0649 src/OrderService.cs: Field 'Trading.OrderService.pending' is never assigned to"],
            new RollbackHints(Fields: ["Trading.OrderService.pending"]),
            "write_text");

        Assert.Contains("never assigned: Trading.OrderService.pending", error.Remedy, StringComparison.Ordinal);
        Assert.DoesNotContain("replace_symbol", error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileRegression_WithNoHint_KeepsTheGenericRemedy()
    {
        var error = Errors.CompileRegression(["CS1002 src/A.cs: ; expected"], tool: "add_member");

        Assert.DoesNotContain("never assigned", error.Remedy, StringComparison.Ordinal);
        Assert.Contains("allowErrors=true", error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileRegression_ForAnUnimplementedInterfaceMember_NamesTheTypeSymbolIdsBatchThatKeepsTheTreeCompiling()
    {
        var error = Errors.CompileRegression(
            ["CS0535 src/A.cs: 'Handler' does not implement interface member"],
            new RollbackHints(Implementers: ["Trading.Handler", "Trading.Router"]),
            "add_member");

        Assert.Contains("add_member typeSymbolIds=[", error.Remedy, StringComparison.Ordinal);
        Assert.Contains("\"Trading.Handler\", \"Trading.Router\"", error.Remedy, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileRegression_WhenSeveralHintsAreSet_PrefersTheMoreSpecificOne()
    {
        var error = Errors.CompileRegression(
            ["CS0246 src/A.cs: the type or namespace name 'ImmutableArray' could not be found"],
            new RollbackHints(Imports: ["System.Collections.Immutable"], Fields: ["Trading.OrderService.pending"]),
            "add_member");

        Assert.Contains("usings=[\"System.Collections.Immutable\"]", error.Remedy, StringComparison.Ordinal);
        Assert.DoesNotContain("never assigned", error.Remedy, StringComparison.Ordinal);
    }
}
