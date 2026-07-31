namespace Fixture.Trading.Views;

public sealed class OrderViewModel
{
    public string Symbol { get; init; } = string.Empty;

    public decimal Volume { get; init; }

    public Order? Selected { get; init; }
}
