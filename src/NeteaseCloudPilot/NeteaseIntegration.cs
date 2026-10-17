using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using Serilog;
using Amireux999000.NeteaseCloudPilot.Actions;
using Amireux999000.NeteaseCloudPilot.Media;
using Amireux999000.NeteaseCloudPilot.State;
using Amireux999000.NeteaseCloudPilot.Variables;

namespace Amireux999000.NeteaseCloudPilot;

/// <summary>
/// Main plugin integration: registers actions, variables, the music-player
/// provider and connection-status issues.
/// </summary>
#pragma warning disable CA1001
public sealed class NeteaseIntegration : IPluginIntegration, IVariableProvider, IMusicPlayerProvider, IIntegrationIssueProvider
#pragma warning restore CA1001
{
    private const string IssueNotRunning = "netease-not-running";

    private readonly NeteaseStateManager _state;
    private readonly SmtcMonitor _monitor;
    private readonly WindowController _window;
    private readonly NeteaseVariableProvider _variables;
    private readonly ILogger _logger;
    private readonly NeteaseMusicPlayer _player;

    public NeteaseIntegration(
        NeteaseStateManager state,
        SmtcMonitor monitor,
        WindowController window,
        NeteaseVariableProvider variables,
        ILogger logger)
    {
        _state = state;
        _monitor = monitor;
        _window = window;
        _variables = variables;
        _logger = logger.ForContext<NeteaseIntegration>();
        _player = new NeteaseMusicPlayer(state, monitor, logger);

        Actions = BuildActions();
    }

    public IReadOnlyList<IActionDefinition> Actions { get; }

    // ── Variables ────────────────────────────────────────────────────────────

    public IReadOnlyList<VariableDefinition> Variables => _variables.Variables;
    public string CatalogName => NeteaseValues.PluginName;

    public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) =>
        _variables.ReadAsync(localId, cancellationToken);

    public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value, CancellationToken cancellationToken = default) =>
        _variables.SetValueAsync(localId, value, cancellationToken);

    // ── Music player ─────────────────────────────────────────────────────────

    public string ProviderName => NeteaseValues.PluginName;

    public IReadOnlyList<MusicPlayerInstance> GetInstances() =>
        [new MusicPlayerInstance(NeteaseVariableCatalog.InstanceId, "NetEase Cloud Music")];

    public IMusicPlayer? GetPlayer(string instanceId) =>
        string.Equals(instanceId, NeteaseVariableCatalog.InstanceId, StringComparison.Ordinal)
            ? _player
            : null;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public Task InitializeAsync(IIntegrationContext context)
    {
        _logger.Information("NetEase Cloud Music Deck initializing. SMTC monitor will detect the player automatically.");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        _logger.Information("NetEase Cloud Music Deck shutting down.");
        return Task.CompletedTask;
    }

    // ── Issues ───────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken)
    {
        var issues = new List<IntegrationIssue>();

        if (!_state.Current.IsConnected)
        {
            issues.Add(new IntegrationIssue
            {
                Id = IssueNotRunning,
                Title = Strings.Issues.NotRunning.Title(),
                Description = Strings.Issues.NotRunning.Description(),
                Severity = IntegrationIssueSeverity.Warning,
                ActionLabel = Strings.Issues.NotRunning.Action(),
            });
        }

        return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
    }

    public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken)
    {
        if (issueId == IssueNotRunning)
        {
            var launched = _window.Launch();
            return Task.FromResult(launched
                ? IssueResolution.Ok(Strings.Issues.NotRunning.ResolveOk())
                : IssueResolution.Failed(Strings.Issues.NotRunning.ResolveFailed()));
        }

        return Task.FromResult(IssueResolution.Failed(Strings.Issues.Unknown()));
    }

    // ── Action catalog ───────────────────────────────────────────────────────

    private IReadOnlyList<IActionDefinition> BuildActions() =>
    [
        new PlayAction(_state, _monitor, _window, _logger),
        new PauseAction(_state, _monitor, _window, _logger),
        new TogglePlayAction(_state, _monitor, _window, _logger),
        new NextAction(_state, _monitor, _window, _logger),
        new PreviousAction(_state, _monitor, _window, _logger),
        new RestartTrackAction(_state, _monitor, _window, _logger),
        new SeekToAction(_state, _monitor, _window, _logger),
        new SetVolumeAction(_state, _monitor, _window, _logger),
        new VolumeStepAction(_state, _monitor, _window, _logger),
        new SetMuteAction(_state, _monitor, _window, _logger),
        new ToggleLikeAction(_state, _monitor, _window, _logger),
        new ToggleLyricsAction(_state, _monitor, _window, _logger),
        new OpenAppAction(_state, _monitor, _window, _logger),
    ];
}
