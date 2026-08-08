namespace Fixture.Trading;

public sealed class Reconciler
{
    /// <summary>
    /// Reports whether the order carries any volume at all.
    /// </summary>
    /// <param name="order">The order to inspect.</param>
    /// <returns>One when the order has volume, zero otherwise.</returns>
    public int Reconcile(Order order) => order.Volume > 0 ? 1 : 0;

    // A deliberate inline comment, so a comment-stripping read has something to strip.
    public int Reconcile(Order order, decimal tolerance) => order.Volume > tolerance ? 1 : 0;

    public int Reconcile(Dictionary<string, int> pending, Order order) => pending.Count + Reconcile(order);

    public/*none either side*/decimal Butted(Order order)
    {
        var negated = 1 - /*space before, none after*/-1;

        return/*the only separator*/order.Volume * negated; // and a trailing one
    }
}
