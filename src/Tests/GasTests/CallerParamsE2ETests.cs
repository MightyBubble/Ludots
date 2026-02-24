using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// End-to-end scenario tests for the CallerParams pipeline:
    ///   EffectRequest → EffectProposal → phase execution with merged config.
    /// Tests cover:
    ///   - CallerParams override template ConfigParams for instant effects
    ///   - CallerParams propagation to duration effect entities
    ///   - Multiple CallerParams keys in a single request
    ///   - Graph → ApplyEffectTemplate → CallerParams bridge
    /// </summary>
    [TestFixture]
    public class CallerParamsE2ETests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        // ════════════════════════════════════════════════════════════════════
        //  Scenario: CallerParams override ForceX/Y in ApplyForce2D preset
        // ════════════════════════════════════════════════════════════════════

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

                var templates = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, templates);
                loader.Load("GAS/effects.json");

                using var world = World.Create();
                int fxAttrId = AttributeRegistry.GetId("Physics.ForceRequestX");
                int fyAttrId = AttributeRegistry.GetId("Physics.ForceRequestY");
                That(fxAttrId, Is.GreaterThanOrEqualTo(0));
                That(fyAttrId, Is.GreaterThanOrEqualTo(0));

                var target = world.Create(new AttributeBuffer());
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

                var chainOrders = new OrderQueue();
                chainOrders.TryEnqueue(new Order { OrderTagId = TestGasOrderTags.ChainPass });
                chainOrders.TryEnqueue(new Order { OrderTagId = TestGasOrderTags.ChainPass });

                var proposalSys = new Ludots.Core.Gameplay.GAS.Systems.EffectProposalProcessingSystem(
                    world, requests, budget: null, templates: templates,
                    inputRequests: null, chainOrders: chainOrders);
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

        // ════════════════════════════════════════════════════════════════════
        //  Scenario: Request without CallerParams falls back to template ConfigParams
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void NoCallerParams_FallsBackToTemplateConfigParams()
        {
            string root = CreateTempRoot();
            try
            {
                SetupEffectsJson(root);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var templates = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, templates);
                loader.Load("GAS/effects.json");

                using var world = World.Create();
                int fxAttrId = AttributeRegistry.GetId("Physics.ForceRequestX");
                int fyAttrId = AttributeRegistry.GetId("Physics.ForceRequestY");

                var target = world.Create(new AttributeBuffer());
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

                var chainOrders = new OrderQueue();
                chainOrders.TryEnqueue(new Order { OrderTagId = TestGasOrderTags.ChainPass });
                chainOrders.TryEnqueue(new Order { OrderTagId = TestGasOrderTags.ChainPass });

                var proposalSys = new Ludots.Core.Gameplay.GAS.Systems.EffectProposalProcessingSystem(
                    world, requests, budget: null, templates: templates,
                    inputRequests: null, chainOrders: chainOrders);
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

        // ════════════════════════════════════════════════════════════════════
        //  Scenario: Graph-originated CallerParams bridge
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void GraphBridge_EffectArgs_ConvertsToCallerParams()
        {
            using var world = World.Create();
            var requests = new EffectRequestQueue();
            var api = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: requests);

            var target = world.Create();

            // Graph: ConstFloat(5.5) → fx, ConstFloat(-3.3) → fy, ApplyEffectTemplate(target, fx, fy)
            var program = new GraphInstruction[]
            {
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 5.5f },
                new() { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = -3.3f },
                new() { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 1, B = 0, C = 1, Flags = 2, Imm = 777 },
            };

            GraphExecutor.Execute(world, caster: default, explicitTarget: target, targetPos: new IntVector2(0, 0), program, api);

            That(requests.Count, Is.EqualTo(1));
            var req = requests[0];
            That(req.TemplateId, Is.EqualTo(777));
            That(req.HasCallerParams, Is.True);

            That(req.CallerParams.TryGetFloat(EffectParamKeys.ForceXAttribute, out float fx), Is.True);
            That(fx, Is.EqualTo(5.5f).Within(1e-6f));

            That(req.CallerParams.TryGetFloat(EffectParamKeys.ForceYAttribute, out float fy), Is.True);
            That(fy, Is.EqualTo(-3.3f).Within(1e-6f));
        }

        // ════════════════════════════════════════════════════════════════════
        //  Scenario: EffectRequest.CallerParams multiple keys
        // ════════════════════════════════════════════════════════════════════

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

        // ════════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════════

        private static void SetupEffectsJson(string root)
        {
            Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
            File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                """
                [
                  {
                    "id": "Effect.Preset.ApplyForce2D",
                    "tags": ["Effect.ApplyForce"],
                    "presetType": "ApplyForce2D",
                    "lifetime": "Instant",
                    "excludeFromChain": true,
                    "configParams": {
                      "_ep.forceXTargetAttrId": { "type": "attribute", "value": "Physics.ForceRequestX" },
                      "_ep.forceYTargetAttrId": { "type": "attribute", "value": "Physics.ForceRequestY" }
                    }
                  }
                ]
                """);
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
