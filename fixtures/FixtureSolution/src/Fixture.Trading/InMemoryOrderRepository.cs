namespace Fixture.Trading;

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly List<Order> orders = [];

    public int PendingCount => orders.Count;

    public bool Submit(Order order)
    {
        orders.Add(order);

        return true;
    }
}
