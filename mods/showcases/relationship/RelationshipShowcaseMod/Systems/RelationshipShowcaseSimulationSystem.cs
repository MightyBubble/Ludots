using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using RelationshipShowcaseMod.Runtime;

namespace RelationshipShowcaseMod.Systems
{
    internal sealed class RelationshipShowcaseSimulationSystem : ISystem<float>
    {
        private static readonly QueryDescription MemberQuery = new QueryDescription().WithAll<Name, Team, MapEntity>();

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly RelationshipShowcaseScenarioState _state;
        private bool _scenarioReady;
        private int _loyaltyId;
        private int _supportId;
        private int _threatId;
        private int _trustedFlagId;
        private int _trustedTypeId;
        private int _hostilityTypeId;
        private int _oathTagId;
        private int _synergyTagId;
        private int _setupReasonId;
        private int _doctrineReasonId;
        private int _drillReasonId;
        private int _tauntReasonId;

        private RelationshipShowcaseConfig Config => _state.Config;
        private RelationshipShowcaseScenarioContext? ScenarioContext => _state.ScenarioContext;

        public RelationshipShowcaseSimulationSystem(GameEngine engine, RelationshipShowcaseScenarioState state)
        {
            _engine = engine;
            _world = engine.World;
            _state = state;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!IsShowcaseMap())
            {
                if (_scenarioReady)
                {
                    _scenarioReady = false;
                    _state.ResetScenarioContext();
                }

                return;
            }

            if (!EnsureScenarioReady())
            {
                return;
            }

            _state.AdvanceFrame();

            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
            {
                HandlePlayerInput(input);
            }

            if ((_state.Frame % Config.Behaviors.EnemyPressure.IntervalFrames) == 0)
            {
                ExecuteEnemyPressure();
            }

            RefreshDerivedState();
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private bool IsShowcaseMap()
        {
            return string.Equals(_engine.CurrentMapSession?.MapId.Value, Config.MapId, StringComparison.OrdinalIgnoreCase);
        }

        private bool EnsureScenarioReady()
        {
            if (_state.ScenarioContext != null)
            {
                _scenarioReady = true;
                return true;
            }

            if (!TryResolveScenarioContext(out RelationshipShowcaseScenarioContext? context))
            {
                return false;
            }

            InitializeIdentifiers();
            _state.SetScenarioContext(context);
            _state.SelectedHeroIndex = 0;
            _state.SelectedName = Config.Scenario.Heroes[0].Name;
            _state.AddLog(Config.Logs.ScenarioBootstrap);
            _engine.GlobalContext[RelationshipShowcaseIds.ScenarioKey] = context;

            InitializeEntities(context);
            SeedInitialMetrics();
            _scenarioReady = true;
            return true;
        }

        private void InitializeIdentifiers()
        {
            RelationshipTypeRegistry types = _engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("RelationshipTypeRegistry is missing.");
            RelationshipMetricRegistry metrics = _engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("RelationshipMetricRegistry is missing.");
            RelationshipFlagRegistry flags = _engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
                ?? throw new InvalidOperationException("RelationshipFlagRegistry is missing.");
            RelationshipReasonRegistry reasons = _engine.GetService(CoreServiceKeys.RelationshipReasonRegistry)
                ?? throw new InvalidOperationException("RelationshipReasonRegistry is missing.");

            _loyaltyId = metrics.GetId(Config.Metrics.Loyalty);
            _supportId = metrics.GetId(Config.Metrics.Support);
            _threatId = metrics.GetId(Config.Metrics.Threat);
            _trustedFlagId = flags.GetId(Config.Flags.Trusted);
            _trustedTypeId = types.GetId(Config.State.Trusted.Type);
            _hostilityTypeId = types.GetId(Config.Behaviors.EnemyPressure.RelationshipType);
            _oathTagId = TagRegistry.GetId(Config.Status.OathBondTag);
            _synergyTagId = TagRegistry.GetId(Config.Status.SynergyTag);
            _setupReasonId = reasons.Register(Config.Reasons.Setup);
            _doctrineReasonId = reasons.Register(Config.Reasons.Doctrine);
            _drillReasonId = reasons.Register(Config.Reasons.Drill);
            _tauntReasonId = reasons.Register(Config.Reasons.Taunt);
        }

        private void SeedInitialMetrics()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");

            foreach (RelationshipMetricSeedDefinition seed in Config.Seeds.InitialMetrics)
            {
                Entity source = ScenarioContext.GetEntityByName(seed.SourceName);
                Entity target = ScenarioContext.GetEntityByName(seed.TargetName);
                if (source == Entity.Null || target == Entity.Null)
                {
                    continue;
                }

                runtime.SetMetric(
                    source,
                    target,
                    ResolveTypeId(seed.Type),
                    ResolveMetricId(seed.Metric),
                    seed.Value,
                    ResolveReasonId(seed.Reason));
            }

            SeedThreats(runtime);
        }

        private void InitializeEntities(RelationshipShowcaseScenarioContext context)
        {
            for (int i = 0; i < context.Config.Scenario.Heroes.Length; i++)
            {
                PrepareEntity(context.HeroEntities[i], context.Config.Scenario.Heroes[i].Tags);
            }

            for (int i = 0; i < context.Config.Scenario.Enemies.Length; i++)
            {
                PrepareEntity(context.EnemyEntities[i], context.Config.Scenario.Enemies[i].Tags);
            }
        }

        private void SeedThreats(RelationshipRuntime runtime)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            foreach (EnemyDefinition enemyDefinition in Config.Scenario.Enemies)
            {
                Entity enemy = ScenarioContext.GetEntityByName(enemyDefinition.Name);
                if (enemy == Entity.Null)
                {
                    continue;
                }

                foreach (ThreatEntry threat in enemyDefinition.Threats)
                {
                    Entity target = ScenarioContext.GetEntityByName(threat.TargetName);
                    if (target == Entity.Null)
                    {
                        continue;
                    }

                    runtime.SetMetric(enemy, target, _hostilityTypeId, _threatId, threat.Value, _setupReasonId);
                }
            }
        }

        private bool TryResolveScenarioContext(out RelationshipShowcaseScenarioContext context)
        {
            HeroDefinition[] heroDefs = Config.Scenario.Heroes;
            EnemyDefinition[] enemyDefs = Config.Scenario.Enemies;
            var heroIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var enemyIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < heroDefs.Length; i++)
            {
                heroIndices[heroDefs[i].Name] = i;
            }

            for (int i = 0; i < enemyDefs.Length; i++)
            {
                enemyIndices[enemyDefs[i].Name] = i;
            }

            var heroes = new Entity[heroDefs.Length];
            var enemies = new Entity[enemyDefs.Length];
            var entityByName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

            _world.Query(in MemberQuery, (Entity entity, ref Name name, ref Team _, ref MapEntity mapEntity) =>
            {
                if (!IsMapMatch(mapEntity.MapId.Value) || string.IsNullOrWhiteSpace(name.Value))
                {
                    return;
                }

                entityByName.TryAdd(name.Value, entity);
                if (heroIndices.TryGetValue(name.Value, out int heroIdx))
                {
                    heroes[heroIdx] = entity;
                    return;
                }

                if (enemyIndices.TryGetValue(name.Value, out int enemyIdx))
                {
                    enemies[enemyIdx] = entity;
                }
            });

            if (heroes.Any(entity => entity == Entity.Null) || enemies.Any(entity => entity == Entity.Null))
            {
                context = default!;
                return false;
            }

            TeamEntityLookup teamLookup = _engine.GetService(CoreServiceKeys.TeamEntityLookup)
                ?? throw new InvalidOperationException("TeamEntityLookup is missing.");
            var teams = new Dictionary<int, Entity>();
            foreach (TeamDefinition team in Config.Scenario.Teams)
            {
                Entity teamEntity = teamLookup.Get(team.Id);
                if (teamEntity != Entity.Null)
                {
                    teams[team.Id] = teamEntity;
                }
            }

            context = new RelationshipShowcaseScenarioContext(Config, heroes, enemies, entityByName, teams);
            return true;
        }

        private bool IsMapMatch(string? mapId)
        {
            return string.Equals(mapId, Config.MapId, StringComparison.OrdinalIgnoreCase);
        }

        private void HandlePlayerInput(IInputActionReader input)
        {
            if (_state.HeroNames.Count == 0 || ScenarioContext == null)
            {
                return;
            }

            if (input.PressedThisFrame(Config.Actions.NextHero))
            {
                _state.SelectedHeroIndex = (_state.SelectedHeroIndex + 1) % _state.HeroNames.Count;
                _state.SelectedName = _state.HeroNames[_state.SelectedHeroIndex];
                _state.AddLog(string.Format(Config.Logs.SelectionRotated, _state.SelectedName));
            }

            if (input.PressedThisFrame(Config.Actions.Doctrine))
            {
                ExecuteDoctrine();
            }

            if (input.PressedThisFrame(Config.Actions.Drill))
            {
                ExecuteDrill();
            }

            if (input.PressedThisFrame(Config.Actions.Taunt))
            {
                ExecuteTaunt();
            }

            if (input.PressedThisFrame(Config.Actions.Rally))
            {
                ExecuteRally();
            }
        }

        private void ExecuteDoctrine()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            Entity leader = GetSelectedHero();
            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            EffectRequestQueue queue = _engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue is missing.");
            int doctrineEffectId = EffectTemplateIdRegistry.GetId(Config.Behaviors.Doctrine.EffectTemplate);
            int relationshipTypeId = ResolveTypeId(Config.Behaviors.Doctrine.RelationshipType);

            foreach (Entity ally in ScenarioContext.HeroEntities)
            {
                if (ally == leader)
                {
                    continue;
                }

                runtime.AddMetric(leader, ally, relationshipTypeId, _loyaltyId, Config.Behaviors.Doctrine.LoyaltyDelta, _doctrineReasonId);
                runtime.AddMetric(ally, leader, relationshipTypeId, _supportId, Config.Behaviors.Doctrine.ReciprocalSupportDelta, _doctrineReasonId);
                queue.Publish(new EffectRequest
                {
                    Source = leader,
                    Target = ally,
                    TargetContext = leader,
                    TemplateId = doctrineEffectId,
                });
            }

            _state.AddLog(string.Format(Config.Logs.Doctrine, ReadName(leader)));
        }

        private void ExecuteDrill()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            DrillBindingDefinition binding = ResolveDrillBinding();
            Entity source = ScenarioContext.GetEntityByName(binding.SourceName);
            Entity target = ScenarioContext.GetEntityByName(binding.TargetName);
            if (source == Entity.Null || target == Entity.Null)
            {
                return;
            }

            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            int relationshipTypeId = ResolveTypeId(Config.Behaviors.Drill.RelationshipType);
            runtime.AddMetric(source, target, relationshipTypeId, _supportId, Config.Behaviors.Drill.SupportDelta, _drillReasonId);
            runtime.AddMetric(target, source, relationshipTypeId, _supportId, Config.Behaviors.Drill.SupportDelta, _drillReasonId);
            _state.AddLog(string.Format(Config.Logs.Drill, ReadName(source), ReadName(target)));
        }

        private void ExecuteTaunt()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            Entity hero = GetSelectedHero();
            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            EffectRequestQueue queue = _engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue is missing.");
            int tauntEffectId = EffectTemplateIdRegistry.GetId(Config.Behaviors.Taunt.EffectTemplate);
            int relationshipTypeId = ResolveTypeId(Config.Behaviors.Taunt.RelationshipType);

            foreach (Entity enemy in ScenarioContext.EnemyEntities)
            {
                runtime.AddMetric(enemy, hero, relationshipTypeId, _threatId, Config.Behaviors.Taunt.ThreatDelta, _tauntReasonId);
            }

            queue.Publish(new EffectRequest
            {
                Source = hero,
                Target = hero,
                TargetContext = hero,
                TemplateId = tauntEffectId,
            });
            _state.AddLog(string.Format(Config.Logs.Taunt, ReadName(hero)));
        }

        private void ExecuteRally()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            if (!_state.TrustedUnlocked || !_state.OathBondUnlocked || !_state.SynergyActive)
            {
                _state.AddLog(Config.Logs.RallyDenied);
                return;
            }

            Entity source = GetSelectedHero();
            EffectRequestQueue queue = _engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue is missing.");
            int rallyEffectId = EffectTemplateIdRegistry.GetId(Config.Behaviors.Rally.EffectTemplate);

            foreach (Entity hero in ScenarioContext.HeroEntities)
            {
                queue.Publish(new EffectRequest
                {
                    Source = source,
                    Target = hero,
                    TargetContext = source,
                    TemplateId = rallyEffectId,
                });
            }

            _state.AddLog(string.Format(Config.Logs.Rally, ReadName(source)));
        }

        private void ExecuteEnemyPressure()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            EffectRequestQueue queue = _engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue is missing.");
            int strikeEffectId = EffectTemplateIdRegistry.GetId(Config.Behaviors.EnemyPressure.EffectTemplate);

            foreach (Entity enemy in ScenarioContext.EnemyEntities)
            {
                if (!runtime.TryGetHighestMetricTarget(
                        enemy,
                        ScenarioContext.HeroEntities.ToArray(),
                        _hostilityTypeId,
                        ResolveMetricId(Config.Behaviors.EnemyPressure.MetricId),
                        out Entity focus,
                        out short threat))
                {
                    continue;
                }

                queue.Publish(new EffectRequest
                {
                    Source = enemy,
                    Target = focus,
                    TargetContext = enemy,
                    TemplateId = strikeEffectId,
                });

                _state.EnemyFocusName = ReadName(focus);
                _state.AddLog(string.Format(Config.Logs.Pressure, ReadName(enemy), _state.EnemyFocusName, threat));
            }
        }

        private void RefreshDerivedState()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            _state.SelectedName = Config.Scenario.Heroes[Math.Clamp(_state.SelectedHeroIndex, 0, ScenarioContext.HeroEntities.Count - 1)].Name;

            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            _state.TrustedUnlocked = CountTrustedTargets(runtime) >= Config.State.Trusted.MinimumMatches;
            _state.OathBondUnlocked = CountTaggedEntities(Config.State.OathBond.EntityNames, _oathTagId) >= Config.State.OathBond.MinimumTaggedCount;
            _state.SynergyActive = EntityHasTag(ScenarioContext.GetTeamById(Config.State.Synergy.TeamId), _synergyTagId);

            if (string.IsNullOrWhiteSpace(_state.EnemyFocusName))
            {
                _state.EnemyFocusName = Config.Behaviors.EnemyPressure.EmptyFocusText;
            }
        }

        private Entity GetSelectedHero()
        {
            if (ScenarioContext == null || ScenarioContext.HeroEntities.Count == 0)
            {
                return Entity.Null;
            }

            return ScenarioContext.HeroEntities[Math.Clamp(_state.SelectedHeroIndex, 0, ScenarioContext.HeroEntities.Count - 1)];
        }

        private string ReadName(Entity entity)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<Name>(entity))
            {
                return $"Entity#{entity.Id}";
            }

            return _world.Get<Name>(entity).Value;
        }

        private bool EntityHasTag(Entity entity, int tagId)
        {
            return tagId > 0 &&
                   entity != Entity.Null &&
                   _world.IsAlive(entity) &&
                   _world.Has<GameplayTagContainer>(entity) &&
                   _world.Get<GameplayTagContainer>(entity).HasTag(tagId);
        }

        private void PrepareEntity(Entity entity, IEnumerable<string> tags)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity))
            {
                return;
            }

            if (!_world.Has<GameplayTagContainer>(entity))
            {
                entity.Add(new GameplayTagContainer());
            }

            if (!_world.Has<TagCountContainer>(entity))
            {
                entity.Add(new TagCountContainer());
            }

            ref GameplayTagContainer tagContainer = ref _world.Get<GameplayTagContainer>(entity);
            ref TagCountContainer tagCounts = ref _world.Get<TagCountContainer>(entity);
            TagOps tagOps = _engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps is missing.");

            foreach (string tag in tags)
            {
                int tagId = TagRegistry.Register(tag);
                tagOps.AddTag(ref tagContainer, ref tagCounts, tagId);
            }
        }

        private DrillBindingDefinition ResolveDrillBinding()
        {
            string selectedName = _state.SelectedName;
            foreach (DrillBindingDefinition binding in Config.Actions.DrillBindings)
            {
                if (string.Equals(binding.SelectedName, selectedName, StringComparison.OrdinalIgnoreCase))
                {
                    return binding;
                }
            }

            throw new InvalidOperationException($"Relationship showcase config does not define a drill binding for selected hero '{selectedName}'.");
        }

        private int CountTrustedTargets(RelationshipRuntime runtime)
        {
            if (ScenarioContext == null)
            {
                return 0;
            }

            Entity source = ScenarioContext.GetEntityByName(Config.State.Trusted.SourceName);
            if (source == Entity.Null)
            {
                return 0;
            }

            int count = 0;
            foreach (string targetName in Config.State.Trusted.TargetNames)
            {
                Entity target = ScenarioContext.GetEntityByName(targetName);
                if (target != Entity.Null && runtime.HasFlag(source, target, _trustedTypeId, _trustedFlagId))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountTaggedEntities(IEnumerable<string> names, int tagId)
        {
            if (ScenarioContext == null)
            {
                return 0;
            }

            int count = 0;
            foreach (string name in names)
            {
                if (EntityHasTag(ScenarioContext.GetEntityByName(name), tagId))
                {
                    count++;
                }
            }

            return count;
        }

        private int ResolveMetricId(string metricName)
        {
            if (string.Equals(metricName, Config.Metrics.Loyalty, StringComparison.Ordinal))
            {
                return _loyaltyId;
            }

            if (string.Equals(metricName, Config.Metrics.Support, StringComparison.Ordinal))
            {
                return _supportId;
            }

            if (string.Equals(metricName, Config.Metrics.Threat, StringComparison.Ordinal))
            {
                return _threatId;
            }

            RelationshipMetricRegistry metrics = _engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("RelationshipMetricRegistry is missing.");
            return metrics.GetId(metricName);
        }

        private int ResolveReasonId(string reasonName)
        {
            if (string.Equals(reasonName, Config.Reasons.Setup, StringComparison.Ordinal))
            {
                return _setupReasonId;
            }

            if (string.Equals(reasonName, Config.Reasons.Doctrine, StringComparison.Ordinal))
            {
                return _doctrineReasonId;
            }

            if (string.Equals(reasonName, Config.Reasons.Drill, StringComparison.Ordinal))
            {
                return _drillReasonId;
            }

            if (string.Equals(reasonName, Config.Reasons.Taunt, StringComparison.Ordinal))
            {
                return _tauntReasonId;
            }

            RelationshipReasonRegistry reasons = _engine.GetService(CoreServiceKeys.RelationshipReasonRegistry)
                ?? throw new InvalidOperationException("RelationshipReasonRegistry is missing.");
            return reasons.Register(reasonName);
        }

        private int ResolveTypeId(string typeName)
        {
            if (string.Equals(typeName, Config.State.Trusted.Type, StringComparison.Ordinal))
            {
                return _trustedTypeId;
            }

            RelationshipTypeRegistry types = _engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("RelationshipTypeRegistry is missing.");
            return types.GetId(typeName);
        }
    }
}
