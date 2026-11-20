using TerseSharp.Server.Tools;

namespace TerseSharp.UnitTests;

public sealed class DeclaredSourceTests
{
    [Fact]
    public async Task ChosenAsync_ASyntaxMissWhoseAnswerCarriesAQueuedNote_FallsBackToTheSemanticRead()
    {
        var answer = await DeclaredSource.ChosenAsync(
            record => Task.FromResult(record(null) + "\nNOTE queued 2400ms before this call ran, while run_tests was in flight on this solution for 30s"),
            () => Task.FromResult("OrderService  class public"));

        Assert.Equal("OrderService  class public", answer);
    }

    [Fact]
    public async Task ChosenAsync_ASyntaxHit_KeepsTheDecoratedAnswer()
    {
        var answer = await DeclaredSource.ChosenAsync(
            record => Task.FromResult(record("src/OrderService.cs:11-11\npublic bool Submit(Order order) => true;") + "\nNOTE queued 2400ms"),
            () => Task.FromResult("semantic"));

        Assert.Equal("src/OrderService.cs:11-11\npublic bool Submit(Order order) => true;\nNOTE queued 2400ms", answer);
    }
}
