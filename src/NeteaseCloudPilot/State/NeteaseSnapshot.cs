using MacroDeck.Sdk.MusicPlayer;

namespace Amireux999000.NeteaseCloudPilot.State;

/// <summary>
/// Immutable snapshot of everything we know about NetEase Cloud Music playback.
/// Mirrors the fields Macro Deck's music-player and variable surfaces expect.
/// </summary>
public sealed record NeteaseSnapshot
{
    public DateTimeOffset CapturedAt { get; init; }

    // ── Transport ────────────────────────────────────────────────────────────

    public bool IsPlaying { get; init; }
    public bool IsPlayingKnown { get; init; }
    public bool IsPaused { get; init; }
    public bool IsPausedKnown { get; init; }

    public TimeSpan Position { get; init; }
    public bool PositionKnown { get; init; }
    public TimeSpan? Duration { get; init; }

    public int? VolumePercent { get; init; }
    public bool? Muted { get; init; }

    public RepeatMode RepeatMode { get; init; } = RepeatMode.Off;
    public bool RepeatModeKnown { get; init; }

    public bool ShuffleEnabled { get; init; }
    public bool ShuffleEnabledKnown { get; init; }

    // ── Track ────────────────────────────────────────────────────────────────

    public string? TrackName { get; init; }
    public string? ArtistName { get; init; }
    public string? AlbumName { get; init; }
    public string? AlbumArtistName { get; init; }

    /// <summary>
    /// Opaque artwork id. For SMTC we use a cache key derived from track+album;
    /// the actual bytes are fetched on demand through the music-player surface.
    /// </summary>
    public string? ArtworkId { get; init; }

    public int? TrackNumber { get; init; }

    // ── Connection ───────────────────────────────────────────────────────────

    public bool IsConnected { get; init; }

    public string? SessionAppId { get; init; }

    public string ConnectionStatus => IsConnected ? "Connected" : "Waiting for NetEase Cloud Music";

    // ── Computed ─────────────────────────────────────────────────────────────

    public PlaybackState PlaybackState =>
        IsPlaying ? PlaybackState.Playing : IsPaused ? PlaybackState.Paused : PlaybackState.Stopped;

    public double? ProgressFraction
    {
        get
        {
            if (!PositionKnown || Duration is not { } duration || duration <= TimeSpan.Zero)
            {
                return null;
            }

            if (Position <= TimeSpan.Zero)
            {
                return 0d;
            }

            return Position >= duration ? 1d : Position.TotalSeconds / duration.TotalSeconds;
        }
    }

    /// <summary>
    /// Live position that advances while playing, even between SMTC updates.
    /// </summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            if (!IsPlaying)
            {
                return Position < TimeSpan.Zero ? TimeSpan.Zero : Position;
            }

            var elapsed = DateTimeOffset.UtcNow - CapturedAt;
            if (elapsed <= TimeSpan.Zero)
            {
                return Position;
            }

            var advanced = Position + elapsed;
            return Duration is { } duration && advanced > duration ? duration : advanced;
        }
    }

    public TimeSpan? Remaining
    {
        get
        {
            if (!PositionKnown || Duration is not { } duration)
            {
                return null;
            }

            var remaining = duration - CurrentPosition;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }
}
