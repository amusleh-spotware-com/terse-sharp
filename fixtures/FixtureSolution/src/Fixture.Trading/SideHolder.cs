namespace Fixture.Trading;

public sealed class SideHolder
{
    public OrderSide OrderSide { get; init; }

    public OrderSide Flip() => OrderSide is OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
}
