using System.Text.Json;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class WriteSerializerTests
{
    private static readonly JsonElement Mutating = JsonElement.Parse("""{"properties":{"dryRun":{"type":"boolean"},"path":{"type":"string"}}}""");

    private static readonly JsonElement Reading = JsonElement.Parse("""{"properties":{"path":{"type":"string"}}}""");

    [Fact]
    public void Writes_AToolDeclaringDryRunCalledWithoutIt_IsSerialized() => Assert.True(WriteSerializer.Writes("edit_text", Mutating, new Dictionary<string, JsonElement>()));

    [Fact]
    public void Writes_ADryRun_IsNotSerialized() => Assert.False(WriteSerializer.Writes("edit_text", Mutating, new Dictionary<string, JsonElement> { ["dryRun"] = JsonElement.Parse("true") }));

    [Fact]
    public void Writes_AReadTool_IsNotSerialized() => Assert.False(WriteSerializer.Writes("read_text", Reading, null));

    [Fact]
    public void Writes_UndoLastChange_IsSerializedThoughItDeclaresNoDryRun() => Assert.True(WriteSerializer.Writes("undo_last_change", Reading, null));
}
