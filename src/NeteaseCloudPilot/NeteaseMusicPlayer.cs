using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using Amireux999000.NeteaseCloudPilot.Media;
using Amireux999000.NeteaseCloudPilot.State;

namespace Amireux999000.NeteaseCloudPilot;

/// <summary>
/// Implements <see cref="IMusicPlayer"/> so Macro Deck can show the now-playing
/// widget (artwork, title, artist, progress) and send transport commands.
/// </summary>
public sealed class NeteaseMusicPlayer(
    NeteaseStateManager state,
    SmtcMonitor monitor,
    ILogger? logger = null) : IMusicPlayer
{
    private readonly ILogger _log = (logger ?? Log.Logger).ForContext<NeteaseMusicPlayer>();

    public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = state.Current;

        return Task.FromResult(new MusicPlayerState
        {
            IsConnected = snapshot.IsConnected,
            IsUnavailable = !snapshot.IsConnected,
            StatusMessage = snapshot.IsConnected ? null : "NetEase Cloud Music is not running.",
            PlaybackState = snapshot.PlaybackState,
            TrackName = snapshot.TrackName,
            Artists = snapshot.ArtistName is { } artist ? [artist] : [],
            AlbumName = snapshot.AlbumName,
            ArtworkId = snapshot.ArtworkId,
            Position = snapshot.CurrentPosition,
            Duration = snapshot.Duration,
            VolumePercent = snapshot.VolumePercent,
            ShuffleEnabled = snapshot.ShuffleEnabled,
            RepeatMode = snapshot.RepeatMode,
            DeviceName = "NetEase Cloud Music",
            DeviceType = "Desktop",
        });
    }

    public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default)
    {
        var bytes = monitor.GetArtwork(artworkId);
        if (bytes is null || bytes.Length == 0)
        {
            return Task.FromResult<MusicPlayerArtwork?>(null);
        }

        return Task.FromResult<MusicPlayerArtwork?>(new MusicPlayerArtwork(bytes, "image/jpeg"));
    }

    public Task PlayAsync(CancellationToken cancellationToken = default) =>
        SendAsync(static s => s.TryPlayAsync().AsTask(), "Play", cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        SendAsync(static s => s.TryPauseAsync().AsTask(), "Pause", cancellationToken);

    public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = state.Current;
        return snapshot.IsPlaying
            ? SendAsync(static s => s.TryPauseAsync().AsTask(), "Pause", cancellationToken)
            : SendAsync(static s => s.TryPlayAsync().AsTask(), "Play", cancellationToken);
    }

    public Task NextAsync(CancellationToken cancellationToken = default) =>
        SendAsync(static s => s.TrySkipNextAsync().AsTask(), "Next", cancellationToken);

    public Task PreviousAsync(CancellationToken cancellationToken = default) =>
        SendAsync(static s => s.TrySkipPreviousAsync().AsTask(), "Previous", cancellationToken);

    public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        var session = monitor.CurrentSession;
        if (session is null)
        {
            throw new InvalidOperationException("NetEase Cloud Music is not running.");
        }

        return session.TryChangePlaybackPositionAsync((long)position.TotalMicroseconds)
            .AsTask().WaitAsync(cancellationToken);
    }

    public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        // Volume is controlled via keyboard shortcuts; SMTC does not expose it.
        _log.Debug("SetVolumeAsync called with {Volume}; using keyboard shortcuts.", volumePercent);
        return Task.CompletedTask;
    }

    public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _log.Debug("SetShuffleAsync called with {Enabled}; SMTC may not support this for NetEase Cloud Music.", enabled);
        return Task.CompletedTask;
    }

    public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
    {
        _log.Debug("SetRepeatModeAsync called with {Mode}; SMTC may not support this for NetEase Cloud Music.", mode);
        return Task.CompletedTask;
    }

    private async Task SendAsync(
        Func<Windows.Media.Control.GlobalSystemMediaTransportControlsSession, Task<bool>> command,
        string commandName,
        CancellationToken cancellationToken)
    {
        var session = monitor.CurrentSession;
        if (session is null)
        {
            throw new InvalidOperationException("NetEase Cloud Music is not running.");
        }

        var result = await command(session).WaitAsync(cancellationToken);
        if (!result)
        {
            _log.Debug("Music player command {Command} was not accepted.", commandName);
        }
    }
}
