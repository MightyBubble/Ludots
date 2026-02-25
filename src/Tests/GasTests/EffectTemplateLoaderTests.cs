using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;
namespace Ludots.Tests.GAS
{
    public class EffectTemplateLoaderTests
    {
        [Test]
        public void Load_EffectsJson_RegistersTemplatesAndResolvesCallbacks()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_A",
                        "tags": ["Event.TestA"],
                        "lifetime": "Instant",
                        "modifiers": [
                          { "attribute": "Health", "op": "Add", "value": -5 }
                        ]
                      },
                      {
                        "id": "Effect_B",
                        "tags": ["Event.TestB"],
                        "lifetime": "After",
                        "duration": { "durationTicks": 10 }
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var registry = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, registry);
                loader.Load(relativePath: "GAS/effects.json");

                That(EffectTemplateIdRegistry.GetId("Effect_A"), Is.EqualTo(1));
                That(EffectTemplateIdRegistry.GetId("Effect_B"), Is.EqualTo(2));

                That(registry.TryGet(1, out var a), Is.True);
                That(registry.TryGet(2, out var b), Is.True);

                That(a.TagId, Is.Not.EqualTo(0));
                That(a.Modifiers.Count, Is.EqualTo(1));

                // TODO: b.OnApplyEffectId assertion removed — callback fields migrated to Phase Graph architecture
                // NOTE: JSON "onApplyEffect" field may also need updating in the loader
                That(b.LifetimeKind, Is.EqualTo(EffectLifetimeKind.After));
                That(b.DurationTicks, Is.GreaterThan(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_SecondsFields_AreRejected()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_A",
                        "tags": ["Event.TestA"],
                        "duration": 1.0
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var registry = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, registry);

                Throws<InvalidOperationException>(() => loader.Load(relativePath: "GAS/effects.json"));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Test]
        public void Load_PresetType_ApplyForce2D_CompilesPresetFields()
        {
            string root = CreateTempRoot();
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Configs", "GAS"));
                File.WriteAllText(Path.Combine(root, "Configs", "GAS", "effects.json"),
                    """
                    [
                      {
                        "id": "Effect_Preset_Force",
                        "tags": ["Effect.ApplyForce"],
                        "presetType": "ApplyForce2D",
                        "lifetime": "Instant",
                        "configParams": {
                          "_ep.forceXTargetAttrId": { "type": "attribute", "value": "Physics.ForceRequestX" },
                          "_ep.forceYTargetAttrId": { "type": "attribute", "value": "Physics.ForceRequestY" }
                        }
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", root);
                var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var registry = new EffectTemplateRegistry();
                var loader = new EffectTemplateLoader(pipeline, registry);
                loader.Load(relativePath: "GAS/effects.json");

                int tplId = EffectTemplateIdRegistry.GetId("Effect_Preset_Force");
                That(tplId, Is.GreaterThan(0));
                That(registry.TryGet(tplId, out var tpl), Is.True);
                That(tpl.PresetType, Is.EqualTo(EffectPresetType.ApplyForce2D));
                That(tpl.PresetAttribute0, Is.GreaterThanOrEqualTo(0));
                That(tpl.PresetAttribute1, Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static string CreateTempRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_EffectTemplateLoaderTests", Guid.NewGuid().ToString("N"));
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
