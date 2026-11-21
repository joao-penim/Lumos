using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumos.App.Services;
using Lumos.Core.Games;

namespace Lumos.App.ViewModels;

/// <summary>One game in the "Choose from installed games" list. The exe and icon are resolved in the background.</summary>
public partial class InstalledGameRow(InstalledGame game, bool isAdded) : ObservableObject
{
    private Task? _resolving;

    public InstalledGame Game { get; } = game;
    public bool IsAdded { get; } = isAdded;

    /// <summary>Already-added games are shown greyed out and can't be picked again.</summary>
    public double RowOpacity => IsAdded ? 0.45 : 1.0;
    public string Name => Game.Name;
    public string Source => Game.Source;
    public string InstallDir => Game.InstallDir;

    /// <summary>Exes to watch, best first. Empty until <see cref="ResolveAsync"/> completes.</summary>
    public IReadOnlyList<string> Candidates { get; private set; } = [];

    [ObservableProperty] private Bitmap? _icon;

    /// <summary>At most this many game folders are walked at once, so a big library doesn't flood the disk.</summary>
    private static readonly SemaphoreSlim Gate = new(4);

    /// <summary>
    /// Resolves the exe candidates and icon once; later calls return the same task.
    /// A call that was cancelled before it started is retried by the next call.
    /// </summary>
    public Task ResolveAsync(CancellationToken cancellationToken = default) =>
        _resolving is { IsCanceled: false } running ? running : _resolving = ResolveCoreAsync(cancellationToken);

    private async Task ResolveCoreAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            Candidates = await Task.Run(() => GameExeResolver.Resolve(Game), CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Candidates = [];
        }
        finally
        {
            Gate.Release();
        }
        if (Candidates.Count > 0) Icon = await IconLoader.LoadAsync(Candidates[0]);
    }
}
