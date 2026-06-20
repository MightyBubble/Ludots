using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Technology;
using Ludots.Core.Gameplay.Technology.Components;
using Ludots.Core.Gameplay.Technology.Config;
using Ludots.Core.Gameplay.Technology.Registry;
using Ludots.Core.Gameplay.Technology.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using System.Text.Json.Nodes;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class TechnologyRequirementTests
    {
        [SetUp]
        public void SetUp()
        {
            AbilityIdRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            TagRegistry.Clear();
            TechnologyIdRegistry.Clear();
            TechnologyRequirementIdRegistry.Clear();
        }

        [Test]
        public void CityScopedTechnology_UnlocksOnlyEntitiesBoundToThatCity()
        {
            using var world = World.Create();
            int techId = TechnologyIdRegistry.Register("Tech.CitySpears");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.CitySpears");
            var scopeKeys = new TechnologyScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.TechCompleted,
                new TechnologyScopeSpec(TechnologyScopeKind.Named, cityScopeId),
                TechnologyRequirementEntitySource.ScopeMembers,
                techId));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, scopeKeys);
            Entity cityA = world.Create(new TechnologyStateBuffer());
            Entity cityB = world.Create(new TechnologyStateBuffer());
            Entity barracksA = world.Create();
            Entity barracksB = world.Create();
            PrepareScopeHost(world, cityA);
            PrepareScopeHost(world, cityB);
            PrepareScopeMember(world, barracksA);
            PrepareScopeMember(world, barracksB);

            Assert.That(evaluator.TryBindScope(barracksA, cityScopeId, cityA), Is.True);
            Assert.That(evaluator.TryBindScope(barracksB, cityScopeId, cityB), Is.True);
            Assert.That(evaluator.TryComplete(cityA, techId), Is.True);

            var contextA = new TechnologyRequirementEvaluationContext(barracksA, barracksA);
            var contextB = new TechnologyRequirementEvaluationContext(barracksB, barracksB);
            Assert.That(evaluator.Evaluate(reqId, in contextA), Is.True);
            Assert.That(evaluator.Evaluate(reqId, in contextB), Is.False);
        }

        [Test]
        public void EntityCountRequirement_CanRequireTaggedHeroInsideCityScope()
        {
            using var world = World.Create();
            int heroTag = TagRegistry.Register("Hero.GuanYu");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.GuanYuInCity");
            var scopeKeys = new TechnologyScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var tags = default(GameplayTagContainer);
            tags.AddTag(heroTag);

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.EntityCount,
                new TechnologyScopeSpec(TechnologyScopeKind.Named, cityScopeId),
                TechnologyRequirementEntitySource.ScopeMembers,
                technologyId: 0,
                requiredCount: 1,
                requiredTags: in tags));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, scopeKeys);
            Entity cityA = world.Create(new TechnologyStateBuffer());
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

            var contextA = new TechnologyRequirementEvaluationContext(barracksA, barracksA);
            var contextB = new TechnologyRequirementEvaluationContext(barracksB, barracksB);
            Assert.That(evaluator.Evaluate(reqId, in contextA), Is.True);
            Assert.That(evaluator.Evaluate(reqId, in contextB), Is.False);
        }

        [Test]
        public void TechnologyLevels_SupportSetAtLeastAndDeltaSemantics()
        {
            using var world = World.Create();
            int techId = TechnologyIdRegistry.Register("Tech.WeaponForging");
            int level2ReqId = TechnologyRequirementIdRegistry.Register("Req.WeaponForging2");

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(level2ReqId, CreateSingleNodeRequirement(
                level2ReqId,
                TechnologyRequirementNodeKind.TechLevelAtLeast,
                TechnologyScopeSpec.Self,
                TechnologyRequirementEntitySource.ScopeHost,
                techId,
                requiredCount: 2));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, new TechnologyScopeKeyRegistry());
            Entity city = world.Create(new TechnologyStateBuffer());
            var context = new TechnologyRequirementEvaluationContext(city, city);

            Assert.That(evaluator.TryApply(city, techId, new TechnologyLevelChange(level: 1, delta: 0)), Is.True);
            Assert.That(evaluator.Evaluate(level2ReqId, in context), Is.False);

            Assert.That(evaluator.TryApply(city, techId, new TechnologyLevelChange(level: 0, delta: 1)), Is.True);
            Assert.That(evaluator.Evaluate(level2ReqId, in context), Is.True);

            ref readonly var state = ref world.Get<TechnologyStateBuffer>(city);
            Assert.That(state.GetLevel(techId), Is.EqualTo(2));
        }

        [Test]
        public void AbilitySystem_UseRequirement_BlocksUntilEntityScopedTechnologyCompletes()
        {
            using var world = World.Create();
            int abilityId = AbilityIdRegistry.Register("Ability.TrainGuard");
            int techId = TechnologyIdRegistry.Register("Tech.GuardTraining");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.GuardTraining");
            var scopeKeys = new TechnologyScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.TechCompleted,
                new TechnologyScopeSpec(TechnologyScopeKind.Named, cityScopeId),
                TechnologyRequirementEntitySource.ScopeHost,
                techId));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, scopeKeys);
            var definitions = new AbilityDefinitionRegistry();
            var definition = new AbilityDefinition
            {
                UseTechnologyRequirementId = reqId,
                HasUseTechnologyRequirement = true
            };
            definitions.Register(abilityId, in definition);

            var abilities = new AbilityStateBuffer();
            abilities.AddAbility(abilityId);

            Entity city = world.Create(new TechnologyStateBuffer());
            Entity barracks = world.Create(abilities);
            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);

            var system = new AbilitySystem(world, effectRequests: null, definitions, technologyRequirements: evaluator);
            Assert.That(system.TryActivateAbility(barracks, 0), Is.False);

            Assert.That(evaluator.TryComplete(city, techId), Is.True);
            Assert.That(system.TryActivateAbility(barracks, 0), Is.True);
        }

        [Test]
        public void CompleteTechnologyBuiltin_AppliesToExplicitTargetContextScope()
        {
            using var world = World.Create();
            int techId = TechnologyIdRegistry.Register("Tech.CityDrill");
            var evaluator = new TechnologyRequirementEvaluator(
                world,
                new TechnologyRequirementRegistry(),
                new TechnologyScopeKeyRegistry());
            var runtime = new BuiltinHandlerExecutionContext { TechnologyEvaluator = evaluator };

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            Entity source = world.Create();
            Entity target = world.Create();
            Entity city = world.Create(new TechnologyStateBuffer());
            var context = new EffectContext
            {
                Source = source,
                Target = target,
                TargetContext = city
            };
            var template = new EffectTemplateData
            {
                TechnologyId = techId,
                TechnologyScope = new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                TechnologyChange = new TechnologyLevelChange(level: 2, delta: 0)
            };
            var parameters = default(EffectConfigParams);

            registry.Invoke(
                BuiltinHandlerId.CompleteTechnology,
                world,
                default,
                ref context,
                in parameters,
                in template,
                runtime);

            Assert.That(world.Has<TechnologyStateBuffer>(city), Is.True);
            Assert.That(world.Get<TechnologyStateBuffer>(city).HasLevelAtLeast(techId, 2), Is.True);
            Assert.That(world.Has<TechnologyStateBuffer>(source), Is.False);
        }

        [Test]
        public void CompleteTechnologyBuiltin_ExplicitScopeFailsWithoutTargetContext()
        {
            using var world = World.Create();
            int techId = TechnologyIdRegistry.Register("Tech.CityDrill");
            var evaluator = new TechnologyRequirementEvaluator(
                world,
                new TechnologyRequirementRegistry(),
                new TechnologyScopeKeyRegistry());
            var runtime = new BuiltinHandlerExecutionContext { TechnologyEvaluator = evaluator };

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            Entity source = world.Create(new TechnologyStateBuffer());
            Entity target = world.Create(new TechnologyStateBuffer());
            var context = new EffectContext
            {
                Source = source,
                Target = target,
                TargetContext = default
            };
            var template = new EffectTemplateData
            {
                TechnologyId = techId,
                TechnologyScope = new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                TechnologyChange = new TechnologyLevelChange(level: 1, delta: 0)
            };
            var parameters = default(EffectConfigParams);

            Assert.Throws<InvalidOperationException>(() =>
                registry.Invoke(
                    BuiltinHandlerId.CompleteTechnology,
                    world,
                    default,
                    ref context,
                    in parameters,
                    in template,
                    runtime));
            Assert.That(world.Get<TechnologyStateBuffer>(source).HasCompleted(techId), Is.False);
            Assert.That(world.Get<TechnologyStateBuffer>(target).HasCompleted(techId), Is.False);
        }

        [Test]
        public void CompleteTechnologyBuiltin_DoesNotAddTechnologyStateBufferInEffectHotPath()
        {
            using var world = World.Create();
            int techId = TechnologyIdRegistry.Register("Tech.CityDrill");
            var evaluator = new TechnologyRequirementEvaluator(
                world,
                new TechnologyRequirementRegistry(),
                new TechnologyScopeKeyRegistry());
            var runtime = new BuiltinHandlerExecutionContext { TechnologyEvaluator = evaluator };

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
                TechnologyId = techId,
                TechnologyScope = new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                TechnologyChange = new TechnologyLevelChange(level: 1, delta: 0)
            };
            var parameters = default(EffectConfigParams);

            Assert.Throws<InvalidOperationException>(() =>
                registry.Invoke(
                    BuiltinHandlerId.CompleteTechnology,
                    world,
                    default,
                    ref context,
                    in parameters,
                    in template,
                    runtime));
            Assert.That(world.Has<TechnologyStateBuffer>(city), Is.False);
        }

        [Test]
        public void EffectProcessingLoop_CompleteTechnologyCarriesEvaluatorIntoBuiltinRuntime()
        {
            using var world = World.Create();
            const int templateId = 701;
            int techId = TechnologyIdRegistry.Register("Tech.CityDrillViaLoop");
            var evaluator = new TechnologyRequirementEvaluator(
                world,
                new TechnologyRequirementRegistry(),
                new TechnologyScopeKeyRegistry());

            var presetTypes = new PresetTypeRegistry();
            var preset = new PresetTypeDefinition
            {
                Type = EffectPresetType.CompleteTechnology,
                Components = ComponentFlags.None,
                ActivePhases = PhaseFlags.InstantCore,
                AllowedLifetimes = LifetimeFlags.InstantOnly
            };
            preset.DefaultPhaseHandlers[EffectPhaseId.OnApply] = PhaseHandler.Builtin(BuiltinHandlerId.CompleteTechnology);
            presetTypes.Register(in preset);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);

            var templates = new EffectTemplateRegistry();
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.CompleteTechnology,
                LifetimeKind = EffectLifetimeKind.Instant,
                TechnologyId = techId,
                TechnologyScope = new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                TechnologyChange = new TechnologyLevelChange(level: 2, delta: 0)
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
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                technologyEvaluator: evaluator);

            Entity actor = world.Create();
            Entity target = world.Create();
            Entity city = world.Create(new TechnologyStateBuffer());
            queue.Publish(new EffectRequest
            {
                Source = actor,
                Target = target,
                TargetContext = city,
                TemplateId = templateId
            });

            loop.Update(0f);

            Assert.That(world.Get<TechnologyStateBuffer>(city).HasLevelAtLeast(techId, 2), Is.True);
        }

        [Test]
        public void AbilitySystem_ExplicitUseRequirementRequiresTargetContext()
        {
            using var world = World.Create();
            int abilityId = AbilityIdRegistry.Register("Ability.RaiseEliteGuard");
            int techId = TechnologyIdRegistry.Register("Tech.CityEliteGuard");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.CityEliteGuard");

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.TechCompleted,
                new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                TechnologyRequirementEntitySource.ScopeHost,
                techId));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, new TechnologyScopeKeyRegistry());
            var definitions = new AbilityDefinitionRegistry();
            var definition = new AbilityDefinition
            {
                UseTechnologyRequirementId = reqId,
                HasUseTechnologyRequirement = true
            };
            definitions.Register(abilityId, in definition);

            var abilities = new AbilityStateBuffer();
            abilities.AddAbility(abilityId);
            Entity city = world.Create(new TechnologyStateBuffer());
            Entity barracks = world.Create(abilities);
            Assert.That(evaluator.TryComplete(city, techId), Is.True);

            var system = new AbilitySystem(world, effectRequests: null, definitions, technologyRequirements: evaluator);
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
            int techId = TechnologyIdRegistry.Register("Tech.CityGuardCharter");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.CityGuardCharter");

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.TechCompleted,
                new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                TechnologyRequirementEntitySource.ScopeHost,
                techId));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, new TechnologyScopeKeyRegistry());
            Entity city = world.Create(new TechnologyStateBuffer());
            Entity actor = CreateCastActor(world, abilityId, castAbilityOrderTypeId, orderId: 21);
            Entity target = world.Create();
            Assert.That(evaluator.TryComplete(city, techId), Is.True);

            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.SelectionGate, tick: 0, tagId: 77);
            spec.SetItem(1, ExecItemKind.EffectSignal, tick: 0, templateId: effectTemplateId);

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = spec,
                HasUseTechnologyRequirement = true,
                UseTechnologyRequirementId = reqId
            });

            var selectionRequests = new SelectionRequestQueue();
            var selectionResponses = new SelectionResponseBuffer();
            var effectRequests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(16);
            var orderTypes = CreateCastOrderTypes(castAbilityOrderTypeId);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                selectionRequests,
                selectionResponses,
                effectRequests,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: orderTypes,
                technologyRequirements: evaluator);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);
            Assert.That(selectionRequests.Count, Is.EqualTo(1));
            ref var waiting = ref world.Get<AbilityExecInstance>(actor);
            Assert.That(waiting.State, Is.EqualTo(AbilityExecRunState.GateWaiting));
            Assert.That(waiting.PendingTechnologyUseRequirement, Is.EqualTo(1));
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastFailed), Is.False);

            var response = default(SelectionResponse);
            response.RequestId = 21;
            response.ResponseTagId = 77;
            response.TargetContext = city;
            response.Count = 1;
            response.SetEntity(0, target);
            Assert.That(selectionResponses.TryAdd(response), Is.True);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);
            ref var resolved = ref world.Get<AbilityExecInstance>(actor);
            Assert.That(resolved.State, Is.EqualTo(AbilityExecRunState.Running));
            Assert.That(resolved.PendingTechnologyUseRequirement, Is.EqualTo(0));
            Assert.That(resolved.TargetContext, Is.EqualTo(city));
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastFailed), Is.False);

            system.Update(0f);

            Assert.That(effectRequests.Count, Is.EqualTo(1));
            Assert.That(effectRequests[0].Target, Is.EqualTo(target));
            Assert.That(effectRequests[0].TargetContext, Is.EqualTo(city));
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False);
        }

        [Test]
        public void AbilityExecSystem_ExplicitUseRequirementFailsAfterSelectionGateWhenScopeLacksTechnology()
        {
            using var world = World.Create();
            const int castAbilityOrderTypeId = 100;
            const int effectTemplateId = 502;
            int abilityId = AbilityIdRegistry.Register("Ability.RaiseCityGuardBlocked");
            int techId = TechnologyIdRegistry.Register("Tech.CityGuardCharterBlocked");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.CityGuardCharterBlocked");

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.TechCompleted,
                new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                TechnologyRequirementEntitySource.ScopeHost,
                techId));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, new TechnologyScopeKeyRegistry());
            Entity cityWithoutTech = world.Create(new TechnologyStateBuffer());
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
                HasUseTechnologyRequirement = true,
                UseTechnologyRequirementId = reqId
            });

            var selectionRequests = new SelectionRequestQueue();
            var selectionResponses = new SelectionResponseBuffer();
            var effectRequests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(16);
            var orderTypes = CreateCastOrderTypes(castAbilityOrderTypeId);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                selectionRequests,
                selectionResponses,
                effectRequests,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: orderTypes,
                technologyRequirements: evaluator);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);
            Assert.That(selectionRequests.Count, Is.EqualTo(1));
            Assert.That(ContainsPresentationEvent(presentationEvents, GasPresentationEventKind.CastFailed), Is.False);

            var response = default(SelectionResponse);
            response.RequestId = 22;
            response.ResponseTagId = 78;
            response.TargetContext = cityWithoutTech;
            response.Count = 1;
            response.SetEntity(0, target);
            Assert.That(selectionResponses.TryAdd(response), Is.True);

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
            int techId = TechnologyIdRegistry.Register("Tech.UnsafeTimelineScope");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.UnsafeTimelineScope");

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.TechCompleted,
                new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                TechnologyRequirementEntitySource.ScopeHost,
                techId));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, new TechnologyScopeKeyRegistry());
            Entity actor = CreateCastActor(world, abilityId, castAbilityOrderTypeId, orderId: 23);

            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.EffectSignal, tick: 0, templateId: effectTemplateId);
            spec.SetItem(1, ExecItemKind.SelectionGate, tick: 0, tagId: 79);

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = spec,
                HasUseTechnologyRequirement = true,
                UseTechnologyRequirementId = reqId
            });

            var selectionRequests = new SelectionRequestQueue();
            var effectRequests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(16);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                selectionRequests,
                new SelectionResponseBuffer(),
                effectRequests,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: CreateCastOrderTypes(castAbilityOrderTypeId),
                technologyRequirements: evaluator);

            system.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False);
            Assert.That(selectionRequests.Count, Is.EqualTo(0));
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
            int techId = TechnologyIdRegistry.Register("Tech.EventGateScope");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.EventGateScope");

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.TechCompleted,
                new TechnologyScopeSpec(TechnologyScopeKind.Explicit),
                TechnologyRequirementEntitySource.ScopeHost,
                techId));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, new TechnologyScopeKeyRegistry());
            Entity actor = CreateCastActor(world, abilityId, castAbilityOrderTypeId, orderId: 24);

            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.EventGate, tick: 0, tagId: 80);
            spec.SetItem(1, ExecItemKind.EffectSignal, tick: 0, templateId: effectTemplateId);

            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition
            {
                ExecSpec = spec,
                HasUseTechnologyRequirement = true,
                UseTechnologyRequirementId = reqId
            });

            var effectRequests = new EffectRequestQueue();
            var presentationEvents = new GasPresentationEventBuffer(16);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new SelectionRequestQueue(),
                new SelectionResponseBuffer(),
                effectRequests,
                definitions,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                presentationEvents: presentationEvents,
                orderTypeRegistry: CreateCastOrderTypes(castAbilityOrderTypeId),
                technologyRequirements: evaluator);

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
            int graphReqId = TechnologyRequirementIdRegistry.Register("Req.GraphBacked");
            var requirements = new TechnologyRequirementRegistry();
            var emptyTags = default(GameplayTagContainer);
            var nodes = new[]
            {
                new TechnologyRequirementNode(
                    TechnologyRequirementNodeKind.All,
                    TechnologyScopeSpec.Self,
                    TechnologyRequirementEntitySource.Subject,
                    firstChild: 0,
                    childCount: 1,
                    technologyId: 0,
                    requiredCount: 0,
                    graphProgramId: 0,
                    in emptyTags),
                new TechnologyRequirementNode(
                    TechnologyRequirementNodeKind.GraphValidation,
                    TechnologyScopeSpec.Self,
                    TechnologyRequirementEntitySource.Subject,
                    firstChild: 0,
                    childCount: 0,
                    technologyId: 0,
                    requiredCount: 0,
                    graphProgramId: 12,
                    in emptyTags)
            };
            requirements.Register(graphReqId, new TechnologyRequirementDefinition(graphReqId, nodes, new[] { 1 }));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, new TechnologyScopeKeyRegistry());

            Assert.That(evaluator.UsesGraphValidation(graphReqId), Is.True);
        }

        [Test]
        public void ComputeRevision_ForScopeMembersChangesWhenMemberTagsChange()
        {
            using var world = World.Create();
            int heroTag = TagRegistry.Register("Hero.GuanYu");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.GuanYuInCity");
            var scopeKeys = new TechnologyScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(heroTag);

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.EntityCount,
                new TechnologyScopeSpec(TechnologyScopeKind.Named, cityScopeId),
                TechnologyRequirementEntitySource.ScopeMembers,
                technologyId: 0,
                requiredCount: 1,
                requiredTags: in requiredTags));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, scopeKeys);
            Entity city = world.Create(new TechnologyStateBuffer());
            Entity barracks = world.Create();
            Entity hero = world.Create(new GameplayTagContainer());
            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            PrepareScopeMember(world, hero);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);
            Assert.That(evaluator.TryBindScope(hero, cityScopeId, city), Is.True);

            var context = new TechnologyRequirementEvaluationContext(barracks, barracks);
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
            int reqId = TechnologyRequirementIdRegistry.Register("Req.GuanYuInCity");
            var scopeKeys = new TechnologyScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(heroTag);

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.EntityCount,
                new TechnologyScopeSpec(TechnologyScopeKind.Named, cityScopeId),
                TechnologyRequirementEntitySource.ScopeMembers,
                technologyId: 0,
                requiredCount: 1,
                requiredTags: in requiredTags));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, scopeKeys);
            Entity city = world.Create(new TechnologyStateBuffer());
            Entity barracks = world.Create();
            Entity hero = world.Create(new GameplayTagContainer(), new TagCountContainer(), new DirtyFlags());
            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            PrepareScopeMember(world, hero);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);
            Assert.That(evaluator.TryBindScope(hero, cityScopeId, city), Is.True);

            var context = new TechnologyRequirementEvaluationContext(barracks, barracks);
            uint before = evaluator.ComputeScopeRevision(reqId, in context);
            uint hostRevisionBefore = world.Get<TechnologyScopeMembershipRevision>(city).Revision;

            ref var heroTags = ref world.Get<GameplayTagContainer>(hero);
            ref var heroCounts = ref world.Get<TagCountContainer>(hero);
            ref var dirty = ref world.Get<DirtyFlags>(hero);
            var tagOps = new TagOps();
            Assert.That(tagOps.AddTag(ref heroTags, ref heroCounts, heroTag, ref dirty), Is.True);

            var triggerQueue = new DeferredTriggerQueue();
            var collectionSystem = new DeferredTriggerCollectionSystem(world, triggerQueue, tagOps);
            collectionSystem.Update(0f);
            Assert.That(world.Has<GameplayTagEffectiveChangedBits>(hero), Is.True);

            var revisionSystem = new TechnologyScopeTagRevisionSystem(world);
            revisionSystem.Update(0f);

            uint after = evaluator.ComputeScopeRevision(reqId, in context);
            Assert.That(world.Get<TechnologyScopeMembershipRevision>(city).Revision, Is.GreaterThan(hostRevisionBefore));
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(evaluator.Evaluate(reqId, in context), Is.True);
        }

        [Test]
        public void RebindingScopeMember_BumpsOldAndNewScopeRevisions()
        {
            using var world = World.Create();
            int heroTag = TagRegistry.Register("Hero.GuanYu");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.GuanYuInCity");
            var scopeKeys = new TechnologyScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(heroTag);

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.EntityCount,
                new TechnologyScopeSpec(TechnologyScopeKind.Named, cityScopeId),
                TechnologyRequirementEntitySource.ScopeMembers,
                technologyId: 0,
                requiredCount: 1,
                requiredTags: in requiredTags));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, scopeKeys);
            Entity cityA = world.Create(new TechnologyStateBuffer());
            Entity cityB = world.Create(new TechnologyStateBuffer());
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

            var contextA = new TechnologyRequirementEvaluationContext(barracksA, barracksA);
            var contextB = new TechnologyRequirementEvaluationContext(barracksB, barracksB);
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
            int reqId = TechnologyRequirementIdRegistry.Register("Req.CityFortified");
            var scopeKeys = new TechnologyScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requiredTags = default(GameplayTagContainer);
            requiredTags.AddTag(fortifiedTag);

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.TagAll,
                new TechnologyScopeSpec(TechnologyScopeKind.Named, cityScopeId),
                TechnologyRequirementEntitySource.ScopeHost,
                technologyId: 0,
                requiredTags: in requiredTags));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, scopeKeys);
            Entity city = world.Create(new TechnologyStateBuffer(), new GameplayTagContainer(), new TagCountContainer(), new DirtyFlags());
            Entity barracks = world.Create();
            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);

            var context = new TechnologyRequirementEvaluationContext(barracks, barracks);
            uint before = evaluator.ComputeScopeRevision(reqId, in context);
            uint hostRevisionBefore = world.Get<TechnologyScopeMembershipRevision>(city).Revision;
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

            var revisionSystem = new TechnologyScopeTagRevisionSystem(world);
            revisionSystem.Update(0f);

            uint after = evaluator.ComputeScopeRevision(reqId, in context);
            Assert.That(world.Get<TechnologyScopeMembershipRevision>(city).Revision, Is.GreaterThan(hostRevisionBefore));
            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(evaluator.Evaluate(reqId, in context), Is.True);
        }

        [Test]
        public void TechnologyConfigLoader_UsesTechnologyDefaultScopeAndFreezesNamedScopes()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Technology"));
                File.WriteAllText(Path.Combine(root, "Configs", "Technology", "scopes.json"),
                    """
                    [
                      { "id": "city" },
                      { "id": "province" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Technology", "technologies.json"),
                    """
                    [
                      { "id": "Tech.CityArchery", "scope": "city" },
                      { "id": "Tech.ProvinceTrade", "scope": "province" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Technology", "requirements.json"),
                    """
                    [
                      {
                        "id": "Req.CityArchery2",
                        "root": { "kind": "TechLevelAtLeast", "technology": "Tech.CityArchery", "level": 2 }
                      }
                    ]
                    """);

                var loader = CreateTechnologyLoader(root, out var requirements, out var scopeKeys);
                loader.Load(CreateTechnologyCatalog());

                int requirementId = TechnologyRequirementIdRegistry.GetId("Req.CityArchery2");
                Assert.That(requirements.TryGet(requirementId, out var requirement), Is.True);
                Assert.That(requirement.Nodes[0].Scope.Kind, Is.EqualTo(TechnologyScopeKind.Named));
                Assert.That(requirement.Nodes[0].Scope.ScopeKeyId, Is.EqualTo(scopeKeys.GetId("city")));

                using var world = World.Create();
                var evaluator = new TechnologyRequirementEvaluator(world, requirements, scopeKeys);
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
        public void EffectTemplateLoader_CompleteTechnology_UsesDefinitionDefaultScopeAndLevel()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "Technology"));
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "Technology", "scopes.json"),
                    """
                    [
                      { "id": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Technology", "technologies.json"),
                    """
                    [
                      { "id": "Tech.CityArmor", "scope": "city" }
                    ]
                    """);
                File.WriteAllText(Path.Combine(root, "Configs", "Technology", "requirements.json"), "[]");
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect.CompleteCityArmor2",
                        "presetType": "CompleteTechnology",
                        "lifetime": "Instant",
                        "participatesInResponse": false,
                        "technology": { "id": "Tech.CityArmor", "level": 2 }
                      }
                    ]
                    """);

                var techLoader = CreateTechnologyLoader(root, out _, out var scopeKeys, out var technologies, out var pipeline);
                techLoader.Load(CreateTechnologyCatalog());

                var effects = new EffectTemplateRegistry();
                var effectLoader = new EffectTemplateLoader(
                    pipeline,
                    effects,
                    technologyScopeKeys: scopeKeys,
                    technologyDefinitions: technologies);
                effectLoader.Load(CreateEffectsCatalog(), relativePath: "GAS/effects.json");

                int templateId = EffectTemplateIdRegistry.GetId("Effect.CompleteCityArmor2");
                Assert.That(effects.TryGet(templateId, out var template), Is.True);
                Assert.That(template.PresetType, Is.EqualTo(EffectPresetType.CompleteTechnology));
                Assert.That(template.TechnologyScope.Kind, Is.EqualTo(TechnologyScopeKind.Named));
                Assert.That(template.TechnologyScope.ScopeKeyId, Is.EqualTo(scopeKeys.GetId("city")));
                Assert.That(template.TechnologyChange.Level, Is.EqualTo(2));
                Assert.That(template.TechnologyChange.Delta, Is.EqualTo(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void TechnologyScopeAuthoring_BindsMembersToConfiguredScopeHosts()
        {
            using var world = World.Create();
            int techId = TechnologyIdRegistry.Register("Tech.CityDrill");
            int reqId = TechnologyRequirementIdRegistry.Register("Req.CityDrill");
            var scopeKeys = new TechnologyScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);

            var requirements = new TechnologyRequirementRegistry();
            requirements.Register(reqId, CreateSingleNodeRequirement(
                reqId,
                TechnologyRequirementNodeKind.TechCompleted,
                new TechnologyScopeSpec(TechnologyScopeKind.Named, cityScopeId),
                TechnologyRequirementEntitySource.ScopeHost,
                techId));

            var evaluator = new TechnologyRequirementEvaluator(world, requirements, scopeKeys);
            var bindingSystem = new TechnologyScopeBindingSystem(world, evaluator, scopeKeys);

            Entity cityA = world.Create();
            Entity cityB = world.Create();
            Entity barracksA = world.Create();
            Entity barracksB = world.Create();
            Ludots.Core.Config.ComponentRegistry.Apply(cityA, "TechnologyScopeHost", JsonNode.Parse("""{ "scope": "city", "hostKey": "chang_an" }""")!);
            Ludots.Core.Config.ComponentRegistry.Apply(cityB, "TechnologyScopeHost", JsonNode.Parse("""{ "scope": "city", "hostKey": "luo_yang" }""")!);
            Ludots.Core.Config.ComponentRegistry.Apply(barracksA, "TechnologyScopeBinding", JsonNode.Parse("""{ "scope": "city", "hostKey": "chang_an" }""")!);
            Ludots.Core.Config.ComponentRegistry.Apply(barracksB, "TechnologyScopeBinding", JsonNode.Parse("""{ "scope": "city", "hostKey": "luo_yang" }""")!);

            bindingSystem.Update(0f);
            Assert.That(evaluator.TryComplete(cityA, techId), Is.True);

            var contextA = new TechnologyRequirementEvaluationContext(barracksA, barracksA);
            var contextB = new TechnologyRequirementEvaluationContext(barracksB, barracksB);
            Assert.That(evaluator.Evaluate(reqId, in contextA), Is.True);
            Assert.That(evaluator.Evaluate(reqId, in contextB), Is.False);

            uint before = world.Get<TechnologyScopeMembershipRevision>(cityA).Revision;
            bindingSystem.Update(0f);
            Assert.That(world.Get<TechnologyScopeMembershipRevision>(cityA).Revision, Is.EqualTo(before));
        }

        [Test]
        public void TechnologyScopeAuthoring_FailsFastWhenBindingHostIsMissing()
        {
            using var world = World.Create();
            var scopeKeys = new TechnologyScopeKeyRegistry();
            scopeKeys.Register("city");
            var evaluator = new TechnologyRequirementEvaluator(
                world,
                new TechnologyRequirementRegistry(),
                scopeKeys);
            var bindingSystem = new TechnologyScopeBindingSystem(world, evaluator, scopeKeys);

            Entity barracks = world.Create();
            Ludots.Core.Config.ComponentRegistry.Apply(barracks, "TechnologyScopeBinding", JsonNode.Parse("""{ "scope": "city", "hostKey": "missing_city" }""")!);

            var ex = Assert.Throws<InvalidOperationException>(() => bindingSystem.Update(0f));
            Assert.That(ex?.Message, Does.Contain("missing host"));
            Assert.That(ex?.Message, Does.Contain("city"));
            Assert.That(ex?.Message, Does.Contain("missing_city"));
        }

        [Test]
        public void TryBindScope_RequiresPreallocatedAuthoringComponents()
        {
            using var world = World.Create();
            var scopeKeys = new TechnologyScopeKeyRegistry();
            int cityScopeId = RegisterCityScope(scopeKeys);
            var evaluator = new TechnologyRequirementEvaluator(
                world,
                new TechnologyRequirementRegistry(),
                scopeKeys);
            Entity city = world.Create(new TechnologyStateBuffer());
            Entity barracks = world.Create();

            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.False);

            PrepareScopeHost(world, city);
            PrepareScopeMember(world, barracks);
            Assert.That(evaluator.TryBindScope(barracks, cityScopeId, city), Is.True);
        }

        private static TechnologyRequirementDefinition CreateSingleNodeRequirement(
            int requirementId,
            TechnologyRequirementNodeKind kind,
            TechnologyScopeSpec scope,
            TechnologyRequirementEntitySource entitySource,
            int technologyId,
            int requiredCount = 1,
            int graphProgramId = 0,
            in GameplayTagContainer requiredTags = default)
        {
            var nodes = new[]
            {
                new TechnologyRequirementNode(
                    kind,
                    scope,
                    entitySource,
                    firstChild: 0,
                    childCount: 0,
                    technologyId,
                    requiredCount,
                    graphProgramId,
                    in requiredTags)
            };
            return new TechnologyRequirementDefinition(requirementId, nodes, Array.Empty<int>());
        }

        private static int RegisterCityScope(TechnologyScopeKeyRegistry scopeKeys)
        {
            return scopeKeys.Register("city");
        }

        private static void PrepareScopeHost(World world, Entity entity)
        {
            if (!world.Has<TechnologyScopeMembershipRevision>(entity))
            {
                world.Add(entity, new TechnologyScopeMembershipRevision());
            }
        }

        private static void PrepareScopeMember(World world, Entity entity)
        {
            if (!world.Has<TechnologyScopeRefBuffer>(entity))
            {
                world.Add(entity, new TechnologyScopeRefBuffer());
            }

            if (!world.Has<TechnologyScopeMemberTag>(entity))
            {
                world.Add(entity, new TechnologyScopeMemberTag());
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

        private static TechnologyConfigLoader CreateTechnologyLoader(
            string root,
            out TechnologyRequirementRegistry requirements,
            out TechnologyScopeKeyRegistry scopeKeys)
        {
            return CreateTechnologyLoader(root, out requirements, out scopeKeys, out _, out _);
        }

        private static TechnologyConfigLoader CreateTechnologyLoader(
            string root,
            out TechnologyRequirementRegistry requirements,
            out TechnologyScopeKeyRegistry scopeKeys,
            out TechnologyDefinitionRegistry technologies,
            out ConfigPipeline pipeline)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", root);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            pipeline = new ConfigPipeline(vfs, modLoader);
            technologies = new TechnologyDefinitionRegistry();
            requirements = new TechnologyRequirementRegistry();
            scopeKeys = new TechnologyScopeKeyRegistry();
            return new TechnologyConfigLoader(pipeline, technologies, requirements, scopeKeys);
        }

        private static ConfigCatalog CreateTechnologyCatalog()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("Technology/scopes.json", ConfigMergePolicy.ArrayById, "id"));
            catalog.Add(new ConfigCatalogEntry("Technology/technologies.json", ConfigMergePolicy.ArrayById, "id"));
            catalog.Add(new ConfigCatalogEntry("Technology/requirements.json", ConfigMergePolicy.ArrayById, "id"));
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
            string root = Path.Combine(Path.GetTempPath(), "Ludots_TechnologyRequirementTests", Guid.NewGuid().ToString("N"));
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
