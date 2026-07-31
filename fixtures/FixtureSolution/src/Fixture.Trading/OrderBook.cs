namespace Fixture.Trading;

public sealed class OrderBook
{
    private readonly Dictionary<string, List<Order>> bySymbol = new(StringComparer.Ordinal);

    public int SymbolCount => bySymbol.Count;

    public void Add(Order order)
    {
        if (!bySymbol.TryGetValue(order.Symbol, out var orders))
        {
            orders = [];
            bySymbol[order.Symbol] = orders;
        }

        orders.Add(order);
    }

    public bool Remove(Order order)
    {
        if (!bySymbol.TryGetValue(order.Symbol, out var orders))
            return false;

        var removed = orders.Remove(order);

        if (orders.Count is 0)
            bySymbol.Remove(order.Symbol);

        return removed;
    }

    public decimal TotalVolume(string symbol)
    {
        if (!bySymbol.TryGetValue(symbol, out var orders))
            return 0m;

        var total = 0m;

        foreach (var order in orders)
            total += order.Volume;

        return total;
    }

    public decimal LargestVolume(string symbol)
    {
        if (!bySymbol.TryGetValue(symbol, out var orders))
            return 0m;

        var largest = 0m;

        foreach (var order in orders)
        {
            if (order.Volume > largest)
                largest = order.Volume;
        }

        return largest;
    }

    public IReadOnlyList<Order> For(string symbol) =>
        bySymbol.TryGetValue(symbol, out var orders) ? orders : [];

    public IReadOnlyList<string> Symbols()
    {
        var symbols = new List<string>(bySymbol.Count);

        foreach (var entry in bySymbol)
            symbols.Add(entry.Key);

        symbols.Sort(StringComparer.Ordinal);

        return symbols;
    }

    public void Clear() => bySymbol.Clear();
}
