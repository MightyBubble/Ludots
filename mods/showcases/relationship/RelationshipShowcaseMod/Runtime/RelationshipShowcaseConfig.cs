using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RelationshipShowcaseMod.Runtime
{
    public sealed class RelationshipShowcaseConfig
    {
        public string MapId { get; set; } = string.Empty;
        public int SynergyTeamId { get; set; }
        public RelationshipScenarioDefinition Scenario { get; set; } = new();
        public RelationshipActionConfig Actions { get; set; } = new();
        public RelationshipEventConfig Events { get; set; } = new();
        public RelationshipTypeNameConfig Types { get; set; } = new();
        public RelationshipFlagConfig Flags { get; set; } = new();
        public RelationshipTagConfig Tags { get; set; } = new();
        public RelationshipEffectConfig Effects { get; set; } = new();
        public RelationshipMetricConfig Metrics { get; set; } = new();
        public RelationshipReasonConfig Reasons { get; set; } = new();
        public RelationshipSeedConfig Seeds { get; set; } = new();
        public RelationshipSeedConfig Seed
        {
            get => Seeds;
            set => Seeds = value ?? new RelationshipSeedConfig();
        }
        public RelationshipStatusConfig Status { get; set; } = new();
        public RelationshipStateConfig State { get; set; } = new();
        public RelationshipBehaviorConfig Behaviors { get; set; } = new();
        public RelationshipUiConfig Ui { get; set; } = new();
        public RelationshipPresentationConfig Presentation { get; set; } = new();
        public TeamRelationConfig[] TeamRelations { get; set; } = Array.Empty<TeamRelationConfig>();
        public RelationshipLogConfig Logs { get; set; } = new();

        public static RelationshipShowcaseConfig Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            RelationshipShowcaseConfig? config = JsonSerializer.Deserialize<RelationshipShowcaseConfig>(stream, options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize relationship showcase config.");
            }

            config.Validate();
            return config;
        }

        public IEnumerable<string> HeroNames => Scenario.Heroes.Select(h => h.Name);
        public IEnumerable<string> EnemyNames => Scenario.Enemies.Select(e => e.Name);
        public string[] HeroNamesArray() => HeroNames.ToArray();

        private void Validate()
        {
            Require(MapId, nameof(MapId));
            RequireScenario();
            RequireActionConfig();
            RequireEventConfig();
            RequireTypeConfig();
            RequireFlagConfig();
            RequireTagConfig();
            RequireEffectConfig();
            RequireMetricConfig();
            RequireReasonConfig();
            RequireBehaviorConfig();
            RequireStatusConfig();
            RequireStateConfig();
            RequireUiConfig();
            RequirePresentationConfig();
            RequireLogs();
            RequireSeeds();
        }

        private void RequireScenario()
        {
            if (Scenario.Teams.Length == 0)
            {
                throw new InvalidOperationException("Relationship showcase config requires at least one team.");
            }

            if (Scenario.Heroes.Length == 0)
            {
                throw new InvalidOperationException("Relationship showcase config requires at least one hero.");
            }

            if (Scenario.Enemies.Length == 0)
            {
                throw new InvalidOperationException("Relationship showcase config requires at least one enemy.");
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ActorDefinition actor in Scenario.Heroes.Cast<ActorDefinition>().Concat(Scenario.Enemies))
            {
                Require(actor.Name, nameof(actor.Name));
                if (!names.Add(actor.Name))
                {
                    throw new InvalidOperationException($"Relationship showcase config contains duplicate actor name '{actor.Name}'.");
                }
            }
        }

        private void RequireActionConfig()
        {
            Require(Actions.NextHero, nameof(Actions.NextHero));
            Require(Actions.Doctrine, nameof(Actions.Doctrine));
            Require(Actions.Drill, nameof(Actions.Drill));
            Require(Actions.Taunt, nameof(Actions.Taunt));
            Require(Actions.Rally, nameof(Actions.Rally));
        }

        private void RequireEventConfig()
        {
            Require(Events.TrustedUnlocked, nameof(Events.TrustedUnlocked));
            Require(Events.OathBondUnlocked, nameof(Events.OathBondUnlocked));
            Require(Events.SynergyActivated, nameof(Events.SynergyActivated));
            Require(Events.FocusLocked, nameof(Events.FocusLocked));
        }

        private void RequireTypeConfig()
        {
            Require(Types.SocialBond, nameof(Types.SocialBond));
            Require(Types.Hostility, nameof(Types.Hostility));
        }

        private void RequireFlagConfig()
        {
            Require(Flags.Trusted, nameof(Flags.Trusted));
        }

        private void RequireTagConfig()
        {
            Require(Tags.OathBond, nameof(Tags.OathBond));
            Require(Tags.Synergy, nameof(Tags.Synergy));
            Require(Tags.FocusedByEnemy, nameof(Tags.FocusedByEnemy));
        }

        private void RequireEffectConfig()
        {
            Require(Effects.BenevolencePulse, nameof(Effects.BenevolencePulse));
            Require(Effects.TrustedGuard, nameof(Effects.TrustedGuard));
            Require(Effects.OathSpark, nameof(Effects.OathSpark));
            Require(Effects.TauntGuard, nameof(Effects.TauntGuard));
            Require(Effects.RallyBanner, nameof(Effects.RallyBanner));
            Require(Effects.EnemyStrike, nameof(Effects.EnemyStrike));
        }

        private void RequireMetricConfig()
        {
            Require(Metrics.Loyalty, nameof(Metrics.Loyalty));
            Require(Metrics.Support, nameof(Metrics.Support));
            Require(Metrics.Threat, nameof(Metrics.Threat));
        }

        private void RequireReasonConfig()
        {
            Require(Reasons.Setup, nameof(Reasons.Setup));
            Require(Reasons.Doctrine, nameof(Reasons.Doctrine));
            Require(Reasons.Drill, nameof(Reasons.Drill));
            Require(Reasons.Taunt, nameof(Reasons.Taunt));
        }

        private void RequireSeeds()
        {
            if (Seeds.InitialMetrics.Length == 0)
            {
                throw new InvalidOperationException("Relationship showcase config requires seed metrics.");
            }
        }

        private void RequireBehaviorConfig()
        {
            Require(Behaviors.Doctrine.RelationshipType, nameof(Behaviors.Doctrine.RelationshipType));
            Require(Behaviors.Doctrine.EffectTemplate, nameof(Behaviors.Doctrine.EffectTemplate));
            Require(Behaviors.Drill.RelationshipType, nameof(Behaviors.Drill.RelationshipType));
            Require(Behaviors.Drill.SourceName, nameof(Behaviors.Drill.SourceName));
            Require(Behaviors.Drill.TargetName, nameof(Behaviors.Drill.TargetName));
            Require(Behaviors.Taunt.RelationshipType, nameof(Behaviors.Taunt.RelationshipType));
            Require(Behaviors.Taunt.EffectTemplate, nameof(Behaviors.Taunt.EffectTemplate));
            Require(Behaviors.Rally.EffectTemplate, nameof(Behaviors.Rally.EffectTemplate));
            Require(Behaviors.EnemyPressure.RelationshipType, nameof(Behaviors.EnemyPressure.RelationshipType));
            Require(Behaviors.EnemyPressure.MetricId, nameof(Behaviors.EnemyPressure.MetricId));
            Require(Behaviors.EnemyPressure.EffectTemplate, nameof(Behaviors.EnemyPressure.EffectTemplate));
            Require(Behaviors.EnemyPressure.EmptyFocusText, nameof(Behaviors.EnemyPressure.EmptyFocusText));
            Require(Actions.DrillBindings, nameof(Actions.DrillBindings));
            if (Behaviors.EnemyPressure.IntervalFrames <= 0)
            {
                throw new InvalidOperationException("Relationship showcase config requires Behaviors.EnemyPressure.IntervalFrames > 0.");
            }
        }

        private void RequireStateConfig()
        {
            Require(State.Trusted.SourceName, nameof(State.Trusted.SourceName));
            Require(State.Trusted.Type, nameof(State.Trusted.Type));
            if (State.Trusted.TargetNames.Length == 0 || State.Trusted.MinimumMatches <= 0)
            {
                throw new InvalidOperationException("Relationship showcase config requires trusted state thresholds.");
            }

            if (State.OathBond.EntityNames.Length == 0 || State.OathBond.MinimumTaggedCount <= 0)
            {
                throw new InvalidOperationException("Relationship showcase config requires oath-bond state thresholds.");
            }
        }

        private void RequireStatusConfig()
        {
            Require(Status.OathBondTag, nameof(Status.OathBondTag));
            Require(Status.SynergyTag, nameof(Status.SynergyTag));
            Require(Status.TrustedRelationshipType, nameof(Status.TrustedRelationshipType));
            if (Status.TrustedPairs.Length == 0)
            {
                throw new InvalidOperationException("Relationship showcase config requires trusted status pairs.");
            }

            if (Status.OathBondMembers.Length == 0)
            {
                throw new InvalidOperationException("Relationship showcase config requires oath-bond status members.");
            }
        }

        private void RequireUiConfig()
        {
            Require(Ui.ControlsLine, nameof(Ui.ControlsLine));
            Require(Ui.CoverageLine, nameof(Ui.CoverageLine));
        }

        private void RequirePresentationConfig()
        {
            Require(Presentation.TitlePrefix, nameof(Presentation.TitlePrefix));
            Require(Presentation.SelectedHeroLabel, nameof(Presentation.SelectedHeroLabel));
            Require(Presentation.EnemyFocusLabel, nameof(Presentation.EnemyFocusLabel));
            Require(Presentation.TrustedLabel, nameof(Presentation.TrustedLabel));
            Require(Presentation.OathBondLabel, nameof(Presentation.OathBondLabel));
            Require(Presentation.SynergyLabel, nameof(Presentation.SynergyLabel));
            Require(Presentation.BattleLogTitle, nameof(Presentation.BattleLogTitle));
            Require(Presentation.MetricsPendingText, nameof(Presentation.MetricsPendingText));
            Require(Presentation.EnemyFocusPendingText, nameof(Presentation.EnemyFocusPendingText));
            Require(Presentation.ReadyText, nameof(Presentation.ReadyText));
            Require(Presentation.LockedText, nameof(Presentation.LockedText));
            Require(Presentation.Metrics.LoyaltyPairs, nameof(Presentation.Metrics.LoyaltyPairs));
            Require(Presentation.Metrics.SupportPairs, nameof(Presentation.Metrics.SupportPairs));
            Require(Presentation.Metrics.ThreatSourceName, nameof(Presentation.Metrics.ThreatSourceName));
            if (Presentation.Metrics.LoyaltyPairs.Length < 2)
            {
                throw new InvalidOperationException("Relationship showcase config requires at least two loyalty display pairs.");
            }
        }

        private void RequireLogs()
        {
            Require(Logs.ScenarioBootstrap, nameof(Logs.ScenarioBootstrap));
            Require(Logs.SelectionRotated, nameof(Logs.SelectionRotated));
            Require(Logs.Doctrine, nameof(Logs.Doctrine));
            Require(Logs.Drill, nameof(Logs.Drill));
            Require(Logs.Taunt, nameof(Logs.Taunt));
            Require(Logs.Rally, nameof(Logs.Rally));
            Require(Logs.RallyDenied, nameof(Logs.RallyDenied));
            Require(Logs.Pressure, nameof(Logs.Pressure));
            Require(Logs.TriggerTrustedUnlocked, nameof(Logs.TriggerTrustedUnlocked));
            Require(Logs.TriggerOathBondUnlocked, nameof(Logs.TriggerOathBondUnlocked));
            Require(Logs.TriggerSynergyActivated, nameof(Logs.TriggerSynergyActivated));
            Require(Logs.TriggerFocusLocked, nameof(Logs.TriggerFocusLocked));
            Require(Logs.SystemInstalled, nameof(Logs.SystemInstalled));
        }

        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Relationship showcase config requires non-empty '{name}'.");
            }
        }

        private static void Require<T>(T[] values, string name)
        {
            if (values == null || values.Length == 0)
            {
                throw new InvalidOperationException($"Relationship showcase config requires non-empty '{name}'.");
            }
        }
    }

    public sealed class RelationshipScenarioDefinition
    {
        public TeamDefinition[] Teams { get; set; } = Array.Empty<TeamDefinition>();
        public HeroDefinition[] Heroes { get; set; } = Array.Empty<HeroDefinition>();
        public EnemyDefinition[] Enemies { get; set; } = Array.Empty<EnemyDefinition>();
    }

    public sealed class TeamDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ActorDefinition
    {
        public string Name { get; set; } = string.Empty;
        public int TeamId { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    public sealed class HeroDefinition : ActorDefinition
    {
    }

    public sealed class EnemyDefinition : ActorDefinition
    {
        public ThreatEntry[] Threats { get; set; } = Array.Empty<ThreatEntry>();
    }

    public sealed class ThreatEntry
    {
        public string TargetName { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public sealed class RelationshipActionConfig
    {
        public string NextHero { get; set; } = string.Empty;
        public string Doctrine { get; set; } = string.Empty;
        public string Drill { get; set; } = string.Empty;
        public string Taunt { get; set; } = string.Empty;
        public string Rally { get; set; } = string.Empty;
        public DrillBindingDefinition[] DrillBindings { get; set; } = Array.Empty<DrillBindingDefinition>();
    }

    public sealed class DrillBindingDefinition
    {
        public string SelectedName { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
    }

    public sealed class RelationshipEventConfig
    {
        public string TrustedUnlocked { get; set; } = string.Empty;
        public string OathBondUnlocked { get; set; } = string.Empty;
        public string SynergyActivated { get; set; } = string.Empty;
        public string FocusLocked { get; set; } = string.Empty;
    }

    public sealed class RelationshipTypeNameConfig
    {
        public string SocialBond { get; set; } = string.Empty;
        public string Hostility { get; set; } = string.Empty;
    }

    public sealed class RelationshipFlagConfig
    {
        public string Trusted { get; set; } = string.Empty;
    }

    public sealed class RelationshipTagConfig
    {
        public string OathBond { get; set; } = string.Empty;
        public string Synergy { get; set; } = string.Empty;
        public string FocusedByEnemy { get; set; } = string.Empty;
    }

    public sealed class RelationshipEffectConfig
    {
        public string BenevolencePulse { get; set; } = string.Empty;
        public string TrustedGuard { get; set; } = string.Empty;
        public string OathSpark { get; set; } = string.Empty;
        public string TauntGuard { get; set; } = string.Empty;
        public string RallyBanner { get; set; } = string.Empty;
        public string EnemyStrike { get; set; } = string.Empty;
    }

    public sealed class RelationshipMetricConfig
    {
        public string Loyalty { get; set; } = string.Empty;
        public string Support { get; set; } = string.Empty;
        public string Threat { get; set; } = string.Empty;
    }

    public sealed class RelationshipReasonConfig
    {
        public string Setup { get; set; } = string.Empty;
        public string Doctrine { get; set; } = string.Empty;
        public string Drill { get; set; } = string.Empty;
        public string Taunt { get; set; } = string.Empty;
    }

    public sealed class RelationshipSeedConfig
    {
        public RelationshipMetricSeedDefinition[] Metrics { get; set; } = Array.Empty<RelationshipMetricSeedDefinition>();

        public RelationshipMetricSeedDefinition[] InitialMetrics
        {
            get => Metrics;
            set => Metrics = value ?? Array.Empty<RelationshipMetricSeedDefinition>();
        }
    }

    public sealed class RelationshipMetricSeedDefinition
    {
        public string SourceName { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Metric { get; set; } = string.Empty;
        public int Value { get; set; }
        public string Reason { get; set; } = string.Empty;

        public string ReasonId
        {
            get => Reason;
            set => Reason = value ?? string.Empty;
        }
    }

    public sealed class RelationshipStatusConfig
    {
        public string OathBondTag { get; set; } = string.Empty;
        public string SynergyTag { get; set; } = string.Empty;
        public string TrustedRelationshipType { get; set; } = string.Empty;
        public RelationshipPairRefConfig[] TrustedPairs { get; set; } = Array.Empty<RelationshipPairRefConfig>();
        public string[] OathBondMembers { get; set; } = Array.Empty<string>();
    }

    public sealed class RelationshipStateConfig
    {
        public TrustedStateRule Trusted { get; set; } = new();
        public OathBondStateRule OathBond { get; set; } = new();
        public SynergyStateRule Synergy { get; set; } = new();
    }

    public sealed class TrustedStateRule
    {
        public string SourceName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string[] TargetNames { get; set; } = Array.Empty<string>();
        public int MinimumMatches { get; set; }
    }

    public sealed class OathBondStateRule
    {
        public string[] EntityNames { get; set; } = Array.Empty<string>();
        public int MinimumTaggedCount { get; set; }
    }

    public sealed class SynergyStateRule
    {
        public int TeamId { get; set; }
    }

    public sealed class RelationshipPairRefConfig
    {
        public string SourceName { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
    }

    public sealed class RelationshipPresentationConfig
    {
        public string TitlePrefix { get; set; } = string.Empty;
        public string SelectedHeroLabel { get; set; } = string.Empty;
        public string EnemyFocusLabel { get; set; } = string.Empty;
        public string TrustedLabel { get; set; } = string.Empty;
        public string OathBondLabel { get; set; } = string.Empty;
        public string SynergyLabel { get; set; } = string.Empty;
        public string ControlsLine { get; set; } = string.Empty;
        public string CoverageLine { get; set; } = string.Empty;
        public string BattleLogTitle { get; set; } = string.Empty;
        public string MetricsPendingText { get; set; } = string.Empty;
        public string EnemyFocusPendingText { get; set; } = string.Empty;
        public string ReadyText { get; set; } = string.Empty;
        public string LockedText { get; set; } = string.Empty;
        public RelationshipMetricDisplayConfig Metrics { get; set; } = new();
    }

    public sealed class RelationshipUiConfig
    {
        public string ControlsLine { get; set; } = string.Empty;
        public string CoverageLine { get; set; } = string.Empty;
    }

    public sealed class RelationshipBehaviorConfig
    {
        public DoctrineBehaviorConfig Doctrine { get; set; } = new();
        public DrillBehaviorConfig Drill { get; set; } = new();
        public TauntBehaviorConfig Taunt { get; set; } = new();
        public RallyBehaviorConfig Rally { get; set; } = new();
        public EnemyPressureBehaviorConfig EnemyPressure { get; set; } = new();
    }

    public sealed class DoctrineBehaviorConfig
    {
        public string RelationshipType { get; set; } = string.Empty;
        public int LoyaltyDelta { get; set; }
        public int ReciprocalSupportDelta { get; set; }
        public string EffectTemplate { get; set; } = string.Empty;
    }

    public sealed class DrillBehaviorConfig
    {
        public string RelationshipType { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public int SupportDelta { get; set; }
    }

    public sealed class TauntBehaviorConfig
    {
        public string RelationshipType { get; set; } = string.Empty;
        public int ThreatDelta { get; set; }
        public string EffectTemplate { get; set; } = string.Empty;
    }

    public sealed class RallyBehaviorConfig
    {
        public string EffectTemplate { get; set; } = string.Empty;
    }

    public sealed class EnemyPressureBehaviorConfig
    {
        public string RelationshipType { get; set; } = string.Empty;
        public string MetricId { get; set; } = string.Empty;
        public int IntervalFrames { get; set; }
        public string EffectTemplate { get; set; } = string.Empty;
        public string EmptyFocusText { get; set; } = string.Empty;
    }

    public sealed class RelationshipMetricDisplayConfig
    {
        public RelationshipNamedPair[] LoyaltyPairs { get; set; } = Array.Empty<RelationshipNamedPair>();
        public RelationshipNamedPair[] SupportPairs { get; set; } = Array.Empty<RelationshipNamedPair>();
        public string ThreatSourceName { get; set; } = string.Empty;
    }

    public sealed class RelationshipNamedPair
    {
        public string SourceName { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
    }

    public sealed class TeamRelationConfig
    {
        public int TeamA { get; set; }
        public int TeamB { get; set; }
        public string Relationship { get; set; } = string.Empty;
    }

    public sealed class RelationshipLogConfig
    {
        public string ScenarioBootstrap { get; set; } = string.Empty;
        public string SelectionRotated { get; set; } = string.Empty;
        public string Doctrine { get; set; } = string.Empty;
        public string Drill { get; set; } = string.Empty;
        public string Taunt { get; set; } = string.Empty;
        public string Rally { get; set; } = string.Empty;
        public string RallyDenied { get; set; } = string.Empty;
        public string Pressure { get; set; } = string.Empty;
        public string TriggerTrustedUnlocked { get; set; } = string.Empty;
        public string TriggerOathBondUnlocked { get; set; } = string.Empty;
        public string TriggerSynergyActivated { get; set; } = string.Empty;
        public string TriggerFocusLocked { get; set; } = string.Empty;
        public string SystemInstalled { get; set; } = string.Empty;
    }
}
