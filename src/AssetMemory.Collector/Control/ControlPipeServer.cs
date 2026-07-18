using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetMemory.Collector.Control;

/// <summary>
/// Serves <see cref="ControlService"/> over a local named pipe so console-TUI viewers can delegate
/// writes to whichever process owns the collector. Handles one request per connection (viewers open a
/// fresh connection per action), which is plenty for a single-user tracker.
/// </summary>
public sealed class ControlPipeServer : BackgroundService
{
    private readonly ControlService _service;
    private readonly ILogger<ControlPipeServer> _logger;

    public ControlPipeServer(ControlService service, ILogger<ControlPipeServer> logger)
    {
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Control pipe listening on {Pipe}", ControlProtocol.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    ControlProtocol.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await HandleConnectionAsync(server, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Control pipe connection failed");
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (line is null)
            return;

        string response;
        try
        {
            response = _service.Handle(line);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Control request errored");
            response = "{}";
        }

        await writer.WriteLineAsync(response.AsMemory(), ct).ConfigureAwait(false);
    }
}
