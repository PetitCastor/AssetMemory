using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AssetMemory.Collector.Control;

/// <summary>
/// Client side of the control channel, used by TUI viewers to delegate writes to the collector-owning
/// process. Each call opens a fresh pipe connection, sends one request line, and reads one response
/// line. <see cref="Info"/> doubles as a reachability probe (it throws if no server is listening).
/// </summary>
public sealed class ControlPipeClient
{
    private readonly int _connectTimeoutMs;

    public ControlPipeClient(int connectTimeoutMs = 3000) => _connectTimeoutMs = connectTimeoutMs;

    public ControlInfo Info() => Request<ControlInfo>(new ControlRequest("info"));
    public SyncResult Sync() => Request<SyncResult>(new ControlRequest("sync"));
    public void Clear() => Request<ControlOk>(new ControlRequest("clear"));
    public ControlSetPathResult SetPath(string folder, bool startFresh)
        => Request<ControlSetPathResult>(new ControlRequest("setpath", folder, startFresh));

    private T Request<T>(ControlRequest req)
    {
        using var client = new NamedPipeClientStream(".", ControlProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        client.Connect(_connectTimeoutMs);

        using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        writer.WriteLine(JsonSerializer.Serialize(req, ControlProtocol.Json));
        var line = reader.ReadLine() ?? throw new IOException("No response from control pipe");
        return JsonSerializer.Deserialize<T>(line, ControlProtocol.Json)
               ?? throw new IOException("Malformed control response");
    }
}
