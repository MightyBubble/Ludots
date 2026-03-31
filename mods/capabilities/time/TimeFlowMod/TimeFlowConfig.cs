using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS;

namespace TimeFlowMod;

public sealed class TimeFlowConfig
{
    public Dictionary<string, TimeFlowProfileConfig> Profiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TimeFlowProfileConfig
{
    public string Description { get; set; } = string.Empty;
    public float? GlobalTimeScale { get; set; }
    public int? SimulationScalePermille { get; set; }
    public int? GasScalePermille { get; set; }
    public int? Physics2DScalePermille { get; set; }
    public int? Navigation2DScalePermille { get; set; }
    public int? TasksScalePermille { get; set; }
    public SimulationLoopMode? LoopMode { get; set; }
    public GasStepMode? GasMode { get; set; }
    public int? GasStepEveryFixedTicks { get; set; }
    public int? PhysicsTargetHz { get; set; }
    public int? PhysicsMaxStepsPerFixedTick { get; set; }
    public int? NavigationTargetHz { get; set; }
    public int? NavigationMaxStepsPerFixedTick { get; set; }
}

public sealed class TimeFlowConfigLoader
{
    private readonly ConfigPipeline _pipeline;

    public TimeFlowConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public TimeFlowConfig Load(
        ConfigCatalog? catalog = null,
        ConfigConflictReport? report = null,
        string relativePath = "TimeFlow/profiles.json")
    {
        if (catalog == null)
        {
            return new TimeFlowConfig();
        }

        ConfigCatalogEntry entry = ConfigPipeline.GetEntryOrDefault(
            catalog,
            relativePath,
            ConfigMergePolicy.DeepObject);
        JsonObject? merged = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
        if (merged is null)
        {
            return new TimeFlowConfig();
        }

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        string json = merged.ToJsonString();
        TimeFlowConfig? config = JsonSerializer.Deserialize<TimeFlowConfig>(json, options);
        return config ?? new TimeFlowConfig();
    }
}
