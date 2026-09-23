using System;
using System.Collections.Generic;
using System.Text;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using EntityQueryTacticsShowcaseMod.Runtime;

namespace EntityQueryTacticsShowcaseMod.Systems
{
    internal sealed class EntityQueryTacticsSimulationSystem : ISystem<float>
    {
        private static readonly QueryDescription NamedMapEntityQuery = new QueryDescription().WithAll<Name, MapEntity>();
        private const int CollectionNamePreviewCount = 8;

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly EntityQueryTacticsScenarioState _state;
        private readonly Entity[] _selectionScratch;
        private readonly Entity[] _collectionScratch;
        private readonly Entity[] _formationScratch;
        private readonly EntityCollectionRowFlags[] _rowFlags;
        private IGraphRuntimeApi? _graphApi;

        private bool _scenarioReady;
        private bool _generatedPlansBuilt;
        private bool _generatedSpawnEnqueued;
        private int _runtimeSpawnReceiptChannelId;
        private int _generatedReceiptsBound;
        private int _generatedReceiptCount;
        private EntityQueryTacticsGeneratedActorPlan[] _generatedPlans = Array.Empty<EntityQueryTacticsGeneratedActorPlan>();
        private EntityQueryTacticsActorConfig[] _resolvedAlliesConfig = Array.Empty<EntityQueryTacticsActorConfig>();
        private EntityQueryTacticsActorConfig[] _resolvedEnemiesConfig = Array.Empty<EntityQueryTacticsActorConfig>();
        private EntityQueryTacticsActorConfig[] _resolvedObjectivesConfig = Array.Empty<EntityQueryTacticsActorConfig>();
        private int _tacticalIntelTypeId;
        private int _threatMetricId;
        private int _focusMetricId;
        private int _priorityTargetFlagId;
        private int _setupReasonId;
        private int _pressurePulseReasonId;
        private int _commandableTagId;
        private int _routedTagId;
        private int _objectiveTagId;
        private int _commandPowerAttributeId;
        private int _supplyAttributeId;
        private int _threatValueAttributeId;
        private int _selectedFriendliesGraphId;
        private int _hostileThreatsGraphId;
        private int _formationCacheGraphId;
        private uint _randomSeed = 0xEC51A11u;
        private uint _lastSyncedLiveSelectionRevision;
        private uint _lastMirroredLiveSelectionRevision;
        private bool _formalSelectionMirrorReady;
        private string _uiBoxNames = string.Empty;
        private string _formalSelectionNames = string.Empty;
        private string _formationInputNames = string.Empty;
        private string _formationResultNames = string.Empty;
        private string _hostileThreatNames = string.Empty;
        private int _nextDemoStepIndex;
        private uint _lastPressureTimingPulseCount;
        private int _pressureTimingSamplesRemaining;
        private bool _demoPlaybackEnabled;

        private EntityQueryTacticsShowcaseConfig Config => _state.Config;
        private EntityQueryTacticsScenarioContext? ScenarioContext => _state.ScenarioContext;

        public EntityQueryTacticsSimulationSystem(GameEngine engine, EntityQueryTacticsScenarioState state)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _world = engine.World;
            _state = state ?? throw new ArgumentNullException(nameof(state));
            int scratchCapacity = Math.Max(64, _state.Config.Scenario.TotalActorCount + 8);
            _selectionScratch = new Entity[scratchCapacity];
            _collectionScratch = new Entity[scratchCapacity];
            _formationScratch = new Entity[scratchCapacity];
            _rowFlags = new EntityCollectionRowFlags[scratchCapacity];
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
            MirrorFormalSelectionToCollection();
            MaintainFormationSnapshotFromSelectionChange();
            RunDemoPlayback();

            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
            {
                HandlePlayerInput(input);
            }

            RefreshScenarioState();
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
            if (ScenarioContext != null)
            {
                _scenarioReady = true;
                return true;
            }

            InitializeIdentifiers();
            EnsureGeneratedPlansBuilt();
            if (!EnsureGeneratedActorsReady())
            {
                return false;
            }

            if (!TryResolveScenarioContext(out EntityQueryTacticsScenarioContext? context))
            {
                return false;
            }

            PrepareEntities(context);
            SeedRelationshipRuntime(context);
            BindSelectionRuntime(context.Owner);
            _state.SetScenarioContext(context);
            _engine.GlobalContext[EntityQueryTacticsShowcaseIds.ScenarioKey] = context;
            _state.AddLog(Config.Logs.ScenarioReady);
            _demoPlaybackEnabled = IsDemoPlaybackEnabled();
            _nextDemoStepIndex = 0;

            MirrorFormalSelectionToCollection(force: true);
            MaintainFormationSnapshotFromSelectionChange(force: true);
            ExecuteGraphs();
            RefreshScenarioState();
            _scenarioReady = true;
            return true;
        }

        private bool TryResolveScenarioContext(out EntityQueryTacticsScenarioContext context)
        {
            EntityQueryTacticsActorConfig[] alliesConfig = _resolvedAlliesConfig.Length == 0 ? Config.Scenario.Allies : _resolvedAlliesConfig;
            EntityQueryTacticsActorConfig[] enemiesConfig = _resolvedEnemiesConfig.Length == 0 ? Config.Scenario.Enemies : _resolvedEnemiesConfig;
            EntityQueryTacticsActorConfig[] objectivesConfig = _resolvedObjectivesConfig.Length == 0 ? Config.Scenario.Objectives : _resolvedObjectivesConfig;
            var allNames = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
            var allies = new Entity[alliesConfig.Length];
            var enemies = new Entity[enemiesConfig.Length];
            var objectives = new Entity[objectivesConfig.Length];
            Entity owner = Entity.Null;
            Entity enemyCommander = Entity.Null;

            _world.Query(in NamedMapEntityQuery, (Entity entity, ref Name name, ref MapEntity mapEntity) =>
            {
                if (!IsMapMatch(mapEntity.MapId) || string.IsNullOrWhiteSpace(name.Value))
                {
                    return;
                }

                allNames.TryAdd(name.Value, entity);
                if (string.Equals(name.Value, Config.Scenario.PlayerCommanderName, StringComparison.OrdinalIgnoreCase))
                {
                    owner = entity;
                    return;
                }

                if (string.Equals(name.Value, Config.Scenario.EnemyCommanderName, StringComparison.OrdinalIgnoreCase))
                {
                    enemyCommander = entity;
                    return;
                }

                for (int i = 0; i < alliesConfig.Length; i++)
                {
                    if (string.Equals(name.Value, alliesConfig[i].Name, StringComparison.OrdinalIgnoreCase))
                    {
                        allies[i] = entity;
                        return;
                    }
                }

                for (int i = 0; i < enemiesConfig.Length; i++)
                {
                    if (string.Equals(name.Value, enemiesConfig[i].Name, StringComparison.OrdinalIgnoreCase))
                    {
                        enemies[i] = entity;
                        return;
                    }
                }

                for (int i = 0; i < objectivesConfig.Length; i++)
                {
                    if (string.Equals(name.Value, objectivesConfig[i].Name, StringComparison.OrdinalIgnoreCase))
                    {
                        objectives[i] = entity;
                        return;
                    }
                }
            });

            if (owner == Entity.Null ||
                enemyCommander == Entity.Null ||
                ContainsNull(allies) ||
                ContainsNull(enemies) ||
                ContainsNull(objectives))
            {
                context = default!;
                return false;
            }

            context = new EntityQueryTacticsScenarioContext(owner, enemyCommander, allies, enemies, objectives, allNames);
            return true;
        }

        private bool IsMapMatch(MapId mapId)
        {
            return string.Equals(mapId.Value, Config.MapId, StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureGeneratedPlansBuilt()
        {
            if (_generatedPlansBuilt)
            {
                return;
            }

            int generatedCount = Config.Scenario.CountGeneratedActors(EntityQueryTacticsGeneratedActorRoles.Ally) +
                                 Config.Scenario.CountGeneratedActors(EntityQueryTacticsGeneratedActorRoles.Enemy) +
                                 Config.Scenario.CountGeneratedActors(EntityQueryTacticsGeneratedActorRoles.Objective);
            _generatedPlans = generatedCount == 0
                ? Array.Empty<EntityQueryTacticsGeneratedActorPlan>()
                : new EntityQueryTacticsGeneratedActorPlan[generatedCount];

            var allies = new EntityQueryTacticsActorConfig[Config.Scenario.Allies.Length + Config.Scenario.CountGeneratedActors(EntityQueryTacticsGeneratedActorRoles.Ally)];
            var enemies = new EntityQueryTacticsActorConfig[Config.Scenario.Enemies.Length + Config.Scenario.CountGeneratedActors(EntityQueryTacticsGeneratedActorRoles.Enemy)];
            var objectives = new EntityQueryTacticsActorConfig[Config.Scenario.Objectives.Length + Config.Scenario.CountGeneratedActors(EntityQueryTacticsGeneratedActorRoles.Objective)];
            Array.Copy(Config.Scenario.Allies, allies, Config.Scenario.Allies.Length);
            Array.Copy(Config.Scenario.Enemies, enemies, Config.Scenario.Enemies.Length);
            Array.Copy(Config.Scenario.Objectives, objectives, Config.Scenario.Objectives.Length);
            int allyIndex = Config.Scenario.Allies.Length;
            int enemyIndex = Config.Scenario.Enemies.Length;
            int objectiveIndex = Config.Scenario.Objectives.Length;
            int planIndex = 0;

            for (int cohortIndex = 0; cohortIndex < Config.Scenario.GeneratedCohorts.Length; cohortIndex++)
            {
                EntityQueryTacticsGeneratedCohortConfig cohort = Config.Scenario.GeneratedCohorts[cohortIndex];
                if (cohort.Count <= 0)
                {
                    continue;
                }

                RequireTemplate(cohort.Template);
                for (int i = 0; i < cohort.Count; i++)
                {
                    string name = $"{cohort.NamePrefix} {cohort.FirstIndex + i:0000}";
                    int column = i % cohort.Grid.Columns;
                    int row = i / cohort.Grid.Columns;
                    int x = cohort.Grid.OriginXCm + column * cohort.Grid.SpacingXCm;
                    int y = cohort.Grid.OriginYCm + row * cohort.Grid.SpacingYCm;
                    var actor = new EntityQueryTacticsActorConfig
                    {
                        Name = name,
                        Template = cohort.Template,
                        TeamId = cohort.TeamId,
                        Tags = cohort.Tags,
                    };
                    _generatedPlans[planIndex] = new EntityQueryTacticsGeneratedActorPlan(
                        planIndex,
                        cohortIndex,
                        i,
                        name,
                        cohort.Role,
                        cohort.Template,
                        cohort.TeamId,
                        x,
                        y,
                        cohort.FacingRad);
                    planIndex++;

                    if (string.Equals(cohort.Role, EntityQueryTacticsGeneratedActorRoles.Ally, StringComparison.OrdinalIgnoreCase))
                    {
                        allies[allyIndex++] = actor;
                    }
                    else if (string.Equals(cohort.Role, EntityQueryTacticsGeneratedActorRoles.Enemy, StringComparison.OrdinalIgnoreCase))
                    {
                        enemies[enemyIndex++] = actor;
                    }
                    else
                    {
                        objectives[objectiveIndex++] = actor;
                    }
                }
            }

            _resolvedAlliesConfig = allies;
            _resolvedEnemiesConfig = enemies;
            _resolvedObjectivesConfig = objectives;
            _generatedReceiptCount = planIndex;
            _state.GeneratedActorCount = _generatedReceiptCount;
            _generatedPlansBuilt = true;
        }

        private bool EnsureGeneratedActorsReady()
        {
            if (_generatedReceiptCount == 0)
            {
                return true;
            }

            if (!_generatedSpawnEnqueued)
            {
                EnqueueGeneratedActors();
                _generatedSpawnEnqueued = true;
                return false;
            }

            BindGeneratedReceipts();
            return _generatedReceiptsBound == _generatedReceiptCount;
        }

        private void EnqueueGeneratedActors()
        {
            RuntimeEntitySpawnQueue spawnQueue = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
                ?? throw new InvalidOperationException("Entity query tactics showcase requires RuntimeEntitySpawnQueue.");
            RuntimeEntitySpawnReceiptQueue receiptQueue = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
                ?? throw new InvalidOperationException("Entity query tactics showcase requires RuntimeEntitySpawnReceiptQueue.");
            int channelId = ResolveRuntimeSpawnReceiptChannelId();
            while (receiptQueue.TryDequeueForChannel(channelId, out _))
            {
            }

            spawnQueue.RemoveForReceiptChannel(channelId);
            if (spawnQueue.FreeCapacity < _generatedReceiptCount)
            {
                throw new InvalidOperationException(
                    $"Entity query tactics showcase requires RuntimeEntitySpawnQueue free capacity {_generatedReceiptCount}, actual {spawnQueue.FreeCapacity}.");
            }

            MapSession session = _engine.CurrentMapSession
                ?? throw new InvalidOperationException("Entity query tactics showcase requires an active map before runtime cohort spawn.");
            for (int i = 0; i < _generatedReceiptCount; i++)
            {
                EntityQueryTacticsGeneratedActorPlan plan = _generatedPlans[i];
                var request = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = plan.Template,
                    MapId = session.MapId,
                    WorldPositionCm = Fix64Vec2.FromInt(plan.WorldXCm, plan.WorldYCm),
                    HasWorldPosition = 1,
                    FacingAngleRad = plan.FacingRad,
                    HasFacing = 1,
                    EmitReceipt = 1,
                    ReceiptChannelId = channelId,
                    ReceiptId = plan.ReceiptId,
                };
                if (!spawnQueue.TryEnqueue(in request))
                {
                    throw new InvalidOperationException("Entity query tactics showcase failed to enqueue generated runtime actor spawn.");
                }
            }
        }

        private void BindGeneratedReceipts()
        {
            RuntimeEntitySpawnReceiptQueue receipts = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
                ?? throw new InvalidOperationException("Entity query tactics showcase requires RuntimeEntitySpawnReceiptQueue.");
            int channelId = ResolveRuntimeSpawnReceiptChannelId();
            bool boundAny = false;
            while (receipts.TryDequeueForChannel(channelId, out RuntimeEntitySpawnReceipt receipt))
            {
                if ((uint)receipt.ReceiptId >= (uint)_generatedPlans.Length)
                {
                    throw new InvalidOperationException($"Entity query tactics showcase received unknown generated spawn receipt id {receipt.ReceiptId}.");
                }

                EntityQueryTacticsGeneratedActorPlan plan = _generatedPlans[receipt.ReceiptId];
                BindGeneratedActor(in receipt, in plan);
                _generatedPlans[receipt.ReceiptId] = plan.WithEntity(receipt.Entity);
                _generatedReceiptsBound++;
                boundAny = true;
            }

            if (boundAny)
            {
                _state.GeneratedActorReadyCount = _generatedReceiptsBound;
            }
        }

        private void BindGeneratedActor(in RuntimeEntitySpawnReceipt receipt, in EntityQueryTacticsGeneratedActorPlan plan)
        {
            if (receipt.Kind != RuntimeEntitySpawnKind.Template ||
                !string.Equals(receipt.TemplateId, plan.Template, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Entity query tactics showcase generated receipt mismatch for '{plan.Name}'.");
            }

            Entity entity = receipt.Entity;
            if (!_world.IsAlive(entity))
            {
                throw new InvalidOperationException($"Entity query tactics showcase generated entity '{plan.Name}' is not alive.");
            }

            UpsertComponent(entity, new Name { Value = plan.Name });
            UpsertComponent(entity, new Team { Id = plan.TeamId });
            if (plan.TeamId == Config.Scenario.PlayerTeamId)
            {
                UpsertComponent(entity, new PlayerOwner { PlayerId = 1 });
            }

            ApplyGeneratedAttributes(entity, Config.Scenario.GeneratedCohorts[plan.CohortIndex], plan.ActorIndex);
            EnsureInitialSelectableVisibility(entity);
        }

        private void ApplyGeneratedAttributes(Entity entity, EntityQueryTacticsGeneratedCohortConfig cohort, int actorIndex)
        {
            if (!_world.Has<AttributeBuffer>(entity))
            {
                _world.Add(entity, new AttributeBuffer());
            }

            ref AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(entity);
            for (int i = 0; i < cohort.Attributes.Length; i++)
            {
                EntityQueryTacticsAttributePatternConfig pattern = cohort.Attributes[i];
                int attributeId = ResolveAttribute(pattern.Attribute);
                attributes.SetBase(attributeId, pattern.Evaluate(actorIndex));
            }
        }

        private void SeedGeneratedRelationships(EntityQueryTacticsScenarioContext context)
        {
            if (_generatedReceiptCount == 0)
            {
                return;
            }

            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            for (int i = 0; i < _generatedReceiptCount; i++)
            {
                EntityQueryTacticsGeneratedActorPlan plan = _generatedPlans[i];
                Entity target = plan.Entity;
                if (!_world.IsAlive(target))
                {
                    throw new InvalidOperationException($"Entity query tactics generated plan '{plan.Name}' was not bound before relationship seeding.");
                }

                EntityQueryTacticsGeneratedCohortConfig cohort = Config.Scenario.GeneratedCohorts[plan.CohortIndex];
                for (int r = 0; r < cohort.Relations.Length; r++)
                {
                    EntityQueryTacticsGeneratedRelationConfig relation = cohort.Relations[r];
                    Entity source = context.GetEntityByName(relation.SourceName);
                    if (source == Entity.Null)
                    {
                        throw new InvalidOperationException(
                            $"Entity query tactics generated relation source '{relation.SourceName}' is unknown.");
                    }

                    int metricId = ResolveMetric(relation.Metric);
                    runtime.SetMetric(source, target, _tacticalIntelTypeId, metricId, relation.Evaluate(plan.ActorIndex), _setupReasonId);
                    if (relation.ShouldApplyFlags(plan.ActorIndex))
                    {
                        for (int f = 0; f < relation.Flags.Length; f++)
                        {
                            int flagId = ResolveFlag(relation.Flags[f]);
                            runtime.SetFlag(source, target, _tacticalIntelTypeId, flagId, true, _setupReasonId);
                        }
                    }
                }
            }
        }

        private int ResolveRuntimeSpawnReceiptChannelId()
        {
            if (_runtimeSpawnReceiptChannelId > 0)
            {
                return _runtimeSpawnReceiptChannelId;
            }

            RuntimeEntitySpawnReceiptChannelRegistry channels = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
                ?? throw new InvalidOperationException("Entity query tactics showcase requires RuntimeEntitySpawnReceiptChannelRegistry.");
            _runtimeSpawnReceiptChannelId = channels.Register(Config.Scenario.RuntimeSpawnReceiptChannelKey);
            return _runtimeSpawnReceiptChannelId;
        }

        private void RequireTemplate(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId) || !_engine.MapLoader.TemplateRegistry.Contains(templateId))
            {
                throw new InvalidOperationException($"Entity query tactics showcase requires configured entity template '{templateId}'.");
            }
        }

        private static bool ContainsNull(Entity[] entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == Entity.Null)
                {
                    return true;
                }
            }

            return false;
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

            _tacticalIntelTypeId = types.GetId(Config.Relationships.TacticalIntel);
            _threatMetricId = metrics.GetId(Config.Metrics.Threat);
            _focusMetricId = metrics.GetId(Config.Metrics.Focus);
            _priorityTargetFlagId = flags.GetId(Config.Flags.PriorityTarget);
            _setupReasonId = reasons.Register("Scenario.Setup");
            _pressurePulseReasonId = reasons.Register("Player.PressurePulse");
            _commandableTagId = TagRegistry.GetId(Config.Tags.Commandable);
            _routedTagId = TagRegistry.GetId(Config.Tags.Routed);
            _objectiveTagId = TagRegistry.GetId(Config.Tags.Objective);
            _commandPowerAttributeId = AttributeRegistry.GetId(Config.Attributes.CommandPower);
            _supplyAttributeId = AttributeRegistry.GetId(Config.Attributes.Supply);
            _threatValueAttributeId = AttributeRegistry.GetId(Config.Attributes.ThreatValue);
            _selectedFriendliesGraphId = GraphIdRegistry.GetId(Config.Graphs.SelectedFriendlies);
            _hostileThreatsGraphId = GraphIdRegistry.GetId(Config.Graphs.HostileThreats);
            _formationCacheGraphId = GraphIdRegistry.GetId(Config.Graphs.FormationCache);

            RequireNonNegative(_tacticalIntelTypeId, Config.Relationships.TacticalIntel);
            RequireNonNegative(_threatMetricId, Config.Metrics.Threat);
            RequireNonNegative(_focusMetricId, Config.Metrics.Focus);
            RequireNonNegative(_priorityTargetFlagId, Config.Flags.PriorityTarget);
            RequirePositive(_commandableTagId, Config.Tags.Commandable);
            RequirePositive(_routedTagId, Config.Tags.Routed);
            RequirePositive(_objectiveTagId, Config.Tags.Objective);
            RequireNonNegative(_commandPowerAttributeId, Config.Attributes.CommandPower);
            RequireNonNegative(_supplyAttributeId, Config.Attributes.Supply);
            RequireNonNegative(_threatValueAttributeId, Config.Attributes.ThreatValue);
            RequirePositive(_selectedFriendliesGraphId, Config.Graphs.SelectedFriendlies);
            RequirePositive(_hostileThreatsGraphId, Config.Graphs.HostileThreats);
            RequirePositive(_formationCacheGraphId, Config.Graphs.FormationCache);
        }

        private static void RequirePositive(int id, string name)
        {
            if (id <= 0)
            {
                throw new InvalidOperationException($"Entity query tactics showcase could not resolve '{name}'.");
            }
        }

        private static void RequireNonNegative(int id, string name)
        {
            if (id < 0)
            {
                throw new InvalidOperationException($"Entity query tactics showcase could not resolve '{name}'.");
            }
        }

        private void PrepareEntities(EntityQueryTacticsScenarioContext context)
        {
            EntityQueryTacticsActorConfig[] alliesConfig = _resolvedAlliesConfig.Length == 0 ? Config.Scenario.Allies : _resolvedAlliesConfig;
            EntityQueryTacticsActorConfig[] enemiesConfig = _resolvedEnemiesConfig.Length == 0 ? Config.Scenario.Enemies : _resolvedEnemiesConfig;
            EntityQueryTacticsActorConfig[] objectivesConfig = _resolvedObjectivesConfig.Length == 0 ? Config.Scenario.Objectives : _resolvedObjectivesConfig;
            for (int i = 0; i < context.Allies.Length; i++)
            {
                Entity ally = context.Allies[i];
                EnsureInitialSelectableVisibility(ally);
                ApplyConfiguredTags(ally, alliesConfig[i].Tags);
            }

            for (int i = 0; i < context.Enemies.Length; i++)
            {
                EnsureInitialSelectableVisibility(context.Enemies[i]);
                ApplyConfiguredTags(context.Enemies[i], enemiesConfig[i].Tags);
            }

            for (int i = 0; i < context.Objectives.Length; i++)
            {
                ApplyConfiguredTags(context.Objectives[i], objectivesConfig[i].Tags);
                if (objectivesConfig[i].Tags.Length == 0)
                {
                    AddTag(context.Objectives[i], _objectiveTagId);
                }
            }
        }

        private void EnsureInitialSelectableVisibility(Entity entity)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity))
            {
                return;
            }

            if (!_world.Has<CullState>(entity))
            {
                _world.Add(entity, new CullState { IsVisible = true, LOD = LODLevel.High });
            }
            else
            {
                ref CullState cull = ref _world.Get<CullState>(entity);
                cull.IsVisible = true;
                _world.Set(entity, cull);
            }
        }

        private void AddTag(Entity entity, int tagId)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity))
            {
                throw new InvalidOperationException("Entity query tactics showcase cannot add a configured tag to a missing entity.");
            }

            if (tagId <= 0)
            {
                throw new InvalidOperationException($"Entity query tactics showcase cannot add invalid tag id '{tagId}'.");
            }

            if (!_world.Has<GameplayTagContainer>(entity))
            {
                entity.Add(new GameplayTagContainer());
            }

            if (!_world.Has<TagCountContainer>(entity))
            {
                entity.Add(new TagCountContainer());
            }

            ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(entity);
            ref TagCountContainer counts = ref _world.Get<TagCountContainer>(entity);
            TagOps tagOps = _engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps is missing.");
            if (!tagOps.AddTag(ref tags, ref counts, tagId))
            {
                throw new InvalidOperationException($"Entity query tactics showcase tag rule rejected configured tag id '{tagId}'.");
            }
        }

        private void ApplyConfiguredTags(Entity entity, string[] tags)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                int tagId = ResolveTag(tags[i]);
                AddTag(entity, tagId);
            }
        }

        private bool IsDemoPlaybackEnabled()
        {
            if (Config.DemoPlayback.Enabled)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(Config.DemoPlayback.ActivationEnv))
            {
                return false;
            }

            string? value = Environment.GetEnvironmentVariable(Config.DemoPlayback.ActivationEnv);
            return string.Equals(value, "1", StringComparison.Ordinal) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private void RunDemoPlayback()
        {
            if (!_demoPlaybackEnabled || ScenarioContext == null)
            {
                return;
            }

            EntityQueryTacticsDemoStepConfig[] steps = Config.DemoPlayback.Steps;
            while (_nextDemoStepIndex < steps.Length && _state.Frame >= steps[_nextDemoStepIndex].Frame)
            {
                ExecuteDemoStep(steps[_nextDemoStepIndex]);
                _nextDemoStepIndex++;
            }
        }

        private void ExecuteDemoStep(EntityQueryTacticsDemoStepConfig step)
        {
            switch (step.Op)
            {
                case "WriteUiBox":
                    WriteUiAcquisitionCollection(step);
                    ExecuteGraphs();
                    _state.AddLog("Demo playback wrote UI acquisition through EntityCollectionStore.");
                    return;

                case "CommitSelection":
                    CommitSelectionFromUiBox(requireNonEmpty: true);
                    ExecuteGraphs();
                    _state.AddLog(Config.Logs.SelectionCommitted);
                    return;

                case "ExecuteGraphs":
                    ExecuteGraphs();
                    _state.AddLog(Config.Logs.GraphsExecuted);
                    return;

                case "RotateFormation":
                    RotateFormation();
                    ExecuteGraphs();
                    _state.AddLog(Config.Logs.FormationRotated);
                    return;

                case "CacheProbe":
                    ProbeRetainedCache();
                    _state.AddLog(Config.Logs.CacheProbe);
                    return;

                case "PressurePulse":
                    ApplyPressurePulse();
                    ExecuteGraphs();
                    _state.AddLog(Config.Logs.PressurePulse);
                    return;

                default:
                    throw new InvalidOperationException($"Entity query tactics demo playback op '{step.Op}' is not supported.");
            }
        }

        private void WriteUiAcquisitionCollection(EntityQueryTacticsDemoStepConfig step)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            int count = ResolveDemoStepEntities(step, _selectionScratch);
            var descriptor = EntityCollectionDescriptor.Create(
                Config.Collections.UiBox,
                EntityCollectionSourceKind.UiAcquisition,
                EntityCollectionRoleKind.AcquisitionPreview,
                ScenarioContext.Owner,
                count > 0 ? _selectionScratch[0] : Entity.Null,
                "UI acquisition",
                $"Demo playback | {count} entities");
            collections.Replace(ScenarioContext.Owner, descriptor, _selectionScratch.AsSpan(0, count), default, BuildPrimaryFlags(count));
        }

        private int ResolveDemoStepEntities(EntityQueryTacticsDemoStepConfig step, Entity[] destination)
        {
            if (!string.IsNullOrWhiteSpace(step.Role))
            {
                return ResolveRoleEntities(step.Role, destination);
            }

            return ResolveNamedEntities(step.Entities, destination);
        }

        private int ResolveRoleEntities(string role, Entity[] destination)
        {
            if (ScenarioContext == null)
            {
                return 0;
            }

            Entity[] source = role switch
            {
                EntityQueryTacticsGeneratedActorRoles.Ally => ScenarioContext.Allies,
                EntityQueryTacticsGeneratedActorRoles.Enemy => ScenarioContext.Enemies,
                EntityQueryTacticsGeneratedActorRoles.Objective => ScenarioContext.Objectives,
                _ => throw new InvalidOperationException($"Entity query tactics demo playback role '{role}' is not supported."),
            };
            if (source.Length > destination.Length)
            {
                throw new InvalidOperationException(
                    $"Entity query tactics demo playback role '{role}' references {source.Length} entities, capacity is {destination.Length}.");
            }

            source.AsSpan().CopyTo(destination);
            return source.Length;
        }

        private int ResolveNamedEntities(string[] names, Entity[] destination)
        {
            if (ScenarioContext == null)
            {
                return 0;
            }

            if (names.Length > destination.Length)
            {
                throw new InvalidOperationException(
                    $"Entity query tactics demo playback step references {names.Length} entities, capacity is {destination.Length}.");
            }

            for (int i = 0; i < names.Length; i++)
            {
                Entity entity = ScenarioContext.GetEntityByName(names[i]);
                if (entity == Entity.Null || !_world.IsAlive(entity))
                {
                    throw new InvalidOperationException($"Entity query tactics demo playback references unknown entity '{names[i]}'.");
                }

                destination[i] = entity;
            }

            return names.Length;
        }

        private int ResolveTag(string tagName)
        {
            if (string.Equals(tagName, Config.Tags.Commandable, StringComparison.Ordinal))
            {
                return _commandableTagId;
            }

            if (string.Equals(tagName, Config.Tags.Routed, StringComparison.Ordinal))
            {
                return _routedTagId;
            }

            if (string.Equals(tagName, Config.Tags.Objective, StringComparison.Ordinal))
            {
                return _objectiveTagId;
            }

            int tagId = TagRegistry.GetId(tagName);
            if (tagId <= 0)
            {
                throw new InvalidOperationException($"Entity query tactics showcase could not resolve configured tag '{tagName}'.");
            }

            return tagId;
        }

        private void SeedRelationshipRuntime(EntityQueryTacticsScenarioContext context)
        {
            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");

            for (int i = 0; i < Config.Scenario.RelationSeeds.Length; i++)
            {
                EntityQueryTacticsRelationSeed seed = Config.Scenario.RelationSeeds[i];
                Entity source = context.GetEntityByName(seed.SourceName);
                Entity target = context.GetEntityByName(seed.TargetName);
                if (source == Entity.Null || target == Entity.Null)
                {
                    throw new InvalidOperationException(
                        $"Entity query tactics showcase relation seed '{seed.SourceName}' -> '{seed.TargetName}' references an unknown entity.");
                }

                int metricId = ResolveMetric(seed.Metric);
                runtime.SetMetric(source, target, _tacticalIntelTypeId, metricId, seed.Value, _setupReasonId);
                for (int f = 0; f < seed.Flags.Length; f++)
                {
                    int flagId = ResolveFlag(seed.Flags[f]);
                    runtime.SetFlag(source, target, _tacticalIntelTypeId, flagId, true, _setupReasonId);
                }
            }

            SeedGeneratedRelationships(context);
        }

        private int ResolveMetric(string metricName)
        {
            if (string.Equals(metricName, Config.Metrics.Threat, StringComparison.Ordinal))
            {
                return _threatMetricId;
            }

            if (string.Equals(metricName, Config.Metrics.Focus, StringComparison.Ordinal))
            {
                return _focusMetricId;
            }

            RelationshipMetricRegistry metrics = _engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("RelationshipMetricRegistry is missing.");
            int metricId = metrics.GetId(metricName);
            if (metricId < 0)
            {
                throw new InvalidOperationException($"Entity query tactics showcase could not resolve configured relationship metric '{metricName}'.");
            }

            return metricId;
        }

        private int ResolveAttribute(string attributeName)
        {
            if (string.Equals(attributeName, Config.Attributes.CommandPower, StringComparison.Ordinal))
            {
                return _commandPowerAttributeId;
            }

            if (string.Equals(attributeName, Config.Attributes.Supply, StringComparison.Ordinal))
            {
                return _supplyAttributeId;
            }

            if (string.Equals(attributeName, Config.Attributes.ThreatValue, StringComparison.Ordinal))
            {
                return _threatValueAttributeId;
            }

            int attributeId = AttributeRegistry.GetId(attributeName);
            if (attributeId < 0)
            {
                throw new InvalidOperationException($"Entity query tactics showcase could not resolve configured attribute '{attributeName}'.");
            }

            return attributeId;
        }

        private int ResolveFlag(string flagName)
        {
            if (string.Equals(flagName, Config.Flags.PriorityTarget, StringComparison.Ordinal))
            {
                return _priorityTargetFlagId;
            }

            RelationshipFlagRegistry flags = _engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
                ?? throw new InvalidOperationException("RelationshipFlagRegistry is missing.");
            int flagId = flags.GetId(flagName);
            if (flagId < 0)
            {
                throw new InvalidOperationException($"Entity query tactics showcase could not resolve configured relationship flag '{flagName}'.");
            }

            return flagId;
        }

        private void BindSelectionRuntime(Entity owner)
        {
            SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime is missing.");

            _engine.SetService(CoreServiceKeys.LocalPlayerEntity, owner);
            selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.LivePrimary, out _);
            selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.FormationPrimary, out _);
            if (!SelectionContextRuntime.TrySetCurrentView(
                    _world,
                    _engine.GlobalContext,
                    selection,
                    owner,
                    SelectionViewKeys.Primary,
                    owner,
                    SelectionSetKeys.LivePrimary,
                    out _))
            {
                throw new InvalidOperationException("Entity query tactics showcase failed to bind primary selection view.");
            }
        }

        private void HandlePlayerInput(IInputActionReader input)
        {
            if (input.PressedThisFrame(Config.Actions.CommitSelection))
            {
                CommitSelectionFromUiBox();
                ExecuteGraphs();
                _state.AddLog(Config.Logs.SelectionCommitted);
            }

            if (input.PressedThisFrame(Config.Actions.ExecuteGraphs))
            {
                ExecuteGraphs();
                _state.AddLog(Config.Logs.GraphsExecuted);
            }

            if (input.PressedThisFrame(Config.Actions.RotateFormation))
            {
                RotateFormation();
                ExecuteGraphs();
                _state.AddLog(Config.Logs.FormationRotated);
            }

            if (input.PressedThisFrame(Config.Actions.PressurePulse))
            {
                ApplyPressurePulse();
                ExecuteGraphs();
                _state.AddLog(Config.Logs.PressurePulse);
            }

            if (input.PressedThisFrame(Config.Actions.CacheProbe))
            {
                ProbeRetainedCache();
                _state.AddLog(Config.Logs.CacheProbe);
            }
        }

        private void MirrorFormalSelectionToCollection(bool force = false)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime is missing.");
            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            uint liveRevision = selection.TryDescribeSelection(
                    ScenarioContext.Owner,
                    SelectionSetKeys.LivePrimary,
                    out SelectionContainerDescriptor selectionDescriptor)
                ? selectionDescriptor.Revision
                : 0u;
            if (!force &&
                _formalSelectionMirrorReady &&
                liveRevision == _lastMirroredLiveSelectionRevision)
            {
                return;
            }

            int count = selection.CopySelection(ScenarioContext.Owner, SelectionSetKeys.LivePrimary, _selectionScratch);
            var descriptor = EntityCollectionDescriptor.Create(
                Config.Collections.FormalSelectionMirror,
                EntityCollectionSourceKind.SelectionContainer,
                EntityCollectionRoleKind.FormalSelection,
                contextEntity: ScenarioContext.Owner,
                primaryEntity: count > 0 ? _selectionScratch[0] : Entity.Null,
                title: "Formal selection mirror",
                summary: $"SelectionRuntime live primary | {count} entities");
            collections.Replace(ScenarioContext.Owner, descriptor, _selectionScratch.AsSpan(0, count), default, BuildPrimaryFlags(count));
            _lastMirroredLiveSelectionRevision = liveRevision;
            _formalSelectionMirrorReady = true;
        }

        private void CommitSelectionFromUiBox(bool requireNonEmpty = false)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime is missing.");
            int count = collections.CopyEntities(ScenarioContext.Owner, Config.Collections.UiBox, _selectionScratch);
            if (requireNonEmpty && count == 0)
            {
                throw new InvalidOperationException(
                    $"Entity query tactics demo playback could not commit '{Config.Collections.UiBox}' because it is empty.");
            }

            if (!selection.ReplaceSelection(ScenarioContext.Owner, SelectionSetKeys.LivePrimary, _selectionScratch.AsSpan(0, count)))
            {
                throw new InvalidOperationException("Entity query tactics showcase failed to replace SelectionRuntime live primary.");
            }

            int committed = selection.GetSelectionCount(ScenarioContext.Owner, SelectionSetKeys.LivePrimary);
            if (committed != count)
            {
                throw new InvalidOperationException(
                    $"Entity query tactics showcase SelectionRuntime committed {committed} entities, expected {count}.");
            }

            MirrorFormalSelectionToCollection(force: true);
            MaintainFormationSnapshotFromSelectionChange(force: true);
        }

        private ReadOnlySpan<EntityCollectionRowFlags> BuildPrimaryFlags(int count)
        {
            if (count <= 0)
            {
                return ReadOnlySpan<EntityCollectionRowFlags>.Empty;
            }

            Array.Clear(_rowFlags, 0, count);
            _rowFlags[0] = EntityCollectionRowFlags.Primary;
            return _rowFlags.AsSpan(0, count);
        }

        private void MaintainFormationSnapshotFromSelectionChange(bool force = false)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime is missing.");
            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            uint liveRevision = selection.TryDescribeSelection(
                    ScenarioContext.Owner,
                    SelectionSetKeys.LivePrimary,
                    out SelectionContainerDescriptor descriptor)
                ? descriptor.Revision
                : 0u;
            if (!force && liveRevision == _lastSyncedLiveSelectionRevision)
            {
                return;
            }

            int count = selection.CopySelection(ScenarioContext.Owner, SelectionSetKeys.LivePrimary, _formationScratch);
            selection.ReplaceSelection(ScenarioContext.Owner, SelectionSetKeys.FormationPrimary, _formationScratch.AsSpan(0, count));
            WriteFormationCollection(count);
            _lastSyncedLiveSelectionRevision = liveRevision;
        }

        private void WriteFormationCollection(int count)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            var descriptor = EntityCollectionDescriptor.Create(
                Config.Collections.FormationPrimary,
                EntityCollectionSourceKind.SelectionView,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: ScenarioContext.Owner,
                primaryEntity: count > 0 ? _formationScratch[0] : Entity.Null,
                title: "Formation primary",
                summary: $"Formation snapshot | {count} entities");
            EntityCollectionHandle handle = collections.Replace(ScenarioContext.Owner, descriptor, _formationScratch.AsSpan(0, count), default, BuildPrimaryFlags(count));
            _state.FormationRevision = handle.Revision;
        }

        private void RotateFormation()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            SelectionRuntime selection = _engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime is missing.");
            int count = selection.CopySelection(ScenarioContext.Owner, SelectionSetKeys.FormationPrimary, _formationScratch);
            if (count > 1)
            {
                Entity first = _formationScratch[0];
                for (int i = 1; i < count; i++)
                {
                    _formationScratch[i - 1] = _formationScratch[i];
                }

                _formationScratch[count - 1] = first;
            }

            selection.ReplaceSelection(ScenarioContext.Owner, SelectionSetKeys.FormationPrimary, _formationScratch.AsSpan(0, count));
            WriteFormationCollection(count);
        }

        private void ApplyPressurePulse()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            RelationshipRuntime runtime = _engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime is missing.");
            Entity target = ScenarioContext.GetEntityByName(Config.Scenario.PressurePulse.TargetName);
            if (target == Entity.Null)
            {
                throw new InvalidOperationException(
                    $"Entity query tactics showcase pressure pulse target '{Config.Scenario.PressurePulse.TargetName}' is unknown.");
            }

            int metricId = ResolveMetric(Config.Scenario.PressurePulse.Metric);
            runtime.AddMetric(ScenarioContext.Owner, target, _tacticalIntelTypeId, metricId, Config.Scenario.PressurePulse.Delta, _pressurePulseReasonId);
            for (int i = 0; i < Config.Scenario.PressurePulse.Flags.Length; i++)
            {
                int flagId = ResolveFlag(Config.Scenario.PressurePulse.Flags[i]);
                runtime.SetFlag(ScenarioContext.Owner, target, _tacticalIntelTypeId, flagId, true, _pressurePulseReasonId);
            }

            _state.PressurePulseCount++;
            _pressureTimingSamplesRemaining = 120;
        }

        private void ProbeRetainedCache()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            uint before = TryGetCollectionRevision(collections, Config.Collections.FormationCacheResult, out uint revision)
                ? revision
                : 0u;

            ExecuteGraphs();

            uint after = TryGetCollectionRevision(collections, Config.Collections.FormationCacheResult, out revision)
                ? revision
                : 0u;
            _state.CacheProbeCount++;
            _state.LastCacheProbeUnchanged = before != 0u && before == after;
        }

        private bool TryGetCollectionRevision(EntityCollectionStore collections, string key, out uint revision)
        {
            revision = 0u;
            if (ScenarioContext == null ||
                !collections.TryGet(ScenarioContext.Owner, key, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view))
            {
                return false;
            }

            revision = view.Revision;
            return true;
        }

        private void ExecuteGraphs()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            GraphReturnWriter writer = _engine.GetService(CoreServiceKeys.GraphReturnWriter)
                ?? throw new InvalidOperationException("GraphReturnWriter is missing.");
            IGraphRuntimeApi api = _graphApi ??= CreateGraphApi();
            IntVector2 targetPos = default;

            writer.ExecuteAndWrite(_selectedFriendliesGraphId, ScenarioContext.Owner, ScenarioContext.Owner, Entity.Null, Entity.Null, targetPos, NextSeed(), api);
            writer.ExecuteAndWrite(_hostileThreatsGraphId, ScenarioContext.Owner, ScenarioContext.Owner, Entity.Null, Entity.Null, targetPos, NextSeed(), api);
            writer.ExecuteAndWrite(_formationCacheGraphId, ScenarioContext.Owner, ScenarioContext.Owner, Entity.Null, Entity.Null, targetPos, NextSeed(), api);
            _state.GraphExecutionCount++;
        }

        private IGraphRuntimeApi CreateGraphApi()
        {
            return GasGraphRuntimeApi.CreateProduction(
                _world,
                _engine.SpatialQueries,
                _engine.SpatialCoords,
                _engine.EventBus,
                _engine.GetService(CoreServiceKeys.EffectRequestQueue),
                _engine.GlobalContext);
        }

        private uint NextSeed()
        {
            _randomSeed ^= _randomSeed << 13;
            _randomSeed ^= _randomSeed >> 17;
            _randomSeed ^= _randomSeed << 5;
            return _randomSeed == 0u ? 1u : _randomSeed;
        }

        private void RefreshScenarioState()
        {
            if (ScenarioContext == null)
            {
                return;
            }

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            GraphOutputValueStore values = _engine.GetService(CoreServiceKeys.GraphOutputValueStore)
                ?? throw new InvalidOperationException("GraphOutputValueStore is missing.");

            ReadCollectionState(collections, Config.Collections.UiBox, _state.UiBoxRevision, ref _uiBoxNames, out uint uiBoxRevision, out int uiBoxCount);
            ReadCollectionState(collections, Config.Collections.FormalSelectionMirror, _state.FormalSelectionRevision, ref _formalSelectionNames, out uint formalSelectionRevision, out int formalSelectionCount);
            ReadCollectionState(collections, Config.Collections.FormationPrimary, _state.FormationRevision, ref _formationInputNames, out uint formationRevision, out _);
            ReadCollectionState(collections, Config.Collections.FormationCacheResult, _state.FormationResultRevision, ref _formationResultNames, out uint formationResultRevision, out int formationCount);
            ReadCollectionState(collections, Config.Collections.HostileThreatResult, _state.HostileResultRevision, ref _hostileThreatNames, out uint hostileResultRevision, out int threatCount);
            _state.UiBoxRevision = uiBoxRevision;
            _state.UiBoxCount = uiBoxCount;
            _state.UiBoxNames = _uiBoxNames;
            _state.FormalSelectionRevision = formalSelectionRevision;
            _state.FormalSelectionCount = formalSelectionCount;
            _state.FormationRevision = formationRevision;
            _state.SelectedNames = _formalSelectionNames;
            _state.FormationResultRevision = formationResultRevision;
            _state.FormationCount = formationCount;
            _state.FormationNames = string.IsNullOrWhiteSpace(_formationResultNames) ? _formationInputNames : _formationResultNames;
            _state.HostileResultRevision = hostileResultRevision;
            _state.ThreatCount = threatCount;
            _state.ThreatNames = _hostileThreatNames;

            _state.SelectedCount = ReadInt(values, Config.SummaryKeys.SelectedCount);
            _state.SelectedCommandPowerSum = ReadFloat(values, Config.SummaryKeys.SelectedCommandPower);
            _state.SelectedSupplySum = ReadFloat(values, Config.SummaryKeys.SelectedSupply);
            _state.SelectedBest = ReadEntity(values, Config.SummaryKeys.SelectedBestEntity);
            _state.ThreatCount = ReadInt(values, Config.SummaryKeys.ThreatCount);
            _state.ThreatSum = ReadInt(values, Config.SummaryKeys.ThreatSum);
            _state.ThreatAverage = ReadInt(values, Config.SummaryKeys.ThreatAverage);
            _state.ThreatMax = ReadInt(values, Config.SummaryKeys.ThreatMax);
            _state.ThreatBest = ReadEntity(values, Config.SummaryKeys.ThreatBestEntity);
            if (_state.PressurePulseCount != _lastPressureTimingPulseCount ||
                _pressureTimingSamplesRemaining > 0)
            {
                float frameMs = ResolveFrameMs();
                if (frameMs > 0.001f &&
                    (_state.LastFrameMs <= 0.001f || frameMs < _state.LastFrameMs))
                {
                    _state.LastFrameMs = frameMs;
                }

                _lastPressureTimingPulseCount = _state.PressurePulseCount;
                if (_pressureTimingSamplesRemaining > 0)
                {
                    _pressureTimingSamplesRemaining--;
                }
            }

            _state.FormationCount = ReadInt(values, Config.SummaryKeys.FormationCount);
            _state.FormationMaxCommandPower = ReadFloat(values, Config.SummaryKeys.FormationMaxCommandPower);
            _state.FormationMinSupply = ReadFloat(values, Config.SummaryKeys.FormationMinSupply);
            _state.FormationBest = ReadEntity(values, Config.SummaryKeys.FormationBestEntity);
        }

        private float ResolveFrameMs()
        {
            PresentationTimingDiagnostics? timing = _engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            if (timing == null)
            {
                return 0f;
            }

            if (timing.WallFrameMs > 0.001f)
            {
                return timing.WallFrameMs;
            }

            if (timing.FrameMs > 0.001f)
            {
                return timing.FrameMs;
            }

            if (timing.LastWallFrameMs > 0.001f)
            {
                return timing.LastWallFrameMs;
            }

            return timing.LastFrameMs;
        }

        private void ReadCollectionState(
            EntityCollectionStore collections,
            string key,
            uint knownRevision,
            ref string cachedNames,
            out uint revision,
            out int count)
        {
            revision = 0u;
            count = 0;
            if (ScenarioContext == null ||
                !collections.TryGet(ScenarioContext.Owner, key, out EntityCollectionHandle handle) ||
                !collections.TryGetView(handle, out EntityCollectionView view))
            {
                if (knownRevision != 0u)
                {
                    cachedNames = string.Empty;
                }

                return;
            }

            revision = view.Revision;
            count = view.Count;
            if (revision == knownRevision)
            {
                return;
            }

            int written = collections.CopyEntities(handle, 0, _collectionScratch);
            cachedNames = JoinNames(_collectionScratch.AsSpan(0, written));
        }

        private int ReadInt(GraphOutputValueStore values, string key)
        {
            return values.TryGet(ScenarioContext!.Owner, key, out GraphOutputValueHandle handle) &&
                   values.TryGetView(handle, out GraphOutputValueView view)
                ? view.IntValue
                : 0;
        }

        private float ReadFloat(GraphOutputValueStore values, string key)
        {
            return values.TryGet(ScenarioContext!.Owner, key, out GraphOutputValueHandle handle) &&
                   values.TryGetView(handle, out GraphOutputValueView view)
                ? view.FloatValue
                : 0f;
        }

        private Entity ReadEntity(GraphOutputValueStore values, string key)
        {
            return values.TryGet(ScenarioContext!.Owner, key, out GraphOutputValueHandle handle) &&
                   values.TryGetView(handle, out GraphOutputValueView view)
                ? view.EntityValue
                : Entity.Null;
        }

        private string JoinNames(ReadOnlySpan<Entity> entities)
        {
            if (entities.Length == 0)
            {
                return "(none)";
            }

            int previewCount = Math.Min(CollectionNamePreviewCount, entities.Length);
            var result = new StringBuilder(previewCount * 24);
            for (int i = 0; i < previewCount; i++)
            {
                if (i > 0)
                {
                    result.Append(", ");
                }

                result.Append(ReadName(entities[i]));
            }

            if (entities.Length > previewCount)
            {
                result.Append(", +");
                result.Append(entities.Length - previewCount);
                result.Append(" more (");
                result.Append(entities.Length);
                result.Append(" rows)");
            }

            return result.ToString();
        }

        private string ReadName(Entity entity)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<Name>(entity))
            {
                return $"Entity#{entity.Id}";
            }

            return _world.Get<Name>(entity).Value;
        }

        private void UpsertComponent<T>(Entity entity, T component)
        {
            if (_world.Has<T>(entity))
            {
                _world.Set(entity, component);
            }
            else
            {
                _world.Add(entity, component);
            }
        }

        private readonly struct EntityQueryTacticsGeneratedActorPlan
        {
            public EntityQueryTacticsGeneratedActorPlan(
                int receiptId,
                int cohortIndex,
                int actorIndex,
                string name,
                string role,
                string template,
                int teamId,
                int worldXCm,
                int worldYCm,
                float facingRad,
                Entity entity = default)
            {
                ReceiptId = receiptId;
                CohortIndex = cohortIndex;
                ActorIndex = actorIndex;
                Name = name;
                Role = role;
                Template = template;
                TeamId = teamId;
                WorldXCm = worldXCm;
                WorldYCm = worldYCm;
                FacingRad = facingRad;
                Entity = entity;
            }

            public int ReceiptId { get; }
            public int CohortIndex { get; }
            public int ActorIndex { get; }
            public string Name { get; }
            public string Role { get; }
            public string Template { get; }
            public int TeamId { get; }
            public int WorldXCm { get; }
            public int WorldYCm { get; }
            public float FacingRad { get; }
            public Entity Entity { get; }

            public EntityQueryTacticsGeneratedActorPlan WithEntity(Entity entity)
            {
                return new EntityQueryTacticsGeneratedActorPlan(
                    ReceiptId,
                    CohortIndex,
                    ActorIndex,
                    Name,
                    Role,
                    Template,
                    TeamId,
                    WorldXCm,
                    WorldYCm,
                    FacingRad,
                    entity);
            }
        }
    }
}
