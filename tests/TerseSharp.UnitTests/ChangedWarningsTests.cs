using TerseSharp.Core;

namespace TerseSharp.UnitTests;

public sealed class ChangedWarningsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "terse-warnings");

    [Fact]
    public void FileOf_ForACompilerWarning_IsTheFileBeforeItsPosition() =>
        Assert.Equal(
            Path.Combine(Root, "src", "Calculator.cs"),
            ChangedWarnings.FileOf(Path.Combine(Root, "src", "Calculator.cs") + "(11,13): warning CS0219: The variable 'x' is assigned [" + Path.Combine(Root, "src", "App.csproj") + "]"));

    [Fact]
    public void FileOf_ForAProjectLevelWarning_IsTheProjectFile() =>
        Assert.Equal(
            Path.Combine(Root, "src", "App.csproj"),
            ChangedWarnings.FileOf(Path.Combine(Root, "src", "App.csproj") + " : warning NU1701: Package restored using a fallback framework"));

    [Fact]
    public void FileOf_ForALineThatIsNoWarning_IsEmpty() =>
        Assert.Equal(string.Empty, ChangedWarnings.FileOf("Build succeeded."));

    [Fact]
    public void Count_OverTrackedAndUntrackedChanges_CountsOnlyTheWarningsInThoseFiles()
    {
        var changed = ChangedWarnings.Paths(Root, "src/Changed.cs\n", "src/Added.cs\n");
        string[] warnings =
        [
            Path.Combine(Root, "src", "Changed.cs") + "(1,1): warning CS0168: unused",
            Path.Combine(Root, "src", "Added.cs") + "(2,2): warning CS0219: unread",
            Path.Combine(Root, "src", "Untouched.cs") + "(3,3): warning CS0169: never used",
        ];

        Assert.Equal(2, ChangedWarnings.Count(warnings, changed));
    }

    [Fact]
    public void Count_WithNothingChanged_IsZero() =>
        Assert.Equal(0, ChangedWarnings.Count([Path.Combine(Root, "src", "A.cs") + "(1,1): warning CS0168: unused"], ChangedWarnings.Paths(Root, string.Empty, string.Empty)));
}
