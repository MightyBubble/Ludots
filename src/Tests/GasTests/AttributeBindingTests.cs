using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Physics;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class AttributeBindingTests
    {
        [Test]
        public void AttributeBindingLoader_AndSystem_WriteForceInput2D()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
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

                var sinks = new AttributeSinkRegistry();
                GasAttributeSinks.RegisterBuiltins(sinks);

                var registry = new AttributeBindingRegistry();
                var loader = new AttributeBindingLoader(pipeline, sinks, registry);
                loader.Load(relativePath: "GAS/attribute_bindings.json");

                using var world = World.Create();
                int fxId = AttributeRegistry.Register("Physics.ForceRequestX");
                int fyId = AttributeRegistry.Register("Physics.ForceRequestY");
                var e = world.Create(new AttributeBuffer(), new ForceInput2D());
                ref var attr = ref world.Get<AttributeBuffer>(e);
                attr.SetCurrent(fxId, 3f);
                attr.SetCurrent(fyId, 4f);

                var system = new Ludots.Core.Gameplay.GAS.Systems.AttributeBindingSystem(world, sinks, registry);
                system.Update(0.016f);

                ref var force = ref world.Get<ForceInput2D>(e);
                That(force.Force.X.ToFloat(), Is.EqualTo(3f).Within(0.001f));
                That(force.Force.Y.ToFloat(), Is.EqualTo(4f).Within(0.001f));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_AttributeBindingTests", Guid.NewGuid().ToString("N"));
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
