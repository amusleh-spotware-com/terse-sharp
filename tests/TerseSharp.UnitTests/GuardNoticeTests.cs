using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class GuardNoticeTests
{
    [Fact]
    public void Prefixed_ARoutingCarryingAQuote_StaysOneSingleQuotedShellWord()
    {
        var command = GuardNotice.Prefixed("git branch -a", "search_text query=\"it's\"");

        Assert.Equal("printf '%s\\n' 'terse guard: part of this command was removed and did NOT run - call instead: search_text query=\"it'\\''s\"' >&2\ngit branch -a", command);
    }
}