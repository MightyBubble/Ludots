using System;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using NUnit.Framework;
using static NUnit.Framework.Assert;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// End-to-end scenario tests for the CallerParams pipeline:
    ///   EffectRequest 鈫?EffectProposal 鈫?phase execution with merged config.
    /// Tests cover:
    ///   - CallerParams override template ConfigParams for instant effects
    ///   - CallerParams propagation to duration effect entities
    ///   - Multiple CallerParams keys in a single request
    ///   - Graph -> ApplyEffectTemplate -> CallerParams pipeline
    /// </summary>
    [TestFixture]
    public class CallerParamsE2ETests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        // ------------------------------------------------------------
        //  Scenario: CallerParams override ForceX/Y in ApplyForce2D preset
        // ------------------------------------------------------------

        [Test]
        public void CallerParams_OverrideForceValues_InInstantEffect()
        {
            string root = CreateTempRoot();
            try
            {
                SetupEffectsJson(root);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = ConfigCatalogLoader.Load(pipeline);

                var templates = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, templates);
                loader.Load(catalog, relativePath: "GAS/effects.json");
                FinalizeEffectTemplates(pipeline, catalog, templates);

                using var world = World.Create();
                int fxAttrId = AttributeRegistry.GetId("Physics.ForceRequestX");
                int fyAttrId = AttributeRegistry.GetId("Physics.ForceRequestY");
                That(fxAttrId, Is.GreaterThanOrEqualTo(0));
                That(fyAttrId, Is.GreaterThanOrEqualTo(0));

                var target = world.Create(new AttributeBuffer(), new DirtyFlags());
                var requests = new EffectRequestQueue();

                // Publish request with CallerParams overriding default force
                int tplId = EffectTemplateIdRegistry.GetId("Effect.Preset.ApplyForce2D");
                That(tplId, Is.GreaterThan(0), "Template should be registered");

                var req = new EffectRequest
                {
                    Source = default,
                    Target = target,
                    TemplateId = tplId,
                    HasCallerParams = true,
                };
                req.CallerParams.TryAddFloat(EffectParamKeys.ForceXAttribute, 100.0f);
                req.CallerParams.TryAddFloat(EffectParamKeys.ForceYAttribute, -50.0f);
                requests.Publish(req);

                var chainOrders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
                chainOrders.TryEnqueue(new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass });
                chainOrders.TryEnqueue(new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass });

                var proposalSys = new Ludots.Core.Gameplay.GAS.Systems.EffectProposalProcessingSystem(
                    world, requests, GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME, new Ludots.Core.Engine.DiscreteClock(), budget: new GasBudget(), templates: templates,
                    inputRequests: new InputRequestQueue(), chainOrders: chainOrders,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
                proposalSys.Update(0.016f);

                ref var attr = ref world.Get<AttributeBuffer>(target);
                That(attr.GetCurrent(fxAttrId), Is.EqualTo(100.0f), "ForceX should use CallerParams override");
                That(attr.GetCurrent(fyAttrId), Is.EqualTo(-50.0f), "ForceY should use CallerParams override");
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void CallerParams_PartialOverride_PreservesTemplateDefaultsInInstantEffect()
        {
            string root = CreateTempRoot();
            try
            {
                SetupEffectsJsonWithDefaultForce(root);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = ConfigCatalogLoader.Load(pipeline);

                var templates = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, templates);
                loader.Load(catalog, relativePath: "GAS/effects.json");
                FinalizeEffectTemplates(pipeline, catalog, templates);

                using var world = World.Create();
                int fxAttrId = AttributeRegistry.GetId("Physics.ForceRequestX");
                int fyAttrId = AttributeRegistry.GetId("Physics.ForceRequestY");
                var target = world.Create(new AttributeBuffer(), new DirtyFlags());
                var requests = new EffectRequestQueue();

                var req = new EffectRequest
                {
                    Source = default,
                    Target = target,
                    TemplateId = EffectTemplateIdRegistry.GetId("Effect.Preset.ApplyForce2D"),
                    HasCallerParams = true,
                };
                req.CallerParams.TryAddFloat(EffectParamKeys.ForceXAttribute, 100.0f);
                requests.Publish(req);

                var proposalSys = new Ludots.Core.Gameplay.GAS.Systems.EffectProposalProcessingSystem(
                    world,
                    requests,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new Ludots.Core.Engine.DiscreteClock(),
                    budget: new GasBudget(),
                    templates: templates,
                    inputRequests: new InputRequestQueue(),
                    chainOrders: new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64)),
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
                proposalSys.Update(0.016f);

                ref var attr = ref world.Get<AttributeBuffer>(target);
                That(attr.GetCurrent(fxAttrId), Is.EqualTo(100.0f));
                That(attr.GetCurrent(fyAttrId), Is.EqualTo(-25.0f));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        // ------------------------------------------------------------
        //  Scenario: Request without CallerParams uses template ConfigParams
        // ------------------------------------------------------------

        [Test]
        public void NoCallerParams_UsesTemplateConfigParams()
        {
            string root = CreateTempRoot();
            try
            {
                SetupEffectsJson(root);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);
                var catalog = ConfigCatalogLoader.Load(pipeline);

                var templates = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, templates);
                loader.Load(catalog, relativePath: "GAS/effects.json");
                FinalizeEffectTemplates(pipeline, catalog, templates);

                using var world = World.Create();
                int fxAttrId = AttributeRegistry.GetId("Physics.ForceRequestX");
                int fyAttrId = AttributeRegistry.GetId("Physics.ForceRequestY");

                var target = world.Create(new AttributeBuffer(), new DirtyFlags());
                var requests = new EffectRequestQueue();

                int tplId = EffectTemplateIdRegistry.GetId("Effect.Preset.ApplyForce2D");
                var req = new EffectRequest
                {
                    Source = default,
                    Target = target,
                    TemplateId = tplId,
                    HasCallerParams = false,
                };
                requests.Publish(req);

                var chainOrders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
                chainOrders.TryEnqueue(new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass });
                chainOrders.TryEnqueue(new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass });

                var proposalSys = new Ludots.Core.Gameplay.GAS.Systems.EffectProposalProcessingSystem(
                    world, requests, GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME, new Ludots.Core.Engine.DiscreteClock(), budget: new GasBudget(), templates: templates,
                    inputRequests: new InputRequestQueue(), chainOrders: chainOrders,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
                proposalSys.Update(0.016f);

                // Without CallerParams, force values should be 0 (template doesn't define them in configParams)
                ref var attr = ref world.Get<AttributeBuffer>(target);
                That(attr.GetCurrent(fxAttrId), Is.EqualTo(0f));
                That(attr.GetCurrent(fyAttrId), Is.EqualTo(0f));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        // ------------------------------------------------------------
        //  Scenario: Graph-originated CallerParams bridge
        // ------------------------------------------------------------

        [Test]
        public void GraphBridge_EffectArgs_ConvertsToCallerParams()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: requests);

            var target = world.Create();

            // Graph: ConstFloat(5.5) 鈫?fx, ConstFloat(-3.3) 鈫?fy, ApplyEffectTemplate(target, fx, fy)
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 5.5f },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = -3.3f },
                new() { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 1, B = 0, C = 1, Flags = 2, Imm = 777 },
                new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };

            GraphExecutor.Execute(world, caster: default, explicitTarget: target, targetPosCm: new IntVector2(0, 0), program, api);

            That(requests.Count, Is.EqualTo(1));
            var req = requests[0];
            That(req.TemplateId, Is.EqualTo(777));
            That(req.HasCallerParams, Is.True);

            That(req.CallerParams.TryGetFloat(EffectParamKeys.ForceXAttribute, out float fx), Is.True);
            That(fx, Is.EqualTo(5.5f).Within(1e-6f));

            That(req.CallerParams.TryGetFloat(EffectParamKeys.ForceYAttribute, out float fy), Is.True);
            That(fy, Is.EqualTo(-3.3f).Within(1e-6f));
        }

        // ------------------------------------------------------------
        //  Scenario: EffectRequest.CallerParams multiple keys
        // ------------------------------------------------------------

        [Test]
        public void CallerParams_MultipleKeys_AllPreservedInRequest()
        {
            EffectParamKeys.Initialize();

            var req = new EffectRequest { HasCallerParams = true };
            req.CallerParams.TryAddFloat(EffectParamKeys.ForceXAttribute, 1.0f);
            req.CallerParams.TryAddFloat(EffectParamKeys.ForceYAttribute, 2.0f);
            req.CallerParams.TryAddFloat(EffectParamKeys.QueryRadius, 10.0f);
            req.CallerParams.TryAddInt(EffectParamKeys.PayloadEffectId, 42);

            That(req.CallerParams.Count, Is.EqualTo(4));
            That(req.CallerParams.TryGetFloat(EffectParamKeys.QueryRadius, out float r), Is.True);
            That(r, Is.EqualTo(10.0f));
            That(req.CallerParams.TryGetInt(EffectParamKeys.PayloadEffectId, out int pid), Is.True);
            That(pid, Is.EqualTo(42));
        }

        [Test]
        public void CallerParams_DurationTicks_OverridesMaterializedEffectLifetime()
        {
            EffectParamKeys.Initialize();

            using var world = World.Create();
            var templates = new EffectTemplateRegistry();
            const int templateId = 801;
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.Step,
                DurationTicks = 30,
                PeriodTicks = 0,
                ParticipatesInResponse = false,
            });
            FinalizeEffectTemplates(templates, "Test/CallerParams.DurationOverride.json");

            var source = world.Create();
            var target = world.Create();
            var requests = new EffectRequestQueue();
            var req = new EffectRequest
            {
                RootId = 1,
                Source = source,
                Target = target,
                TemplateId = templateId,
                HasCallerParams = true,
            };
            req.CallerParams.TryAddInt(EffectParamKeys.DurationTicks, 75);
            requests.Publish(req);

            var proposalSys = new Ludots.Core.Gameplay.GAS.Systems.EffectProposalProcessingSystem(
                world,
                requests,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new Ludots.Core.Engine.DiscreteClock(),
                budget: new GasBudget(),
                templates: templates,
                inputRequests: new InputRequestQueue(),
                chainOrders: new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64)),
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            proposalSys.Update(0.016f);

            That(TryFindEffect(world, templateId, out GameplayEffect effect), Is.True);
            That(effect.TotalTicks, Is.EqualTo(75));
            That(effect.RemainingTicks, Is.EqualTo(75));
        }

        [Test]
        public void CallerParams_PayloadEffectId_OverridesTargetResolverDispatch()
        {
            EffectParamKeys.Initialize();

            using var world = World.Create();
            var source = world.Create();
            var originalTarget = world.Create();
            var resolved = world.Create();
            var buffer = new[] { resolved };
            var commands = new FanOutCommandBuffer(4);
            var budget = new RootBudgetTable(4);
            var mergedParams = default(EffectConfigParams);
            mergedParams.TryAddEffectTemplateId(EffectParamKeys.PayloadEffectId, 902);

            int count = TargetResolverFanOutHelper.ValidateAndCollect(
                world,
                new EffectContext
                {
                    RootId = 1,
                    Source = source,
                    Target = originalTarget,
                },
                new TargetQueryDescriptor
                {
                    Kind = TargetResolverKind.BuiltinSpatial,
                    Spatial = new BuiltinSpatialDescriptor { Shape = SpatialShape.Circle },
                },
                new TargetFilterDescriptor
                {
                    RelationFilter = RelationshipFilter.All,
                },
                new TargetDispatchDescriptor
                {
                    PayloadEffectTemplateId = 901,
                    ContextMapping = TargetResolverContextMapping.Default,
                },
                in mergedParams,
                buffer,
                candidateCount: 1,
                budget,
                commands);

            That(count, Is.EqualTo(1));
            That(commands.Count, Is.EqualTo(1));
            That(commands[0].PayloadEffectTemplateId, Is.EqualTo(902));
        }

        [Test]
        public void AbilityJson_EffectClipDurationAndCallerPeriod_DrivePeriodicLockoutUntilExpiry()
        {
            EffectParamKeys.Initialize();
            TagRegistry.Clear();
            EffectTemplateIdRegistry.Clear();
            AbilityIdRegistry.Clear();
            GraphIdRegistry.Clear();

            using var world = World.Create();
            var clock = new DiscreteClock();
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());

            int lockoutTagId = TagRegistry.Register("Cooldown.Tests.ClipPeriodic");
            int pulseAttributeId = AttributeRegistry.Register("Tests.ClipPeriodic.Pulse");
            int templateId = EffectTemplateIdRegistry.Register("Effect.Tests.ClipPeriodicLockout");
            int graphId = GraphIdRegistry.Register("Graph.Tests.ClipPeriodicPulse");

            var programs = new GraphProgramRegistry();
            programs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = 2f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 1, Imm = pulseAttributeId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
            }, GraphKind.Effect);

            var phaseBindings = new EffectPhaseGraphBindings();
            That(phaseBindings.TryAddStep(EffectPhaseId.OnPeriod, PhaseSlot.Post, graphId), Is.True);

            var grantedTags = new EffectGrantedTags();
            That(grantedTags.Add(new TagContribution
            {
                TagId = lockoutTagId,
                Formula = TagContributionFormula.Fixed,
                Amount = 1
            }), Is.True);

            var templates = new EffectTemplateRegistry();
            templates.Register(templateId, new EffectTemplateData
            {
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.FixedFrame,
                DurationTicks = 30,
                PeriodTicks = 0,
                GrantedTags = grantedTags,
                PhaseGraphBindings = phaseBindings,
            });

            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            EffectExecutionPlanCompiler.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                programs,
                GasGraphOpHandlerTable.Instance,
                "Test/CallerParamsE2ETests.ClipPeriodic.json");

            AbilityDefinition ability = AbilityExecLoader.CompileAbility(
                JsonNode.Parse(
                    """
                    {
                      "exec": {
                        "clockId": "Step",
                        "callerParams": [
                          {
                            "entries": [
                              { "key": "_ep.periodTicks", "type": "Int", "value": 1 }
                            ]
                          }
                        ],
                        "items": [
                          {
                            "kind": "EffectClip",
                            "tick": 0,
                            "template": "Effect.Tests.ClipPeriodicLockout",
                            "durationTicks": 3,
                            "clockId": "Step",
                            "callerParamsIdx": 0,
                            "dispatchTarget": "Source"
                          },
                          { "kind": "End", "tick": 0 }
                        ]
                      },
                      "blockTags": {
                        "blockedAny": [ "Cooldown.Tests.ClipPeriodic" ]
                      }
                    }
                    """)!.AsObject(),
                "Ability.Tests.ClipPeriodic",
                "GAS/abilities.json");

            const int abilityId = 9011;
            var abilityDefinitions = new AbilityDefinitionRegistry();
            abilityDefinitions.Register(abilityId, in ability, "CallerParamsE2ETests");

            var abilityState = default(AbilityStateBuffer);
            abilityState.AddAbility(abilityId);
            Entity actor = world.Create(
                abilityState,
                new AttributeBuffer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags(),
                new AbilityExecInstance
                {
                    AbilitySlot = 0,
                    AbilityId = abilityId,
                    State = AbilityExecRunState.Running,
                    ActiveClockId = GasClockId.Step,
                });
            ref var attributes = ref world.Get<AttributeBuffer>(actor);
            attributes.SetBase(pulseAttributeId, 0f);
            attributes.SetCurrent(pulseAttributeId, 0f);

            var requests = new EffectRequestQueue();
            var abilityExec = new AbilityExecSystem(
                world,
                clock,
                new InputRequestQueue(),
                new InputResponseBuffer(),
                requests,
                snapshotCapacity: 16,
                abilityDefinitions: abilityDefinitions,
                tagOps: tagOps);
            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                clock,
                budget: new GasBudget(),
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: tagOps);
            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps);
            var phaseExecutor = new EffectPhaseExecutor(
                programs,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates);
            var application = new EffectApplicationSystem(
                world,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                clock,
                requests,
                templates: templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps);
            var lifetime = new EffectLifetimeSystem(
                world,
                clock,
                new GasConditionRegistry(),
                snapshotCapacity: 16,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                effectRequests: requests,
                templates: templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps,
                presentationEvents: new Ludots.Core.Gameplay.GAS.Presentation.GasPresentationEventBuffer(16));

            abilityExec.Update(0f);
            That(requests.Count, Is.EqualTo(1));
            That(requests[0].HasCallerParams, Is.True);
            That(requests[0].CallerParams.TryGetRawValue(EffectParamKeys.DurationTicks, out ConfigParamType durationType, out int durationValue), Is.True);
            That(durationType, Is.EqualTo(ConfigParamType.Int));
            That(durationValue, Is.EqualTo(3));
            That(requests[0].CallerParams.TryGetRawValue(EffectParamKeys.PeriodTicks, out ConfigParamType periodType, out int periodValue), Is.True);
            That(periodType, Is.EqualTo(ConfigParamType.Int));
            That(periodValue, Is.EqualTo(1));
            That(requests[0].HasClockId, Is.True);
            That(requests[0].ClockId, Is.EqualTo(GasClockId.Step));

            proposal.Update(0f);
            application.Update(0f);

            That(TryFindEffect(world, templateId, out GameplayEffect effect), Is.True);
            That(effect.TotalTicks, Is.EqualTo(3));
            That(effect.RemainingTicks, Is.EqualTo(3));
            That(effect.PeriodTicks, Is.EqualTo(1));
            That(effect.ClockId, Is.EqualTo(GasClockId.Step));
            That(world.Get<GameplayTagContainer>(actor).HasTag(lockoutTagId), Is.True);

            lifetime.Update(0f);
            clock.Advance(ClockDomainId.Step, 1);
            lifetime.Update(0f);
            That(world.Get<AttributeBuffer>(actor).GetCurrent(pulseAttributeId), Is.EqualTo(2f));
            That(world.Get<GameplayTagContainer>(actor).HasTag(lockoutTagId), Is.True);

            clock.Advance(ClockDomainId.Step, 2);
            lifetime.Update(0f);
            That(world.Get<GameplayTagContainer>(actor).HasTag(lockoutTagId), Is.False);
            That(world.Get<ActiveEffectContainer>(actor).Count, Is.EqualTo(0));
        }

        // ------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------

        private static void SetupEffectsJson(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "GAS"));
            File.WriteAllText(Path.Combine(root, "config_catalog.json"),
                """
                [
                  { "Path": "GAS/effects.json", "Policy": "ArrayById", "IdField": "id" },
                  { "Path": "GAS/preset_types.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
            File.WriteAllText(Path.Combine(root, "GAS", "effects.json"),
                """
                [
                  {
                    "id": "Effect.Preset.ApplyForce2D",
                    "categories": ["Effect.ApplyForce"],
                    "presetType": "ApplyForce2D",
                    "lifetime": "Instant",
                    "participatesInResponse": true,
                    "configParams": {
                      "_ep.forceXTargetAttrId": { "type": "Attribute", "value": "Physics.ForceRequestX" },
                      "_ep.forceYTargetAttrId": { "type": "Attribute", "value": "Physics.ForceRequestY" }
                    }
                  }
                ]
                """);
            File.WriteAllText(Path.Combine(root, "GAS", "preset_types.json"),
                """
                [
                  {
                    "id": "ApplyForce2D",
                    "components": ["ForceParams"],
                    "activePhases": ["OnApply"],
                    "allowedLifetimes": ["Instant"],
                    "defaultPhaseHandlers": {
                      "OnApply": { "type": "builtin", "id": "ApplyForce" }
                    }
                  }
                ]
                """);
        }

        private static void SetupEffectsJsonWithDefaultForce(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "GAS"));
            File.WriteAllText(Path.Combine(root, "config_catalog.json"),
                """
                [
                  { "Path": "GAS/effects.json", "Policy": "ArrayById", "IdField": "id" },
                  { "Path": "GAS/preset_types.json", "Policy": "ArrayById", "IdField": "id" }
                ]
                """);
            File.WriteAllText(Path.Combine(root, "GAS", "effects.json"),
                """
                [
                  {
                    "id": "Effect.Preset.ApplyForce2D",
                    "categories": ["Effect.ApplyForce"],
                    "presetType": "ApplyForce2D",
                    "lifetime": "Instant",
                    "participatesInResponse": true,
                    "configParams": {
                      "_ep.forceXTargetAttrId": { "type": "Attribute", "value": "Physics.ForceRequestX" },
                      "_ep.forceYTargetAttrId": { "type": "Attribute", "value": "Physics.ForceRequestY" },
                      "_ep.forceXAttribute": { "type": "Float", "value": 10.0 },
                      "_ep.forceYAttribute": { "type": "Float", "value": -25.0 }
                    }
                  }
                ]
                """);
            File.WriteAllText(Path.Combine(root, "GAS", "preset_types.json"),
                """
                [
                  {
                    "id": "ApplyForce2D",
                    "components": ["ForceParams"],
                    "activePhases": ["OnApply"],
                    "allowedLifetimes": ["Instant"],
                    "defaultPhaseHandlers": {
                      "OnApply": { "type": "builtin", "id": "ApplyForce" }
                    }
                  }
                ]
                """);
        }

        private static void FinalizeEffectTemplates(
            ConfigPipeline pipeline,
            ConfigCatalog catalog,
            EffectTemplateRegistry templates)
        {
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            new PresetTypeLoader(pipeline, presetTypes, builtinHandlers).Load(catalog);
            EffectExecutionPlanCompiler.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                new GraphProgramRegistry(),
                GasGraphOpHandlerTable.Instance,
                "GAS/effects.json");
        }

        private static void FinalizeEffectTemplates(EffectTemplateRegistry templates, string sourceName)
        {
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            EffectExecutionPlanCompiler.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                new GraphProgramRegistry(),
                GasGraphOpHandlerTable.Instance,
                sourceName);
        }

        private static bool TryFindEffect(World world, int templateId, out GameplayEffect effect)
        {
            GameplayEffect foundEffect = default;
            bool found = false;
            var query = new QueryDescription().WithAll<GameplayEffect, EffectTemplateRef>();
            world.Query(in query, (Entity _, ref GameplayEffect current, ref EffectTemplateRef templateRef) =>
            {
                if (found || templateRef.TemplateId != templateId)
                {
                    return;
                }

                foundEffect = current;
                found = true;
            });

            effect = foundEffect;
            return found;
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_CallerParamsE2E", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
        }
    }
}
