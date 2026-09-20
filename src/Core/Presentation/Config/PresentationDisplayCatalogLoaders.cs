using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Presentation.Config
{
    public sealed class PresentationSemanticMapCatalogLoader
    {
        public const string Path = "Presentation/semantic_maps.json";

        private readonly ConfigPipeline _pipeline;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public PresentationSemanticMapCatalogLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public PresentationSemanticMapCatalog Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, Path, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            if (merged.Count == 0)
            {
                return PresentationSemanticMapCatalog.Empty;
            }

            var catalogResult = new PresentationSemanticMapCatalog();
            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<PresentationSemanticMapDefinition>(
                    merged[i].Node.ToJsonString(), _jsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize semantic map at '{Path}' index {i}.");
                catalogResult.Register(definition);
            }

            return catalogResult;
        }
    }

    public sealed class PresentationImageAssetCatalogLoader
    {
        public const string Path = "Presentation/image_assets.json";

        private readonly ConfigPipeline _pipeline;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public PresentationImageAssetCatalogLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public PresentationImageAssetCatalog Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            var entry = ConfigPipeline.RequireEntry(catalog, Path, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            if (merged.Count == 0)
            {
                return PresentationImageAssetCatalog.Empty;
            }

            var catalogResult = new PresentationImageAssetCatalog();
            for (int i = 0; i < merged.Count; i++)
            {
                var definition = JsonSerializer.Deserialize<PresentationImageAssetDefinition>(
                    merged[i].Node.ToJsonString(), _jsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize image asset at '{Path}' index {i}.");
                catalogResult.Register(definition);
            }

            return catalogResult;
        }
    }
}
