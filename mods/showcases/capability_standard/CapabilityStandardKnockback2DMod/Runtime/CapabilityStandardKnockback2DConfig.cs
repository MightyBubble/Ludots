using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace CapabilityStandardKnockback2DMod.Runtime;

internal sealed class CapabilityStandardKnockback2DConfig
{
    public string MapId { get; set; } = string.Empty;
    public int SpawnScratchCapacity { get; set; }
    public CapabilityStandardKnockback2DSpawnConfig[] Spawns { get; set; } = Array.Empty<CapabilityStandardKnockback2DSpawnConfig>();

    public static CapabilityStandardKnockback2DConfig Load(JsonObject configObject)
    {
        using var document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        CapabilityStandardKnockback2DConfig? config = document.RootElement.Deserialize<CapabilityStandardKnockback2DConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize capability-standard Knockback2D config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "spawnScratchCapacity");
        JsonElement spawns = RequireProperty(root, "spawns");
        if (spawns.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Capability-standard Knockback2D config requires spawns as an array.");
        }

        int index = 0;
        foreach (JsonElement spawn in spawns.EnumerateArray())
        {
            RequireProperty(spawn, "id");
            RequireProperty(spawn, "templateId");
            RequireProperty(spawn, "worldXCm");
            RequireProperty(spawn, "worldYCm");
            RequireProperty(spawn, "facingRad");
            index++;
        }

        if (index <= 0)
        {
            throw new InvalidOperationException("Capability-standard Knockback2D config requires at least one spawn.");
        }
    }

    private void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        if (SpawnScratchCapacity <= 0)
        {
            throw new InvalidOperationException("Capability-standard Knockback2D config requires spawnScratchCapacity > 0.");
        }

        if (Spawns.Length <= 0)
        {
            throw new InvalidOperationException("Capability-standard Knockback2D config requires at least one spawn.");
        }

        if (SpawnScratchCapacity < Spawns.Length)
        {
            throw new InvalidOperationException("Capability-standard Knockback2D config requires spawnScratchCapacity >= spawns length.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Spawns.Length; i++)
        {
            Spawns[i].Validate(i);
            if (!ids.Add(Spawns[i].Id))
            {
                throw new InvalidOperationException($"Capability-standard Knockback2D config contains duplicate spawn id '{Spawns[i].Id}'.");
            }
        }
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Capability-standard Knockback2D config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Capability-standard Knockback2D config requires non-empty {fieldName}.");
        }
    }
}

internal sealed class CapabilityStandardKnockback2DSpawnConfig
{
    public string Id { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public int WorldXCm { get; set; }
    public int WorldYCm { get; set; }
    public float FacingRad { get; set; }

    public void Validate(int index)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException($"Capability-standard Knockback2D spawns[{index}] requires non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(TemplateId))
        {
            throw new InvalidOperationException($"Capability-standard Knockback2D spawn '{Id}' requires non-empty templateId.");
        }

        if (!float.IsFinite(FacingRad))
        {
            throw new InvalidOperationException($"Capability-standard Knockback2D spawn '{Id}' requires finite facingRad.");
        }
    }
}

internal sealed class CapabilityStandardKnockback2DConfigLoader
{
    public const string RelativePath = "CapabilityStandardKnockback2DConfig.json";

    private readonly ConfigPipeline _pipeline;

    public CapabilityStandardKnockback2DConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public CapabilityStandardKnockback2DConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
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
            throw new InvalidOperationException($"Capability-standard Knockback2D config '{RelativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"Capability-standard Knockback2D config '{RelativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"Capability-standard Knockback2D requires config '{RelativePath}' through ConfigPipeline.");
        }

        return CapabilityStandardKnockback2DConfig.Load(merged);
    }
}
