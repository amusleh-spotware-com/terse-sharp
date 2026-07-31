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
        catch (OperationCanceledException)
        {
            return Errors.Cancelled().Render();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Describe(exception);
        }
    }

    public static async Task<string> RunAsync(Func<Task<string>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Errors.Cancelled().Render();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Describe(exception);
        }
    }

    private static bool IsExpected(Exception exception) => exception is
        ArgumentException or InvalidOperationException or InvalidCastException or NotSupportedException
        or IOException or UnauthorizedAccessException or RegexMatchTimeoutException or FormatException
        or ObjectDisposedException;

    private static string Describe(Exception exception) =>
        Errors.Invalid(
            string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().Name}: {exception.Message}"),
            "check the arguments; use search_symbols or find_files to get a valid id or path")
            .Render();
}
