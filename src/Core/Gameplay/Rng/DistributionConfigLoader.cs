using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Engine.Randomization;

namespace Ludots.Core.Gameplay.Rng;

public sealed class DistributionConfigLoader
{
    private const string CatalogPath = "Rng/distributions.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ConfigPipeline _pipeline;

    public DistributionConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public List<DistributionTable> Load(ConfigCatalog catalog, ConfigConflictReport report, IRngStreamService streams)
    {
        var entry = ConfigPipeline.RequireEntry(catalog, CatalogPath, ConfigMergePolicy.ArrayById, "id");
        var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);

        var declaredStreamSeeds = new Dictionary<string, uint>(StringComparer.Ordinal);
        var tables = new List<DistributionTable>(merged.Count);
        for (var i = 0; i < merged.Count; i++)
        {
            var config = merged[i].Node.Deserialize<DistributionConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Distribution at index {i} in '{CatalogPath}' failed to deserialize.");

            if (string.IsNullOrWhiteSpace(config.Stream))
            {
                throw new InvalidOperationException(
                    $"Distribution '{config.Id}' must declare a named rng stream; unnamed streams are not allowed.");
            }

            var seed = config.StreamSeed ?? DeriveStreamSeed(config.Stream);
            if (declaredStreamSeeds.TryGetValue(config.Stream, out var existingSeed))
            {
                if (existingSeed != seed)
                {
                    throw new InvalidOperationException(
                        $"Distributions sharing stream '{config.Stream}' declare conflicting seeds {existingSeed} and {seed}; pick one explicit seed.");
                }
            }
            else
            {
                declaredStreamSeeds.Add(config.Stream, seed);
                streams.DeclareStream(config.Stream, seed);
            }

            tables.Add(new DistributionTable(config.Id, config.Stream, config.Entries));
        }

        return tables;
    }

    private static uint DeriveStreamSeed(string streamName)
    {
        var hash = RngSeed.Begin();
        foreach (var c in streamName)
        {
            hash = RngSeed.Mix(hash, c);
        }

        return RngSeed.Finalize(hash);
    }
}
