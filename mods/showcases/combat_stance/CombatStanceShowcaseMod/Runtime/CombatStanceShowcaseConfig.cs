using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace CombatStanceShowcaseMod.Runtime;

internal sealed class CombatStanceShowcaseConfig
{
    public string MapId { get; set; } = string.Empty;
    public CombatStanceShowcaseOrderConfig[] InitialOrders { get; set; } = Array.Empty<CombatStanceShowcaseOrderConfig>();
}

internal sealed class CombatStanceShowcaseOrderConfig
{
    public string Actor { get; set; } = string.Empty;
    public string OrderTypeKey { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Stance { get; set; } = string.Empty;
    public CombatStanceShowcasePointConfig? Destination { get; set; }
    public int? RadiusCm { get; set; }
    public int? LeashRadiusCm { get; set; }
    public int? RetaliationTtlSteps { get; set; }
}

internal sealed class CombatStanceShowcasePointConfig
{
    public int? XCm { get; set; }
    public int? YCm { get; set; }
}

internal sealed class CombatStanceShowcaseConfigLoader
{
    public const string RelativePath = "CombatStanceShowcase/scenario.json";

    private readonly ConfigPipeline _pipeline;

    public CombatStanceShowcaseConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public CombatStanceShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
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
            throw new InvalidOperationException($"Combat stance showcase config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.DeepObject)
        {
            throw new InvalidOperationException($"Combat stance showcase config '{RelativePath}' must use DeepObject merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Combat stance showcase requires config '{RelativePath}' through ConfigPipeline.");
        }

        CombatStanceShowcaseConfig config = merged.Deserialize<CombatStanceShowcaseConfig>(StrictJsonOptions.CreateCamelCase())
            ?? throw new InvalidOperationException($"Combat stance showcase failed to deserialize '{RelativePath}'.");
        Validate(config);
        return config;
    }

    private static void Validate(CombatStanceShowcaseConfig config)
    {
        RequireNonEmpty(config.MapId, "mapId");
        if (config.InitialOrders == null)
        {
            throw new InvalidOperationException("Combat stance showcase config requires explicit initialOrders.");
        }

        for (int i = 0; i < config.InitialOrders.Length; i++)
        {
            CombatStanceShowcaseOrderConfig order = config.InitialOrders[i]
                ?? throw new InvalidOperationException($"Combat stance showcase initialOrders[{i}] requires an object payload.");
            string context = $"Combat stance showcase initialOrders[{i}]";
            RequireNonEmpty(order.Actor, $"{context}.actor");
            RequireNonEmpty(order.OrderTypeKey, $"{context}.orderTypeKey");
            switch (order.OrderTypeKey)
            {
                case "attackMove":
                case "assaultMove":
                    RequireDestination(order.Destination, $"{context}.destination");
                    RequirePositive(order.LeashRadiusCm, $"{context}.leashRadiusCm");
                    break;
                case "guard":
                    RequireNonEmpty(order.Target, $"{context}.target");
                    RequirePositive(order.RadiusCm, $"{context}.radiusCm");
                    RequirePositive(order.LeashRadiusCm, $"{context}.leashRadiusCm");
                    break;
                case "setCombatStance":
                    RequireNonEmpty(order.Stance, $"{context}.stance");
                    if (!string.Equals(order.Stance, "HoldFire", StringComparison.Ordinal))
                    {
                        RequirePositive(order.LeashRadiusCm, $"{context}.leashRadiusCm");
                    }

                    if (string.Equals(order.Stance, "ReturnFire", StringComparison.Ordinal))
                    {
                        RequirePositive(order.RetaliationTtlSteps, $"{context}.retaliationTtlSteps");
                    }
                    break;
                default:
                    throw new InvalidOperationException($"{context}.orderTypeKey references unsupported order '{order.OrderTypeKey}'.");
            }
        }
    }

    private static void RequireDestination(CombatStanceShowcasePointConfig? destination, string context)
    {
        if (destination == null)
        {
            throw new InvalidOperationException($"{context} is required.");
        }

        if (!destination.XCm.HasValue)
        {
            throw new InvalidOperationException($"{context}.xCm is required.");
        }

        if (!destination.YCm.HasValue)
        {
            throw new InvalidOperationException($"{context}.yCm is required.");
        }
    }

    private static void RequireNonEmpty(string value, string context)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{context} requires a non-empty string.");
        }
    }

    private static void RequirePositive(int? value, string context)
    {
        if (!value.HasValue || value.Value <= 0)
        {
            throw new InvalidOperationException($"{context} must be positive.");
        }
    }
}
