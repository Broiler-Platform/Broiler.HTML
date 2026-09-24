using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Broiler.HTML.Tests;

/// <summary>A request as the loopback server received it; header values are Latin-1 decoded octets.</summary>
internal sealed record LoopbackRequest(string Method, string Target, IReadOnlyList<KeyValuePair<string, string>> Headers)
{
    /// <summary>The target without its query.</summary>
    public string Path => Target.Split('?', 2)[0];

    /// <summary>The field's values joined with ", ", or <see langword="null"/> when the request has none.</summary>
    public string? Header(string name)
    {
        var values = Headers.Where(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToArray();
        return values.Length == 0 ? null : string.Join(", ", values);
    }

    public string? Cookie => Header("Cookie");
}

/// <summary>A response for the loopback server to write.</summary>
internal sealed record LoopbackResponse(int Status, byte[] Body, IReadOnlyList<(string Name, string Value)> Headers)
{
    public static LoopbackResponse Ok(string contentType, byte[] body, params (string Name, string Value)[] headers) =>
        new(200, body, [("Content-Type", contentType), .. headers]);

    public static LoopbackResponse Text(string contentType, string body, params (string Name, string Value)[] headers) =>
        Ok(contentType, Encoding.UTF8.GetBytes(body), headers);

    public static LoopbackResponse Redirect(string location, int status = 302) => new(status, [], [("Location", location)]);

    public static LoopbackResponse NotFound() => new(404, [], []);
}

/// <summary>
/// A minimal HTTP/1.1 origin on the loopback interface: one request per connection, a response chosen by a callback,
/// and a log of every request with its headers, so a test can see exactly which cookies a load carried.
/// </summary>
/// <remarks>
/// A raw <see cref="TcpListener"/> rather than <c>HttpListener</c>, which needs a URL reservation on Windows for
/// anything but an elevated process, on an ephemeral port chosen by the OS. It listens on 127.0.0.1 and, when the
/// OS allows, on ::1 with the same port, so <c>http://localhost:PORT</c> (a different site from
/// <c>http://127.0.0.1:PORT</c>, which Broiler.Net pins to the loopback interface, ::1 first) reaches it too
/// without waiting for the IPv4 fallback.
/// </remarks>
internal sealed class LoopbackHttpServer : IDisposable
{
    private readonly List<TcpListener> _listeners = [];
    private readonly Func<LoopbackRequest, CancellationToken, Task<LoopbackResponse>> _respond;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentQueue<LoopbackRequest> _requests = new();
    private readonly object _waitersGate = new();
    private readonly List<(Func<LoopbackRequest, bool> Match, TaskCompletionSource<LoopbackRequest> Done)> _waiters = [];

    public LoopbackHttpServer(Func<LoopbackRequest, LoopbackResponse> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public LoopbackHttpServer(Func<LoopbackRequest, CancellationToken, Task<LoopbackResponse>> respond)
    {
        _respond = respond;
        Port = BindLoopback(_listeners);

        foreach (var listener in _listeners)
            _ = Task.Run(() => AcceptAsync(listener));
    }

    /// <summary>
    /// Listens on 127.0.0.1 at an ephemeral port and on ::1 at the same port, and answers the port.
    /// </summary>
    /// <remarks>
    /// Broiler.Net dials <c>localhost</c> at ::1 first and keeps whichever connection succeeds, so a ::1 port held by
    /// another process would receive this server's <c>localhost</c> requests. The OS hands out IPv4 and IPv6 ports
    /// independently, so a taken ::1 port means another port pair, not an IPv4-only server; only a machine without
    /// IPv6 loopback is served over 127.0.0.1 alone (Broiler.Net falls back to it after a short delay).
    /// </remarks>
    internal static int BindLoopback(List<TcpListener> listeners)
    {
        const int attempts = 20;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (TryBindLoopbackPair(0, listeners, out var port))
                return port;
        }

        throw new InvalidOperationException(
            $"No ephemeral port was free on both 127.0.0.1 and [::1] after {attempts} attempts; localhost requests could not be routed to this server.");
    }

    /// <summary>
    /// One attempt of <see cref="BindLoopback"/>: 127.0.0.1 at <paramref name="ipv4Port"/> (0 for an ephemeral one),
    /// then ::1 at the same port. False, holding nothing, when another listener already has ::1 at that port.
    /// </summary>
    internal static bool TryBindLoopbackPair(int ipv4Port, List<TcpListener> listeners, out int port)
    {
        var ipv4 = new TcpListener(IPAddress.Loopback, ipv4Port);
        ipv4.Start();
        port = ((IPEndPoint)ipv4.LocalEndpoint).Port;

        if (!Socket.OSSupportsIPv6)
        {
            listeners.Add(ipv4);
            return true;
        }

        var ipv6 = new TcpListener(IPAddress.IPv6Loopback, port);
        try
        {
            ipv6.Start();
            listeners.Add(ipv4);
            listeners.Add(ipv6);
            return true;
        }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.AddressNotAvailable or SocketError.AddressFamilyNotSupported)
        {
            // No IPv6 loopback on this machine: localhost reaches the IPv4 listener.
            listeners.Add(ipv4);
            return true;
        }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            // Another process holds [::1]:port and would answer this server's localhost requests.
            ipv4.Stop();
            return false;
        }
    }

    public int Port { get; }

    /// <summary><c>http://127.0.0.1:PORT</c>.</summary>
    public string Origin => $"http://127.0.0.1:{Port}";

    /// <summary><c>http://localhost:PORT</c>: the same server as another site.</summary>
    public string LocalhostOrigin => $"http://localhost:{Port}";

    public string Url(string pathAndQuery) => Origin + pathAndQuery;

    public string LocalhostUrl(string pathAndQuery) => LocalhostOrigin + pathAndQuery;

    public IReadOnlyList<LoopbackRequest> Requests => _requests.ToArray();

    /// <summary>The requests for <paramref name="path"/> (query ignored), in arrival order.</summary>
    public LoopbackRequest[] RequestsFor(string path) => _requests.Where(r => r.Path == path).ToArray();

    /// <summary>The one request for <paramref name="path"/>; fails the test when there is none or more than one.</summary>
    public LoopbackRequest Single(string path)
    {
        var matching = RequestsFor(path);
        Assert.True(matching.Length == 1, $"Expected one request for {path}, got {matching.Length}: {string.Join(", ", _requests.Select(r => r.Target))}");
        return matching[0];
    }

    /// <summary>Completes when a request for <paramref name="path"/> has arrived, including one that already did.</summary>
    public Task<LoopbackRequest> WhenRequested(string path)
    {
        var done = new TaskCompletionSource<LoopbackRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waitersGate)
        {
            var seen = _requests.FirstOrDefault(r => r.Path == path);
            if (seen != null)
                done.SetResult(seen);
            else
                _waiters.Add((r => r.Path == path, done));
        }

        return done.Task;
    }

    private async Task AcceptAsync(TcpListener listener)
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                if (request == null)
                    return;

                Record(request);

                var response = await _respond(request, _stopping.Token);
                var head = new StringBuilder($"HTTP/1.1 {response.Status} Status\r\n");
                foreach (var (name, value) in response.Headers)
                    head.Append(name).Append(": ").Append(value).Append("\r\n");
                head.Append($"Content-Length: {response.Body.Length}\r\nConnection: close\r\n\r\n");

                await stream.WriteAsync(Encoding.Latin1.GetBytes(head.ToString()), _stopping.Token);
                await stream.WriteAsync(response.Body, _stopping.Token);
                await stream.FlushAsync(_stopping.Token);
            }
            catch (Exception)
            {
                // The client hung up or the server stopped; a test asserts on the log, not on this.
            }
        }
    }

    private void Record(LoopbackRequest request)
    {
        lock (_waitersGate)
        {
            _requests.Enqueue(request);
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Match(request))
                {
                    _waiters[i].Done.TrySetResult(request);
                    _waiters.RemoveAt(i);
                }
            }
        }
    }

    private async Task<LoopbackRequest?> ReadRequestAsync(NetworkStream stream)
    {
        // Drain the request line and headers before answering: a response written while the client is still
        // sending can surface as a connection reset rather than a response. Only GET is expected, so no body.
        var received = new List<byte>();
        var buffer = new byte[4096];
        while (!EndsHeaders(received))
        {
            var read = await stream.ReadAsync(buffer, _stopping.Token);
            if (read <= 0)
                return null;

            received.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        var lines = Encoding.Latin1.GetString(received.ToArray()).Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
            return null;

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }

        return new LoopbackRequest(requestLine[0], requestLine[1], headers);
    }

    private static bool EndsHeaders(List<byte> received)
    {
        var n = received.Count;
        return n >= 4 &&
               received[n - 4] == '\r' && received[n - 3] == '\n' &&
               received[n - 2] == '\r' && received[n - 1] == '\n';
    }

    public void Dispose()
    {
        _stopping.Cancel();
        foreach (var listener in _listeners)
            listener.Stop();
    }
}
