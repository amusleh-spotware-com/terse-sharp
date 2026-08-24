namespace Fixture.Trading;

/// <summary>
/// A one-line declaration whose rendered source is longer than the steer it would replace, so
/// get_symbol_source must keep the outline for it even though the declaration itself is one line.
/// </summary>
/// <param name="Cents">The amount, in the smallest unit of the currency.</param>
/// <param name="Currency">The ISO 4217 code.</param>
public readonly record struct Money(long Cents, string Currency);

public readonly record struct Tag(string Name)
{
    public bool IsEmpty => Name.Length is 0;
}

public readonly record struct Wide(string DeliberatelyLongPropertyNameOne, string DeliberatelyLongPropertyNameTwo, string DeliberatelyLongPropertyNameThree)
{
    public bool IsEmpty => DeliberatelyLongPropertyNameOne.Length + DeliberatelyLongPropertyNameTwo.Length + DeliberatelyLongPropertyNameThree.Length is 0;
}
