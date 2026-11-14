using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumos.Core;

public sealed class AgentState
{
    public List<string> OwnedDisplays { get; set; } = [];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LumosSettings))]
[JsonSerializable(typeof(AgentState))]
public sealed partial class LumosJson : JsonSerializerContext;

/// <summary>Compact (single-line) JSON for the newline-delimited IPC protocol.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
public sealed partial class LumosIpcJson : JsonSerializerContext;

public static class LumosPaths
{
    /// <summary>%APPDATA%\Lumos on Windows, ~/.config/Lumos on Linux.</summary>
    public static string ConfigDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumos");

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public static string StateFile => Path.Combine(ConfigDir, "state.json");
    public static string LogFile => Path.Combine(ConfigDir, "agent.log");
}

public static class SettingsStore
{
    public static LumosSettings Load(string? path = null)
    {
        path ??= LumosPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                using var s = File.OpenRead(path);
                return JsonSerializer.Deserialize(s, LumosJson.Default.LumosSettings) ?? new LumosSettings();
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Write($"Failed to read settings: {e.Message}");
        }
        return new LumosSettings();
    }

    public static void Save(LumosSettings settings, string? path = null)
    {
        path ??= LumosPaths.SettingsFile;
        AtomicWrite(path, JsonSerializer.SerializeToUtf8Bytes(settings, LumosJson.Default.LumosSettings));
    }

    internal static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class JsonStateStore(string? path = null) : IStateStore
{
    private readonly string _path = path ?? LumosPaths.StateFile;

    public IReadOnlyCollection<string> LoadOwnedDisplays()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var s = File.OpenRead(_path);
            return JsonSerializer.Deserialize(s, LumosJson.Default.AgentState)?.OwnedDisplays ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Write($"Failed to read state: {e.Message}");
            return [];
        }
    }

    public void SaveOwnedDisplays(IReadOnlyCollection<string> displayIds)
    {
        try
        {
            var state = new AgentState { OwnedDisplays = [.. displayIds] };
            SettingsStore.AtomicWrite(_path, JsonSerializer.SerializeToUtf8Bytes(state, LumosJson.Default.AgentState));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Failed to write state: {e.Message}");
        }
    }
}

/// <summary>Tiny append-only log, capped at 512 KB (rotates to .old). No-op until a path is set.</summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly Lock Gate = new();
    private static string? _path;

    public static void Initialize(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _path = path;
    }

    public static void Write(string message)
    {
        var path = _path;
        if (path is null) return;
        lock (Gate)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes) File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
