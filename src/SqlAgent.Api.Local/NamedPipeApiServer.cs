using System.IO.Pipes;
using System.Text;

namespace SqlAgent.Api.Local;

/// <summary>
/// Hosts <see cref="LocalApiDispatcher"/> over a local named pipe for the WPF client (CD-50 T8, ADR-0003).
/// Framing is newline-delimited JSON: each request is one line in, each response one line out. A fresh
/// dispatcher is built per connection (via <paramref name="dispatcherFactory"/>) so each client gets its own
/// DbContext rather than sharing one across threads.
///
/// ponytail: serves one client connection at a time — fine for a single-user local app. If concurrent WPF
/// windows ever need it, wrap the per-connection handling in Task.Run and loop on accept without awaiting.
/// </summary>
public class NamedPipeApiServer(Func<LocalApiDispatcher> dispatcherFactory, string pipeName = LocalApiContract.DefaultPipeName)
{
    /// <summary>Accepts and serves connections until <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // PipeSecurity is intentionally default (current user / local). Local-only trust boundary per ADR-0003.
            using var server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(ct);
                await ServeAsync(server, dispatcherFactory(), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // Client vanished mid-exchange — drop this connection and accept the next one.
            }
        }
    }

    private static async Task ServeAsync(NamedPipeServerStream server, LocalApiDispatcher dispatcher, CancellationToken ct)
    {
        // Leave the stream open; UTF-8 without BOM. AutoFlush so each response line is sent immediately.
        using var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

        while (!ct.IsCancellationRequested && server.IsConnected)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;            // client closed its write side
            if (line.Length == 0) continue;     // tolerate blank keep-alive lines
            var response = await dispatcher.HandleAsync(line, ct);
            await writer.WriteLineAsync(response.AsMemory(), ct);
        }
    }
}
