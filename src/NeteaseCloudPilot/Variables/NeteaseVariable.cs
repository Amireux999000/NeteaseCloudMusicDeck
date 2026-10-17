using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using Amireux999000.NeteaseCloudPilot.State;

namespace Amireux999000.NeteaseCloudPilot.Variables;

/// <summary>
/// How often a variable should be polled by the host.
/// </summary>
public enum VariableSource
{
    /// <summary>Changes frequently (playback position, state).</summary>
    Transport,
    /// <summary>Changes occasionally (connection status).</summary>
    Client,
    /// <summary>Rarely changes (track metadata).</summary>
    Stable,
}

/// <summary>
/// A single variable definition: its id, type, read function and optional write support.
/// </summary>
public sealed class NeteaseVariable
{
    public string Name { get; }
    public VariableType Type { get; }
    public Func<NeteaseSnapshot, object?> Read { get; }
    public LocalizedText DisplayName { get; }
    public LocalizedText Description { get; }
    public string? Unit { get; }
    public string? SemanticKind { get; }
    public int? DecimalPlaces { get; }
    public bool Writable { get; }
    public VariableSource Source { get; }
    public string? Icon { get; }

    public NeteaseVariable(
        string name,
        VariableType type,
        Func<NeteaseSnapshot, object?> read,
        LocalizedText displayName,
        LocalizedText description,
        string? unit = null,
        string? semanticKind = null,
        int? decimalPlaces = null,
        bool writable = false,
        VariableSource source = VariableSource.Stable,
        string? icon = null)
    {
        Name = name;
        Type = type;
        Read = read;
        DisplayName = displayName;
        Description = description;
        Unit = unit;
        SemanticKind = semanticKind;
        DecimalPlaces = decimalPlaces;
        Writable = writable;
        Source = source;
        Icon = icon;
    }

    /// <summary>
    /// The local id used by the host — underscores in <see cref="Name"/> become hyphens.
    /// </summary>
    public string LocalId => Name.Replace('_', '-');

    public TimeSpan RefreshInterval => Source switch
    {
        VariableSource.Transport => TimeSpan.FromSeconds(1),
        VariableSource.Client => TimeSpan.FromSeconds(2),
        _ => TimeSpan.FromSeconds(5),
    };

    public VariableDefinition ToDefinition() => new()
    {
        Id = LocalId,
        Name = Name,
        Type = Type,
        Materialization = VariableMaterialization.Eager,
        DisplayName = DisplayName,
        Description = Description,
        Icon = Icon,
        Unit = Unit,
        DecimalPlaces = DecimalPlaces,
        SemanticKind = SemanticKind,
        RefreshInterval = RefreshInterval,
        Write = Writable ? new VariableWriteCapability { CommitOnRelease = false } : null,
    };
}
