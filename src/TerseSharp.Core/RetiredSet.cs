using System.Runtime.CompilerServices;

namespace TerseSharp.Core;

public sealed class RetiredSet
{
    private static readonly object Present = new();

    private readonly ConditionalWeakTable<object, object> entries = [];

    public void Add(object retired) => entries.AddOrUpdate(retired, Present);

    public int Retained(Action collect) => Alive() is 0 ? 0 : Collected(collect);

    private int Collected(Action collect)
    {
        collect();

        return Alive();
    }

    private int Alive() => entries.Count();
}
