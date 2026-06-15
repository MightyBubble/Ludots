using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace EntityQueryTacticsShowcaseMod.Runtime
{
    public sealed class EntityQueryTacticsShowcaseConfig
    {
        public string MapId { get; set; } = string.Empty;
        public EntityQueryTacticsScenarioConfig Scenario { get; set; } = new();
        public EntityQueryTacticsActionConfig Actions { get; set; } = new();
        public EntityQueryTacticsCollectionConfig Collections { get; set; } = new();
        public EntityQueryTacticsGraphConfig Graphs { get; set; } = new();
        public EntityQueryTacticsSummaryKeys SummaryKeys { get; set; } = new();
        public EntityQueryTacticsRelationshipNames Relationships { get; set; } = new();
        public EntityQueryTacticsMetricNames Metrics { get; set; } = new();
        public EntityQueryTacticsFlagNames Flags { get; set; } = new();
        public EntityQueryTacticsTagNames Tags { get; set; } = new();
        public EntityQueryTacticsAttributes Attributes { get; set; } = new();
        public EntityQueryTacticsLogs Logs { get; set; } = new();
        public EntityQueryTacticsPresentationText Presentation { get; set; } = new();
        public EntityQueryTacticsDemoPlaybackConfig DemoPlayback { get; set; } = new();

        public static EntityQueryTacticsShowcaseConfig Load(JsonObject configObject)
        {
            ArgumentNullException.ThrowIfNull(configObject);
            var options = StrictJsonOptions.CreateCamelCase();
            EntityQueryTacticsShowcaseConfig? config = configObject.Deserialize<EntityQueryTacticsShowcaseConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize entity query tactics showcase config.");
            }

            config.Validate();
            return config;
        }

        private void Validate()
        {
            Require(MapId, nameof(MapId));
            Require(Scenario.PlayerCommanderName, nameof(Scenario.PlayerCommanderName));
            Require(Scenario.PlayerTeamName, nameof(Scenario.PlayerTeamName));
            Require(Scenario.EnemyCommanderName, nameof(Scenario.EnemyCommanderName));
            Require(Scenario.EnemyTeamName, nameof(Scenario.EnemyTeamName));
            Require(Scenario.PressurePulse.TargetName, nameof(Scenario.PressurePulse.TargetName));
            Require(Scenario.PressurePulse.Metric, nameof(Scenario.PressurePulse.Metric));
            if (Scenario.Allies.Length == 0 || Scenario.Enemies.Length == 0)
            {
                throw new InvalidOperationException("Entity query tactics showcase requires allies and enemies.");
            }

            Require(Actions.CommitSelection, nameof(Actions.CommitSelection));
            Require(Actions.ExecuteGraphs, nameof(Actions.ExecuteGraphs));
            Require(Actions.RotateFormation, nameof(Actions.RotateFormation));
            Require(Actions.PressurePulse, nameof(Actions.PressurePulse));
            Require(Actions.CacheProbe, nameof(Actions.CacheProbe));
            Require(Collections.UiBox, nameof(Collections.UiBox));
            Require(Collections.FormalSelectionMirror, nameof(Collections.FormalSelectionMirror));
            Require(Collections.FormationPrimary, nameof(Collections.FormationPrimary));
            Require(Collections.SelectedFriendliesResult, nameof(Collections.SelectedFriendliesResult));
            Require(Collections.HostileThreatResult, nameof(Collections.HostileThreatResult));
            Require(Collections.FormationCacheResult, nameof(Collections.FormationCacheResult));
            Require(Graphs.SelectedFriendlies, nameof(Graphs.SelectedFriendlies));
            Require(Graphs.HostileThreats, nameof(Graphs.HostileThreats));
            Require(Graphs.FormationCache, nameof(Graphs.FormationCache));
            Require(Relationships.TacticalIntel, nameof(Relationships.TacticalIntel));
            Require(Metrics.Threat, nameof(Metrics.Threat));
            Require(Metrics.Focus, nameof(Metrics.Focus));
            Require(Flags.PriorityTarget, nameof(Flags.PriorityTarget));
            Require(Tags.Commandable, nameof(Tags.Commandable));
            Require(Tags.Routed, nameof(Tags.Routed));
            Require(Attributes.CommandPower, nameof(Attributes.CommandPower));
            Require(Attributes.Supply, nameof(Attributes.Supply));
            Require(Attributes.ThreatValue, nameof(Attributes.ThreatValue));
            Require(Presentation.Title, nameof(Presentation.Title));
            DemoPlayback.Validate();
        }

        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Entity query tactics showcase config requires non-empty '{name}'.");
            }
        }
    }

    public sealed class EntityQueryTacticsShowcaseConfigLoader
    {
        public const string RelativePath = "EntityQueryTacticsShowcaseConfig.json";

        private readonly ConfigPipeline _pipeline;

        public EntityQueryTacticsShowcaseConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public EntityQueryTacticsShowcaseConfig Load(ConfigCatalog catalog, ConfigConflictReport report)
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
                throw new InvalidOperationException($"Entity query tactics showcase config '{RelativePath}' must be registered in config_catalog.json.");
            }

            if (entry.MergePolicy != ConfigMergePolicy.Replace)
            {
                throw new InvalidOperationException($"Entity query tactics showcase config '{RelativePath}' must use Replace merge policy.");
            }

            JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
            if (merged == null)
            {
                throw new InvalidOperationException($"Entity query tactics showcase requires config '{RelativePath}' through ConfigPipeline.");
            }

            return EntityQueryTacticsShowcaseConfig.Load(merged);
        }
    }

    public sealed class EntityQueryTacticsScenarioConfig
    {
        public int PlayerTeamId { get; set; } = 1;
        public int EnemyTeamId { get; set; } = 2;
        public string PlayerTeamName { get; set; } = string.Empty;
        public string EnemyTeamName { get; set; } = string.Empty;
        public string PlayerCommanderName { get; set; } = string.Empty;
        public string EnemyCommanderName { get; set; } = string.Empty;
        public EntityQueryTacticsActorConfig[] Allies { get; set; } = Array.Empty<EntityQueryTacticsActorConfig>();
        public EntityQueryTacticsActorConfig[] Enemies { get; set; } = Array.Empty<EntityQueryTacticsActorConfig>();
        public EntityQueryTacticsActorConfig[] Objectives { get; set; } = Array.Empty<EntityQueryTacticsActorConfig>();
        public EntityQueryTacticsRelationSeed[] RelationSeeds { get; set; } = Array.Empty<EntityQueryTacticsRelationSeed>();
        public EntityQueryTacticsPressurePulseConfig PressurePulse { get; set; } = new();
    }

    public sealed class EntityQueryTacticsActorConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public int TeamId { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    public sealed class EntityQueryTacticsRelationSeed
    {
        public string SourceName { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string Metric { get; set; } = string.Empty;
        public int Value { get; set; }
        public string[] Flags { get; set; } = Array.Empty<string>();
    }

    public sealed class EntityQueryTacticsPressurePulseConfig
    {
        public string TargetName { get; set; } = string.Empty;
        public string Metric { get; set; } = string.Empty;
        public int Delta { get; set; } = 1;
        public string[] Flags { get; set; } = Array.Empty<string>();
    }

    public sealed class EntityQueryTacticsActionConfig
    {
        public string CommitSelection { get; set; } = string.Empty;
        public string ExecuteGraphs { get; set; } = string.Empty;
        public string RotateFormation { get; set; } = string.Empty;
        public string PressurePulse { get; set; } = string.Empty;
        public string CacheProbe { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsCollectionConfig
    {
        public string UiBox { get; set; } = string.Empty;
        public string FormalSelectionMirror { get; set; } = string.Empty;
        public string FormationPrimary { get; set; } = string.Empty;
        public string SelectedFriendliesResult { get; set; } = string.Empty;
        public string HostileThreatResult { get; set; } = string.Empty;
        public string FormationCacheResult { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsGraphConfig
    {
        public string SelectedFriendlies { get; set; } = string.Empty;
        public string HostileThreats { get; set; } = string.Empty;
        public string FormationCache { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsSummaryKeys
    {
        public string SelectedCount { get; set; } = string.Empty;
        public string SelectedCommandPower { get; set; } = string.Empty;
        public string SelectedSupply { get; set; } = string.Empty;
        public string SelectedBestEntity { get; set; } = string.Empty;
        public string ThreatCount { get; set; } = string.Empty;
        public string ThreatSum { get; set; } = string.Empty;
        public string ThreatAverage { get; set; } = string.Empty;
        public string ThreatMax { get; set; } = string.Empty;
        public string ThreatBestEntity { get; set; } = string.Empty;
        public string FormationCount { get; set; } = string.Empty;
        public string FormationMaxCommandPower { get; set; } = string.Empty;
        public string FormationMinSupply { get; set; } = string.Empty;
        public string FormationBestEntity { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsRelationshipNames
    {
        public string TacticalIntel { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsMetricNames
    {
        public string Threat { get; set; } = string.Empty;
        public string Focus { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsFlagNames
    {
        public string PriorityTarget { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsTagNames
    {
        public string Commandable { get; set; } = string.Empty;
        public string Routed { get; set; } = string.Empty;
        public string Objective { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsAttributes
    {
        public string CommandPower { get; set; } = string.Empty;
        public string Supply { get; set; } = string.Empty;
        public string ThreatValue { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsLogs
    {
        public string SystemInstalled { get; set; } = "[EntityQueryTacticsShowcaseMod] systems registered.";
        public string ScenarioReady { get; set; } = "Scenario ready.";
        public string SelectionCommitted { get; set; } = "UI acquisition committed to formal selection.";
        public string GraphsExecuted { get; set; } = "Graphs executed.";
        public string FormationRotated { get; set; } = "Formation snapshot rotated.";
        public string PressurePulse { get; set; } = "Relationship threat pulse applied.";
        public string CacheProbe { get; set; } = "Collection cache probe executed.";
    }

    public sealed class EntityQueryTacticsPresentationText
    {
        public string Title { get; set; } = string.Empty;
        public string ControlsLine { get; set; } = string.Empty;
        public string ArchitectureLine { get; set; } = string.Empty;
    }

    public sealed class EntityQueryTacticsDemoPlaybackConfig
    {
        public bool Enabled { get; set; }
        public string ActivationEnv { get; set; } = string.Empty;
        public EntityQueryTacticsDemoStepConfig[] Steps { get; set; } = Array.Empty<EntityQueryTacticsDemoStepConfig>();

        public void Validate()
        {
            if (Steps.Length == 0)
            {
                return;
            }

            for (int i = 0; i < Steps.Length; i++)
            {
                EntityQueryTacticsDemoStepConfig step = Steps[i];
                if (step.Frame == 0)
                {
                    throw new InvalidOperationException($"Entity query tactics demo playback step {i} requires frame > 0.");
                }

                if (string.IsNullOrWhiteSpace(step.Op))
                {
                    throw new InvalidOperationException($"Entity query tactics demo playback step {i} requires op.");
                }
            }
        }
    }

    public sealed class EntityQueryTacticsDemoStepConfig
    {
        public uint Frame { get; set; }
        public string Op { get; set; } = string.Empty;
        public string[] Entities { get; set; } = Array.Empty<string>();
    }
}
