using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TerseSharp.Server;

namespace TerseSharp.UnitTests;

public sealed class IdenticalCallTests
{
    [Fact]
    public void Record_ForTheFirstCallOfItsKind_AnswersNothing()
    {
        IdenticalCall.Forget();

        Assert.Null(IdenticalCall.Record("build", "build|solution", "build ok  errors=0 warnings=0", 0, 0));
    }

    [Fact]
    public void Record_ForAByteIdenticalRepeatWithNothingWrittenInBetween_NamesTheRepeatItsAgeAndThePreviousVerdict()
    {
        IdenticalCall.Forget();
        IdenticalCall.Record("run_tests", "run_tests|whole", "run_tests PASSED  passed=478", 0, 3);

        var note = IdenticalCall.Record("run_tests", "run_tests|whole", "run_tests PASSED  passed=478", Stopwatch.Frequency * 41, 3);

        Assert.Equal(
            "repeat #2 of this exact run_tests call 41s ago - previous verdict: run_tests PASSED  passed=478; nothing was written in between",
            note);
    }

    [Fact]
    public void Record_WhenDocumentsWereWrittenBetweenTheTwoCalls_CountsThemInsteadOfCallingItWaste()
    {
        IdenticalCall.Forget();
        IdenticalCall.Record("build", "build|solution", "build ok", 0, 4);

        var note = IdenticalCall.Record("build", "build|solution", "build ok", Stopwatch.Frequency * 9, 6);

        Assert.Equal("repeat #2 of this exact build call 9s ago - previous verdict: build ok; 2 document(s) changed since", note);
    }

    [Fact]
    public void Record_ForADifferentArgumentSet_AnswersNothing()
    {
        IdenticalCall.Forget();
        IdenticalCall.Record("run_tests", "run_tests|UnitTests", "run_tests PASSED", 0, 0);

        Assert.Null(IdenticalCall.Record("run_tests", "run_tests|E2ETests", "run_tests PASSED", Stopwatch.Frequency, 0));
    }

    [Fact]
    public void Record_ForAThirdIdenticalCall_CountsIt()
    {
        IdenticalCall.Forget();
        IdenticalCall.Record("list_tests", "list_tests|", "12 tests", 0, 0);
        IdenticalCall.Record("list_tests", "list_tests|", "12 tests", 0, 0);

        var note = IdenticalCall.Record("list_tests", "list_tests|", "12 tests", 0, 0);

        Assert.StartsWith("repeat #3 of this exact list_tests call", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Note_ForAToolTheLadderDoesNotWatch_AnswersNothingHoweverOftenItRepeats()
    {
        IdenticalCall.Forget();
        var parameters = Parameters("read_text", ("path", "src/A.cs"));
        var result = Result("12 lines");

        IdenticalCall.Note("read_text", parameters, result);

        Assert.Null(IdenticalCall.Note("read_text", parameters, result));
    }

    [Fact]
    public void Key_ForTheSameArgumentsInAnotherOrder_IsTheSameKey()
    {
        var first = IdenticalCall.Key("build", Parameters("build", ("project", "Core"), ("configuration", "Release")));
        var second = IdenticalCall.Key("build", Parameters("build", ("configuration", "Release"), ("project", "Core")));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Key_ForADifferentArgumentValue_IsADifferentKey()
    {
        var first = IdenticalCall.Key("build", Parameters("build", ("project", "Core")));
        var second = IdenticalCall.Key("build", Parameters("build", ("project", "Server")));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Verdict_ForAMultiLineResult_KeepsTheFirstLineOnly() =>
        Assert.Equal("build FAILED  errors=2", IdenticalCall.Verdict(Result("build FAILED  errors=2\nCS0103 ...\nCS0246 ...")));

    private static CallToolRequestParams Parameters(string name, params (string Key, string Value)[] arguments)
    {
        var map = new Dictionary<string, JsonElement>(arguments.Length, StringComparer.Ordinal);

        foreach (var (key, value) in arguments)
            map[key] = JsonSerializer.SerializeToElement(value);

        return new CallToolRequestParams { Name = name, Arguments = map };
    }

    private static CallToolResult Result(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };
}
