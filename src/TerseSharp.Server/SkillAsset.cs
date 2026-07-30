using System.Reflection;

namespace TerseSharp.Server;

public static class SkillAsset
{
    private const string ResourceName = "TerseSharp.Server.Assets.SKILL.md";

    public static string Read()
    {
        using var stream = typeof(SkillAsset).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded resource '{ResourceName}' is missing from {Assembly.GetExecutingAssembly().GetName().Name}");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
