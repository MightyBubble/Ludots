using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using NarrativeFrontendMod.Runtime;

namespace EntityQueryTacticsShowcaseMod.Runtime
{
    public sealed class EntityQueryTacticsFrontendConfig
    {
        public string OwnerId { get; set; } = "EntityQueryTactics";
        public string BackdropHex { get; set; } = string.Empty;
        public EntityQueryTacticsSurfaceConfig PromptRibbon { get; set; } = new();
        public EntityQueryTacticsSurfaceConfig SelectionPanel { get; set; } = new();
        public EntityQueryTacticsSurfaceConfig QueryBoard { get; set; } = new();
        public EntityQueryTacticsSurfaceConfig RelationBoard { get; set; } = new();
        public EntityQueryTacticsSurfaceConfig CachePanel { get; set; } = new();

        public static EntityQueryTacticsFrontendConfig Load(JsonObject configObject)
        {
            ArgumentNullException.ThrowIfNull(configObject);
            var options = StrictJsonOptions.CreateCamelCase();
            EntityQueryTacticsFrontendConfig? config = configObject.Deserialize<EntityQueryTacticsFrontendConfig>(options);
            return config ?? throw new InvalidOperationException("Failed to deserialize entity query tactics frontend config.");
        }
    }

    public sealed class EntityQueryTacticsFrontendConfigLoader
    {
        public const string RelativePath = "Frontend/entity_query_tactics_frontend.json";

        private readonly ConfigPipeline _pipeline;

        public EntityQueryTacticsFrontendConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public EntityQueryTacticsFrontendConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
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
                throw new InvalidOperationException($"Entity query tactics frontend config '{RelativePath}' must be registered in config_catalog.json.");
            }

            if (entry.MergePolicy != ConfigMergePolicy.Replace)
            {
                throw new InvalidOperationException($"Entity query tactics frontend config '{RelativePath}' must use Replace merge policy.");
            }

            JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
            if (merged == null)
            {
                throw new InvalidOperationException($"Entity query tactics showcase requires frontend config '{RelativePath}' through ConfigPipeline.");
            }

            return EntityQueryTacticsFrontendConfig.Load(merged);
        }
    }

    public sealed class EntityQueryTacticsSurfaceConfig
    {
        public string Anchor { get; set; } = "TopLeft";
        public float Width { get; set; } = 420f;
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public int ZIndex { get; set; } = 50;
        public string Eyebrow { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Footer { get; set; } = string.Empty;
        public string AccentHex { get; set; } = string.Empty;
        public string BackgroundHex { get; set; } = string.Empty;
        public string BorderHex { get; set; } = string.Empty;
        public string ForegroundHex { get; set; } = string.Empty;
        public string MutedHex { get; set; } = string.Empty;

        public NarrativeFrontendAnchor ResolveAnchor()
        {
            if (Enum.TryParse(Anchor, ignoreCase: true, out NarrativeFrontendAnchor anchor))
            {
                return anchor;
            }

            throw new InvalidOperationException(
                $"Invalid frontend anchor '{Anchor}' in '{EntityQueryTacticsFrontendConfigLoader.RelativePath}'.");
        }
    }
}
