using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class EditGateClassificationTests
{
    private const string MissingMock = "CS0246 C:\\repo\\tests\\OrderTests.cs: The type or namespace name 'Mock<>' could not be found";

    private const string RealRegression = "CS0161 C:\\repo\\src\\OrderService.cs: not all code paths return a value";

    [Fact]
    public void AnUnresolvedNameTheProjectAlreadyFailedToResolve_IsNotCountedAsIntroduced()
    {
        var baseline = new Dictionary<string, int>(StringComparer.Ordinal) { [MissingMock] = 2 };

        Assert.True(EditGate.Unresolvable(MissingMock, [], baseline));
    }

    [Fact]
    public void AnUnresolvedNameInAFileThatDidNotExistBefore_IsNotCountedAsIntroduced()
    {
        var arrived = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:\\repo\\tests\\OrderTests.cs" };

        Assert.True(EditGate.Unresolvable(MissingMock, arrived, []));
    }

    [Fact]
    public void AnUnresolvedNameInAFileThatAlreadyCompiled_IsStillARegression() =>
        Assert.False(EditGate.Unresolvable(MissingMock, [], []));

    [Fact]
    public void AnErrorThatIsNotAnUnresolvedName_IsAlwaysARegression()
    {
        var baseline = new Dictionary<string, int>(StringComparer.Ordinal) { [RealRegression] = 1 };
        var arrived = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:\\repo\\src\\OrderService.cs" };

        Assert.False(EditGate.Unresolvable(RealRegression, arrived, baseline));
    }

    [Fact]
    public void AMissingNamespace_IsClassifiedTheSameWayAsAMissingType()
    {
        const string MissingNamespace = "CS0234 C:\\repo\\tests\\OrderTests.cs: The type or namespace name 'Moq' does not exist";
        var baseline = new Dictionary<string, int>(StringComparer.Ordinal) { [MissingNamespace] = 1 };

        Assert.True(EditGate.Unresolvable(MissingNamespace, [], baseline));
    }
}
