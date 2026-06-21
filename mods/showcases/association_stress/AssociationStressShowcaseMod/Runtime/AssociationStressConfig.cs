using System;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;

namespace AssociationStressShowcaseMod.Runtime;

internal sealed class AssociationStressConfig
{
    public string MapId { get; set; } = AssociationStressIds.MapId;
    public ScaleConfig[] Scales { get; set; } = Array.Empty<ScaleConfig>();
    public int InitialScaleIndex { get; set; }
    public int ViewerCount { get; set; } = 24;
    public int ExpiryTickOffset { get; set; } = 12;
    public string Header { get; set; } = "Entity Association Core";
    public string Summary { get; set; } = "Scale knowledge and collection churn with zero per-frame allocation after warmup.";
    public string Controls { get; set; } = "[ / ] scale | P pulse | C compact";

    public static AssociationStressConfig Load(Stream stream)
    {
        AssociationStressConfig? config = JsonSerializer.Deserialize<AssociationStressConfig>(
            stream,
            StrictJsonOptions.CreateExact());
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize AssociationStressConfig.");
        }

        if (config.Scales.Length == 0)
        {
            throw new InvalidOperationException("Association stress showcase requires at least one scale.");
        }

        return config;
    }
}

internal sealed class ScaleConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SquadCount { get; set; }
    public int MembersPerSquad { get; set; }
    public int PulseStride { get; set; } = 1;
}
