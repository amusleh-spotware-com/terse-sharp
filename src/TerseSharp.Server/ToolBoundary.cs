using System.Text.RegularExpressions;

namespace TerseSharp.Server;

public static class ToolBoundary
{
    public static string Run(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Render(exception);
        }
    }

    public static async Task<string> RunAsync(Func<Task<string>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Render(exception);
        }
    }

    private static bool IsExpected(Exception exception) => exception switch
    {
        AggregateException aggregate => aggregate.Flatten().InnerExceptions.All(IsExpected),
        OperationCanceledException => true,
        ArgumentException or InvalidOperationException or InvalidCastException or NotSupportedException
            or IOException or UnauthorizedAccessException or RegexMatchTimeoutException or FormatException
            or ObjectDisposedException => true,
        _ => false,
    };

    private static string Render(Exception exception)
    {
        var first = First(exception);

        return first is OperationCanceledException ? Errors.Cancelled().Render() : Describe(first);
    }

    private static Exception First(Exception exception) =>
        exception is AggregateException aggregate && aggregate.Flatten().InnerExceptions is [var inner, ..]
            ? inner
            : exception;

    private static string Describe(Exception exception) =>
        Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().Name}: {exception.Message}"),
            "check the arguments; use search_symbols or find_files to get a valid id or path")
            .Render();
}
