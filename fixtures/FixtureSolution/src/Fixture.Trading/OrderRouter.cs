namespace Fixture.Trading;

public sealed class OrderRouter
{
    private readonly OrderService service;

    public OrderRouter(OrderService service) => this.service = service;

    public bool Route(Order order) => service.Submit(order);

    public bool Retry(Order order) => service.Submit(order);
}
