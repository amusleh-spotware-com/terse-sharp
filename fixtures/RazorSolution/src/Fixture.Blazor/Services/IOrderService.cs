using Fixture.Blazor.Models;

namespace Fixture.Blazor.Services;

public interface IOrderService
{
    IReadOnlyList<Order> Open { get; }
}
