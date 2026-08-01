namespace Fixture.Blazor.Components;

public sealed partial class Card
{
    public void Dispose() => Filter = string.Empty;
}
