namespace Fixture.Trading;

public sealed class NullOrderRepository : IOrderRepository
{
    public int PendingCount => 0;

    public bool Submit(Order order) => false;
}
