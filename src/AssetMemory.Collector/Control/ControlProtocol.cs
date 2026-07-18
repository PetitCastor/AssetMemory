using System.Text.Json;

namespace AssetMemory.Collector.Control;

/// <summary>
/// Wire contract for the collector's control channel: a local named pipe served by whichever process
/// owns the collector (the Blazor/tray host, or a sole-instance console TUI) and consumed by TUI
/// viewers. One JSON request line in, one JSON response line out.
/// </summary>
public static class ControlProtocol
{
    /// <summary>Machine-local pipe name (resolves to <c>\\.\pipe\AssetMemory.Control</c>).</summary>
    public const string PipeName = "AssetMemory.Control";

    /// <summary>Shared serializer options so client and server agree on casing.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

/// <summary>A control request. <see cref="Op"/> is one of: info, sync, clear, setpath.</summary>
public sealed record ControlRequest(string Op, string? Folder = null, bool StartFresh = false);

/// <summary>Response to <c>info</c> — where the owning process keeps its data.</summary>
public sealed record ControlInfo(string DbPath, string? GameLogPath);

/// <summary>Response to <c>clear</c>.</summary>
public sealed record ControlOk(bool Ok);

/// <summary>Response to <c>setpath</c>.</summary>
public sealed record ControlSetPathResult(bool Ok, string? ResolvedPath, string? Error);
