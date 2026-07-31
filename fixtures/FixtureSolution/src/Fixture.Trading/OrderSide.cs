namespace Fixture.Trading;

public enum OrderSide
{
    Buy,
    Sell,
}

public delegate void OrderSubmitted(Order order);
