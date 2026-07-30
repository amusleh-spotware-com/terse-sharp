namespace Fixture.Trading;

public interface IOrderRepository
{
    int PendingCount { get; }

    bool Submit(Order order);
}
