namespace Fixture.Trading;

public sealed class OrderService
{
    private readonly IOrderRepository repository;

    public OrderService(IOrderRepository repository) => this.repository = repository;

    public int PendingCount => repository.PendingCount;

    public bool Submit(Order order) => repository.Submit(order);

    public bool SubmitTwice(Order order) => Submit(order) && Submit(order);

    public int Unused() => 7;

    private int NeverCalled() => 42;
}
