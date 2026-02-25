using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using GraphInstruction = Ludots.Core.GraphRuntime.GraphInstruction;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Physics;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class ApplyForceEndToEndTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EffectParamKeys.Initialize();
        }

        [Test]
        public void Graph_ApplyEffectTemplateArgs_PresetApplyForce2D_BindsToForceInput2D()
        {
            string root = CreateTempRoot();
            try
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
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "attribute_bindings.json"),
                    """
                    [
                      {
                        "id": "Bind.Physics.ForceInput2D.X",
                        "attribute": "Physics.ForceRequestX",
                        "sink": "Physics.ForceInput2D",
                        "channel": 0,
                        "mode": "Override",
                        "scale": 1.0,
                        "resetPolicy": "ResetToZeroPerLogicFrame"
                      },
                      {
                        "id": "Bind.Physics.ForceInput2D.Y",
                        "attribute": "Physics.ForceRequestY",
                        "sink": "Physics.ForceInput2D",
                        "channel": 1,
                        "mode": "Override",
                        "scale": 1.0,
                        "resetPolicy": "ResetToZeroPerLogicFrame"
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var templates = new EffectTemplateRegistry();
                var templateLoader = new EffectTemplateLoader(pipeline, templates);
                templateLoader.Load(relativePath: "GAS/effects.json");

                var sinks = new AttributeSinkRegistry();
                GasAttributeSinks.RegisterBuiltins(sinks);
                var bindings = new AttributeBindingRegistry();
                var bindingLoader = new AttributeBindingLoader(pipeline, sinks, bindings);
                bindingLoader.Load(relativePath: "GAS/attribute_bindings.json");

                var graphCfg = new GraphConfig
                {
                    Id = "Test.ApplyForce2D",
                    Kind = "Effect",
                    Entry = "t1",
                    Nodes =
                    {
                        new GraphNodeConfig { Id = "t1", Op = "LoadExplicitTarget", Next = "fx" },
                        new GraphNodeConfig { Id = "fx", Op = "ConstFloat", FloatValue = 12.5f, Next = "fy" },
                        new GraphNodeConfig { Id = "fy", Op = "ConstFloat", FloatValue = -7.0f, Next = "a1" },
                        new GraphNodeConfig { Id = "a1", Op = "ApplyEffectTemplate", EffectTemplate = "Effect.Preset.ApplyForce2D", Inputs = { "t1", "fx", "fy" } }
                    }
                };

                var (pkg, diags) = GraphCompiler.Compile(graphCfg);
                That(pkg.HasValue, Is.True);
                That(diags.Count, Is.EqualTo(0));

                var symbols = pkg.Value.Symbols;
                var program = pkg.Value.Program;
                for (int i = 0; i < program.Length; i++)
                {
                    ref var ins = ref program[i];
                    if (ins.Op == (ushort)GraphNodeOp.ApplyEffectTemplate)
                    {
                        string name = symbols[ins.Imm];
                        ins.Imm = EffectTemplateIdRegistry.GetId(name);
                    }
                }

                using var world = World.Create();
                var requests = new EffectRequestQueue();
                var api = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: requests);

                int fxId = AttributeRegistry.GetId("Physics.ForceRequestX");
                int fyId = AttributeRegistry.GetId("Physics.ForceRequestY");
                That(fxId, Is.GreaterThanOrEqualTo(0));
                That(fyId, Is.GreaterThanOrEqualTo(0));

                var target = world.Create(new AttributeBuffer(), new ForceInput2D());

                GraphExecutor.Execute(world, caster: default, explicitTarget: target, targetPos: new IntVector2(0, 0), program, api);

                var chainOrders = new OrderQueue();
                chainOrders.TryEnqueue(new Order { OrderTagId = TestGasOrderTags.ChainPass });
                chainOrders.TryEnqueue(new Order { OrderTagId = TestGasOrderTags.ChainPass });

                var proposal = new Ludots.Core.Gameplay.GAS.Systems.EffectProposalProcessingSystem(world, requests, budget: null, templates: templates, inputRequests: null, chainOrders: chainOrders);
                proposal.Update(0.016f);

                ref var attr = ref world.Get<AttributeBuffer>(target);
                That(attr.GetCurrent(fxId), Is.EqualTo(12.5f));
                That(attr.GetCurrent(fyId), Is.EqualTo(-7.0f));

                var bindingSystem = new Ludots.Core.Gameplay.GAS.Systems.AttributeBindingSystem(world, sinks, bindings);
                bindingSystem.Update(0.016f);

                ref var force = ref world.Get<ForceInput2D>(target);
                That(force.Force.X.ToFloat(), Is.EqualTo(12.5f).Within(0.001f));
                That(force.Force.Y.ToFloat(), Is.EqualTo(-7.0f).Within(0.001f));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ApplyForceEndToEndTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
