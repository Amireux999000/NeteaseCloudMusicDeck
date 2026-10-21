using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using Amireux999000.NeteaseCloudPilot.State;

namespace Amireux999000.NeteaseCloudPilot.Variables;

/// <summary>
/// Declarative catalog of every variable the plugin exposes to Macro Deck.
/// </summary>
public static class NeteaseVariableCatalog
{
    public const string InstanceId = "netease-cloud-music";

    public static IReadOnlyList<NeteaseVariable> All { get; } = Build();

    private static readonly Dictionary<string, NeteaseVariable> ByLocalId =
        All.ToDictionary(v => v.LocalId, StringComparer.OrdinalIgnoreCase);

    public static NeteaseVariable? Find(string localId) => ByLocalId.GetValueOrDefault(localId);

    public static IReadOnlyList<VariableDefinition> Definitions { get; } =
        All.Select(v => v.ToDefinition()).ToArray();

    private static string FormatDuration(TimeSpan duration) =>
        $"{(long)duration.TotalMinutes}:{duration.Seconds:00}";

    private static IReadOnlyList<NeteaseVariable> Build() =>
    [
        // ── Playback state ────────────────────────────────────────────────────

        new("is_playing", VariableType.Boolean,
            s => s.IsPlayingKnown ? s.IsPlaying : null,
            Strings.Variables.IsPlaying.Display(), Strings.Variables.IsPlaying.Description(),
            source: VariableSource.Transport),

        new("playback_state", VariableType.Text,
            s => !s.IsPlayingKnown && !s.IsPausedKnown ? null : s.PlaybackState switch
            {
                PlaybackState.Playing => "Playing",
                PlaybackState.Paused => "Paused",
                _ => "Stopped",
            },
            Strings.Variables.PlaybackState.Display(), Strings.Variables.PlaybackState.Description(),
            source: VariableSource.Transport, icon: "play"),

        new("current_position", VariableType.Text,
            s => s.PositionKnown ? FormatDuration(s.CurrentPosition) : null,
            Strings.Variables.CurrentPosition.Display(), Strings.Variables.CurrentPosition.Description(),
            source: VariableSource.Transport, writable: true),

        new("track_duration", VariableType.Text,
            s => s.Duration is { } d ? FormatDuration(d) : null,
            Strings.Variables.TrackDuration.Display(), Strings.Variables.TrackDuration.Description(),
            source: VariableSource.Transport),

        new("progress_seconds", VariableType.Numeric,
            s => s.PositionKnown ? Math.Round(s.CurrentPosition.TotalSeconds) : null,
            Strings.Variables.ProgressSeconds.Display(), Strings.Variables.ProgressSeconds.Description(),
            unit: "s", decimalPlaces: 0, source: VariableSource.Transport, writable: true),

        new("progress_percentage", VariableType.Numeric,
            s => NeteaseValues.Percent(s.ProgressFraction),
            Strings.Variables.ProgressPercentage.Display(), Strings.Variables.ProgressPercentage.Description(),
            unit: "%", semanticKind: VariableSemanticKinds.Percentage, decimalPlaces: 1,
            source: VariableSource.Transport, writable: true),

        // ── Track info ────────────────────────────────────────────────────────

        new("track_name", VariableType.Text,
            s => s.TrackName,
            Strings.Variables.TrackName.Display(), Strings.Variables.TrackName.Description(),
            source: VariableSource.Transport),

        new("artist_name", VariableType.Text,
            s => s.ArtistName,
            Strings.Variables.ArtistName.Display(), Strings.Variables.ArtistName.Description(),
            source: VariableSource.Transport),

        new("album_name", VariableType.Text,
            s => s.AlbumName,
            Strings.Variables.AlbumName.Display(), Strings.Variables.AlbumName.Description(),
            source: VariableSource.Transport),

        new("album_artist", VariableType.Text,
            s => s.AlbumArtistName,
            Strings.Variables.AlbumArtist.Display(), Strings.Variables.AlbumArtist.Description(),
            source: VariableSource.Transport),

        new("track_number", VariableType.Numeric,
            s => s.TrackNumber,
            Strings.Variables.TrackNumber.Display(), Strings.Variables.TrackNumber.Description(),
            source: VariableSource.Stable),

        // ── Playback settings ─────────────────────────────────────────────────

        new("volume", VariableType.Numeric,
            s => s.VolumePercent,
            Strings.Variables.Volume.Display(), Strings.Variables.Volume.Description(),
            unit: "%", semanticKind: VariableSemanticKinds.Percentage, decimalPlaces: 0,
            source: VariableSource.Transport, writable: true),

        new("shuffle", VariableType.Boolean,
            s => s.ShuffleEnabledKnown ? s.ShuffleEnabled : null,
            Strings.Variables.Shuffle.Display(), Strings.Variables.Shuffle.Description(),
            source: VariableSource.Transport),

        new("repeat_mode", VariableType.Text,
            s => !s.RepeatModeKnown ? null : s.RepeatMode switch
            {
                RepeatMode.Track => "Track",
                RepeatMode.Context => "Context",
                _ => "Off",
            },
            Strings.Variables.RepeatMode.Display(), Strings.Variables.RepeatMode.Description(),
            source: VariableSource.Transport),

        // ── Connection ────────────────────────────────────────────────────────

        new("connection_status", VariableType.Text,
            s => s.ConnectionStatus,
            Strings.Variables.ConnectionStatus.Display(), Strings.Variables.ConnectionStatus.Description(),
            source: VariableSource.Client),
    ];
}
