using System;
using System.IO;
using System.Linq;
using Ludots.Core.Config;
using Ludots.Core.Input.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class InputConfigBindingRemovalTests
    {
        [Test]
        public void LaterFragment_RemovesBinding_AndCanAddItBack()
        {
            string root = TempRoot();
            try
            {
                WritePair(
                    root,
                    """
                    {
                      "actions": [ { "id": "PrecisionModifier", "name": "Precision", "type": "Button" }, { "id": "ModifierSubtract", "name": "Subtract", "type": "Button" } ],
                      "contexts": [ {
                        "id": "Default_Gameplay",
                        "bindings": [
                          { "actionId": "PrecisionModifier", "path": "<Keyboard>/leftCtrl" },
                          { "actionId": "ModifierSubtract", "path": "<Keyboard>/leftCtrl" }
                        ]
                      } ]
                    }
                    """,
                    """
                    {
                      "actions": [],
                      "contexts": [ {
                        "id": "Default_Gameplay",
                        "bindings": [
                          { "actionId": "ModifierSubtract", "path": "<Keyboard>/leftCtrl", "removed": true },
                          { "actionId": "PrecisionModifier", "path": "<Keyboard>/leftSuper" }
                        ]
                      } ]
                    }
                    """);

                InputConfigRoot merged = Load(root);
                string[] keys = Keys(merged, "Default_Gameplay");
                Assert.That(keys, Is.EqualTo(new[]
                {
                    "PrecisionModifier|<Keyboard>/leftCtrl",
                    "PrecisionModifier|<Keyboard>/leftSuper",
                }));

                File.WriteAllText(Path.Combine(root, "ModA", "assets", "Input", "default_input.json"), """
                    {
                      "actions": [],
                      "contexts": [ {
                        "id": "Default_Gameplay",
                        "bindings": [
                          { "actionId": "ModifierSubtract", "path": "<Keyboard>/leftCtrl", "removed": true },
                          { "actionId": "PrecisionModifier", "path": "<Keyboard>/leftSuper" },
                          { "actionId": "ModifierSubtract", "path": "<Keyboard>/leftCtrl" }
                        ]
                      } ]
                    }
                    """);
                string[] restored = Keys(Load(root), "Default_Gameplay");
                Assert.That(restored, Does.Contain("ModifierSubtract|<Keyboard>/leftCtrl"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void Removal_OfMissingBinding_Fails()
        {
            string root = TempRoot();
            try
            {
                WritePair(
                    root,
                    """
                    {
                      "actions": [ { "id": "Move", "name": "Move", "type": "Button" } ],
                      "contexts": [ { "id": "Default_Gameplay", "bindings": [ { "actionId": "Move", "path": "<Keyboard>/w" } ] } ]
                    }
                    """,
                    """
                    {
                      "actions": [],
                      "contexts": [ { "id": "Default_Gameplay", "bindings": [ { "actionId": "ModifierSubtract", "path": "<Keyboard>/leftCtrl", "removed": true } ] } ]
                    }
                    """);

                var ex = Assert.Throws<InvalidOperationException>(() => Load(root));
                Assert.That(ex!.Message, Does.Contain("LUDOTS_INPUT_BINDING_REMOVE_ABSENT"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void RootDefaults_KeepCtrlSubtract_UntilS4RemovesIt()
        {
            string repo = FindRepoRoot();
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(repo, "assets"));
            var coreOnly = new InputConfigPipelineLoader(new ConfigPipeline(vfs, modLoader: null!)).Load();
            Assert.That(Keys(coreOnly, "Default_Gameplay"), Does.Contain("ModifierSubtract|<Keyboard>/leftCtrl"));
            Assert.That(Keys(coreOnly, "Default_Gameplay"), Does.Contain("AlternateModifier|<Keyboard>/leftAlt"));
            Assert.That(Keys(coreOnly, "Default_Gameplay"), Does.Not.Contain("PrecisionModifier|<Keyboard>/leftSuper"));
            Assert.That(Keys(coreOnly, "Default_Gameplay"), Does.Not.Contain("ModifierSubtract|<Keyboard>/leftAlt"));

            vfs.Mount("CrowdSimulationS4DeployMod", Path.Combine(repo, "mods", "showcases", "crowd_simulation", "CrowdSimulationS4DeployMod"));
            var mods = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            mods.LoadedModIds.Add("CrowdSimulationS4DeployMod");
            InputConfigRoot withS4 = new InputConfigPipelineLoader(new ConfigPipeline(vfs, mods)).Load();
            string[] gameplay = Keys(withS4, "Default_Gameplay");
            Assert.That(gameplay, Does.Not.Contain("ModifierSubtract|<Keyboard>/leftCtrl"));
            Assert.That(gameplay, Does.Contain("PrecisionModifier|<Keyboard>/leftCtrl"));
            Assert.That(gameplay, Does.Contain("PrecisionModifier|<Keyboard>/rightCtrl"));
            Assert.That(gameplay, Does.Contain("PrecisionModifier|<Keyboard>/leftSuper"));
            Assert.That(gameplay, Does.Contain("PrecisionModifier|<Keyboard>/rightSuper"));
            Assert.That(gameplay, Does.Contain("QueueModifier|<Keyboard>/leftShift"));
            Assert.That(gameplay, Does.Contain("AlternateModifier|<Keyboard>/leftAlt"));
            Assert.That(gameplay, Does.Not.Contain("ModifierSubtract|<Keyboard>/leftAlt"));
        }

        private static string[] Keys(InputConfigRoot config, string contextId)
        {
            InputContextDef context = config.Contexts.Single(c => string.Equals(c.Id, contextId, StringComparison.Ordinal));
            return context.Bindings
                .Select(b => $"{b.ActionId}|{b.Path}")
                .ToArray();
        }

        private static InputConfigRoot Load(string root)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(root, "Core"));
            vfs.Mount("ModA", Path.Combine(root, "ModA"));
            var mods = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            mods.LoadedModIds.Add("ModA");
            var pipeline = new ConfigPipeline(vfs, mods);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("Input/default_input.json", ConfigMergePolicy.DeepObject));
            return new InputConfigPipelineLoader(pipeline).Load(catalog);
        }

        private static void WritePair(string root, string coreJson, string modJson)
        {
            string coreDir = Path.Combine(root, "Core", "Input");
            string modDir = Path.Combine(root, "ModA", "assets", "Input");
            Directory.CreateDirectory(coreDir);
            Directory.CreateDirectory(modDir);
            File.WriteAllText(Path.Combine(coreDir, "default_input.json"), coreJson);
            File.WriteAllText(Path.Combine(modDir, "default_input.json"), modJson);
            File.WriteAllText(Path.Combine(root, "Core", "config_catalog.json"), "[]");
        }

        private static string TempRoot()
            => Path.Combine(Path.GetTempPath(), "Ludots_InputBindingRemoval", Guid.NewGuid().ToString("N"));

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }
    }
}
