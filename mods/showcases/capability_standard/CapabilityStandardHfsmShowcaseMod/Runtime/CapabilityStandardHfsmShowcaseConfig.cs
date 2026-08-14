using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace CapabilityStandardHfsmShowcaseMod.Runtime;

internal sealed class CapabilityStandardHfsmShowcaseConfig
{
    public int SchemaVersion { get; set; }
    public string MapId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string HeroInstanceId { get; set; } = string.Empty;
    public string WaterStationInstanceId { get; set; } = string.Empty;
    public string TrackCenterInstanceId { get; set; } = string.Empty;
    public HfsmPointConfig StartPosition { get; set; } = new();
    public HfsmPointConfig WaterPoint { get; set; } = new();
    public HfsmPointConfig TrackCenter { get; set; } = new();
    public float TrackRadiusCm { get; set; }
    public float TrackEntryAngleDeg { get; set; }
    public float MoveSpeedCmPerSecond { get; set; }
    public float TrackAngularSpeedDegPerSecond { get; set; }
    public float DrinkRadiusCm { get; set; }
    public float StartWater { get; set; }
    public float MaxWater { get; set; }
    public float LowWaterThreshold { get; set; }
    public float DrinkCompleteThreshold { get; set; }
    public float DrinkWaterPerSecond { get; set; }
    public float RunWaterDrainPerSecond { get; set; }
    public int StartHealth { get; set; }
    public HfsmShortcutConfig Shortcuts { get; set; } = new();
    public List<HfsmStateConfig> States { get; set; } = new();
    public HfsmAnyStateConfig AnyState { get; set; } = new();
    public HfsmGraphDebugConfig GraphDebug { get; set; } = new();

    public static CapabilityStandardHfsmShowcaseConfig Load(JsonElement root)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        CapabilityStandardHfsmShowcaseConfig config = root.Deserialize<CapabilityStandardHfsmShowcaseConfig>(options)
            ?? throw new InvalidOperationException("HFSM showcase config is empty.");
        config.Validate();
        return config;
    }

    public HfsmStateConfig RequireState(string stateId)
    {
        for (int i = 0; i < States.Count; i++)
        {
            if (string.Equals(States[i].Id, stateId, StringComparison.Ordinal))
            {
                return States[i];
            }
        }

        throw new InvalidOperationException($"HFSM showcase config references missing state '{stateId}'.");
    }

    public string ComposePath(string stateId)
    {
        HfsmStateConfig state = RequireState(stateId);
        if (string.IsNullOrWhiteSpace(state.Parent))
        {
            return state.Label;
        }

        HfsmStateConfig parent = RequireState(state.Parent);
        return string.IsNullOrWhiteSpace(parent.Parent)
            ? $"{parent.Label} > {state.Label}"
            : $"{ComposePath(parent.Id)} > {state.Label}";
    }

    private void Validate()
    {
        if (SchemaVersion != 1)
        {
            throw new InvalidOperationException("HFSM showcase requires schemaVersion 1.");
        }

        RequireNonEmpty(MapId, nameof(MapId));
        RequireNonEmpty(Title, nameof(Title));
        RequireNonEmpty(HeroInstanceId, nameof(HeroInstanceId));
        RequireNonEmpty(WaterStationInstanceId, nameof(WaterStationInstanceId));
        RequireNonEmpty(TrackCenterInstanceId, nameof(TrackCenterInstanceId));
        StartPosition.Validate(nameof(StartPosition));
        WaterPoint.Validate(nameof(WaterPoint));
        TrackCenter.Validate(nameof(TrackCenter));
        RequirePositive(TrackRadiusCm, nameof(TrackRadiusCm));
        RequirePositive(MoveSpeedCmPerSecond, nameof(MoveSpeedCmPerSecond));
        RequirePositive(TrackAngularSpeedDegPerSecond, nameof(TrackAngularSpeedDegPerSecond));
        RequirePositive(DrinkRadiusCm, nameof(DrinkRadiusCm));
        RequirePositive(MaxWater, nameof(MaxWater));
        RequireRange(StartWater, 0f, MaxWater, nameof(StartWater));
        RequireRange(LowWaterThreshold, 0f, MaxWater, nameof(LowWaterThreshold));
        RequireRange(DrinkCompleteThreshold, LowWaterThreshold, MaxWater, nameof(DrinkCompleteThreshold));
        RequirePositive(DrinkWaterPerSecond, nameof(DrinkWaterPerSecond));
        RequirePositive(RunWaterDrainPerSecond, nameof(RunWaterDrainPerSecond));
        if (StartHealth <= 0)
        {
            throw new InvalidOperationException("HFSM showcase StartHealth must be positive.");
        }

        Shortcuts.Validate();
        AnyState.Validate();
        ValidateStates();
        GraphDebug.Validate();
    }

    private void ValidateStates()
    {
        if (States.Count == 0)
        {
            throw new InvalidOperationException("HFSM showcase requires state definitions.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < States.Count; i++)
        {
            States[i].Validate($"states[{i}]");
            if (!ids.Add(States[i].Id))
            {
                throw new InvalidOperationException($"HFSM showcase has duplicate state '{States[i].Id}'.");
            }
        }

        RequireState(CapabilityStandardHfsmShowcaseRuntime.StateAlive);
        RequireState(CapabilityStandardHfsmShowcaseRuntime.StateHydrate);
        RequireState(CapabilityStandardHfsmShowcaseRuntime.StateExercise);
        RequireState(CapabilityStandardHfsmShowcaseRuntime.StateGoDrink);
        RequireState(CapabilityStandardHfsmShowcaseRuntime.StateDrinking);
        RequireState(CapabilityStandardHfsmShowcaseRuntime.StateGoTrack);
        RequireState(CapabilityStandardHfsmShowcaseRuntime.StateRunning);
        RequireState(CapabilityStandardHfsmShowcaseRuntime.StateDead);
        if (!string.Equals(AnyState.TargetState, CapabilityStandardHfsmShowcaseRuntime.StateDead, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HFSM showcase AnyState target must be Dead.");
        }
    }

    private static void RequireNonEmpty(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != value.Length)
        {
            throw new InvalidOperationException($"HFSM showcase config field '{field}' must be non-empty and trimmed.");
        }
    }

    private static void RequirePositive(float value, string field)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new InvalidOperationException($"HFSM showcase config field '{field}' must be finite and positive.");
        }
    }

    private static void RequireRange(float value, float min, float max, string field)
    {
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new InvalidOperationException(
                $"HFSM showcase config field '{field}' must be in range {min.ToString(CultureInfo.InvariantCulture)}..{max.ToString(CultureInfo.InvariantCulture)}.");
        }
    }
}

internal sealed class HfsmGraphDebugConfig
{
    public string RootGraphId { get; set; } = string.Empty;
    public string RootTitle { get; set; } = string.Empty;
    public List<HfsmGraphNodeConfig> Nodes { get; set; } = new();
    public List<HfsmGraphEdgeConfig> Edges { get; set; } = new();
    public List<HfsmImplementationGraphConfig> Implementations { get; set; } = new();

    public HfsmImplementationGraphConfig? FindImplementation(string graphId)
    {
        for (int i = 0; i < Implementations.Count; i++)
        {
            if (string.Equals(Implementations[i].Id, graphId, StringComparison.Ordinal))
            {
                return Implementations[i];
            }
        }

        return null;
    }

    public HfsmImplementationGraphConfig? FindImplementationForState(string stateId)
    {
        for (int i = 0; i < Implementations.Count; i++)
        {
            if (string.Equals(Implementations[i].OwnerStateId, stateId, StringComparison.Ordinal))
            {
                return Implementations[i];
            }
        }

        return null;
    }

    public bool ContainsNode(string nodeId)
    {
        if (ContainsNode(Nodes, nodeId))
        {
            return true;
        }

        for (int i = 0; i < Implementations.Count; i++)
        {
            if (ContainsNode(Implementations[i].Nodes, nodeId))
            {
                return true;
            }
        }

        return false;
    }

    public void Validate()
    {
        RequireNonEmpty(RootGraphId, "graphDebug.rootGraphId");
        RequireNonEmpty(RootTitle, "graphDebug.rootTitle");
        ValidateGraph("graphDebug", Nodes, Edges);
        if (Implementations.Count == 0)
        {
            throw new InvalidOperationException("HFSM showcase graphDebug requires implementation graphs.");
        }

        var implementationIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Implementations.Count; i++)
        {
            Implementations[i].Validate($"graphDebug.implementations[{i}]");
            if (!implementationIds.Add(Implementations[i].Id))
            {
                throw new InvalidOperationException($"HFSM showcase graphDebug has duplicate implementation '{Implementations[i].Id}'.");
            }
        }
    }

    private static bool ContainsNode(List<HfsmGraphNodeConfig> nodes, string nodeId)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (string.Equals(nodes[i].Id, nodeId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static void ValidateGraph(string path, List<HfsmGraphNodeConfig> nodes, List<HfsmGraphEdgeConfig> edges)
    {
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException($"HFSM showcase {path} requires nodes.");
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            nodes[i].Validate($"{path}.nodes[{i}]");
            if (!nodeIds.Add(nodes[i].Id))
            {
                throw new InvalidOperationException($"HFSM showcase {path} has duplicate node '{nodes[i].Id}'.");
            }
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < edges.Count; i++)
        {
            edges[i].Validate($"{path}.edges[{i}]");
            if (!edgeIds.Add(edges[i].Id))
            {
                throw new InvalidOperationException($"HFSM showcase {path} has duplicate edge '{edges[i].Id}'.");
            }

            if (!nodeIds.Contains(edges[i].From) || !nodeIds.Contains(edges[i].To))
            {
                throw new InvalidOperationException($"HFSM showcase {path} edge '{edges[i].Id}' references a missing node.");
            }
        }
    }

    private static void RequireNonEmpty(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != value.Length)
        {
            throw new InvalidOperationException($"HFSM showcase {field} must be non-empty and trimmed.");
        }
    }
}

internal sealed class HfsmImplementationGraphConfig
{
    public string Id { get; set; } = string.Empty;
    public string OwnerStateId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<HfsmGraphNodeConfig> Nodes { get; set; } = new();
    public List<HfsmGraphEdgeConfig> Edges { get; set; } = new();

    public void Validate(string path)
    {
        RequireNonEmpty(Id, $"{path}.id");
        RequireNonEmpty(OwnerStateId, $"{path}.ownerStateId");
        RequireNonEmpty(Title, $"{path}.title");
        RequireNonEmpty(Summary, $"{path}.summary");
        HfsmGraphDebugConfig.ValidateGraph(path, Nodes, Edges);
    }

    private static void RequireNonEmpty(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != value.Length)
        {
            throw new InvalidOperationException($"HFSM showcase {field} must be non-empty and trimmed.");
        }
    }
}

internal sealed class HfsmGraphNodeConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string OpCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImplementationGraphId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public List<HfsmGraphPinConfig> InputPins { get; set; } = new();
    public List<HfsmGraphPinConfig> OutputPins { get; set; } = new();

    public void Validate(string path)
    {
        RequireNonEmpty(Id, $"{path}.id");
        RequireNonEmpty(Label, $"{path}.label");
        RequireNonEmpty(Kind, $"{path}.kind");
        if (!float.IsFinite(X) || !float.IsFinite(Y))
        {
            throw new InvalidOperationException($"HFSM showcase {path} position must be finite.");
        }

        ValidatePins(InputPins, $"{path}.inputPins");
        ValidatePins(OutputPins, $"{path}.outputPins");
    }

    private static void ValidatePins(List<HfsmGraphPinConfig> pins, string path)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < pins.Count; i++)
        {
            pins[i].Validate($"{path}[{i}]");
            if (!ids.Add(pins[i].Id))
            {
                throw new InvalidOperationException($"HFSM showcase {path} has duplicate pin '{pins[i].Id}'.");
            }
        }
    }

    private static void RequireNonEmpty(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != value.Length)
        {
            throw new InvalidOperationException($"HFSM showcase {field} must be non-empty and trimmed.");
        }
    }
}

internal sealed class HfsmGraphPinConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    public void Validate(string path)
    {
        RequireNonEmpty(Id, $"{path}.id");
        RequireNonEmpty(Label, $"{path}.label");
        RequireNonEmpty(Type, $"{path}.type");
    }

    private static void RequireNonEmpty(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != value.Length)
        {
            throw new InvalidOperationException($"HFSM showcase {field} must be non-empty and trimmed.");
        }
    }
}

internal sealed class HfsmGraphEdgeConfig
{
    public string Id { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string FromPin { get; set; } = string.Empty;
    public string ToPin { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;

    public void Validate(string path)
    {
        RequireNonEmpty(Id, $"{path}.id");
        RequireNonEmpty(From, $"{path}.from");
        RequireNonEmpty(To, $"{path}.to");
        RequireNonEmpty(Kind, $"{path}.kind");
    }

    private static void RequireNonEmpty(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != value.Length)
        {
            throw new InvalidOperationException($"HFSM showcase {field} must be non-empty and trimmed.");
        }
    }
}

internal sealed class HfsmPointConfig
{
    public float X { get; set; }
    public float Y { get; set; }

    public void Validate(string field)
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y))
        {
            throw new InvalidOperationException($"HFSM showcase point '{field}' must be finite.");
        }
    }
}

internal sealed class HfsmShortcutConfig
{
    public string FatalDamage { get; set; } = string.Empty;
    public string Thirst { get; set; } = string.Empty;
    public string Reset { get; set; } = string.Empty;

    public void Validate()
    {
        RequireKeyboardPath(FatalDamage, nameof(FatalDamage));
        RequireKeyboardPath(Thirst, nameof(Thirst));
        RequireKeyboardPath(Reset, nameof(Reset));
    }

    private static void RequireKeyboardPath(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"HFSM showcase shortcuts.{field} must be an explicit keyboard path.");
        }
    }
}

internal sealed class HfsmStateConfig
{
    public string Id { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string PlayerStory { get; set; } = string.Empty;

    public void Validate(string field)
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Trim().Length != Id.Length)
        {
            throw new InvalidOperationException($"HFSM showcase {field}.id must be non-empty and trimmed.");
        }

        if (!string.IsNullOrWhiteSpace(Parent) && Parent.Trim().Length != Parent.Length)
        {
            throw new InvalidOperationException($"HFSM showcase {field}.parent must be trimmed.");
        }

        if (string.IsNullOrWhiteSpace(Label) || string.IsNullOrWhiteSpace(PlayerStory))
        {
            throw new InvalidOperationException($"HFSM showcase {field} requires label and playerStory.");
        }
    }
}

internal sealed class HfsmAnyStateConfig
{
    public string Condition { get; set; } = string.Empty;
    public string TargetState { get; set; } = string.Empty;
    public string PlayerStory { get; set; } = string.Empty;

    public void Validate()
    {
        if (!string.Equals(Condition, "HealthAtOrBelowZero", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HFSM showcase AnyState condition must be HealthAtOrBelowZero.");
        }

        if (string.IsNullOrWhiteSpace(TargetState) || string.IsNullOrWhiteSpace(PlayerStory))
        {
            throw new InvalidOperationException("HFSM showcase AnyState requires targetState and playerStory.");
        }
    }
}
