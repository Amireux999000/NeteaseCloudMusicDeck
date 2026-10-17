using System.Globalization;
using Windows.Media.Control;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;
using Amireux999000.NeteaseCloudPilot.Media;
using Amireux999000.NeteaseCloudPilot.State;

namespace Amireux999000.NeteaseCloudPilot.Actions;

/// <summary>
/// Base class for all NetEase Cloud Music actions.
/// Handles connection checks, error translation and the executor boilerplate.
/// </summary>
public abstract class NeteaseActionBase(
    NeteaseStateManager state,
    SmtcMonitor monitor,
    WindowController window,
    ILogger logger) : IActionDefinition
{
    protected NeteaseStateManager State { get; } = state;
    protected SmtcMonitor Monitor { get; } = monitor;
    protected WindowController Window { get; } = window;
    protected ILogger Log { get; } = logger.ForContext<NeteaseActionBase>();

    public abstract string Id { get; }
    public abstract LocalizedText Name { get; }
    public abstract LocalizedText Description { get; }

    public virtual IReadOnlyList<ActionParameter> Parameters => Array.Empty<ActionParameter>();

    public virtual IActionExecutor CreateExecutor() => new Executor(this);

    /// <summary>
    /// Execute the action. Return null on success, or a LocalizedText error message.
    /// </summary>
    protected abstract Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context);

    /// <summary>
    /// Sends an SMTC transport command (play/pause/next/previous) to the current session.
    /// </summary>
    protected async Task<LocalizedText?> SendSmtcCommandAsync(
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command,
        string commandName,
        CancellationToken cancellationToken)
    {
        var session = Monitor.CurrentSession;
        if (session is null)
        {
            return Strings.Actions.Errors.NotRunning();
        }

        try
        {
            var result = await command(session).WaitAsync(cancellationToken);
            if (result)
            {
                Log.Debug("SMTC command {Command} succeeded.", commandName);
                return null;
            }

            Log.Debug("SMTC command {Command} was not accepted by the session.", commandName);
            return Strings.Actions.Errors.CommandFailed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Strings.Actions.Errors.Cancelled();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SMTC command {Command} failed unexpectedly.", commandName);
            return Strings.Actions.Errors.Unexpected();
        }
    }

    protected static double ReadDouble(
        IReadOnlyDictionary<string, object?> parameters,
        string key,
        double defaultValue)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return defaultValue;
        }

        return raw switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            string text => double.TryParse(text, NumberStyles.Any,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue,
            _ => defaultValue,
        };
    }

    protected static string? ReadString(
        IReadOnlyDictionary<string, object?> parameters,
        string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw.ToString();
    }

    private sealed class Executor(NeteaseActionBase action) : IActionExecutor
    {
        public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
        {
            try
            {
                var failure = await action.ExecuteAsync(context).ConfigureAwait(false);
                return failure is null
                    ? ActionResult.Success()
                    : ActionResult.Failed(ActionErrorCodes.ProviderError, failure.Value);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.Errors.Cancelled());
            }
            catch (Exception ex)
            {
                action.Log.Error(ex, "The action {Action} failed unexpectedly.", action.Id);
                return ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Actions.Errors.Unexpected());
            }
        }
    }
}
