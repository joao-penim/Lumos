using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Lumos.Core;

public static class IpcCommands
{
    public const string Status = "status";
    public const string Reload = "reload";
    public const string SetHdr = "set-hdr";
    public const string Launch = "launch";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Exit = "exit";
}

public sealed class IpcRequest
{
    public string Command { get; set; } = "";
    public string? AppId { get; set; }
    public string? DisplayId { get; set; }
    public bool Enabled { get; set; }
}

public sealed class IpcResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public AgentStatus? Status { get; set; }

    public static IpcResponse Success(AgentStatus? status = null) => new() { Ok = true, Status = status };
    public static IpcResponse Fail(string error) => new() { Ok = false, Error = error };
}

public sealed class AgentStatus
{
    public bool Paused { get; set; }
    public List<DisplayStatus> Displays { get; set; } = [];
    public List<SessionStatus> Sessions { get; set; } = [];
}

public sealed class DisplayStatus
{
    public string Id { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public bool IsPrimary { get; set; }
    public bool HdrSupported { get; set; }
    public bool HdrEnabled { get; set; }

    /// <summary>True when Lumos turned HDR on for this display and will turn it back off.</summary>
    public bool OwnedByLumos { get; set; }
}

public sealed class SessionStatus
{
    public string AppId { get; set; } = "";
    public string AppName { get; set; } = "";
    public int ProcessId { get; set; }
    public bool PendingLaunch { get; set; }
    public List<string> DisplayIds { get; set; } = [];
}

/// <summary>One request per connection, newline-delimited JSON. Works on Windows named pipes and Unix domain sockets.</summary>
public static class IpcClient
{
    public const string PipeName = "Lumos.Agent";

    public static async Task<IpcResponse?> SendAsync(IpcRequest request, int timeoutMs = 2000, CancellationToken ct = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
            await WriteAsync(pipe, request, LumosIpcJson.Default.IpcRequest, ct).ConfigureAwait(false);
            return await ReadAsync(pipe, LumosIpcJson.Default.IpcResponse, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is TimeoutException or IOException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    public static async Task WriteAsync<T>(Stream stream, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, info);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        stream.WriteByte((byte)'\n');
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(line) ? default : JsonSerializer.Deserialize(line, info);
    }
}
