using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.WorldState;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class AiConfigLoaderTests
    {
        private const int AttackOrderTypeId = 102;

        [Test]
        public void AiConfigLoader_LoadsAndCompilesFromVfs()
        {
            using var fixture = AiConfigFixture.Create();

            var runtime = fixture.Load();

            Assert.That(runtime.Atoms.Count, Is.EqualTo(2));
            Assert.That(runtime.ProjectionTable.Rules.Length, Is.EqualTo(1));
            Assert.That(runtime.GoalSelector.Count, Is.EqualTo(1));
            Assert.That(runtime.ActionLibrary.Count, Is.EqualTo(1));
            Assert.That(runtime.ActionLibrary.OrderSpec[0].OrderTypeId, Is.EqualTo(AttackOrderTypeId));
            Assert.That(runtime.GoapGoals.Count, Is.EqualTo(1));
        }

        [Test]
        public void AiConfigLoader_RejectsLegacyOrderTagId()
        {
            using var fixture = AiConfigFixture.Create(orderJson: "{ \"OrderTagId\": 1234, \"SubmitMode\": 0, \"PlayerId\": 0 }");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("OrderTagId"));
            Assert.That(ex.Message, Does.Contain("OrderTypeKey or OrderTypeId"));
        }

        [Test]
        public void AiConfigLoader_RejectsUnknownOrderTypeId()
        {
            using var fixture = AiConfigFixture.Create(orderJson: "{ \"OrderTypeId\": 1234, \"SubmitMode\": 0, \"PlayerId\": 0 }");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown order type id 1234"));
        }

        [Test]
        public void AiConfigLoader_RejectsUnknownOrderTypeKey()
        {
            using var fixture = AiConfigFixture.Create(orderJson: "{ \"OrderTypeKey\": \"missingOrder\", \"SubmitMode\": 0, \"PlayerId\": 0 }");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown order type key 'missingOrder'"));
        }

        [Test]
        public void AiConfigLoader_RejectsUnknownAbilityId()
        {
            using var fixture = AiConfigFixture.Create(orderJson: "{ \"OrderTypeKey\": \"attackTarget\", \"AbilityId\": 9090, \"SubmitMode\": 0, \"PlayerId\": 0 }");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown ability id 9090"));
        }

        [Test]
        public void AiConfigLoader_RejectsUnknownAbilityKey()
        {
            using var fixture = AiConfigFixture.Create(orderJson: "{ \"OrderTypeKey\": \"attackTarget\", \"AbilityKey\": \"Ability.Missing\", \"SubmitMode\": 0, \"PlayerId\": 0 }");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown ability key 'Ability.Missing'"));
        }

        private sealed class AiConfigFixture : IDisposable
        {
            private readonly string _root;
            private readonly ConfigPipeline _pipeline;
            private readonly AiConfigValidationContext _validation;

            private AiConfigFixture(string root, ConfigPipeline pipeline, AiConfigValidationContext validation)
            {
                _root = root;
                _pipeline = pipeline;
                _validation = validation;
            }

            public static AiConfigFixture Create(string? orderJson = null)
            {
                string root = Path.Combine(Path.GetTempPath(), "Ludots_AiConfigLoaderTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);

                string core = Path.Combine(root, "Core");
                string mod = Path.Combine(root, "ModA");
                Directory.CreateDirectory(Path.Combine(core, "Configs", "AI"));
                Directory.CreateDirectory(Path.Combine(mod, "assets", "Configs", "AI"));

                orderJson ??= "{ \"OrderTypeKey\": \"attackTarget\", \"SubmitMode\": 0, \"PlayerId\": 0 }";

                File.WriteAllText(Path.Combine(core, "Configs", "AI", "atoms.json"), "[ { \"id\": \"HasEnemy\" } ]");
                File.WriteAllText(Path.Combine(core, "Configs", "AI", "projection.json"), "[ { \"id\": \"R0\", \"Atom\": \"HasEnemy\", \"Op\": \"EntityIsNonNull\", \"EntityKey\": 1 } ]");
                File.WriteAllText(Path.Combine(core, "Configs", "AI", "utility.json"), "[ { \"id\": \"G0\", \"GoalPresetId\": 1, \"PlanningStrategyId\": 1, \"Weight\": 1, \"Bool\": [ { \"Atom\": \"HasEnemy\", \"TrueScore\": 1, \"FalseScore\": 0 } ] } ]");
                File.WriteAllText(Path.Combine(core, "Configs", "AI", "goap_actions.json"), $"[ {{ \"id\": \"A0\", \"Cost\": 1, \"Pre\": {{\"Mask\":[],\"Values\":[]}}, \"Post\": {{\"Mask\":[],\"Values\":[]}}, \"Order\": {orderJson}, \"Bindings\": [] }} ]");
                File.WriteAllText(Path.Combine(core, "Configs", "AI", "goap_goals.json"), "[ { \"id\": \"GG0\", \"GoalPresetId\": 1, \"HeuristicWeight\": 1, \"Goal\": { \"Mask\": [\"HasEnemy\"], \"Values\": [\"HasEnemy\"] } } ]");
                File.WriteAllText(Path.Combine(core, "Configs", "AI", "htn_domain.json"), "{ \"Tasks\": [], \"Methods\": [], \"Subtasks\": [], \"Roots\": [] }");

                File.WriteAllText(Path.Combine(mod, "assets", "Configs", "AI", "atoms.json"), "[ { \"id\": \"HasCover\" } ]");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", core);
                vfs.Mount("ModA", mod);
                var modLoader = new ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
                modLoader.LoadedModIds.Add("ModA");
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var orderTypes = new OrderTypeRegistry();
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = "attackTarget",
                    OrderTypeId = AttackOrderTypeId
                });

                AbilityIdRegistry.Clear();
                int abilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
                var abilities = new AbilityDefinitionRegistry();
                abilities.Register(abilityId, new AbilityDefinition());

                return new AiConfigFixture(root, pipeline, new AiConfigValidationContext(orderTypes, abilities));
            }

            public AiCompiledRuntime Load()
            {
                var atoms = new AtomRegistry(capacity: 256);
                var loader = new AiConfigLoader(_pipeline, atoms, _validation);
                return loader.LoadAndCompile(AiConfigCatalog.CreateDefault());
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
