using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.AI.WorldState;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class AiConfigLoaderTests
    {
        [Test]
        public void AiConfigLoader_LoadsAndCompilesFromVfs()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_AiConfigLoaderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            string core = Path.Combine(root, "Core");
            string mod = Path.Combine(root, "ModA");
            Directory.CreateDirectory(Path.Combine(core, "Configs", "AI"));
            Directory.CreateDirectory(Path.Combine(mod, "assets", "Configs", "AI"));

            File.WriteAllText(Path.Combine(core, "Configs", "AI", "atoms.json"), "[ { \"Id\": \"HasEnemy\" } ]");
            File.WriteAllText(Path.Combine(core, "Configs", "AI", "projection.json"), "[ { \"Id\": \"R0\", \"Atom\": \"HasEnemy\", \"Op\": \"EntityIsNonNull\", \"EntityKey\": 1 } ]");
            File.WriteAllText(Path.Combine(core, "Configs", "AI", "utility.json"), "[ { \"Id\": \"G0\", \"GoalPresetId\": 1, \"PlanningStrategyId\": 1, \"Weight\": 1, \"Bool\": [ { \"Atom\": \"HasEnemy\", \"TrueScore\": 1, \"FalseScore\": 0 } ] } ]");
            File.WriteAllText(Path.Combine(core, "Configs", "AI", "goap_actions.json"), "[ { \"Id\": \"A0\", \"Cost\": 1, \"Pre\": {\"Mask\":[],\"Values\":[]}, \"Post\": {\"Mask\":[],\"Values\":[]}, \"Order\": { \"OrderTagId\": 1234, \"SubmitMode\": 0, \"PlayerId\": 0 }, \"Bindings\": [] } ]");
            File.WriteAllText(Path.Combine(core, "Configs", "AI", "goap_goals.json"), "[ { \"Id\": \"GG0\", \"GoalPresetId\": 1, \"HeuristicWeight\": 1, \"Goal\": { \"Mask\": [\"HasEnemy\"], \"Values\": [\"HasEnemy\"] } } ]");
            File.WriteAllText(Path.Combine(core, "Configs", "AI", "htn_domain.json"), "{ \"Tasks\": [], \"Methods\": [], \"Subtasks\": [], \"Roots\": [] }");

            File.WriteAllText(Path.Combine(mod, "assets", "Configs", "AI", "atoms.json"), "[ { \"Id\": \"HasCover\" } ]");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            vfs.Mount("ModA", mod);
            var modLoader = new ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
            modLoader.LoadedModIds.Add("ModA");
            var pipeline = new ConfigPipeline(vfs, modLoader);

            var atoms = new AtomRegistry(capacity: 256);
            var loader = new AiConfigLoader(pipeline, atoms);
            var runtime = loader.LoadAndCompile(AiConfigCatalog.CreateDefault());

            Assert.That(runtime.Atoms.Count, Is.EqualTo(2));
            Assert.That(runtime.ProjectionTable.Rules.Length, Is.EqualTo(1));
            Assert.That(runtime.GoalSelector.Count, Is.EqualTo(1));
            Assert.That(runtime.ActionLibrary.Count, Is.EqualTo(1));
            Assert.That(runtime.GoapGoals.Count, Is.EqualTo(1));
        }
    }
}

