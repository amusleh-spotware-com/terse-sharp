namespace Gap.Core;

public abstract class GapBase
{
    public abstract int Update(int amount);
}

public sealed class GapService : GapBase
{
    public int Total { get; private set; }

    public override int Update(int amount) => Total += amount;
}

public sealed class GapConsumer
{
    public int Run(GapService service)
    {
        service.Update(1);
        service.Update(2);

        return service.Total;
    }
}
