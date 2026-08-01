namespace TerseSharp.Core;

public static class UpdateCheck
{
    public const string DefaultEndpoint = "https://github.com/amusleh-spotware-com/terse-sharp/releases/latest";

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    public static async Task<ReleaseVersion?> RunAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        var cached = await ReadAsync(request.StatePath, cancellationToken).ConfigureAwait(false);

        if (cached is { } state && DateTimeOffset.UtcNow - state.CheckedUtc < request.Window)
            return state.Latest;

        var latest = await LatestAsync(request, cancellationToken).ConfigureAwait(false);

        await TrySaveAsync(request.StatePath, new UpdateState(DateTimeOffset.UtcNow, latest), cancellationToken).ConfigureAwait(false);

        return latest;
    }

    public static string? Notice(ReleaseVersion running, ReleaseVersion? latest) =>
        latest is { } published && published.IsNewerThan(running)
            ? string.Create(CultureInfo.InvariantCulture, $"UPDATE terse {running} -> {published} is available - run: dotnet tool update -g TerseSharp")
            : null;

    private static async Task<ReleaseVersion?> LatestAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = Deadline };
            using var message = new HttpRequestMessage(HttpMethod.Head, new Uri(request.Endpoint, UriKind.Absolute));

            message.Headers.UserAgent.ParseAdd("terse/" + request.Running);

            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            return Tag(response.Headers.Location);
        }
        catch (Exception exception) when (Recoverable(exception, cancellationToken))
        {
            return null;
        }
    }

    private static bool Recoverable(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && exception is HttpRequestException or OperationCanceledException or UriFormatException or InvalidOperationException;

    private static ReleaseVersion? Tag(Uri? location)
    {
        if (location is null)
            return null;

        var text = location.OriginalString.AsSpan();
        var slash = text.LastIndexOf('/');

        return ReleaseVersion.TryParse(slash < 0 ? text : text[(slash + 1)..], out var version) ? version : null;
    }

    private static async Task<UpdateState?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

            return UpdateState.TryParse(text, out var state) ? state : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<bool> TrySaveAsync(string path, UpdateState state, CancellationToken cancellationToken)
    {
        try
        {
            await AtomicWrite.TextAsync(path, state.Render(), cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
