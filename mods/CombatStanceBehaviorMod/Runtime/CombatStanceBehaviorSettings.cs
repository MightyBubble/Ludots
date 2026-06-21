using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace CombatStanceBehaviorMod.Runtime;

public readonly struct CombatStanceBehaviorSettings
{
    public CombatStanceBehaviorSettings(int arrivalRadiusCm, int defaultRetaliationTtlSteps)
    {
        if (arrivalRadiusCm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrivalRadiusCm), "Combat stance arrival radius must be positive.");
        }

        if (defaultRetaliationTtlSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultRetaliationTtlSteps), "Combat stance default retaliation TTL must be positive.");
        }

        ArrivalRadiusCm = arrivalRadiusCm;
        DefaultRetaliationTtlSteps = defaultRetaliationTtlSteps;
    }

    public int ArrivalRadiusCm { get; }
    public int DefaultRetaliationTtlSteps { get; }
}

internal sealed class CombatStanceBehaviorConfig
{
    public int? ArrivalRadiusCm { get; set; }
    public int? DefaultRetaliationTtlSteps { get; set; }
}

internal sealed class CombatStanceBehaviorSettingsLoader
{
    public const string RelativePath = "CombatStance/behavior.json";

    private readonly ConfigPipeline _pipeline;

    public CombatStanceBehaviorSettingsLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public CombatStanceBehaviorSettings Load(ConfigCatalog catalog, ConfigConflictReport report)
    {
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (!catalog.TryGet(RelativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"Combat stance behavior config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.DeepObject)
        {
            throw new InvalidOperationException($"Combat stance behavior config '{RelativePath}' must use DeepObject merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Combat stance behavior requires config '{RelativePath}' through ConfigPipeline.");
        }

        CombatStanceBehaviorConfig cfg = merged.Deserialize<CombatStanceBehaviorConfig>(StrictJsonOptions.CreateCamelCase())
            ?? throw new InvalidOperationException($"Combat stance behavior failed to deserialize '{RelativePath}'.");

        int arrivalRadiusCm = RequirePositive(cfg.ArrivalRadiusCm, "arrivalRadiusCm");
        int defaultRetaliationTtlSteps = RequirePositive(cfg.DefaultRetaliationTtlSteps, "defaultRetaliationTtlSteps");
        return new CombatStanceBehaviorSettings(arrivalRadiusCm, defaultRetaliationTtlSteps);
    }

    private static int RequirePositive(int? value, string field)
    {
        if (!value.HasValue)
        {
            throw new InvalidOperationException($"Combat stance behavior config requires '{field}'.");
        }

        if (value.Value <= 0)
        {
            throw new InvalidOperationException($"Combat stance behavior config '{field}' must be positive.");
        }

        return value.Value;
    }
}
