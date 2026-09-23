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
            Require(Scenario.RuntimeSpawnReceiptChannelKey, nameof(Scenario.RuntimeSpawnReceiptChannelKey));
            Require(Scenario.PressurePulse.TargetName, nameof(Scenario.PressurePulse.TargetName));
            Require(Scenario.PressurePulse.Metric, nameof(Scenario.PressurePulse.Metric));
            if (Scenario.Allies.Length == 0 || Scenario.Enemies.Length == 0)
            {
                throw new InvalidOperationException("Entity query tactics showcase requires allies and enemies.");
            }

            Scenario.ValidateGeneratedCohorts();
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
        public string RuntimeSpawnReceiptChannelKey { get; set; } = "entityquery.tactics.runtimeSpawn";
        public EntityQueryTacticsActorConfig[] Allies { get; set; } = Array.Empty<EntityQueryTacticsActorConfig>();
        public EntityQueryTacticsActorConfig[] Enemies { get; set; } = Array.Empty<EntityQueryTacticsActorConfig>();
        public EntityQueryTacticsActorConfig[] Objectives { get; set; } = Array.Empty<EntityQueryTacticsActorConfig>();
        public EntityQueryTacticsGeneratedCohortConfig[] GeneratedCohorts { get; set; } = Array.Empty<EntityQueryTacticsGeneratedCohortConfig>();
        public EntityQueryTacticsRelationSeed[] RelationSeeds { get; set; } = Array.Empty<EntityQueryTacticsRelationSeed>();
        public EntityQueryTacticsPressurePulseConfig PressurePulse { get; set; } = new();

        public int CountGeneratedActors(string role)
        {
            if (GeneratedCohorts.Length == 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < GeneratedCohorts.Length; i++)
            {
                EntityQueryTacticsGeneratedCohortConfig cohort = GeneratedCohorts[i];
                if (string.Equals(cohort.Role, role, StringComparison.OrdinalIgnoreCase))
                {
                    count += Math.Max(0, cohort.Count);
                }
            }

            return count;
        }

        public int TotalActorCount =>
            Allies.Length +
            Enemies.Length +
            Objectives.Length +
            CountGeneratedActors(EntityQueryTacticsGeneratedActorRoles.Ally) +
            CountGeneratedActors(EntityQueryTacticsGeneratedActorRoles.Enemy) +
            CountGeneratedActors(EntityQueryTacticsGeneratedActorRoles.Objective);

        public void ValidateGeneratedCohorts()
        {
            for (int i = 0; i < GeneratedCohorts.Length; i++)
            {
                GeneratedCohorts[i].Validate(i);
            }
        }
    }

    public sealed class EntityQueryTacticsActorConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public int TeamId { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    public static class EntityQueryTacticsGeneratedActorRoles
    {
        public const string Ally = "Ally";
        public const string Enemy = "Enemy";
        public const string Objective = "Objective";
    }

    public sealed class EntityQueryTacticsGeneratedCohortConfig
    {
        public string Role { get; set; } = string.Empty;
        public string NamePrefix { get; set; } = string.Empty;
        public int FirstIndex { get; set; } = 1;
        public int Count { get; set; }
        public string Template { get; set; } = string.Empty;
        public int TeamId { get; set; }
        public float FacingRad { get; set; }
        public EntityQueryTacticsGeneratedGridConfig Grid { get; set; } = new();
        public string[] Tags { get; set; } = Array.Empty<string>();
        public EntityQueryTacticsAttributePatternConfig[] Attributes { get; set; } = Array.Empty<EntityQueryTacticsAttributePatternConfig>();
        public EntityQueryTacticsGeneratedRelationConfig[] Relations { get; set; } = Array.Empty<EntityQueryTacticsGeneratedRelationConfig>();

        public void Validate(int index)
        {
            if (Count < 0)
            {
                throw new InvalidOperationException($"Entity query tactics generated cohort {index} requires count >= 0.");
            }

            if (Count == 0)
            {
                return;
            }

            if (!string.Equals(Role, EntityQueryTacticsGeneratedActorRoles.Ally, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Role, EntityQueryTacticsGeneratedActorRoles.Enemy, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Role, EntityQueryTacticsGeneratedActorRoles.Objective, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Entity query tactics generated cohort {index} has unsupported role '{Role}'.");
            }

            RequireNonEmpty(NamePrefix, $"GeneratedCohorts[{index}].{nameof(NamePrefix)}");
            RequireNonEmpty(Template, $"GeneratedCohorts[{index}].{nameof(Template)}");
            Grid.Validate(index);
            for (int i = 0; i < Attributes.Length; i++)
            {
                Attributes[i].Validate(index, i);
            }

            for (int i = 0; i < Relations.Length; i++)
            {
                Relations[i].Validate(index, i);
            }
        }

        private static void RequireNonEmpty(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Entity query tactics showcase config requires non-empty '{name}'.");
            }
        }
    }

    public sealed class EntityQueryTacticsGeneratedGridConfig
    {
        public int OriginXCm { get; set; }
        public int OriginYCm { get; set; }
        public int Columns { get; set; } = 1;
        public int SpacingXCm { get; set; } = 100;
        public int SpacingYCm { get; set; } = 100;

        public void Validate(int cohortIndex)
        {
            if (Columns <= 0)
            {
                throw new InvalidOperationException($"Entity query tactics generated cohort {cohortIndex} requires grid.columns > 0.");
            }
        }
    }

    public sealed class EntityQueryTacticsAttributePatternConfig
    {
        public string Attribute { get; set; } = string.Empty;
        public float BaseValue { get; set; }
        public float Step { get; set; }
        public int Modulo { get; set; } = 1;

        public void Validate(int cohortIndex, int index)
        {
            if (string.IsNullOrWhiteSpace(Attribute))
            {
                throw new InvalidOperationException(
                    $"Entity query tactics showcase config requires non-empty 'GeneratedCohorts[{cohortIndex}].Attributes[{index}].{nameof(Attribute)}'.");
            }

            if (Modulo <= 0)
            {
                throw new InvalidOperationException($"Entity query tactics generated cohort {cohortIndex} attribute pattern {index} requires modulo > 0.");
            }
        }

        public float Evaluate(int actorIndex)
        {
            return BaseValue + Step * (actorIndex % Modulo);
        }
    }

    public sealed class EntityQueryTacticsGeneratedRelationConfig
    {
        public string SourceName { get; set; } = string.Empty;
        public string Metric { get; set; } = string.Empty;
        public int BaseValue { get; set; }
        public int Step { get; set; }
        public int Modulo { get; set; } = 1;
        public string[] Flags { get; set; } = Array.Empty<string>();
        public int FlagEvery { get; set; } = 1;
        public int FlagOffset { get; set; }

        public void Validate(int cohortIndex, int index)
        {
            if (string.IsNullOrWhiteSpace(SourceName))
            {
                throw new InvalidOperationException(
                    $"Entity query tactics showcase config requires non-empty 'GeneratedCohorts[{cohortIndex}].Relations[{index}].{nameof(SourceName)}'.");
            }

            if (string.IsNullOrWhiteSpace(Metric))
            {
                throw new InvalidOperationException(
                    $"Entity query tactics showcase config requires non-empty 'GeneratedCohorts[{cohortIndex}].Relations[{index}].{nameof(Metric)}'.");
            }

            if (Modulo <= 0)
            {
                throw new InvalidOperationException($"Entity query tactics generated cohort {cohortIndex} relation pattern {index} requires modulo > 0.");
            }
        }

        public int Evaluate(int actorIndex)
        {
            return BaseValue + Step * (actorIndex % Modulo);
        }

        public bool ShouldApplyFlags(int actorIndex)
        {
            return FlagEvery <= 1 || ((actorIndex + FlagOffset) % FlagEvery) == 0;
        }
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
        public string Role { get; set; } = string.Empty;
    }
}
