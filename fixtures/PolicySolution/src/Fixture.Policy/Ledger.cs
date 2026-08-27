namespace Fixture.Policy;

public sealed class Ledger
{
    private readonly List<int> entries = [];

    public void Record(int amount) => entries.Add(amount);

    public int Total()
    {
        var total = 0;

        foreach (var entry in entries)
            total += entry;

        return total;
    }
}
