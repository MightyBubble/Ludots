using System;
using System.Collections.Generic;
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
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
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
        private static readonly QueryDescription SelectableKnowledgeQuery = new QueryDescription().WithAll<CommandSourceSelectableTag, MapEntity>();

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly EntityQueryTacticsScenarioState _state;
        private readonly Entity[] _selectionScratch = new Entity[64];
        private readonly Entity[] _collectionScratch = new Entity[64];
        private readonly Entity[] _formationScratch = new Entity[64];
        private readonly EntityCollectionRowFlags[] _rowFlags = new EntityCollectionRowFlags[64];
        private IGraphRuntimeApi? _graphApi;

        private bool _scenarioReady;
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
        private uint _lastSyncedCommandSourceRevision;
        private uint _lastMirroredCommandSourceRevision;
        private bool _commandSourceMirrorReady;
        private string _uiBoxNames = string.Empty;
        private string _commandSourceNames = string.Empty;
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
            PublishSelectableKnowledge(ScenarioContext.Owner);
            MirrorCommandSourceToCollection();
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

            if (!TryResolveScenarioContext(out EntityQueryTacticsScenarioContext? context))
            {
                return false;
            }

            InitializeIdentifiers();
            PrepareEntities(context);
            SeedRelationshipRuntime(context);
            BindCommandSourceOwner(context.Owner);
            PublishSelectableKnowledge(context.Owner);
            _state.SetScenarioContext(context);
            _engine.GlobalContext[EntityQueryTacticsShowcaseIds.ScenarioKey] = context;
            _state.AddLog(Config.Logs.ScenarioReady);
            _demoPlaybackEnabled = IsDemoPlaybackEnabled();
            _nextDemoStepIndex = 0;

            MirrorCommandSourceToCollection(force: true);
            MaintainFormationSnapshotFromSelectionChange(force: true);
            ExecuteGraphs();
            RefreshScenarioState();
            _scenarioReady = true;
            return true;
        }

        private bool TryResolveScenarioContext(out EntityQueryTacticsScenarioContext context)
        {
            EntityQueryTacticsActorConfig[] alliesConfig = Config.Scenario.Allies;
            EntityQueryTacticsActorConfig[] enemiesConfig = Config.Scenario.Enemies;
            EntityQueryTacticsActorConfig[] objectivesConfig = Config.Scenario.Objectives;
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
            for (int i = 0; i < context.Allies.Length; i++)
            {
                Entity ally = context.Allies[i];
                EnsureInitialSelectableVisibility(ally);
                ApplyConfiguredTags(ally, Config.Scenario.Allies[i].Tags);
            }

            for (int i = 0; i < context.Enemies.Length; i++)
            {
                EnsureInitialSelectableVisibility(context.Enemies[i]);
                ApplyConfiguredTags(context.Enemies[i], Config.Scenario.Enemies[i].Tags);
            }

            for (int i = 0; i < context.Objectives.Length; i++)
            {
                ApplyConfiguredTags(context.Objectives[i], Config.Scenario.Objectives[i].Tags);
                if (Config.Scenario.Objectives[i].Tags.Length == 0)
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

        private void PublishSelectableKnowledge(Entity viewer)
        {
            if (viewer == Entity.Null || !_world.IsAlive(viewer))
            {
                return;
            }

            KnowledgeProjectionStore knowledge = _engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
                ?? throw new InvalidOperationException("Entity query tactics showcase requires KnowledgeProjectionStore before publishing selectable visibility.");
            var empty = KnowledgeIdMask256.Empty;
            int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(_engine.GlobalContext);
            string mapId = Config.MapId;
            _world.Query(in SelectableKnowledgeQuery, (Entity target, ref CommandSourceSelectableTag _, ref MapEntity mapEntity) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, mapId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                knowledge.Upsert(
                    viewer,
                    target,
                    new KnowledgeDisclosureRecord(
                        KnowledgePresence.LiveVisible,
                        KnowledgePositionAccess.Live,
                        empty,
                        empty,
                        empty,
                        viewer,
                        observedTick,
                        expiryTick: 0,
                        confidencePermille: 1000,
                        revision: 0));
            });
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

            TagStateInstaller.EnsureInstalled(_world, entity);
            TagOps tagOps = _engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps is missing.");
            if (!tagOps.AddTag(_world, entity, tagId))
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
                    WriteUiAcquisitionCollection(step.Entities);
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

        private void WriteUiAcquisitionCollection(string[] names)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            int count = ResolveNamedEntities(names, _selectionScratch);
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

        private void BindCommandSourceOwner(Entity owner)
        {
            _engine.SetService(CoreServiceKeys.LocalPlayerEntity, owner);
            if (_world.TryGet(owner, out PlayerOwner playerOwner) && playerOwner.PlayerId > 0)
            {
                _engine.SetService(CoreServiceKeys.LocalPlayerId, playerOwner.PlayerId);
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

        private void MirrorCommandSourceToCollection(bool force = false)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            uint commandSourceRevision = collections.TryGetView(
                    ScenarioContext.Owner,
                    EntityCollectionKeys.CommandSource,
                    out EntityCollectionView commandSourceView)
                ? commandSourceView.Revision
                : 0u;
            if (!force &&
                _commandSourceMirrorReady &&
                commandSourceRevision == _lastMirroredCommandSourceRevision)
            {
                return;
            }

            int count = collections.CopyEntities(ScenarioContext.Owner, EntityCollectionKeys.CommandSource, _selectionScratch);
            var descriptor = EntityCollectionDescriptor.Create(
                Config.Collections.CommandSourceMirror,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: ScenarioContext.Owner,
                primaryEntity: count > 0 ? _selectionScratch[0] : Entity.Null,
                title: "Command source mirror",
                summary: $"collection.command.source | {count} entities");
            collections.Replace(ScenarioContext.Owner, descriptor, _selectionScratch.AsSpan(0, count), default, BuildPrimaryFlags(count));
            _lastMirroredCommandSourceRevision = commandSourceRevision;
            _commandSourceMirrorReady = true;
        }

        private void CommitSelectionFromUiBox(bool requireNonEmpty = false)
        {
            if (ScenarioContext == null)
            {
                return;
            }

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            int count = collections.CopyEntities(ScenarioContext.Owner, Config.Collections.UiBox, _selectionScratch);
            if (requireNonEmpty && count == 0)
            {
                throw new InvalidOperationException(
                    $"Entity query tactics demo playback could not commit '{Config.Collections.UiBox}' because it is empty.");
            }

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.UiAcquisition,
                EntityCollectionRoleKind.CommandSource,
                ScenarioContext.Owner,
                count > 0 ? _selectionScratch[0] : Entity.Null,
                "Command source",
                $"Committed UI box | {count} entities");
            collections.Replace(ScenarioContext.Owner, descriptor, _selectionScratch.AsSpan(0, count), ScenarioContext.Owner);
            int committed = collections.CopyEntities(ScenarioContext.Owner, EntityCollectionKeys.CommandSource, _collectionScratch);
            if (committed != count)
            {
                throw new InvalidOperationException(
                    $"Entity query tactics showcase command source committed {committed} entities, expected {count}.");
            }

            MirrorCommandSourceToCollection(force: true);
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

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            uint commandSourceRevision = collections.TryGetView(
                    ScenarioContext.Owner,
                    EntityCollectionKeys.CommandSource,
                    out EntityCollectionView descriptor)
                ? descriptor.Revision
                : 0u;
            if (!force && commandSourceRevision == _lastSyncedCommandSourceRevision)
            {
                return;
            }

            int count = collections.CopyEntities(ScenarioContext.Owner, EntityCollectionKeys.CommandSource, _formationScratch);
            WriteFormationCollection(count);
            _lastSyncedCommandSourceRevision = commandSourceRevision;
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
                EntityCollectionSourceKind.Explicit,
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

            EntityCollectionStore collections = _engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore is missing.");
            int count = collections.CopyEntities(ScenarioContext.Owner, Config.Collections.FormationPrimary, _formationScratch);
            if (count > 1)
            {
                Entity first = _formationScratch[0];
                for (int i = 1; i < count; i++)
                {
                    _formationScratch[i - 1] = _formationScratch[i];
                }

                _formationScratch[count - 1] = first;
            }

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
            return _engine.GetService(CoreServiceKeys.GasGraphRuntimeApi)
                ?? throw new InvalidOperationException("Engine-owned production GasGraphRuntimeApi is missing.");
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
            ReadCollectionState(collections, Config.Collections.CommandSourceMirror, _state.CommandSourceRevision, ref _commandSourceNames, out uint commandSourceRevision, out int commandSourceCount);
            ReadCollectionState(collections, Config.Collections.FormationPrimary, _state.FormationRevision, ref _formationInputNames, out uint formationRevision, out _);
            ReadCollectionState(collections, Config.Collections.FormationCacheResult, _state.FormationResultRevision, ref _formationResultNames, out uint formationResultRevision, out int formationCount);
            ReadCollectionState(collections, Config.Collections.HostileThreatResult, _state.HostileResultRevision, ref _hostileThreatNames, out uint hostileResultRevision, out int threatCount);
            _state.UiBoxRevision = uiBoxRevision;
            _state.UiBoxCount = uiBoxCount;
            _state.UiBoxNames = _uiBoxNames;
            _state.CommandSourceRevision = commandSourceRevision;
            _state.CommandSourceCount = commandSourceCount;
            _state.FormationRevision = formationRevision;
            _state.SelectedNames = _commandSourceNames;
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

            string result = string.Empty;
            for (int i = 0; i < entities.Length; i++)
            {
                string name = ReadName(entities[i]);
                result = i == 0 ? name : $"{result}, {name}";
            }

            return result;
        }

        private string ReadName(Entity entity)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<Name>(entity))
            {
                return $"Entity#{entity.Id}";
            }

            return _world.Get<Name>(entity).Value;
        }
    }
}
