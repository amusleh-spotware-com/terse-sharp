namespace Fixture.Trading.Views;

public sealed class ShellView
{
    public int Clicks { get; private set; }

    public void OnSubmitClicked() => Clicks++;
}
