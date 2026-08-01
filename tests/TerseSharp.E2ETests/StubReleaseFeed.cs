using System.Net;
using System.Net.Sockets;

namespace TerseSharp.E2ETests;

internal sealed class StubReleaseFeed : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly string location;

    private int requests;

    public StubReleaseFeed(string tag)
    {
        var port = FreePort();

        location = "https://github.com/amusleh-spotware-com/terse-sharp/releases/tag/" + tag;
        Endpoint = string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}/releases/latest");

        listener.Prefixes.Add(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{port}/"));
        listener.Start();

        _ = Task.Run(ServeAsync);
    }

    public string Endpoint { get; }

    public int Requests => Volatile.Read(ref requests);

    public void Dispose() => listener.Close();

    private async Task ServeAsync()
    {
        while (listener.IsListening)
        {
            if (await AcceptAsync() is not { } context)
                return;

            Interlocked.Increment(ref requests);

            context.Response.StatusCode = (int)HttpStatusCode.Found;
            context.Response.RedirectLocation = location;
            context.Response.Close();
        }
    }

    private async Task<HttpListenerContext?> AcceptAsync()
    {
        try
        {
            return await listener.GetContextAsync();
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }
}
