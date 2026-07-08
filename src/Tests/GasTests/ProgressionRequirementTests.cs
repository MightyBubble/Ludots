using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Config;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Gameplay.Progression.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;
using System.Text.Json.Nodes;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ProgressionRequirementTests
    {
        [SetUp]
        public void SetUp()
        {
            AbilityIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            TagRegistry.Clear();
            ProgressionIdRegistry.Clear();
            ProgressionRequirementIdRegistry.Clear();
        }

        [Test]
        public void CityScopedProgression_UnlocksOnlyEntitiesBoundToThatCity()
        {
            using var world = World.Create();
            int progressionId = ProgressionIdRegistry.Register("Progression.CitySpears");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.CitySpears");
            var scopeKeys = new ScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.ProgressionCompleted,
                new ScopeKey(ScopeKind.Named, cityScopeId),
                RoleSlot.ScopeMembers,
                progressionId));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
            Entity cityA = world.Create(new ProgressionStateBuffer());
            Entity cityB = world.Create(new ProgressionStateBuffer());
            Entity barracksA = world.Create();
            Entity barracksB = world.Create();
            PrepareScopeHost(world, cityA);
            PrepareScopeHost(world, cityB);
            PrepareScopeMember(world, barracksA);
            PrepareScopeMember(world, barracksB);

            Assert.That(evaluator.TryBindScope(barracksA, cityScopeId, cityA), Is.True);
            Assert.That(evaluator.TryBindScope(barracksB, cityScopeId, cityB), Is.True);
            Assert.That(evaluator.TryComplete(cityA, progressionId), Is.True);

            var contextA = new RoleResolverContext(actor: barracksA, subject: barracksA);
            var contextB = new RoleResolverContext(actor: barracksB, subject: barracksB);
            Assert.That(evaluator.Evaluate(reqId, in contextA), Is.True);
            Assert.That(evaluator.Evaluate(reqId, in contextB), Is.False);
        }

        [Test]
        public void EntityCountRequirement_CanRequireTaggedHeroInsideCityScope()
        {
            using var world = World.Create();
            int heroTag = TagRegistry.Register("Hero.GuanYu");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.GuanYuInCity");
            var scopeKeys = new ScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var tags = default(GameplayTagContainer);
            tags.AddTag(heroTag);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.EntityCount,
                new ScopeKey(ScopeKind.Named, cityScopeId),
                RoleSlot.ScopeMembers,
                progressionId: 0,
                requiredCount: 1,
                requiredTags: in tags));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
            Entity cityA = world.Create(new ProgressionStateBuffer());
            Entity cityB = world.Create();
            Entity barracksA = world.Create();
            Entity barracksB = world.Create();
            Entity hero = world.Create(tags);
            PrepareScopeHost(world, cityA);
            PrepareScopeHost(world, cityB);
            PrepareScopeMember(world, barracksA);
            PrepareScopeMember(world, barracksB);
            PrepareScopeMember(world, hero);

            Assert.That(evaluator.TryBindScope(barracksA, cityScopeId, cityA), Is.True);
            Assert.That(evaluator.TryBindScope(barracksB, cityScopeId, cityB), Is.True);
            Assert.That(evaluator.TryBindScope(hero, cityScopeId, cityA), Is.True);

            var contextA = new RoleResolverContext(actor: barracksA, subject: barracksA);
            var contextB = new RoleResolverContext(actor: barracksB, subject: barracksB);
            Assert.That(evaluator.Evaluate(reqId, in contextA), Is.True);
            Assert.That(evaluator.Evaluate(reqId, in contextB), Is.False);
        }

        [Test]
        public void EntityCountRequirement_CanReadTeamMembersFromEntityCollection()
        {
            using var world = World.Create();
            int workerTag = TagRegistry.Register("Role.Worker");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.TeamNeedsTwoWorkers");
            var scopeKeys = new ScopeKeyRegistry();
            int teamScopeId = scopeKeys.Register("team");
            var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            int teamMembersKeyId = collectionKeys.Register("team.members");
            scopeKeys.RegisterCollectionMembers("team", teamMembersKeyId);
            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 8, initialRowCapacity: 16);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(workerTag);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.EntityCount,
                new ScopeKey(ScopeKind.Named, teamScopeId),
                RoleSlot.ScopeMembers,
                progressionId: 0,
                requiredCount: 2,
                requiredTags: in requiredTags));

            var evaluator = new ProgressionRequirementEvaluator(
                world,
                requirements,
                scopeKeys,
                scopeResolver: new ScopeResolver(world, scopeKeys, collections));
            Entity team = world.Create(new ProgressionStateBuffer());
            Entity researcher = world.Create();
            var workerTags = default(GameplayTagContainer);
            workerTags.AddTag(workerTag);
            Entity workerA = world.Create(workerTags);
            Entity workerB = world.Create(workerTags);
            Entity spectator = world.Create();
            PrepareScopeHost(world, team);
            PrepareScopeMember(world, researcher);
            Assert.That(evaluator.TryBindScope(researcher, teamScopeId, team), Is.True);

            collections.Replace(
                team,
                EntityCollectionDescriptor.Create("team.members", EntityCollectionSourceKind.Explicit, EntityCollectionRoleKind.Display),
                new[] { workerA, workerB, spectator });

            var context = new RoleResolverContext(actor: researcher, subject: researcher);
            Assert.That(evaluator.Evaluate(reqId, in context), Is.True);
        }

        [Test]
        public void EntityCountRequirement_CanReadTeamMembersFromRelationshipEdges()
        {
            using var world = World.Create();
            int workerTag = TagRegistry.Register("Role.Worker");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.TeamNeedsRelationshipWorker");
            var scopeKeys = new ScopeKeyRegistry();
            int teamScopeId = scopeKeys.Register("team");
            var relationshipTypes = new RelationshipTypeRegistry();
            var relationshipMetrics = new RelationshipMetricRegistry();
            var relationshipFlags = new RelationshipFlagRegistry();
            var relationshipBands = new RelationshipBandRegistry();
            var relationshipChanges = new RelationshipChangeBuffer();
            var relationships = new RelationshipRuntime(world, relationshipTypes, relationshipMetrics, relationshipFlags, relationshipBands, relationshipChanges, new RelationshipReverseIndex(world));
            int memberTypeId = relationshipTypes.Register("TeamMember");
            scopeKeys.RegisterRelationshipOutgoingMembers("team", memberTypeId);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(workerTag);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.EntityCount,
                new ScopeKey(ScopeKind.Named, teamScopeId),
                RoleSlot.ScopeMembers,
                progressionId: 0,
                requiredCount: 2,
                requiredTags: in requiredTags));

            var evaluator = new ProgressionRequirementEvaluator(
                world,
                requirements,
                scopeKeys,
                scopeResolver: new ScopeResolver(world, scopeKeys, relationships: relationships));
            Entity team = world.Create(new ProgressionStateBuffer());
            Entity researcher = world.Create();
            var workerTags = default(GameplayTagContainer);
            workerTags.AddTag(workerTag);
            Entity workerA = world.Create(workerTags);
            Entity workerB = world.Create(workerTags);
            Entity spectator = world.Create();
            PrepareScopeHost(world, team);
            PrepareScopeMember(world, researcher);
            Assert.That(evaluator.TryBindScope(researcher, teamScopeId, team), Is.True);
            relationships.EnsureLink(team, workerA, memberTypeId);
            relationships.EnsureLink(team, workerB, memberTypeId);
            relationships.EnsureLink(team, spectator, memberTypeId);

            var context = new RoleResolverContext(actor: researcher, subject: researcher);
            Assert.That(evaluator.Evaluate(reqId, in context), Is.True);
        }

        [Test]
        public void EntityCountRequirement_CollectionBackedScopeEvaluatesWithoutAllocationsAfterWarmup()
        {
            using var world = World.Create();
            int workerTag = TagRegistry.Register("Role.Worker");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.TeamZeroAlloc");
            var scopeKeys = new ScopeKeyRegistry();
            int teamScopeId = scopeKeys.Register("team");
            var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            int teamMembersKeyId = collectionKeys.Register("team.members");
            scopeKeys.RegisterCollectionMembers("team", teamMembersKeyId);
            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 8, initialRowCapacity: 16);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(workerTag);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.EntityCount,
                new ScopeKey(ScopeKind.Named, teamScopeId),
                RoleSlot.ScopeMembers,
                progressionId: 0,
                requiredCount: 2,
                requiredTags: in requiredTags));

            var evaluator = new ProgressionRequirementEvaluator(
                world,
                requirements,
                scopeKeys,
                scopeResolver: new ScopeResolver(world, scopeKeys, collections));
            Entity team = world.Create(new ProgressionStateBuffer());
            Entity researcher = world.Create();
            var workerTags = default(GameplayTagContainer);
            workerTags.AddTag(workerTag);
            Entity workerA = world.Create(workerTags);
            Entity workerB = world.Create(workerTags);
            PrepareScopeHost(world, team);
            PrepareScopeMember(world, researcher);
            Assert.That(evaluator.TryBindScope(researcher, teamScopeId, team), Is.True);
            collections.Replace(
                team,
                EntityCollectionDescriptor.Create("team.members", EntityCollectionSourceKind.Explicit, EntityCollectionRoleKind.Display),
                new[] { workerA, workerB });

            var context = new RoleResolverContext(actor: researcher, subject: researcher);
            Assert.That(evaluator.Evaluate(reqId, in context), Is.True);
            for (int i = 0; i < 64; i++)
            {
                evaluator.Evaluate(reqId, in context);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                evaluator.Evaluate(reqId, in context);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void ProgressionLevels_SupportSetAtLeastAndDeltaSemantics()
        {
            using var world = World.Create();
            int progressionId = ProgressionIdRegistry.Register("Progression.WeaponForging");
            int level2ReqId = ProgressionRequirementIdRegistry.Register("Req.WeaponForging2");

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(level2ReqId, CreateSingleNodeRequirement(
                level2ReqId,
                ProgressionRequirementNodeKind.ProgressionLevelAtLeast,
                ScopeKey.Self,
                RoleSlot.ScopeHost,
                progressionId,
                requiredCount: 2));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, new ScopeKeyRegistry());
            Entity city = world.Create(new ProgressionStateBuffer());
            var context = new RoleResolverContext(actor: city, subject: city);

            Assert.That(evaluator.TryApply(city, progressionId, new ProgressionLevelChange(level: 1, delta: 0)), Is.True);
            Assert.That(evaluator.Evaluate(level2ReqId, in context), Is.False);

            Assert.That(evaluator.TryApply(city, progressionId, new ProgressionLevelChange(level: 0, delta: 1)), Is.True);
            Assert.That(evaluator.Evaluate(level2ReqId, in context), Is.True);

            ref readonly var state = ref world.Get<ProgressionStateBuffer>(city);
            Assert.That(state.GetLevel(progressionId), Is.EqualTo(2));
        }

        [Test]
        public void AbilitySystem_UseRequirement_BlocksUntilEntityScopedProgressionCompletes()
        {
            using var world = World.Create();
            int abilityId = AbilityIdRegistry.Register("Ability.TrainGuard");
            int progressionId = ProgressionIdRegistry.Register("Progression.GuardTraining");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.GuardTraining");
            var scopeKeys = new ScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.ProgressionCompleted,
                new ScopeKey(ScopeKind.Named, cityScopeId),
                RoleSlot.ScopeHost,
                progressionId));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
            var definitions = new AbilityDefinitionRegistry();
            var definition = new AbilityDefinition
            {
                UseProgressionRequirementId = reqId,
                HasUseProgressionRequirement = true
            };
            definitions.Register(abilityId, in definition);

            var abilities = new AbilityStateBuffer();
            abilities.AddAbility(abilityId);

            Entity city = world.Create(new ProgressionStateBuffer());
            Entity barracks = world.Create(abilities);
            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);

            var system = new AbilitySystem(world, new EffectRequestQueue(), definitions, progressionRequirements: evaluator);
            Assert.That(system.TryActivateAbility(barracks, 0), Is.False);

            Assert.That(evaluator.TryComplete(city, progressionId), Is.True);
            Assert.That(system.TryActivateAbility(barracks, 0), Is.True);
        }

        [Test]
        public void CompleteProgressionBuiltin_AppliesToExplicitTargetContextScope()
        {
            using var world = World.Create();
            int progressionId = ProgressionIdRegistry.Register("Progression.CityDrill");
            var evaluator = new ProgressionRequirementEvaluator(
                world,
                new ProgressionRequirementRegistry(),
                new ScopeKeyRegistry());
            var runtime = new BuiltinHandlerExecutionContext { ProgressionEvaluator = evaluator };

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            Entity source = world.Create();
            Entity target = world.Create();
            Entity city = world.Create(new ProgressionStateBuffer());
            var context = new EffectContext
            {
                Source = source,
                Target = target,
                TargetContext = city
            };
            var template = new EffectTemplateData
            {
                ProgressionId = progressionId,
                ProgressionScope = new ScopeKey(ScopeKind.Explicit),
                ProgressionChange = new ProgressionLevelChange(level: 2, delta: 0)
            };
            var parameters = default(EffectConfigParams);

            registry.Invoke(
                BuiltinHandlerId.CompleteProgression,
                world,
                default,
                ref context,
                in parameters,
                in template,
                runtime);

            Assert.That(world.Has<ProgressionStateBuffer>(city), Is.True);
            Assert.That(world.Get<ProgressionStateBuffer>(city).HasLevelAtLeast(progressionId, 2), Is.True);
            Assert.That(world.Has<ProgressionStateBuffer>(source), Is.False);
        }

        [Test]
        public void CompleteProgressionBuiltin_ExplicitScopeFailsWithoutTargetContext()
        {
            using var world = World.Create();
            int progressionId = ProgressionIdRegistry.Register("Progression.CityDrill");
            var evaluator = new ProgressionRequirementEvaluator(
                world,
                new ProgressionRequirementRegistry(),
                new ScopeKeyRegistry());
            var runtime = new BuiltinHandlerExecutionContext { ProgressionEvaluator = evaluator };

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            Entity source = world.Create(new ProgressionStateBuffer());
            Entity target = world.Create(new ProgressionStateBuffer());
            var context = new EffectContext
            {
                Source = source,
                Target = target,
                TargetContext = default
            };
            var template = new EffectTemplateData
            {
                ProgressionId = progressionId,
                ProgressionScope = new ScopeKey(ScopeKind.Explicit),
                ProgressionChange = new ProgressionLevelChange(level: 1, delta: 0)
            };
            var parameters = default(EffectConfigParams);

            Assert.Throws<InvalidOperationException>(() =>
                registry.Invoke(
                    BuiltinHandlerId.CompleteProgression,
                    world,
                    default,
                    ref context,
                    in parameters,
                    in template,
                    runtime));
            Assert.That(world.Get<ProgressionStateBuffer>(source).HasCompleted(progressionId), Is.False);
            Assert.That(world.Get<ProgressionStateBuffer>(target).HasCompleted(progressionId), Is.False);
        }

        [Test]
        public void CompleteProgressionBuiltin_DoesNotAddProgressionStateBufferInEffectHotPath()
        {
            using var world = World.Create();
            int progressionId = ProgressionIdRegistry.Register("Progression.CityDrill");
            var evaluator = new ProgressionRequirementEvaluator(
                world,
                new ProgressionRequirementRegistry(),
                new ScopeKeyRegistry());
            var runtime = new BuiltinHandlerExecutionContext { ProgressionEvaluator = evaluator };

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            Entity source = world.Create();
            Entity city = world.Create();
            var context = new EffectContext
            {
                Source = source,
                Target = source,
                TargetContext = city
            };
            var template = new EffectTemplateData
            {
                ProgressionId = progressionId,
                ProgressionScope = new ScopeKey(ScopeKind.Explicit),
                ProgressionChange = new ProgressionLevelChange(level: 1, delta: 0)
            };
            var parameters = default(EffectConfigParams);

            Assert.Throws<InvalidOperationException>(() =>
                registry.Invoke(
                    BuiltinHandlerId.CompleteProgression,
                    world,
                    default,
                    ref context,
                    in parameters,
                    in template,
                    runtime));
            Assert.That(world.Has<ProgressionStateBuffer>(city), Is.False);
        }

        [Test]
        public void EffectProcessingLoop_CompleteProgressionCarriesEvaluatorIntoBuiltinRuntime()
        {
            using var world = World.Create();
            const int templateId = 701;
            int progressionId = ProgressionIdRegistry.Register("Progression.CityDrillViaLoop");
            var evaluator = new ProgressionRequirementEvaluator(
                world,
                new ProgressionRequirementRegistry(),
                new ScopeKeyRegistry());

            var presetTypes = new PresetTypeRegistry();
            var preset = new PresetTypeDefinition
            {
                Type = EffectPresetType.CompleteProgression,
                Components = ComponentFlags.None,
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly
            };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.CompleteProgression);
            presetTypes.Register(in preset);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            var templates = new EffectTemplateRegistry();
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.CompleteProgression,
                LifetimeKind = EffectLifetimeKind.Instant,
                ProgressionId = progressionId,
                ProgressionScope = new ScopeKey(ScopeKind.Explicit),
                ProgressionChange = new ProgressionLevelChange(level: 2, delta: 0)
            });

            var queue = new EffectRequestQueue();
            var graphPrograms = new GraphProgramRegistry();
            var graphApi = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: queue);
            var phaseExecutor = new EffectPhaseExecutor(
                graphPrograms,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates);
            var loop = new EffectProcessingLoopSystem(
                world,
                queue,
                new DiscreteClock(),
                new GasConditionRegistry(),
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                progressionEvaluator: evaluator);

            Entity actor = world.Create();
            Entity target = world.Create();
            Entity city = world.Create(new ProgressionStateBuffer());
            queue.Publish(new EffectRequest
            {
                Source = actor,
                Target = target,
                TargetContext = city,
                TemplateId = templateId
            });

            loop.Update(0f);

            Assert.That(world.Get<ProgressionStateBuffer>(city).HasLevelAtLeast(progressionId, 2), Is.True);
        }

        [Test]
        public void AbilitySystem_ExplicitUseRequirementRequiresTargetContext()
        {
            using var world = World.Create();
            int abilityId = AbilityIdRegistry.Register("Ability.RaiseEliteGuard");
            int progressionId = ProgressionIdRegistry.Register("Progression.CityEliteGuard");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.CityEliteGuard");

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.ProgressionCompleted,
                new ScopeKey(ScopeKind.Explicit),
                RoleSlot.ScopeHost,
                progressionId));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, new ScopeKeyRegistry());
            var definitions = new AbilityDefinitionRegistry();
            var definition = new AbilityDefinition
            {
                UseProgressionRequirementId = reqId,
                HasUseProgressionRequirement = true
            };
            definitions.Register(abilityId, in definition);

            var abilities = new AbilityStateBuffer();
            abilities.AddAbility(abilityId);
            Entity city = world.Create(new ProgressionStateBuffer());
            Entity barracks = world.Create(abilities);
            Assert.That(evaluator.TryComplete(city, progressionId), Is.True);

            var system = new AbilitySystem(world, new EffectRequestQueue(), definitions, progressionRequirements: evaluator);
            Assert.That(system.TryActivateAbility(barracks, 0), Is.False);

            var args = new AbilitySystem.AbilityActivationArgs(barracks, ReadOnlySpan<Entity>.Empty, city);
            Assert.That(system.TryActivateAbility(barracks, 0, in args), Is.True);
        }

        [Test]
        public void AbilityExecSystem_ExplicitUseRequirementWaitsForSelectionGateTargetContext()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int effectTemplateId = 501;
            int abilityId = AbilityIdRegistry.Register("Ability.RaiseCityGuard");
            int progressionId = ProgressionIdRegistry.Register("Progression.CityGuardCharter");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.CityGuardCharter");

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.ProgressionCompleted,
                new ScopeKey(ScopeKind.Explicit),
                RoleSlot.ScopeHost,
                progressionId));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, new ScopeKeyRegistry());
            Entity city = world.Create(new ProgressionStateBuffer());
            Entity actor = CreateCastActor(world, abilityId, castAbilityOrderTypeId, orderId: 21);
            Entity target = world.Create();
            Assert.That(evaluator.TryComplete(city, progressionId), Is.True);

            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.SelectionGate, tick: 0, tagId: 77);
            spec.SetItem(1, ExecItemKind.EffectSignal, tick: 0, templateId: effectTemplateId);

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = spec,
                HasUseProgressionRequirement = true,
                UseProgressionRequirementId = reqId
            });

            var inputRequests = new InputRequestQueue();
            var inputResponses = new InputResponseBuffer();
            var effectRequests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(16);
            var orderTypes = CreateCastOrderTypes(castAbilityOrderTypeId);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                inputRequests,
                inputResponses,
                effectRequests,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: orderTypes,
                progressionRequirements: evaluator);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);
            Assert.That(inputRequests.Count, Is.EqualTo(1));
            ref var waiting = ref world.Get<AbilityExecInstance>(actor);
            Assert.That(waiting.State, Is.EqualTo(AbilityExecRunState.GateWaiting));
            Assert.That(waiting.PendingProgressionUseRequirement, Is.EqualTo(1));
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastFailed), Is.False);

            var response = new InputResponse
            {
                RequestId = 21,
                ResponseTagId = 77,
                Target = target,
                TargetContext = city,
            };
            Assert.That(inputResponses.TryAdd(response), Is.True);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);
            ref var resolved = ref world.Get<AbilityExecInstance>(actor);
            Assert.That(resolved.State, Is.EqualTo(AbilityExecRunState.Running));
            Assert.That(resolved.PendingProgressionUseRequirement, Is.EqualTo(0));
            Assert.That(resolved.TargetContext, Is.EqualTo(city));
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastFailed), Is.False);

            system.Update(0f);

            Assert.That(effectRequests.Count, Is.EqualTo(1));
            Assert.That(effectRequests[0].Target, Is.EqualTo(target));
            Assert.That(effectRequests[0].TargetContext, Is.EqualTo(city));
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False);
        }

        [Test]
        public void AbilityExecSystem_ExplicitUseRequirementFailsAfterSelectionGateWhenScopeLacksProgression()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int effectTemplateId = 502;
            int abilityId = AbilityIdRegistry.Register("Ability.RaiseCityGuardBlocked");
            int progressionId = ProgressionIdRegistry.Register("Progression.CityGuardCharterBlocked");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.CityGuardCharterBlocked");

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.ProgressionCompleted,
                new ScopeKey(ScopeKind.Explicit),
                RoleSlot.ScopeHost,
                progressionId));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, new ScopeKeyRegistry());
            Entity cityWithoutTech = world.Create(new ProgressionStateBuffer());
            Entity actor = CreateCastActor(world, abilityId, castAbilityOrderTypeId, orderId: 22);
            Entity target = world.Create();

            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.SelectionGate, tick: 0, tagId: 78);
            spec.SetItem(1, ExecItemKind.EffectSignal, tick: 0, templateId: effectTemplateId);

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = spec,
                HasUseProgressionRequirement = true,
                UseProgressionRequirementId = reqId
            });

            var inputRequests = new InputRequestQueue();
            var inputResponses = new InputResponseBuffer();
            var effectRequests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(16);
            var orderTypes = CreateCastOrderTypes(castAbilityOrderTypeId);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                inputRequests,
                inputResponses,
                effectRequests,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: orderTypes,
                progressionRequirements: evaluator);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);
            Assert.That(inputRequests.Count, Is.EqualTo(1));
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastFailed), Is.False);

            var response = new InputResponse
            {
                RequestId = 22,
                ResponseTagId = 78,
                Target = target,
                TargetContext = cityWithoutTech,
            };
            Assert.That(inputResponses.TryAdd(response), Is.True);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False);
            Assert.That(effectRequests.Count, Is.EqualTo(0));
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastFailed), Is.True);
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastCommitted), Is.True);
        }

        [Test]
        public void AbilityExecSystem_ExplicitUseRequirementDoesNotDeferPastTimelineSideEffects()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int effectTemplateId = 503;
            int abilityId = AbilityIdRegistry.Register("Ability.UnsafeTimelineBeforeScope");
            int progressionId = ProgressionIdRegistry.Register("Progression.UnsafeTimelineScope");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.UnsafeTimelineScope");

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.ProgressionCompleted,
                new ScopeKey(ScopeKind.Explicit),
                RoleSlot.ScopeHost,
                progressionId));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, new ScopeKeyRegistry());
            Entity actor = CreateCastActor(world, abilityId, castAbilityOrderTypeId, orderId: 23);

            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.EffectSignal, tick: 0, templateId: effectTemplateId);
            spec.SetItem(1, ExecItemKind.SelectionGate, tick: 0, tagId: 79);

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = spec,
                HasUseProgressionRequirement = true,
                UseProgressionRequirementId = reqId
            });

            var inputRequests = new InputRequestQueue();
            var effectRequests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(16);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                inputRequests,
                new InputResponseBuffer(),
                effectRequests,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: CreateCastOrderTypes(castAbilityOrderTypeId),
                progressionRequirements: evaluator);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False);
            Assert.That(inputRequests.Count, Is.EqualTo(0));
            Assert.That(effectRequests.Count, Is.EqualTo(0));
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastFailed), Is.True);
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastStarted), Is.False);
        }

        [Test]
        public void AbilityExecSystem_ExplicitUseRequirementDoesNotDeferAcrossEventGate()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int effectTemplateId = 504;
            int abilityId = AbilityIdRegistry.Register("Ability.EventGateCannotResolveScope");
            int progressionId = ProgressionIdRegistry.Register("Progression.EventGateScope");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.EventGateScope");

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.ProgressionCompleted,
                new ScopeKey(ScopeKind.Explicit),
                RoleSlot.ScopeHost,
                progressionId));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, new ScopeKeyRegistry());
            Entity actor = CreateCastActor(world, abilityId, castAbilityOrderTypeId, orderId: 24);

            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.EventGate, tick: 0, tagId: 80);
            spec.SetItem(1, ExecItemKind.EffectSignal, tick: 0, templateId: effectTemplateId);

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = spec,
                HasUseProgressionRequirement = true,
                UseProgressionRequirementId = reqId
            });

            var effectRequests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(16);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                effectRequests,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: CreateCastOrderTypes(castAbilityOrderTypeId),
                progressionRequirements: evaluator);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False);
            Assert.That(effectRequests.Count, Is.EqualTo(0));
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastFailed), Is.True);
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastStarted), Is.False);
        }

        [Test]
        public void UsesGraphValidation_DetectsNestedGraphRequirement()
        {
            using var world = World.Create();
            int graphReqId = ProgressionRequirementIdRegistry.Register("Req.GraphBacked");
            var requirements = new ProgressionRequirementRegistry();
            var emptyTags = default(GameplayTagContainer);
            var nodes = new[]
            {
                new ProgressionRequirementNode(
                    ProgressionRequirementNodeKind.All,
                    ScopeKey.Self,
                    RoleSlot.Subject,
                    firstChild: 0,
                    childCount: 1,
                    progressionId: 0,
                    requiredCount: 0,
                    graphProgramId: 0,
                    in emptyTags),
                new ProgressionRequirementNode(
                    ProgressionRequirementNodeKind.GraphValidation,
                    ScopeKey.Self,
                    RoleSlot.Subject,
                    firstChild: 0,
                    childCount: 0,
                    progressionId: 0,
                    requiredCount: 0,
                    graphProgramId: 12,
                    in emptyTags)
            };
            requirements.Register(graphReqId, new ProgressionRequirementDefinition(graphReqId, nodes, new[] { 1 }));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, new ScopeKeyRegistry());

            Assert.That(evaluator.UsesGraphValidation(graphReqId), Is.True);
        }

        [Test]
        public void ComputeRevision_ForScopeMembersChangesWhenMemberTagsChange()
        {
            using var world = World.Create();
            int heroTag = TagRegistry.Register("Hero.GuanYu");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.GuanYuInCity");
            var scopeKeys = new ScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(heroTag);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.EntityCount,
                new ScopeKey(ScopeKind.Named, cityScopeId),
                RoleSlot.ScopeMembers,
                progressionId: 0,
                requiredCount: 1,
                requiredTags: in requiredTags));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
            Entity city = world.Create(new ProgressionStateBuffer());
            Entity barracks = world.Create();
            Entity hero = world.Create(new GameplayTagContainer());
            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            PrepareScopeMember(world, hero);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);
            Assert.That(evaluator.TryBindScope(hero, cityScopeId, city), Is.True);

            var context = new RoleResolverContext(actor: barracks, subject: barracks);
            uint before = evaluator.ComputeRevision(reqId, in context);
            Assert.That(evaluator.Evaluate(reqId, in context), Is.False);

            ref var heroTags = ref world.Get<GameplayTagContainer>(hero);
            heroTags.AddTag(heroTag);

            uint after = evaluator.ComputeRevision(reqId, in context);
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(evaluator.Evaluate(reqId, in context), Is.True);
        }

        [Test]
        public void ComputeScopeRevision_ForScopeMembersChangesWhenEffectiveTagsChange()
        {
            using var world = World.Create();
            int heroTag = TagRegistry.Register("Hero.GuanYu");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.GuanYuInCity");
            var scopeKeys = new ScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(heroTag);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.EntityCount,
                new ScopeKey(ScopeKind.Named, cityScopeId),
                RoleSlot.ScopeMembers,
                progressionId: 0,
                requiredCount: 1,
                requiredTags: in requiredTags));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
            Entity city = world.Create(new ProgressionStateBuffer());
            Entity barracks = world.Create();
            Entity hero = world.Create(new GameplayTagContainer(), new TagCountContainer(), new DirtyFlags());
            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            PrepareScopeMember(world, hero);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);
            Assert.That(evaluator.TryBindScope(hero, cityScopeId, city), Is.True);

            var context = new RoleResolverContext(actor: barracks, subject: barracks);
            uint before = evaluator.ComputeScopeRevision(reqId, in context);
            uint hostRevisionBefore = world.Get<ScopeMembershipRevision>(city).Revision;

            ref var heroTags = ref world.Get<GameplayTagContainer>(hero);
            ref var heroCounts = ref world.Get<TagCountContainer>(hero);
            ref var dirty = ref world.Get<DirtyFlags>(hero);
            var tagOps = new TagOps();
            Assert.That(tagOps.AddTag(ref heroTags, ref heroCounts, heroTag, ref dirty), Is.True);

            var triggerQueue = new DeferredTriggerQueue();
            var collectionSystem = new DeferredTriggerCollectionSystem(world, triggerQueue, tagOps);
            collectionSystem.Update(0f);
            Assert.That(world.Has<GameplayTagEffectiveChangedBits>(hero), Is.True);

            var revisionSystem = new ProgressionScopeTagRevisionSystem(world);
            revisionSystem.Update(0f);

            uint after = evaluator.ComputeScopeRevision(reqId, in context);
            Assert.That(world.Get<ScopeMembershipRevision>(city).Revision, Is.GreaterThan(hostRevisionBefore));
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(evaluator.Evaluate(reqId, in context), Is.True);
        }

        [Test]
        public void RebindingScopeMember_BumpsOldAndNewScopeRevisions()
        {
            using var world = World.Create();
            int heroTag = TagRegistry.Register("Hero.GuanYu");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.GuanYuInCity");
            var scopeKeys = new ScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(heroTag);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.EntityCount,
                new ScopeKey(ScopeKind.Named, cityScopeId),
                RoleSlot.ScopeMembers,
                progressionId: 0,
                requiredCount: 1,
                requiredTags: in requiredTags));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
            Entity cityA = world.Create(new ProgressionStateBuffer());
            Entity cityB = world.Create(new ProgressionStateBuffer());
            Entity barracksA = world.Create();
            Entity barracksB = world.Create();
            var tags = default(GameplayTagContainer);
            tags.AddTag(heroTag);
            Entity hero = world.Create(tags);
            PrepareScopeHost(world, cityA);
            PrepareScopeHost(world, cityB);
            PrepareScopeMember(world, barracksA);
            PrepareScopeMember(world, barracksB);
            PrepareScopeMember(world, hero);
            Assert.That(evaluator.TryBindScope(barracksA, cityScopeId, cityA), Is.True);
            Assert.That(evaluator.TryBindScope(barracksB, cityScopeId, cityB), Is.True);
            Assert.That(evaluator.TryBindScope(hero, cityScopeId, cityA), Is.True);

            var contextA = new RoleResolverContext(actor: barracksA, subject: barracksA);
            var contextB = new RoleResolverContext(actor: barracksB, subject: barracksB);
            Assert.That(evaluator.Evaluate(reqId, in contextA), Is.True);
            Assert.That(evaluator.Evaluate(reqId, in contextB), Is.False);
            uint revisionA = evaluator.ComputeScopeRevision(reqId, in contextA);
            uint revisionB = evaluator.ComputeScopeRevision(reqId, in contextB);

            Assert.That(evaluator.TryBindScope(hero, cityScopeId, cityB), Is.True);

            Assert.That(evaluator.ComputeScopeRevision(reqId, in contextA), Is.Not.EqualTo(revisionA));
            Assert.That(evaluator.ComputeScopeRevision(reqId, in contextB), Is.Not.EqualTo(revisionB));
            Assert.That(evaluator.Evaluate(reqId, in contextA), Is.False);
            Assert.That(evaluator.Evaluate(reqId, in contextB), Is.True);
        }

        [Test]
        public void ComputeScopeRevision_ForScopeHostTagAllChangesWhenEffectiveTagsChange()
        {
            using var world = World.Create();
            int fortifiedTag = TagRegistry.Register("City.Fortified");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.CityFortified");
            var scopeKeys = new ScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(fortifiedTag);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.TagAll,
                new ScopeKey(ScopeKind.Named, cityScopeId),
                RoleSlot.ScopeHost,
                progressionId: 0,
                requiredTags: in requiredTags));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
            Entity city = world.Create(new ProgressionStateBuffer(), new GameplayTagContainer(), new TagCountContainer(), new DirtyFlags());
            Entity barracks = world.Create();
            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);

            var context = new RoleResolverContext(actor: barracks, subject: barracks);
            uint before = evaluator.ComputeScopeRevision(reqId, in context);
            uint hostRevisionBefore = world.Get<ScopeMembershipRevision>(city).Revision;
            Assert.That(evaluator.Evaluate(reqId, in context), Is.False);

            ref var cityTags = ref world.Get<GameplayTagContainer>(city);
            ref var cityCounts = ref world.Get<TagCountContainer>(city);
            ref var dirty = ref world.Get<DirtyFlags>(city);
            var tagOps = new TagOps();
            Assert.That(tagOps.AddTag(ref cityTags, ref cityCounts, fortifiedTag, ref dirty), Is.True);

            var triggerQueue = new DeferredTriggerQueue();
            var collectionSystem = new DeferredTriggerCollectionSystem(world, triggerQueue, tagOps);
            collectionSystem.Update(0f);
            Assert.That(world.Has<GameplayTagEffectiveChangedBits>(city), Is.True);

            var revisionSystem = new ProgressionScopeTagRevisionSystem(world);
            revisionSystem.Update(0f);

            uint after = evaluator.ComputeScopeRevision(reqId, in context);
            Assert.That(world.Get<ScopeMembershipRevision>(city).Revision, Is.GreaterThan(hostRevisionBefore));
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(evaluator.Evaluate(reqId, in context), Is.True);
        }

        [Test]
        public void ProgressionConfigLoader_LoadsExplicitScopesAndFreezesNamedScopes()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Progression"));
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "scopes.json"),
                    """
                    [
                      { "id": "city" },
                      { "id": "province" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "progressions.json"),
                    """
                    [
                      { "id": "Progression.CityArchery", "scope": "city" },
                      { "id": "Progression.ProvinceTrade", "scope": "province" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "requirements.json"),
                    """
                    [
                      {
                        "id": "Req.CityArchery2",
                        "root": {
                          "kind": "ProgressionLevelAtLeast",
                          "progression": "Progression.CityArchery",
                          "scope": "city",
                          "entitySource": "ScopeHost",
                          "level": 2
                        }
                      }
                    ]
                    """);

                var loader = CreateProgressionLoader(root, out var requirements, out var scopeKeys, out var progressions, out _);
                loader.Load(CreateProgressionCatalog());

                int progressionId = ProgressionIdRegistry.GetId("Progression.CityArchery");
                Assert.That(progressions.TryGet(progressionId, out var progression), Is.True);
                Assert.That(progression.DeclaredScope.Kind, Is.EqualTo(ScopeKind.Named));
                Assert.That(progression.DeclaredScope.ScopeKeyId, Is.EqualTo(scopeKeys.GetId("city")));

                int requirementId = ProgressionRequirementIdRegistry.GetId("Req.CityArchery2");
                Assert.That(requirements.TryGet(requirementId, out var requirement), Is.True);
                Assert.That(requirement.Nodes[0].Scope.Kind, Is.EqualTo(ScopeKind.Named));
                Assert.That(requirement.Nodes[0].Scope.ScopeKeyId, Is.EqualTo(scopeKeys.GetId("city")));
                Assert.That(requirement.Nodes[0].EntitySource, Is.EqualTo(RoleSlot.ScopeHost));

                using var world = World.Create();
                var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
                Entity actor = world.Create();
                Entity province = world.Create();
                PrepareScopeHost(world, province);
                PrepareScopeMember(world, actor);
                Assert.That(evaluator.TryBindScope(actor, "province", province), Is.True);
                Assert.That(evaluator.TryBindScope(actor, "unknownScope", province), Is.False);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void ProgressionConfigLoader_LoadsCollectionBackedScopeMembership()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Progression"));
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "scopes.json"),
                    """
                    [
                      { "id": "team", "memberSource": "EntityCollection", "collection": "team.members" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "progressions.json"),
                    """
                    [
                      { "id": "Progression.TeamLogistics", "scope": "team" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "requirements.json"), "[]");

                using var world = World.Create();
                var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 8, initialRowCapacity: 16);
                var loader = CreateProgressionLoader(root, out _, out var scopeKeys, out _, out _, collections);

                loader.Load(CreateProgressionCatalog());

                int teamScopeId = scopeKeys.GetId("team");
                int teamMembersKeyId = collections.KeyRegistry.GetId("team.members");
                Assert.That(teamScopeId, Is.GreaterThan(0));
                Assert.That(teamMembersKeyId, Is.GreaterThan(0));
                Assert.That(scopeKeys.TryGetMembershipSource(teamScopeId, out ScopeMembershipSource source), Is.True);
                Assert.That(source.Kind, Is.EqualTo(ScopeMembershipSourceKind.EntityCollection));
                Assert.That(source.KeyId, Is.EqualTo(teamMembersKeyId));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void ProgressionConfigLoader_RequiresExplicitProgressionScope()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Progression"));
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "scopes.json"),
                    """
                    [
                      { "id": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "progressions.json"),
                    """
                    [
                      { "id": "Progression.CityArchery" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "requirements.json"), "[]");

                var loader = CreateProgressionLoader(root, out _, out _);
                var ex = Assert.Throws<AggregateException>(() => loader.Load(CreateProgressionCatalog()));
                Assert.That(ex!.InnerException?.Message, Does.Contain("Progression 'Progression.CityArchery'.scope is required"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void ProgressionConfigLoader_RequiresExplicitRequirementScope()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Progression"));
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "scopes.json"),
                    """
                    [
                      { "id": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "progressions.json"),
                    """
                    [
                      { "id": "Progression.CityArchery", "scope": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "requirements.json"),
                    """
                    [
                      {
                        "id": "Req.CityArchery2",
                        "root": {
                          "kind": "ProgressionLevelAtLeast",
                          "progression": "Progression.CityArchery",
                          "entitySource": "ScopeHost",
                          "level": 2
                        }
                      }
                    ]
                    """);

                var loader = CreateProgressionLoader(root, out _, out _);
                var ex = Assert.Throws<AggregateException>(() => loader.Load(CreateProgressionCatalog()));
                Assert.That(ex!.InnerException?.Message, Does.Contain("Req.CityArchery2.root.scope is required"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void ProgressionConfigLoader_RequiresExplicitRequirementEntitySource()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Progression"));
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "scopes.json"),
                    """
                    [
                      { "id": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "progressions.json"),
                    """
                    [
                      { "id": "Progression.CityArchery", "scope": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "requirements.json"),
                    """
                    [
                      {
                        "id": "Req.CityArchery2",
                        "root": {
                          "kind": "ProgressionLevelAtLeast",
                          "progression": "Progression.CityArchery",
                          "scope": "city",
                          "level": 2
                        }
                      }
                    ]
                    """);

                var loader = CreateProgressionLoader(root, out _, out _);
                var ex = Assert.Throws<AggregateException>(() => loader.Load(CreateProgressionCatalog()));
                Assert.That(ex!.InnerException?.Message, Does.Contain("Req.CityArchery2.root.entitySource is required"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void EffectTemplateLoader_CompleteProgression_LoadsExplicitScopeAndLevel()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Progression"));
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "scopes.json"),
                    """
                    [
                      { "id": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "progressions.json"),
                    """
                    [
                      { "id": "Progression.CityArmor", "scope": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "requirements.json"), "[]");
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect.CompleteCityArmor2",
                        "presetType": "CompleteProgression",
                        "lifetime": "Instant",
                        "participatesInResponse": false,
                        "progression": { "id": "Progression.CityArmor", "scope": "city", "level": 2 }
                      }
                    ]
                    """);

                var progressionLoader = CreateProgressionLoader(root, out _, out var scopeKeys, out _, out var pipeline);
                progressionLoader.Load(CreateProgressionCatalog());

                var effects = new EffectTemplateRegistry();
                var effectLoader = new EffectTemplateLoader(
                    pipeline,
                    effects,
                    progressionScopeKeys: scopeKeys);
                effectLoader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                int templateId = EffectTemplateIdRegistry.GetId("Effect.CompleteCityArmor2");
                Assert.That(effects.TryGet(templateId, out var template), Is.True);
                Assert.That(template.PresetType, Is.EqualTo(EffectPresetType.CompleteProgression));
                Assert.That(template.ProgressionScope.Kind, Is.EqualTo(ScopeKind.Named));
                Assert.That(template.ProgressionScope.ScopeKeyId, Is.EqualTo(scopeKeys.GetId("city")));
                Assert.That(template.ProgressionChange.Level, Is.EqualTo(2));
                Assert.That(template.ProgressionChange.Delta, Is.EqualTo(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void EffectTemplateLoader_CompleteProgression_RequiresExplicitScope()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Progression"));
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "scopes.json"),
                    """
                    [
                      { "id": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "progressions.json"),
                    """
                    [
                      { "id": "Progression.CityArmor", "scope": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Progression", "requirements.json"), "[]");
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect.CompleteCityArmor2",
                        "presetType": "CompleteProgression",
                        "lifetime": "Instant",
                        "participatesInResponse": false,
                        "progression": { "id": "Progression.CityArmor", "level": 2 }
                      }
                    ]
                    """);

                var progressionLoader = CreateProgressionLoader(root, out _, out var scopeKeys, out _, out var pipeline);
                progressionLoader.Load(CreateProgressionCatalog());

                var effects = new EffectTemplateRegistry();
                var effectLoader = new EffectTemplateLoader(
                    pipeline,
                    effects,
                    progressionScopeKeys: scopeKeys);
                var ex = Assert.Throws<InvalidOperationException>(() =>
                    effectLoader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json"));
                Assert.That(ex!.Message, Does.Contain("progression.scope is required"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void ProgressionScopeAuthoring_BindsMembersToConfiguredScopeHosts()
        {
            using var world = World.Create();
            int progressionId = ProgressionIdRegistry.Register("Progression.CityDrill");
            int reqId = ProgressionRequirementIdRegistry.Register("Req.CityDrill");
            var scopeKeys = new ScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requirements = new ProgressionRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                ProgressionRequirementNodeKind.ProgressionCompleted,
                new ScopeKey(ScopeKind.Named, cityScopeId),
                RoleSlot.ScopeHost,
                progressionId));

            var evaluator = new ProgressionRequirementEvaluator(world, requirements, scopeKeys);
            var bindingSystem = new ProgressionScopeBindingSystem(world, evaluator, scopeKeys);

            Entity cityA = world.Create();
            Entity cityB = world.Create();
            Entity barracksA = world.Create();
            Entity barracksB = world.Create();
            Ludots.Core.Config.ComponentRegistry.Apply(cityA, "ProgressionScopeHost", JsonNode.Parse("""{ "scope": "city", "hostKey": "chang_an" }""")!);
            Ludots.Core.Config.ComponentRegistry.Apply(cityB, "ProgressionScopeHost", JsonNode.Parse("""{ "scope": "city", "hostKey": "luo_yang" }""")!);
            Ludots.Core.Config.ComponentRegistry.Apply(barracksA, "ProgressionScopeBinding", JsonNode.Parse("""{ "scope": "city", "hostKey": "chang_an" }""")!);
            Ludots.Core.Config.ComponentRegistry.Apply(barracksB, "ProgressionScopeBinding", JsonNode.Parse("""{ "scope": "city", "hostKey": "luo_yang" }""")!);

            bindingSystem.Update(0f);
            Assert.That(evaluator.TryComplete(cityA, progressionId), Is.True);

            var contextA = new RoleResolverContext(actor: barracksA, subject: barracksA);
            var contextB = new RoleResolverContext(actor: barracksB, subject: barracksB);
            Assert.That(evaluator.Evaluate(reqId, in contextA), Is.True);
            Assert.That(evaluator.Evaluate(reqId, in contextB), Is.False);

            uint before = world.Get<ScopeMembershipRevision>(cityA).Revision;
            bindingSystem.Update(0f);
            Assert.That(world.Get<ScopeMembershipRevision>(cityA).Revision, Is.EqualTo(before));
        }

        [Test]
        public void ProgressionScopeAuthoring_FailsFastWhenBindingHostIsMissing()
        {
            using var world = World.Create();
            var scopeKeys = new ScopeKeyRegistry();
            scopeKeys.Register("city");
            var evaluator = new ProgressionRequirementEvaluator(
                world,
                new ProgressionRequirementRegistry(),
                scopeKeys);
            var bindingSystem = new ProgressionScopeBindingSystem(world, evaluator, scopeKeys);

            Entity barracks = world.Create();
            Ludots.Core.Config.ComponentRegistry.Apply(barracks, "ProgressionScopeBinding", JsonNode.Parse("""{ "scope": "city", "hostKey": "missing_city" }""")!);

            var ex = Assert.Throws<InvalidOperationException>(() => bindingSystem.Update(0f));
            Assert.That(ex?.Message, Does.Contain("missing host"));
            Assert.That(ex?.Message, Does.Contain("city"));
            Assert.That(ex?.Message, Does.Contain("missing_city"));
        }

        [Test]
        public void TryBindScope_RequiresPreallocatedAuthoringComponents()
        {
            using var world = World.Create();
            var scopeKeys = new ScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);
            var evaluator = new ProgressionRequirementEvaluator(
                world,
                new ProgressionRequirementRegistry(),
                scopeKeys);
            Entity city = world.Create(new ProgressionStateBuffer());
            Entity barracks = world.Create();

            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.False);

            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);
        }

        private static ProgressionRequirementDefinition CreateSingleNodeRequirement(
            int requirementId,
            ProgressionRequirementNodeKind kind,
            ScopeKey scope,
            RoleSlot entitySource,
            int progressionId,
            int requiredCount = 1,
            int graphProgramId = 0,
            in GameplayTagContainer requiredTags = default)
        {
            var nodes = new[]
            {
                new ProgressionRequirementNode(
                    kind,
                    scope,
                    entitySource,
                    firstChild: 0,
                    childCount: 0,
                    progressionId,
                    requiredCount,
                    graphProgramId,
                    in requiredTags)
            };
            return new ProgressionRequirementDefinition(requirementId, nodes, Array.Empty<int>());
        }

        private static int RegisterCityScope(ScopeKeyRegistry scopeKeys)
        {
            return scopeKeys.Register("city");
        }

        private static void PrepareScopeHost(World world, Entity entity)
        {
            if (!world.Has<ScopeMembershipRevision>(entity))
            {
                world.Add(entity, new ScopeMembershipRevision());
            }
        }

        private static void PrepareScopeMember(World world, Entity entity)
        {
            if (!world.Has<ScopeRefBuffer>(entity))
            {
                world.Add(entity, new ScopeRefBuffer());
            }

            if (!world.Has<ScopeMemberTag>(entity))
            {
                world.Add(entity, new ScopeMemberTag());
            }
        }

        private static Entity CreateCastActor(World world, int abilityId, int castAbilityOrderTypeId, int orderId)
        {
            var abilities = new AbilityStateBuffer();
            abilities.AddAbility(abilityId);
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                abilities);

            var order = new Order
            {
                OrderId = orderId,
                Actor = actor,
                OrderTypeId = castAbilityOrderTypeId,
                Args = new OrderArgs { I0 = 0 }
            };
            ref var orderBuffer = ref world.Get<OrderBuffer>(actor);
            orderBuffer.SetActiveDirect(in order, priority: 100);

            ref var bbInts = ref world.Get<BlackboardIntBuffer>(actor);
            bbInts.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);
            return actor;
        }

        private static OrderTypeRegistry CreateCastOrderTypes(int castAbilityOrderTypeId)
        {
            var orderTypes = new OrderTypeRegistry();
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = castAbilityOrderTypeId,
                AllowQueuedMode = false,
                ClearQueueOnActivate = true,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
                EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                SpatialBlackboardKey = OrderBlackboardKeys.Cast_TargetPosition
            });
            return orderTypes;
        }

        private static bool ContainsPresentationEvent(GasPresentationEventBuffer events, GasPresentationEventKind kind)
        {
            var span = events.Events;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static ProgressionConfigLoader CreateProgressionLoader(
            string root,
            out ProgressionRequirementRegistry requirements,
            out ScopeKeyRegistry scopeKeys)
        {
            return CreateProgressionLoader(root, out requirements, out scopeKeys, out _, out _);
        }

        private static ProgressionConfigLoader CreateProgressionLoader(
            string root,
            out ProgressionRequirementRegistry requirements,
            out ScopeKeyRegistry scopeKeys,
            out ProgressionDefinitionRegistry progressions,
            out ConfigPipeline pipeline,
            EntityCollectionStore? collections = null,
            RelationshipTypeRegistry? relationshipTypes = null)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            pipeline = new ConfigPipeline(vfs, modLoader);
            progressions = new ProgressionDefinitionRegistry();
            requirements = new ProgressionRequirementRegistry();
            scopeKeys = new ScopeKeyRegistry();
            return new ProgressionConfigLoader(pipeline, progressions, requirements, scopeKeys, collections, relationshipTypes);
        }

        private static ConfigCatalog CreateProgressionCatalog()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("Progression/scopes.json", ConfigMergePolicy.ArrayById, "id"));
            catalog.Add(new ConfigCatalogEntry("Progression/progressions.json", ConfigMergePolicy.ArrayById, "id"));
            catalog.Add(new ConfigCatalogEntry("Progression/requirements.json", ConfigMergePolicy.ArrayById, "id"));
            return catalog;
        }

        private static ConfigCatalog CreateEffectsCatalog()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/effects.json", ConfigMergePolicy.ArrayById, "id"));
            return catalog;
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ProgressionRequirementTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
