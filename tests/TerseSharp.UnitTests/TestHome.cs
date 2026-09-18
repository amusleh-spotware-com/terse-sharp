using System.Runtime.CompilerServices;

namespace TerseSharp.UnitTests;

internal static class TestHome
{
    [ModuleInitializer]
    internal static void Isolate()
    {
        var home = Path.Combine(Path.GetTempPath(), "terse-unit-home", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(home);

        Environment.SetEnvironmentVariable("TERSE_HOME", home);
    }
}
