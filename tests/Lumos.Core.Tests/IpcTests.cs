using System.Text.Json;
using Lumos.Core;
using Xunit;

namespace Lumos.Core.Tests;

public class IpcTests
{
    [Fact]
    public void Messages_serialize_to_a_single_line()
    {
        // The protocol is newline-delimited; indented JSON would break it.
        var response = IpcResponse.Success(new AgentStatus
        {
            Displays = [new DisplayStatus { Id = @"\\?\DISPLAY#X&1", FriendlyName = "Monitor" }],
            Sessions = [new SessionStatus { AppName = "Game", DisplayIds = ["a", "b"] }],
        });
        var json = JsonSerializer.Serialize(response, LumosIpcJson.Default.IpcResponse);
        Assert.DoesNotContain('\n', json);

        var request = JsonSerializer.Serialize(new IpcRequest { Command = IpcCommands.Launch, AppId = "x" }, LumosIpcJson.Default.IpcRequest);
        Assert.DoesNotContain('\n', request);
    }

    [Fact]
    public async Task Request_round_trips_through_a_stream()
    {
        using var stream = new MemoryStream();
        await IpcClient.WriteAsync(stream, new IpcRequest { Command = IpcCommands.SetHdr, DisplayId = "d1", Enabled = true },
            LumosIpcJson.Default.IpcRequest, TestContext.Current.CancellationToken);
        stream.Position = 0;
        var back = await IpcClient.ReadAsync(stream, LumosIpcJson.Default.IpcRequest, TestContext.Current.CancellationToken);
        Assert.NotNull(back);
        Assert.Equal(IpcCommands.SetHdr, back.Command);
        Assert.Equal("d1", back.DisplayId);
        Assert.True(back.Enabled);
    }

    [Fact]
    public void Settings_round_trip_with_enum_names()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumos-test-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new LumosSettings
            {
                DefaultTarget = DisplayTargetKind.AllHdrCapable,
                Apps = [new WatchedApp { DisplayName = "Game", ExePath = @"C:\G\g.exe", Target = DisplayTargetKind.Specific, TargetDisplayId = "x" }],
            };
            SettingsStore.Save(settings, path);
            Assert.Contains("\"allHdrCapable\"", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);

            var back = SettingsStore.Load(path);
            Assert.Equal(DisplayTargetKind.AllHdrCapable, back.DefaultTarget);
            Assert.Equal(DisplayTargetKind.Specific, Assert.Single(back.Apps).Target);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
