using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;
using Amireux999000.NeteaseCloudPilot.Media;
using Amireux999000.NeteaseCloudPilot.State;

namespace Amireux999000.NeteaseCloudPilot.Actions;

/// <summary>
/// Toggle "Like" for the current song via Alt+L shortcut.
/// </summary>
public sealed class ToggleLikeAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : NeteaseActionBase(state, monitor, window, logger), IStateProviderActionDefinition
{
    public override string Id => "netease-toggle-like";
    public override LocalizedText Name => Strings.Actions.Like.Name();
    public override LocalizedText Description => Strings.Actions.Like.Description();

    protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
    {
        if (!State.Current.IsConnected)
        {
            return Task.FromResult<LocalizedText?>(Strings.Actions.Errors.NotRunning());
        }

        var ok = Window.ToggleLike();
        return Task.FromResult<LocalizedText?>(ok ? null : Strings.Actions.Errors.CommandFailed());
    }

    public Task<ActionStateSnapshot?> GetActionStateAsync(
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        // SMTC does not reliably expose like status for NetEase Cloud Music,
        // so we show a neutral state.
        ActionStateDefinition[] states =
        [
            new("neutral", Strings.Actions.Like.States.Neutral()),
        ];
        return Task.FromResult<ActionStateSnapshot?>(new(states, "neutral"));
    }
}

/// <summary>
/// Launch or bring to front the NetEase Cloud Music desktop app.
/// </summary>
public sealed class OpenAppAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : NeteaseActionBase(state, monitor, window, logger)
{
    public override string Id => "netease-open-app";
    public override LocalizedText Name => Strings.Actions.OpenApp.Name();
    public override LocalizedText Description => Strings.Actions.OpenApp.Description();

    protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
    {
        var ok = Window.Launch();
        return Task.FromResult<LocalizedText?>(ok ? null : Strings.Actions.OpenApp.Errors.LaunchFailed());
    }
}

/// <summary>
/// Toggle desktop lyrics via Ctrl+D shortcut.
/// </summary>
public sealed class ToggleLyricsAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : NeteaseActionBase(state, monitor, window, logger)
{
    public override string Id => "netease-toggle-lyrics";
    public override LocalizedText Name => Strings.Actions.Lyrics.Name();
    public override LocalizedText Description => Strings.Actions.Lyrics.Description();

    protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
    {
        if (!State.Current.IsConnected)
        {
            return Task.FromResult<LocalizedText?>(Strings.Actions.Errors.NotRunning());
        }

        var ok = Window.ToggleLyrics();
        return Task.FromResult<LocalizedText?>(ok ? null : Strings.Actions.Errors.CommandFailed());
    }
}

/// <summary>
/// Restart the current track (seek to beginning).
/// </summary>
public sealed class RestartTrackAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : NeteaseActionBase(state, monitor, window, logger)
{
    public override string Id => "netease-restart-track";
    public override LocalizedText Name => Strings.Actions.RestartTrack.Name();
    public override LocalizedText Description => Strings.Actions.RestartTrack.Description();

    protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
    {
        var session = Monitor.CurrentSession;
        if (session is null)
        {
            return Strings.Actions.Errors.NotRunning();
        }

        try
        {
            var result = await session.TryChangePlaybackPositionAsync(0)
                .AsTask().WaitAsync(context.CancellationToken);
            return result ? null : Strings.Actions.Errors.CommandFailed();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restart track failed.");
            return Strings.Actions.Errors.Unexpected();
        }
    }
}
