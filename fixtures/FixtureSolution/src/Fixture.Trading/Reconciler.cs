namespace Fixture.Trading;

public sealed class Reconciler
{
    public int Reconcile(Order order) => order.Volume > 0 ? 1 : 0;

    public int Reconcile(Order order, decimal tolerance) => order.Volume > tolerance ? 1 : 0;

    public int Reconcile(Dictionary<string, int> pending, Order order) => pending.Count + Reconcile(order);
}
