using System.IO.Pipes;
using System.Text.Json;
using Lumos.Core;

namespace Lumos.Agent;

/// <summary>Named-pipe server, restricted to the current user. Fully async: no thread is parked while idle.</summary>
internal sealed class IpcServer(Func<IpcRequest, Task<IpcResponse>> handler) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    public void Start() => _ = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(IpcClient.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _ = HandleAsync(server, ct);
                server = null; // ownership passed to HandleAsync
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                Log.Write($"IPC accept failed: {e.Message}");
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe.ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var request = await IpcClient.ReadAsync(pipe, LumosIpcJson.Default.IpcRequest, timeout.Token).ConfigureAwait(false);
            IpcResponse response;
            try
            {
                response = request is null ? IpcResponse.Fail("Empty request") : await handler(request).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Write($"IPC handler failed: {e}");
                response = IpcResponse.Fail(e.Message);
            }
            await IpcClient.WriteAsync(pipe, response, LumosIpcJson.Default.IpcResponse, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or JsonException)
        {
            // client went away
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
