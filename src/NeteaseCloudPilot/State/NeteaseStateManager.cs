using System.Collections.Immutable;
using Serilog;
using Amireux999000.NeteaseCloudPilot.Media;

namespace Amireux999000.NeteaseCloudPilot.State;

/// <summary>
/// Thread-safe owner of the current <see cref="NeteaseSnapshot"/>.
/// The <see cref="SmtcMonitor"/> pushes updates here; actions, variables and the
/// music-player all read from <see cref="Current"/>.
/// </summary>
public sealed class NeteaseStateManager
{
    private readonly ILogger _logger;
    private readonly Lock _sync = new();
    private NeteaseSnapshot _snapshot = new();

    public NeteaseStateManager(ILogger logger)
    {
        _logger = logger.ForContext<NeteaseStateManager>();
    }

    /// <summary>
    /// Raised whenever the snapshot changes. Listeners receive the new snapshot
    /// and the set of field names that changed.
    /// </summary>
    public event Func<StateChange, CancellationToken, Task>? Changed;

    public NeteaseSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return Expire(_snapshot);
            }
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _snapshot.IsConnected;
            }
        }
    }

    public DateTimeOffset? LastUpdate
    {
        get
        {
            lock (_sync)
            {
                return _snapshot.CapturedAt == default ? null : _snapshot.CapturedAt;
            }
        }
    }

    /// <summary>
    /// Atomically replaces the snapshot via a mutator and notifies listeners of
    /// any field-level changes.
    /// </summary>
    public void Update(Func<NeteaseSnapshot, NeteaseSnapshot> mutator)
    {
        StateChange? change;
        lock (_sync)
        {
            var next = mutator(_snapshot) with { CapturedAt = DateTimeOffset.UtcNow };
            var changedFields = Diff(_snapshot, next);
            _snapshot = next;
            change = changedFields.IsEmpty ? null : new StateChange(next, changedFields);
        }

        if (change is not null)
        {
            _ = NotifyAsync(change, CancellationToken.None);
        }
    }

    private async Task NotifyAsync(StateChange change, CancellationToken cancellationToken)
    {
        var handler = Changed;
        if (handler is null) return;

        try
        {
            await handler(change, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "A state change listener failed.");
        }
    }

    /// <summary>
    /// Expires stale field values when the session has been disconnected longer
    /// than <see cref="NeteaseValues.UnavailableTimeout"/>.
    /// </summary>
    private static NeteaseSnapshot Expire(NeteaseSnapshot snapshot)
    {
        if (snapshot.IsConnected) return snapshot;

        var age = DateTimeOffset.UtcNow - snapshot.CapturedAt;
        if (age < NeteaseValues.UnavailableTimeout) return snapshot;

        return snapshot with
        {
            TrackName = null,
            ArtistName = null,
            AlbumName = null,
            AlbumArtistName = null,
            ArtworkId = null,
            IsPlayingKnown = false,
            IsPausedKnown = false,
            PositionKnown = false,
            Duration = null,
            VolumePercent = null,
            RepeatModeKnown = false,
            ShuffleEnabledKnown = false,
        };
    }

    private static ImmutableArray<string> Diff(NeteaseSnapshot before, NeteaseSnapshot after)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        void Compare<T>(string name, T? a, T? b) where T : struct
        {
            if (!a.HasValue && !b.HasValue) return;
            if (a.HasValue != b.HasValue || !EqualityComparer<T>.Default.Equals(a!.Value, b!.Value))
            {
                builder.Add(name);
            }
        }

        void CompareText(string name, string? a, string? b)
        {
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                builder.Add(name);
            }
        }

        Compare<bool>(nameof(NeteaseSnapshot.IsConnected), before.IsConnected, after.IsConnected);
        Compare<bool>(nameof(NeteaseSnapshot.IsPlaying), before.IsPlaying, after.IsPlaying);
        Compare<bool>(nameof(NeteaseSnapshot.IsPaused), before.IsPaused, after.IsPaused);
        Compare<TimeSpan>(nameof(NeteaseSnapshot.Position), before.Position, after.Position);
        Compare(nameof(NeteaseSnapshot.Duration), before.Duration, after.Duration);
        Compare(nameof(NeteaseSnapshot.VolumePercent), before.VolumePercent, after.VolumePercent);
        Compare(nameof(NeteaseSnapshot.Muted), before.Muted, after.Muted);
        Compare<bool>(nameof(NeteaseSnapshot.ShuffleEnabled), before.ShuffleEnabled, after.ShuffleEnabled);
        Compare(nameof(NeteaseSnapshot.RepeatMode), (MacroDeck.Sdk.MusicPlayer.RepeatMode?)before.RepeatMode, after.RepeatMode);

        CompareText(nameof(NeteaseSnapshot.TrackName), before.TrackName, after.TrackName);
        CompareText(nameof(NeteaseSnapshot.ArtistName), before.ArtistName, after.ArtistName);
        CompareText(nameof(NeteaseSnapshot.AlbumName), before.AlbumName, after.AlbumName);
        CompareText(nameof(NeteaseSnapshot.AlbumArtistName), before.AlbumArtistName, after.AlbumArtistName);
        CompareText(nameof(NeteaseSnapshot.ArtworkId), before.ArtworkId, after.ArtworkId);
        CompareText(nameof(NeteaseSnapshot.SessionAppId), before.SessionAppId, after.SessionAppId);
        Compare(nameof(NeteaseSnapshot.TrackNumber), before.TrackNumber, after.TrackNumber);

        return builder.ToImmutable();
    }
}

public sealed record StateChange(NeteaseSnapshot Snapshot, ImmutableArray<string> Fields)
{
    public bool TrackChanged => Fields.Any(name =>
        name is nameof(NeteaseSnapshot.TrackName) or nameof(NeteaseSnapshot.ArtworkId));
}
