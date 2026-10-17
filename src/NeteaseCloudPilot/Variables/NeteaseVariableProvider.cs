using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using Serilog;
using Amireux999000.NeteaseCloudPilot.Media;
using Amireux999000.NeteaseCloudPilot.State;

namespace Amireux999000.NeteaseCloudPilot.Variables;

/// <summary>
/// Reads variable values from the current snapshot and routes writes back to
/// the SMTC session or window controller.
/// </summary>
public sealed class NeteaseVariableProvider : IVariableProvider
{
    private readonly NeteaseStateManager _state;
    private readonly SmtcMonitor _monitor;
    private readonly WindowController _window;
    private readonly ILogger _logger;

    public NeteaseVariableProvider(
        NeteaseStateManager state,
        SmtcMonitor monitor,
        WindowController window,
        ILogger logger)
    {
        _state = state;
        _monitor = monitor;
        _window = window;
        _logger = logger.ForContext<NeteaseVariableProvider>();
    }

    public IReadOnlyList<VariableDefinition> Variables => NeteaseVariableCatalog.Definitions;

    public string CatalogName => NeteaseValues.PluginName;

    public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
    {
        var variable = NeteaseVariableCatalog.Find(localId);
        if (variable is null)
        {
            return ValueTask.FromResult(VariableReading.Unavailable);
        }

        object? value;
        try
        {
            value = variable.Read(_state.Current);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Reading variable {Variable} failed.", variable.LocalId);
            return ValueTask.FromResult(VariableReading.Unavailable);
        }

        if (value is null)
        {
            return ValueTask.FromResult(VariableReading.Unavailable);
        }

        var min = variable.Unit switch { "%" or "s" => 0d, _ => (double?)null };
        var max = variable.Unit switch
        {
            "%" => 100d,
            "s" => _state.Current.Duration?.TotalSeconds,
            _ => null,
        };
        var step = variable.Unit switch { "%" => 1d, "s" => 1d, _ => (double?)null };

        return ValueTask.FromResult(VariableReading.Of(value, min, max, step));
    }

    public async ValueTask<VariableWriteResult> SetValueAsync(
        string localId, object? value, CancellationToken cancellationToken = default)
    {
        var variable = NeteaseVariableCatalog.Find(localId);
        if (variable is null || !variable.Writable)
        {
            return VariableWriteResult.NotWritable();
        }

        if (!_state.Current.IsConnected)
        {
            return VariableWriteResult.Unavailable();
        }

        switch (variable.LocalId)
        {
            case "progress-seconds":
            {
                if (!TryReadNumber(value, out var seconds))
                {
                    return VariableWriteResult.InvalidValue();
                }

                return await SeekAsync(TimeSpan.FromSeconds(Math.Max(0, seconds)), cancellationToken);
            }

            case "progress-percentage":
            {
                if (!TryReadNumber(value, out var percent))
                {
                    return VariableWriteResult.InvalidValue();
                }

                if (_state.Current.Duration is not { } duration || duration <= TimeSpan.Zero)
                {
                    return VariableWriteResult.Unavailable();
                }

                var position = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * Math.Clamp(percent, 0, 100) / 100d);
                return await SeekAsync(position, cancellationToken);
            }

            case "current-position":
            {
                if (!TryReadPosition(value, out var milliseconds))
                {
                    return VariableWriteResult.InvalidValue();
                }

                return await SeekAsync(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)), cancellationToken);
            }

            case "volume":
            {
                if (!TryReadNumber(value, out var percent))
                {
                    return VariableWriteResult.InvalidValue();
                }

                var target = (int)Math.Round(Math.Clamp(percent, 0, 100));
                var current = _state.Current.VolumePercent ?? 50;
                var delta = target - current;
                var up = delta > 0;
                var steps = Math.Min(Math.Abs(delta), 30);

                for (var i = 0; i < steps; i++)
                {
                    if (up) _window.VolumeUp(); else _window.VolumeDown();
                    await Task.Delay(30, cancellationToken);
                }

                _state.Update(s => s with { VolumePercent = target });
                return VariableWriteResult.Applied();
            }

            case "shuffle":
            {
                if (!TryReadBoolean(value, out var shuffle))
                {
                    return VariableWriteResult.InvalidValue();
                }

                // SMTC does not allow setting shuffle for most desktop apps;
                // we report it as applied but it may not take effect.
                _logger.Debug("Shuffle write requested ({Shuffle}); SMTC may not support changing it.", shuffle);
                return VariableWriteResult.Applied();
            }

            case "repeat-mode":
            {
                var mode = value?.ToString()?.ToLowerInvariant() switch
                {
                    "track" => RepeatMode.Track,
                    "context" => RepeatMode.Context,
                    _ => RepeatMode.Off,
                };

                _state.Update(s => s with { RepeatMode = mode, RepeatModeKnown = true });
                return VariableWriteResult.Applied();
            }

            default:
                return VariableWriteResult.NotWritable();
        }
    }

    private async ValueTask<VariableWriteResult> SeekAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        var session = _monitor.CurrentSession;
        if (session is null)
        {
            return VariableWriteResult.Unavailable();
        }

        try
        {
            var result = await session.TryChangePlaybackPositionAsync((long)position.TotalMicroseconds)
                .AsTask().WaitAsync(cancellationToken);
            return result ? VariableWriteResult.Applied() : VariableWriteResult.Failed("Seek rejected.");
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Seek via variable write failed.");
            return VariableWriteResult.Failed(ex.Message);
        }
    }

    private static bool TryReadNumber(object? value, out double number)
    {
        switch (value)
        {
            case double d: number = d; return double.IsFinite(d);
            case float f: number = f; return float.IsFinite(f);
            case int i: number = i; return true;
            case long l: number = l; return true;
            case decimal m: number = (double)m; return true;
            case string text:
                return double.TryParse(text, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out number);
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                number = element.GetDouble();
                return double.IsFinite(number);
            default:
                number = double.NaN;
                return false;
        }
    }

    private static bool TryReadPosition(object? value, out double milliseconds)
    {
        if (value is string text)
        {
            var parts = text.Split(':');
            if (parts.Length == 2
                && long.TryParse(parts[0], out var minutes)
                && int.TryParse(parts[1], out var seconds)
                && minutes >= 0 && seconds is >= 0 and < 60)
            {
                milliseconds = (minutes * 60d + seconds) * 1000d;
                return true;
            }
        }

        return TryReadNumber(value, out milliseconds);
    }

    private static bool TryReadBoolean(object? value, out bool state)
    {
        switch (value)
        {
            case bool b: state = b; return true;
            case string text when bool.TryParse(text, out var parsed): state = parsed; return true;
            case JsonElement { ValueKind: JsonValueKind.True }: state = true; return true;
            case JsonElement { ValueKind: JsonValueKind.False }: state = false; return true;
            default: state = false; return false;
        }
    }
}
