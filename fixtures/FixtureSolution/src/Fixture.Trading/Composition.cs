namespace Fixture.Trading;

public sealed class ServiceBag
{
    public ServiceBag AddSingleton<TService, TImplementation>() => this;

    public ServiceBag AddScoped<TService, TImplementation>() => this;

    public ServiceBag MapGet(string route, string handler) => this;

    public ServiceBag MapPost(string route, string handler) => this;
}

public static class Composition
{
    public static ServiceBag Register(ServiceBag services) => services
        .AddSingleton<IOrderRepository, InMemoryOrderRepository>()
        .AddScoped<IOrderRepository, NullOrderRepository>();

    public static ServiceBag Routes(ServiceBag app) => app
        .MapGet("/orders", nameof(OrderBook))
        .MapPost("/orders", nameof(OrderService));
}
