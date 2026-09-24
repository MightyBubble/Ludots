using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Config;

namespace CapabilityStandardVirtualCameraShowcaseMod.Runtime;

internal enum CameraDebugPanelField : byte
{
    Yaw = 0,
    Pitch = 1,
    DistanceCm = 2,
    FovYDeg = 3
}

internal sealed class CapabilityStandardVirtualCameraShowcaseConfig
{
    public string MapId { get; set; } = string.Empty;
    public CameraDebugPanelConfig DebugPanel { get; set; } = new();
    public CameraImpulseDemoConfig ImpulseDemo { get; set; } = new();

    public static CapabilityStandardVirtualCameraShowcaseConfig Load(JsonObject configObject)
    {
        ArgumentNullException.ThrowIfNull(configObject);
        using JsonDocument document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);

        var options = StrictJsonOptions.CreateCamelCase();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false));
        CapabilityStandardVirtualCameraShowcaseConfig? config =
            document.RootElement.Deserialize<CapabilityStandardVirtualCameraShowcaseConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize capability-standard virtual camera showcase config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        JsonElement panel = RequireProperty(root, "debugPanel");
        RequireProperty(panel, "enabled");
        RequireProperty(panel, "initialVisible");
        RequireProperty(panel, "x");
        RequireProperty(panel, "y");
        RequireProperty(panel, "width");
        RequireProperty(panel, "fontSize");
        RequireProperty(panel, "lineHeight");
        RequireProperty(panel, "toggleActionId");
        RequireProperty(panel, "nextActionId");
        RequireProperty(panel, "previousActionId");
        RequireProperty(panel, "increaseActionId");
        RequireProperty(panel, "decreaseActionId");

        JsonElement adjustments = RequireProperty(panel, "adjustments");
        if (adjustments.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Capability-standard virtual camera debug panel requires adjustments as an array.");
        }

        int count = 0;
        foreach (JsonElement adjustment in adjustments.EnumerateArray())
        {
            RequireProperty(adjustment, "id");
            RequireProperty(adjustment, "displayName");
            RequireProperty(adjustment, "field");
            RequireProperty(adjustment, "step");
            RequireProperty(adjustment, "min");
            RequireProperty(adjustment, "max");
            count++;
        }

        if (count <= 0)
        {
            throw new InvalidOperationException("Capability-standard virtual camera debug panel requires at least one adjustment.");
        }

        JsonElement impulse = RequireProperty(root, "impulseDemo");
        RequireProperty(impulse, "enabled");
        RequireProperty(impulse, "triggerActionId");
        RequireProperty(impulse, "innerRadiusCm");
        RequireProperty(impulse, "radiusCm");
        RequireProperty(impulse, "durationSeconds");
        RequireProperty(impulse, "frequencyHz");
        RequireProperty(impulse, "phaseRadians");
        RequireProperty(impulse, "positionAmplitudeCm");
        RequireProperty(impulse, "yawAmplitudeDeg");
        RequireProperty(impulse, "pitchAmplitudeDeg");
        RequireProperty(impulse, "falloff");
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        DebugPanel.Validate();
        ImpulseDemo.Validate();
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Capability-standard virtual camera showcase config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Capability-standard virtual camera showcase config requires canonical non-empty {fieldName}.");
        }
    }
}

internal sealed class CameraDebugPanelConfig
{
    public bool Enabled { get; set; }
    public bool InitialVisible { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int FontSize { get; set; }
    public int LineHeight { get; set; }
    public string ToggleActionId { get; set; } = string.Empty;
    public string NextActionId { get; set; } = string.Empty;
    public string PreviousActionId { get; set; } = string.Empty;
    public string IncreaseActionId { get; set; } = string.Empty;
    public string DecreaseActionId { get; set; } = string.Empty;
    public CameraDebugPanelAdjustmentConfig[] Adjustments { get; set; } = Array.Empty<CameraDebugPanelAdjustmentConfig>();

    public void Validate()
    {
        if (X < 0 || Y < 0)
        {
            throw new InvalidOperationException("Capability-standard virtual camera debug panel requires non-negative x/y.");
        }

        if (Width <= 0 || FontSize <= 0 || LineHeight <= 0)
        {
            throw new InvalidOperationException("Capability-standard virtual camera debug panel requires positive width/fontSize/lineHeight.");
        }

        RequireActionId(ToggleActionId, nameof(ToggleActionId));
        RequireActionId(NextActionId, nameof(NextActionId));
        RequireActionId(PreviousActionId, nameof(PreviousActionId));
        RequireActionId(IncreaseActionId, nameof(IncreaseActionId));
        RequireActionId(DecreaseActionId, nameof(DecreaseActionId));

        if (Adjustments.Length == 0)
        {
            throw new InvalidOperationException("Capability-standard virtual camera debug panel requires at least one adjustment.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Adjustments.Length; i++)
        {
            Adjustments[i].Validate(i);
            if (!ids.Add(Adjustments[i].Id))
            {
                throw new InvalidOperationException($"Capability-standard virtual camera debug panel contains duplicate adjustment id '{Adjustments[i].Id}'.");
            }
        }
    }

    private static void RequireActionId(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Capability-standard virtual camera debug panel requires canonical non-empty {fieldName}.");
        }
    }
}

internal sealed class CameraImpulseDemoConfig
{
    public bool Enabled { get; set; }
    public string TriggerActionId { get; set; } = string.Empty;
    public float InnerRadiusCm { get; set; }
    public float RadiusCm { get; set; }
    public float DurationSeconds { get; set; }
    public float FrequencyHz { get; set; }
    public float PhaseRadians { get; set; }
    public float PositionAmplitudeCm { get; set; }
    public float YawAmplitudeDeg { get; set; }
    public float PitchAmplitudeDeg { get; set; }
    public CameraImpulseFalloff Falloff { get; set; } = CameraImpulseFalloff.SmoothStep;

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TriggerActionId) || !string.Equals(TriggerActionId, TriggerActionId.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Capability-standard virtual camera impulse demo requires canonical non-empty triggerActionId.");
        }

        RequireFiniteNonNegative(InnerRadiusCm, nameof(InnerRadiusCm));
        RequireFinitePositive(RadiusCm, nameof(RadiusCm));
        RequireFinitePositive(DurationSeconds, nameof(DurationSeconds));
        RequireFiniteNonNegative(FrequencyHz, nameof(FrequencyHz));
        RequireFinite(PhaseRadians, nameof(PhaseRadians));
        RequireFiniteNonNegative(PositionAmplitudeCm, nameof(PositionAmplitudeCm));
        RequireFiniteNonNegative(YawAmplitudeDeg, nameof(YawAmplitudeDeg));
        RequireFiniteNonNegative(PitchAmplitudeDeg, nameof(PitchAmplitudeDeg));
        if (RadiusCm <= InnerRadiusCm)
        {
            throw new InvalidOperationException("Capability-standard virtual camera impulse demo radiusCm must be greater than innerRadiusCm.");
        }

        if (!Enum.IsDefined(Falloff))
        {
            throw new InvalidOperationException($"Capability-standard virtual camera impulse demo declares unsupported falloff '{Falloff}'.");
        }
    }

    private static void RequireFinite(float value, string name)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidOperationException($"Capability-standard virtual camera impulse demo requires finite {name}.");
        }
    }

    private static void RequireFinitePositive(float value, string name)
    {
        RequireFinite(value, name);
        if (value <= 0f)
        {
            throw new InvalidOperationException($"Capability-standard virtual camera impulse demo requires positive {name}.");
        }
    }

    private static void RequireFiniteNonNegative(float value, string name)
    {
        RequireFinite(value, name);
        if (value < 0f)
        {
            throw new InvalidOperationException($"Capability-standard virtual camera impulse demo requires non-negative {name}.");
        }
    }
}

internal sealed class CameraDebugPanelAdjustmentConfig
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public float Step { get; set; }
    public float Min { get; set; }
    public float Max { get; set; }

    public CameraDebugPanelField ResolvedField => ResolveField(Field);

    public void Validate(int index)
    {
        RequireCanonical(Id, $"debugPanel.adjustments[{index}].id");
        RequireCanonical(DisplayName, $"debugPanel.adjustments[{index}].displayName");
        RequireCanonical(Field, $"debugPanel.adjustments[{index}].field");
        ResolveField(Field);

        if (!float.IsFinite(Step) || Step <= 0f)
        {
            throw new InvalidOperationException($"Capability-standard virtual camera debug panel adjustment '{Id}' requires finite positive step.");
        }

        if (!float.IsFinite(Min) || !float.IsFinite(Max) || Max < Min)
        {
            throw new InvalidOperationException($"Capability-standard virtual camera debug panel adjustment '{Id}' requires finite min/max with max >= min.");
        }
    }

    private static void RequireCanonical(string value, string context)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Capability-standard virtual camera showcase config requires canonical non-empty {context}.");
        }
    }

    private static CameraDebugPanelField ResolveField(string field)
    {
        return field switch
        {
            "Yaw" => CameraDebugPanelField.Yaw,
            "Pitch" => CameraDebugPanelField.Pitch,
            "DistanceCm" => CameraDebugPanelField.DistanceCm,
            "FovYDeg" => CameraDebugPanelField.FovYDeg,
            _ => throw new InvalidOperationException(
                $"Capability-standard virtual camera debug panel declares unsupported adjustment field '{field}'.")
        };
    }
}

internal sealed class CapabilityStandardVirtualCameraShowcaseConfigLoader
{
    public const string RelativePath = "CapabilityStandardVirtualCameraShowcaseConfig.json";

    private readonly ConfigPipeline _pipeline;

    public CapabilityStandardVirtualCameraShowcaseConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public CapabilityStandardVirtualCameraShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(report);

        if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"Capability-standard virtual camera showcase config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Capability-standard virtual camera showcase config '{RelativePath}' must use Replace merge policy.");
        }

        if (_pipeline.MergeFromCatalog(in entry, report) is not JsonObject merged)
        {
            throw new InvalidOperationException($"Capability-standard virtual camera showcase requires config '{RelativePath}' through ConfigPipeline.");
        }

        return CapabilityStandardVirtualCameraShowcaseConfig.Load(merged);
    }
}
