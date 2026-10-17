using System.Runtime.InteropServices;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Microsoft.Extensions.Hosting;
using Serilog;
using Amireux999000.NeteaseCloudPilot.State;

namespace Amireux999000.NeteaseCloudPilot.Media;

/// <summary>
/// Monitors Windows System Media Transport Controls (SMTC) for a NetEase Cloud Music
/// session, reads playback info and media properties, and pushes updates into the
/// <see cref="NeteaseStateManager"/>.
///
/// Runs as a hosted service: polls for session appearance/disappearance and subscribes
/// to session events while a matching session is active.
/// </summary>
public sealed class SmtcMonitor : BackgroundService
{
    private readonly NeteaseStateManager _state;
    private readonly ILogger _logger;
    private readonly Lock _sync = new();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _sessionAppId;
    private CancellationTokenSource? _stopCts;
    private bool _disposed;

    // Artwork cache: artwork-id -> jpeg bytes
    private readonly Dictionary<string, byte[]> _artworkCache = new(StringComparer.Ordinal);
    private string? _lastArtworkId;

    public SmtcMonitor(NeteaseStateManager state, ILogger logger)
    {
        _state = state;
        _logger = logger.ForContext<SmtcMonitor>();
    }

    /// <summary>The currently active NetEase session, or null when not running.</summary>
    public GlobalSystemMediaTransportControlsSession? CurrentSession
    {
        get { lock (_sync) { return _session; } }
    }

    public bool IsConnected => CurrentSession is not null;

    /// <summary>
    /// Retrieve cached artwork bytes for the given artwork id.
    /// Returns null when not cached (caller should request a refresh).
    /// </summary>
    public byte[]? GetArtwork(string? artworkId)
    {
        if (artworkId is null) return null;
        lock (_sync)
        {
            return _artworkCache.TryGetValue(artworkId, out var bytes) ? bytes : null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var token = _stopCts.Token;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.Warning("SMTC monitor is only supported on Windows. Playback control will be unavailable.");
            _state.Update(s => s with { IsConnected = false });
            return;
        }

        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialise SMTC session manager.");
            return;
        }

        _logger.Information("SMTC monitor started. Looking for NetEase Cloud Music session…");

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(NeteaseValues.PollIntervalMs));
        while (!token.IsCancellationRequested)
        {
            try
            {
                await PollAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "SMTC poll iteration failed.");
            }

            try
            {
                await timer.WaitForNextTickAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var session = FindNeteaseSession();
        var changed = false;

        lock (_sync)
        {
            if (!ReferenceEquals(session, _session))
            {
                if (_session is not null)
                {
                    _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                }

                _session = session;
                _sessionAppId = session?.SourceAppUserModelId;
                changed = true;

                if (session is not null)
                {
                    session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _logger.Information("NetEase Cloud Music session detected: {AppId}", _sessionAppId);
                }
                else
                {
                    _logger.Information("NetEase Cloud Music session ended.");
                }
            }
        }

        if (changed || session is not null)
        {
            await RefreshSnapshotAsync(session, cancellationToken);
        }

        if (session is null)
        {
            _state.Update(s => s with { IsConnected = false, CapturedAt = DateTimeOffset.UtcNow });
        }
    }

    private GlobalSystemMediaTransportControlsSession? FindNeteaseSession()
    {
        if (_manager is null) return null;

        // Prefer the current session if it is NetEase.
        var current = _manager.GetCurrentSession();
        if (IsNeteaseSession(current))
        {
            return current;
        }

        // Otherwise scan all sessions.
        try
        {
            var sessions = _manager.GetSessions();
            foreach (var session in sessions)
            {
                if (IsNeteaseSession(session))
                {
                    return session;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to enumerate SMTC sessions.");
        }

        return null;
    }

    private static bool IsNeteaseSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (session is null) return false;
        var id = session.SourceAppUserModelId ?? string.Empty;
        return NeteaseValues.SessionIdPatterns.Any(pattern =>
            id.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RefreshSnapshotAsync(
        GlobalSystemMediaTransportControlsSession? session,
        CancellationToken cancellationToken)
    {
        if (session is null) return;

        try
        {
            var playback = session.GetPlaybackInfo();
            var media = await session.TryGetMediaPropertiesAsync();

            var isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var isPaused = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;

            // Timeline properties (position / duration) — accessed via reflection-friendly
            // pattern because the projection name varies across SDK versions.
            var (position, duration) = ReadTimeline(playback);

            var trackName = string.IsNullOrWhiteSpace(media.Title) ? null : media.Title.Trim();
            var artistName = string.IsNullOrWhiteSpace(media.Artist) ? null : media.Artist.Trim();
            var albumName = string.IsNullOrWhiteSpace(media.AlbumTitle) ? null : media.AlbumTitle.Trim();
            var albumArtist = string.IsNullOrWhiteSpace(media.AlbumArtist) ? null : media.AlbumArtist.Trim();
            var trackNumber = media.TrackNumber > 0 ? (int?)media.TrackNumber : null;

            // Artwork
            string? artworkId = null;
            if (trackName is not null)
            {
                artworkId = $"{trackName}|{albumName ?? string.Empty}";
                if (!string.Equals(artworkId, _lastArtworkId, StringComparison.Ordinal))
                {
                    _lastArtworkId = artworkId;
                    await CacheArtworkAsync(media.Thumbnail, artworkId, cancellationToken);
                }
            }

            // Repeat / shuffle: SMTC exposes auto-repeat mode and shuffle status
            var autoRepeat = playback.AutoRepeatMode;
            var repeatMode = autoRepeat switch
            {
                Windows.Media.MediaPlaybackAutoRepeatMode.Track => MacroDeck.Sdk.MusicPlayer.RepeatMode.Track,
                Windows.Media.MediaPlaybackAutoRepeatMode.List => MacroDeck.Sdk.MusicPlayer.RepeatMode.Context,
                _ => MacroDeck.Sdk.MusicPlayer.RepeatMode.Off,
            };

            _state.Update(s => s with
            {
                CapturedAt = DateTimeOffset.UtcNow,
                IsConnected = true,
                SessionAppId = session.SourceAppUserModelId,
                IsPlaying = isPlaying,
                IsPlayingKnown = true,
                IsPaused = isPaused,
                IsPausedKnown = true,
                Position = position,
                PositionKnown = true,
                Duration = duration,
                TrackName = trackName,
                ArtistName = artistName,
                AlbumName = albumName,
                AlbumArtistName = albumArtist,
                TrackNumber = trackNumber,
                ArtworkId = artworkId,
                RepeatMode = repeatMode,
                RepeatModeKnown = true,
                ShuffleEnabled = playback.IsShuffleActive ?? false,
                ShuffleEnabledKnown = true,
            });
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to read SMTC session properties.");
        }
    }

    /// <summary>
    /// Reads position and duration from the playback info's timeline properties.
    /// Uses reflection because the WinRT projection exposes this as either a method
    /// (<c>GetTimelineProperties()</c>) or a property (<c>TimelineProperties</c>)
    /// depending on the Windows SDK version.
    /// </summary>
    private static (TimeSpan Position, TimeSpan? Duration) ReadTimeline(object playbackInfo)
    {
        try
        {
            var type = playbackInfo.GetType();

            // Try method first: GetTimelineProperties()
            var method = type.GetMethod("GetTimelineProperties",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            object? timeline = method?.Invoke(playbackInfo, null);

            // Try property: TimelineProperties
            timeline ??= type.GetProperty("TimelineProperties",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(playbackInfo);

            if (timeline is null)
            {
                return (TimeSpan.Zero, null);
            }

            var timelineType = timeline.GetType();
            var position = timelineType.GetProperty("Position")?.GetValue(timeline) as TimeSpan? ?? TimeSpan.Zero;
            var endTime = timelineType.GetProperty("EndTime")?.GetValue(timeline) as TimeSpan?;
            var duration = endTime is { } d && d > TimeSpan.Zero ? (TimeSpan?)d : null;

            return (position, duration);
        }
        catch
        {
            return (TimeSpan.Zero, null);
        }
    }

    private async Task CacheArtworkAsync(
        IRandomAccessStreamReference? thumbnail,
        string artworkId,
        CancellationToken cancellationToken)
    {
        if (thumbnail is null) return;

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            using var memStream = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(memStream, cancellationToken);
            var bytes = memStream.ToArray();

            if (bytes.Length > 0)
            {
                lock (_sync)
                {
                    _artworkCache[artworkId] = bytes;
                }
                _logger.Debug("Artwork cached for {Track} ({Size} bytes)", artworkId, bytes.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to cache artwork for {Track}", artworkId);
        }
    }

    // ── SMTC event handlers ──────────────────────────────────────────────────

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        // The poll loop will pick up the new session; nothing to do here.
        _logger.Debug("SMTC current session changed.");
    }

    private async void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        try
        {
            await RefreshSnapshotAsync(sender, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Playback info change handler failed.");
        }
    }

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        try
        {
            await RefreshSnapshotAsync(sender, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Media properties change handler failed.");
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stopCts?.Cancel();
        _stopCts?.Dispose();

        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        if (_session is not null)
        {
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        }

        base.Dispose();
    }
}
