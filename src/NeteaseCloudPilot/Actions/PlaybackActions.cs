using Windows.Media.Control;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;
using Amireux999000.NeteaseCloudPilot.Media;
using Amireux999000.NeteaseCloudPilot.State;

namespace Amireux999000.NeteaseCloudPilot.Actions;

// ── Fixed transport actions (play / pause / toggle / next / previous) ────────

public abstract class FixedTransportAction(
    NeteaseStateManager state,
    SmtcMonitor monitor,
    WindowController window,
    ILogger logger) : NeteaseActionBase(state, monitor, window, logger)
{
    protected abstract string CommandName { get; }
    protected abstract Func<GlobalSystemMediaTransportControlsSession, Task<bool>> Command { get; }

    protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context) =>
        SendSmtcCommandAsync(Command, CommandName, context.CancellationToken);
}

public sealed class PlayAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : FixedTransportAction(state, monitor, window, logger)
{
    public override string Id => "netease-play";
    public override LocalizedText Name => Strings.Actions.Transport.Play.Name();
    public override LocalizedText Description => Strings.Actions.Transport.Play.Description();
    protected override string CommandName => "Play";
    protected override Func<GlobalSystemMediaTransportControlsSession, Task<bool>> Command =>
        static session => session.TryPlayAsync().AsTask();
}

public sealed class PauseAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : FixedTransportAction(state, monitor, window, logger)
{
    public override string Id => "netease-pause";
    public override LocalizedText Name => Strings.Actions.Transport.Pause.Name();
    public override LocalizedText Description => Strings.Actions.Transport.Pause.Description();
    protected override string CommandName => "Pause";
    protected override Func<GlobalSystemMediaTransportControlsSession, Task<bool>> Command =>
        static session => session.TryPauseAsync().AsTask();
}

public sealed class TogglePlayAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : NeteaseActionBase(state, monitor, window, logger), IStateProviderActionDefinition
{
    public override string Id => "netease-toggle-play";
    public override LocalizedText Name => Strings.Actions.Transport.TogglePlay.Name();
    public override LocalizedText Description => Strings.Actions.Transport.TogglePlay.Description();

    protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
    {
        var snapshot = State.Current;
        if (snapshot.IsPlaying)
        {
            return await SendSmtcCommandAsync(
                static s => s.TryPauseAsync().AsTask(), "Pause", context.CancellationToken);
        }

        return await SendSmtcCommandAsync(
            static s => s.TryPlayAsync().AsTask(), "Play", context.CancellationToken);
    }

    public Task<ActionStateSnapshot?> GetActionStateAsync(
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var snapshot = State.Current;
        ActionStateDefinition[] states =
        [
            new("paused", Strings.Actions.Transport.TogglePlay.States.Paused()),
            new("playing", Strings.Actions.Transport.TogglePlay.States.Playing()),
        ];
        var active = snapshot.IsConnected && snapshot.IsPlaying ? "playing" : "paused";
        return Task.FromResult<ActionStateSnapshot?>(new(states, active));
    }
}

public sealed class NextAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : FixedTransportAction(state, monitor, window, logger)
{
    public override string Id => "netease-next";
    public override LocalizedText Name => Strings.Actions.Transport.Next.Name();
    public override LocalizedText Description => Strings.Actions.Transport.Next.Description();
    protected override string CommandName => "Next";
    protected override Func<GlobalSystemMediaTransportControlsSession, Task<bool>> Command =>
        static session => session.TrySkipNextAsync().AsTask();
}

public sealed class PreviousAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : FixedTransportAction(state, monitor, window, logger)
{
    public override string Id => "netease-previous";
    public override LocalizedText Name => Strings.Actions.Transport.Previous.Name();
    public override LocalizedText Description => Strings.Actions.Transport.Previous.Description();
    protected override string CommandName => "Previous";
    protected override Func<GlobalSystemMediaTransportControlsSession, Task<bool>> Command =>
        static session => session.TrySkipPreviousAsync().AsTask();
}

// ── Seek ─────────────────────────────────────────────────────────────────────

public sealed class SeekToAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : NeteaseActionBase(state, monitor, window, logger)
{
    public override string Id => "netease-seek-to";
    public override LocalizedText Name => Strings.Actions.SeekTo.Name();
    public override LocalizedText Description => Strings.Actions.SeekTo.Description();

    public override IReadOnlyList<ActionParameter> Parameters =>
    [
        ActionParameter.Slider("percent", 0, 100,
            Strings.Actions.SeekTo.Percent.Label(),
            Strings.Actions.SeekTo.Percent.Description(),
            1, 50),
    ];

    protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
    {
        var session = Monitor.CurrentSession;
        if (session is null)
        {
            return Strings.Actions.Errors.NotRunning();
        }

        var snapshot = State.Current;
        if (snapshot.Duration is not { } duration || duration <= TimeSpan.Zero)
        {
            return Strings.Actions.SeekTo.Errors.NoDuration();
        }

        var percent = Math.Clamp(ReadDouble(context.Parameters, "percent", 50), 0, 100);
        var position = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * percent / 100d);

        try
        {
            var result = await session.TryChangePlaybackPositionAsync((long)position.TotalMicroseconds)
                .AsTask().WaitAsync(context.CancellationToken);
            return result ? null : Strings.Actions.Errors.CommandFailed();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Seek failed.");
            return Strings.Actions.Errors.Unexpected();
        }
    }
}

// ── Volume ───────────────────────────────────────────────────────────────────

public sealed class SetVolumeAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : NeteaseActionBase(state, monitor, window, logger)
{
    public override string Id => "netease-set-volume";
    public override LocalizedText Name => Strings.Actions.SetVolume.Name();
    public override LocalizedText Description => Strings.Actions.SetVolume.Description();

    public override IReadOnlyList<ActionParameter> Parameters =>
    [
        ActionParameter.Slider("volume", 0, 100,
            Strings.Actions.SetVolume.Volume.Label(),
            Strings.Actions.SetVolume.Volume.Description(),
            1, 50),
    ];

    protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
    {
        // NetEase Cloud Music does not expose per-app volume through SMTC.
        // We approximate by sending volume up/down shortcuts from the current level.
        var target = (int)Math.Round(Math.Clamp(ReadDouble(context.Parameters, "volume", 50), 0, 100));
        var current = State.Current.VolumePercent ?? 50;
        var delta = target - current;

        if (delta == 0)
        {
            return Task.FromResult<LocalizedText?>(null);
        }

        var steps = Math.Abs(delta);
        var up = delta > 0;

        for (var i = 0; i < Math.Min(steps, 30); i++)
        {
            if (up) Window.VolumeUp(); else Window.VolumeDown();
            Thread.Sleep(30);
        }

        State.Update(s => s with { VolumePercent = target });
        return Task.FromResult<LocalizedText?>(null);
    }
}

public sealed class VolumeStepAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : NeteaseActionBase(state, monitor, window, logger)
{
    public override string Id => "netease-volume-step";
    public override LocalizedText Name => Strings.Actions.VolumeStep.Name();
    public override LocalizedText Description => Strings.Actions.VolumeStep.Description();

    public override IReadOnlyList<ActionParameter> Parameters =>
    [
        ActionParameter.Slider("step", 1, 30,
            Strings.Actions.VolumeStep.Step.Label(),
            Strings.Actions.VolumeStep.Step.Description(),
            1, 5),
        ActionParameter.Choice("direction", DirectionOptions,
            Strings.Actions.VolumeStep.Direction.Label(),
            Strings.Actions.VolumeStep.Direction.Description(),
            "up", true),
    ];

    private static IReadOnlyList<ActionParameterOption> DirectionOptions { get; } =
    [
        new() { Value = "up", Label = Strings.Actions.VolumeStep.Direction.Up() },
        new() { Value = "down", Label = Strings.Actions.VolumeStep.Direction.Down() },
    ];

    protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
    {
        var up = !string.Equals(ReadString(context.Parameters, "direction"), "down",
            StringComparison.OrdinalIgnoreCase);
        var steps = (int)Math.Clamp(ReadDouble(context.Parameters, "step", 5), 1, 30);

        for (var i = 0; i < steps; i++)
        {
            if (up) Window.VolumeUp(); else Window.VolumeDown();
            Thread.Sleep(30);
        }

        return Task.FromResult<LocalizedText?>(null);
    }
}

public sealed class SetMuteAction(NeteaseStateManager state, SmtcMonitor monitor, WindowController window, ILogger logger)
    : NeteaseActionBase(state, monitor, window, logger)
{
    public override string Id => "netease-set-mute";
    public override LocalizedText Name => Strings.Actions.SetMute.Name();
    public override LocalizedText Description => Strings.Actions.SetMute.Description();

    protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
    {
        var ok = Window.ToggleMute();
        return Task.FromResult<LocalizedText?>(ok ? null : Strings.Actions.Errors.NotRunning());
    }
}
