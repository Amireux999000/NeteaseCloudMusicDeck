namespace Amireux999000.NeteaseCloudPilot;

internal static class NeteaseValues
{
    public const string PluginName = "NetEase Cloud Music";

    public const string InstanceId = "netease-cloud-music";

    /// <summary>
    /// Substrings that identify a NetEase Cloud Music SMTC session.
    /// The app user model id varies by install method (Store vs desktop installer),
    /// so we match on several known patterns.
    /// </summary>
    public static readonly string[] SessionIdPatterns =
    [
        "cloudmusic",
        "netease",
        "163music",
    ];

    /// <summary>
    /// Window title / process name patterns for the desktop client.
    /// </summary>
    public static readonly string[] ProcessNamePatterns =
    [
        "cloudmusic",
        "netease cloud music",
    ];

    public const int PollIntervalMs = 1500;

    public const int ArtworkSize = 300;

    public static readonly TimeSpan UnavailableTimeout = TimeSpan.FromSeconds(10);

    public static double? Percent(double? fraction) =>
        fraction is { } value ? Math.Round(Math.Clamp(value, 0d, 1d) * 100d, 1) : null;
}
