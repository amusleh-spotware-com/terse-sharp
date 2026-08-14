namespace Fixture.Trading;

public interface IHandler
{
    int Handle(int value);
}

public sealed class Awkward : IHandler
{
    private readonly int seed;

    public Awkward(int seed) => this.seed = seed;

    public int this[int index] => seed + index;

    public int Ordinary(int value) => seed + value;

    public TValue Echo<TValue>(TValue value) => value;

    public int Weigh(int count) => seed + count;

    public int Weigh(Boxed<IHandler> boxed) => boxed.Value.Handle(seed);

    public int Weigh((IHandler Left, IHandler Right) pair) => pair.Left.Handle(seed);

    int IHandler.Handle(int value) => seed - value;

    public static Awkward operator +(Awkward left, Awkward right) => new(left.seed + right.seed);
}

public sealed class Boxed<TValue>
{
    public Boxed(TValue value) => Value = value;

    public TValue Value { get; }

    public TValue Unwrap() => Value;
}
