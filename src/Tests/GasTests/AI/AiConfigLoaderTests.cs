using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.AI.Planning;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.AI.WorldState;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public class AiConfigLoaderTests
    {
        private const int CastOrderTypeId = 100;
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
            Assert.That(runtime.ActionLibrary.OrderSpec[0].PayloadKind, Is.EqualTo(AiOrderPayloadKind.TargetEntity));
            Assert.That(runtime.ActionLibrary.GetBindings(0).Length, Is.EqualTo(1));
            Assert.That(runtime.GoapGoals.Count, Is.EqualTo(1));
        }

        [Test]
        public void AiConfigLoader_RejectsLegacyOrderTagId()
        {
            using var fixture = AiConfigFixture.Create(orderJson: "{ \"OrderTagId\": 1234, \"SubmitMode\": 0, \"PlayerId\": 1 }");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("OrderTagId"));
            Assert.That(ex.Message, Does.Contain("OrderTypeKey or OrderTypeId"));
        }

        [Test]
        public void AiConfigLoader_RejectsUnknownOrderTypeId()
        {
            using var fixture = AiConfigFixture.Create(orderJson: "{ \"OrderPayloadKind\": \"TargetEntity\", \"OrderTypeId\": 1234, \"SubmitMode\": 0, \"PlayerId\": 1 }");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown order type id 1234"));
        }

        [Test]
        public void AiConfigLoader_RejectsUnknownOrderTypeKey()
        {
            using var fixture = AiConfigFixture.Create(orderJson: "{ \"OrderPayloadKind\": \"TargetEntity\", \"OrderTypeKey\": \"missingOrder\", \"SubmitMode\": 0, \"PlayerId\": 1 }");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown order type key 'missingOrder'"));
        }

        [Test]
        public void AiConfigLoader_CompilesUtilityAiToFlatArrays()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();

            var runtime = fixture.Load();

            Assert.That(runtime.UtilityRuntime.IsEnabled, Is.True);
            Assert.That(runtime.UtilityRuntime.Profiles.Length, Is.EqualTo(1));
            Assert.That(runtime.UtilityRuntime.DecisionMakers.Length, Is.EqualTo(1));
            Assert.That(runtime.UtilityRuntime.Decisions.Length, Is.EqualTo(1));
            Assert.That(runtime.UtilityRuntime.Considerations.Length, Is.EqualTo(1));
            Assert.That(runtime.UtilityRuntime.TargetFilters.Length, Is.EqualTo(1));
            Assert.That(runtime.UtilityRuntime.TargetFilterOps.Length, Is.EqualTo(2));
            Assert.That(runtime.UtilityRuntime.Tasks.Length, Is.EqualTo(1));
            Assert.That(runtime.UtilityRuntime.Tasks[0].OrderTypeId, Is.EqualTo(CastOrderTypeId));
            Assert.That(runtime.UtilityRuntime.Tasks[0].PayloadKind, Is.EqualTo(AiOrderPayloadKind.CastAbility));
            Assert.That(runtime.UtilityRuntime.Tasks[0].AbilitySlotIndex, Is.EqualTo(0));
            Assert.That(runtime.UtilityRuntime.Decisions[0].TaskOffset, Is.EqualTo(0));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiBareOrderIntArgs()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteUtilityTasksJson("[ { \"id\": \"Task.Attack\", \"Kind\": \"SubmitOrder\", \"OrderPayloadKind\": \"CastAbility\", \"OrderTypeKey\": \"castAbility\", \"IntArg0\": 0, \"SubmitMode\": 0, \"PlayerId\": 0 } ]");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => fixture.Load())!;

            Assert.That(ex.Message, Does.Contain("IntArg0"));
            Assert.That(ex.Message, Does.Contain("Use AbilitySlotIndex"));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiSubmitOrderWithoutPayloadKind()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteUtilityTasksJson("[ { \"id\": \"Task.Attack\", \"Kind\": \"SubmitOrder\", \"OrderTypeKey\": \"castAbility\", \"AbilitySlotIndex\": 0, \"SubmitMode\": 0, \"PlayerId\": 0 } ]");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => fixture.Load())!;

            Assert.That(ex.Message, Does.Contain("OrderPayloadKind"));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiSubmitOrderWithoutAbilitySlot()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteUtilityTasksJson("[ { \"id\": \"Task.Attack\", \"Kind\": \"SubmitOrder\", \"OrderPayloadKind\": \"CastAbility\", \"OrderTypeKey\": \"castAbility\", \"SubmitMode\": 0, \"PlayerId\": 0 } ]");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => fixture.Load())!;

            Assert.That(ex.Message, Does.Contain("AbilitySlotIndex"));
            Assert.That(ex.Message, Does.Contain("typed CastAbility"));
        }

        [Test]
        public void AiConfigLoader_RejectsGoapBareOrderArgBinding()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteGoapActionsJson("[ { \"id\": \"A0\", \"Cost\": 1, \"Pre\": {\"Mask\":[],\"Values\":[]}, \"Post\": {\"Mask\":[],\"Values\":[]}, \"Order\": { \"OrderPayloadKind\": \"CastAbility\", \"OrderTypeKey\": \"castAbility\", \"SubmitMode\": 0, \"PlayerId\": 0 }, \"Bindings\": [ { \"Op\": \"IntToOrderArg0\", \"SourceKey\": 1 } ] } ]");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => fixture.Load())!;

            Assert.That(ex.Message, Does.Contain("IntToOrderArg0"));
            Assert.That(ex.Message, Does.Contain("Use IntToAbilitySlot"));
        }

        [Test]
        public void AiConfigLoader_RejectsGoapTargetEntityOrderWithoutTargetBinding()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteGoapActionsJson("[ { \"id\": \"A0\", \"Cost\": 1, \"Pre\": {\"Mask\":[],\"Values\":[]}, \"Post\": {\"Mask\":[],\"Values\":[]}, \"Order\": { \"OrderPayloadKind\": \"TargetEntity\", \"OrderTypeKey\": \"attackTarget\", \"SubmitMode\": 0, \"PlayerId\": 0 }, \"Bindings\": [] } ]");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => fixture.Load())!;

            Assert.That(ex.Message, Does.Contain("EntityToTarget"));
            Assert.That(ex.Message, Does.Contain("TargetEntity"));
        }

        [Test]
        public void AiConfigLoader_RejectsMissingGoapOrderPlayerId()
        {
            using var fixture = AiConfigFixture.Create(orderJson: "{ \"OrderTypeKey\": \"attackTarget\", \"SubmitMode\": 0 }");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => fixture.Load())!;

            Assert.That(ex.Message, Does.Contain("PlayerId"));
            Assert.That(ex.Message, Does.Contain("must declare"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void AiConfigLoader_RejectsNonPositiveGoapOrderPlayerId(int playerId)
        {
            using var fixture = AiConfigFixture.Create(orderJson: $"{{ \"OrderTypeKey\": \"attackTarget\", \"SubmitMode\": 0, \"PlayerId\": {playerId} }}");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => fixture.Load())!;

            Assert.That(ex.Message, Does.Contain("PlayerId"));
            Assert.That(ex.Message, Does.Contain("positive"));
        }

        [Test]
        public void AiConfigLoader_RejectsMissingUtilityTaskPlayerId()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(includeTaskPlayerId: false);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => fixture.Load())!;

            Assert.That(ex.Message, Does.Contain("PlayerId"));
            Assert.That(ex.Message, Does.Contain("must declare"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void AiConfigLoader_RejectsNonPositiveUtilityTaskPlayerId(int playerId)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(taskPlayerId: playerId);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => fixture.Load())!;

            Assert.That(ex.Message, Does.Contain("PlayerId"));
            Assert.That(ex.Message, Does.Contain("positive"));
        }

        [Test]
        public void AiConfigLoader_CompilesUtilityAiDefaultStanceKey()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(defaultStance: "Stance.ReturnFire");

            var runtime = fixture.Load();

            Assert.That(runtime.UtilityRuntime.Profiles[0].DefaultStanceId, Is.EqualTo(0));
            Assert.That(runtime.UtilityRuntime.Authoring.TryGetStanceId("Stance.ReturnFire", out int stanceId), Is.True);
            Assert.That(stanceId, Is.EqualTo(0));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiDefaultStanceId()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteProfilesJson("[ { \"id\": \"Profile.Basic\", \"DecisionIntervalSteps\": 1, \"MaxCandidates\": 32, \"DecisionMakers\": [ \"DM.Combat\" ], \"DefaultStanceId\": 0 } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("DefaultStanceId"));
            Assert.That(ex.Message, Does.Contain("DefaultStance"));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiUnknownTargetFilter()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(decisionTargetFilter: "TF.Missing");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown target filter 'TF.Missing'"));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiUnknownInput()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(considerationInput: "Input.Missing");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown input 'Input.Missing'"));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiUnknownOrderType()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(taskOrderTypeKey: "missingOrder");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown order type key 'missingOrder'"));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiUnknownGraph()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(includeGraphInput: true, graphKey: "Graph.Missing");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown graph key 'Graph.Missing'"));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiGraphScoreWriteOp()
        {
            using var fixture = AiConfigFixture.Create();
            var ex = Assert.Throws<InvalidOperationException>(() =>
                fixture.RegisterScoreGraph(
                    new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt }))!;

            Assert.That(ex.Message, Does.StartWith(GraphKindOperationPolicy.OperationNotAllowedError));
            Assert.That(ex.Message, Does.Contain("WriteBlackboardFloat"));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiUnknownSourceTag()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(inputKind: "SourceHasTag", inputTag: "Tag.Missing");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown gameplay tag 'Tag.Missing'"));
        }

        [Test]
        public void AiConfigLoader_CompilesActuatorReadinessInputFromActuatorKey()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(considerationInput: "Input.Ready");
            fixture.WriteUtilityInputsJson("[ { \"id\": \"Input.Ready\", \"Kind\": \"ActuatorReadiness01\", \"Actuator\": \"Actuator.Primary\" } ]");
            fixture.WriteUtilityActuatorsJson("[ { \"id\": \"Actuator.Primary\" } ]");

            var runtime = fixture.Load();

            Assert.That(runtime.UtilityRuntime.Inputs[0].Kind, Is.EqualTo(UtilityAiInputKind.ActuatorReadiness01));
            Assert.That(runtime.UtilityRuntime.Inputs[0].Arg0, Is.EqualTo(0));
            Assert.That(runtime.UtilityRuntime.Authoring.TryGetActuatorId("Actuator.Primary", out int actuatorId), Is.True);
            Assert.That(actuatorId, Is.EqualTo(0));
        }

        [Test]
        public void AiConfigLoader_RejectsNumericActuatorReadinessInput()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(considerationInput: "Input.Ready");
            fixture.WriteUtilityInputsJson("[ { \"id\": \"Input.Ready\", \"Kind\": \"ActuatorReadiness01\", \"ActuatorId\": 0 } ]");
            fixture.WriteUtilityActuatorsJson("[ { \"id\": \"Actuator.Primary\" } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("ActuatorId"));
            Assert.That(ex.Message, Does.Contain("Actuator"));
        }

        [Test]
        public void AiConfigLoader_RejectsDeadActuatorReadinessFields()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteUtilityActuatorsJson("[ { \"id\": \"Actuator.Primary\", \"ReadinessInput\": \"Input.Distance\" } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("ReadinessInput"));
            Assert.That(ex.Message, Does.Contain("Unexpected field"));
        }

        [Test]
        public void AiConfigLoader_RejectsNumericProjectionEntityKey()
        {
            using var fixture = AiConfigFixture.Create(
                projectionJson: "[ { \"id\": \"R0\", \"Atom\": \"HasEnemy\", \"Op\": \"EntityIsNonNull\", \"EntityKey\": 1 } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("EntityKey"));
            Assert.That(ex.Message, Does.Contain("semantic string"));
        }

        [Test]
        public void AiConfigLoader_RejectsMissingProjectionIntValue()
        {
            using var fixture = AiConfigFixture.Create(
                projectionJson: "[ { \"id\": \"R0\", \"Atom\": \"HasEnemy\", \"Op\": \"IntEquals\", \"IntKey\": \"Generic.IntParam\" } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("IntValue"));
        }

        [Test]
        public void AiConfigLoader_RejectsWrongProjectionFieldForOp()
        {
            using var fixture = AiConfigFixture.Create(
                projectionJson: "[ { \"id\": \"R0\", \"Atom\": \"HasEnemy\", \"Op\": \"EntityIsNonNull\", \"EntityKey\": \"Attack.TargetEntity\", \"IntValue\": 0 } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("IntValue"));
            Assert.That(ex.Message, Does.Contain("EntityIsNonNull"));
        }

        private sealed class AiConfigFixture : IDisposable
        {
            private readonly string _root;
            private readonly string _core;
            private readonly ConfigPipeline _pipeline;
            private readonly AiConfigValidationContext _validation;
            private readonly GraphProgramRegistry _graphs;

            private AiConfigFixture(
                string root,
                string core,
                ConfigPipeline pipeline,
                AiConfigValidationContext validation,
                GraphProgramRegistry graphs,
                int scoreGraphId)
            {
                _root = root;
                _core = core;
                _pipeline = pipeline;
                _validation = validation;
                _graphs = graphs;
                ScoreGraphId = scoreGraphId;
            }

            public int ScoreGraphId { get; }

            public static AiConfigFixture Create(string? orderJson = null, string? projectionJson = null)
            {
                string root = Path.Combine(Path.GetTempPath(), "Ludots_AiConfigLoaderTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);

                string core = Path.Combine(root, "Core");
                string mod = Path.Combine(root, "ModA");
                Directory.CreateDirectory(Path.Combine(core, "AI"));
                Directory.CreateDirectory(Path.Combine(mod, "assets", "AI"));

                orderJson ??= "{ \"OrderPayloadKind\": \"TargetEntity\", \"OrderTypeKey\": \"attackTarget\", \"SubmitMode\": 0, \"PlayerId\": 1 }";
                projectionJson ??= "[ { \"id\": \"R0\", \"Atom\": \"HasEnemy\", \"Op\": \"EntityIsNonNull\", \"EntityKey\": \"Attack.TargetEntity\" } ]";

                File.WriteAllText(Path.Combine(core, "AI", "atoms.json"), "[ { \"id\": \"HasEnemy\" } ]");
                File.WriteAllText(Path.Combine(core, "AI", "projection.json"), projectionJson);
                File.WriteAllText(Path.Combine(core, "AI", "utility.json"), "[ { \"id\": \"G0\", \"GoalPresetId\": 1, \"PlanningStrategyId\": 1, \"Weight\": 1, \"Bool\": [ { \"Atom\": \"HasEnemy\", \"TrueScore\": 1, \"FalseScore\": 0 } ] } ]");
                File.WriteAllText(Path.Combine(core, "AI", "goap_actions.json"), $"[ {{ \"id\": \"A0\", \"Cost\": 1, \"Pre\": {{\"Mask\":[],\"Values\":[]}}, \"Post\": {{\"Mask\":[],\"Values\":[]}}, \"Order\": {orderJson}, \"Bindings\": [ {{ \"Op\": \"EntityToTarget\", \"SourceKey\": \"Attack.TargetEntity\" }} ] }} ]");
                File.WriteAllText(Path.Combine(core, "AI", "goap_goals.json"), "[ { \"id\": \"GG0\", \"GoalPresetId\": 1, \"HeuristicWeight\": 1, \"Goal\": { \"Mask\": [\"HasEnemy\"], \"Values\": [\"HasEnemy\"] } } ]");
                File.WriteAllText(Path.Combine(core, "AI", "htn_domain.json"), "{ \"Tasks\": [], \"Methods\": [], \"Subtasks\": [], \"Roots\": [] }");

                File.WriteAllText(Path.Combine(mod, "assets", "AI", "atoms.json"), "[ { \"id\": \"HasCover\" } ]");

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", core);
                vfs.Mount("ModA", mod);
                var modLoader = new ModLoader(vfs, new Ludots.Core.Scripting.FunctionRegistry(), new Ludots.Core.Scripting.TriggerManager());
                modLoader.LoadedModIds.Add("ModA");
                var pipeline = new ConfigPipeline(vfs, modLoader);

                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = "castAbility",
                    OrderTypeId = CastOrderTypeId
                });
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = "attackTarget",
                    OrderTypeId = AttackOrderTypeId
                });

                TagRegistry.Clear();
                GraphIdRegistry.Clear();
                OrderBlackboardKeyRegistry.ResetToBuiltins();
                OrderBlackboardKeyRegistry.Register("Attack.TargetEntity");

                var graphs = new GraphProgramRegistry();
                int graphId = GraphIdRegistry.Register("Graph.AI.Score");
                graphs.Register(graphId, new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                }, GraphKind.Score);

                return new AiConfigFixture(root, core, pipeline, new AiConfigValidationContext(orderTypes, graphs), graphs, graphId);
            }

            public void RegisterScoreGraph(params GraphInstruction[] program)
            {
                _graphs.Clear();
                _graphs.Register(ScoreGraphId, program, GraphKind.Score);
            }

            public void WriteUtilityConfig(
                string decisionTargetFilter = "TF.Hostile",
                string considerationInput = "Input.Distance",
                string taskOrderTypeKey = "castAbility",
                bool includeGraphInput = false,
                string graphKey = "Graph.AI.Score",
                string inputKind = "DistanceToTarget",
                string inputTag = "",
                string? defaultStance = null,
                int taskPlayerId = 1,
                bool includeTaskPlayerId = true)
            {
                string ai = Path.Combine(_core, "AI");
                File.WriteAllText(Path.Combine(ai, "target_filters.json"),
                    "[ { \"id\": \"TF.Hostile\", \"MaxResults\": 32, \"Ops\": [ " +
                    "{ \"Kind\": \"SpatialRadius\", \"RadiusCm\": 900 }, " +
                    "{ \"Kind\": \"Relationship\", \"Value\": \"Hostile\" } ] } ]");

                string inputGraph = includeGraphInput
                    ? $", {{ \"id\": \"Input.Graph\", \"Kind\": \"GraphScore\", \"GraphKey\": \"{graphKey}\" }}"
                    : string.Empty;
                string primaryInput = string.Equals(inputKind, "SourceHasTag", StringComparison.Ordinal)
                    ? $"{{ \"id\": \"Input.Distance\", \"Kind\": \"SourceHasTag\", \"Tag\": \"{inputTag}\" }}"
                    : "{ \"id\": \"Input.Distance\", \"Kind\": \"DistanceToTarget\" }";
                File.WriteAllText(Path.Combine(ai, "inputs.json"),
                    "[ " + primaryInput + inputGraph + " ]");
                File.WriteAllText(Path.Combine(ai, "normalizations.json"),
                    "[ { \"id\": \"Norm.Close\", \"Kind\": \"RangeInverse\", \"Min\": 0, \"Max\": 900 } ]");
                File.WriteAllText(Path.Combine(ai, "curves.json"),
                    "[ { \"id\": \"Curve.Linear\", \"Kind\": \"Linear\" } ]");
                string taskPlayerProperty = includeTaskPlayerId
                    ? $", \"PlayerId\": {taskPlayerId}"
                    : string.Empty;
                File.WriteAllText(Path.Combine(ai, "tasks.json"),
                    $"[ {{ \"id\": \"Task.Attack\", \"Kind\": \"SubmitOrder\", \"OrderPayloadKind\": \"CastAbility\", \"OrderTypeKey\": \"{taskOrderTypeKey}\", \"AbilitySlotIndex\": 0, \"SubmitMode\": 0{taskPlayerProperty} }} ]");
                File.WriteAllText(Path.Combine(ai, "decisions.json"),
                    "[ { \"id\": \"Decision.Attack\", " +
                    $"\"TargetFilter\": \"{decisionTargetFilter}\", " +
                    "\"Priority\": 10, \"BaseScore\": 1, \"Weight\": 1, " +
                    "\"Autocast\": true, \"OrdinaryAttack\": true, \"RequiresTarget\": true, " +
                    "\"Considerations\": [ { " +
                    $"\"Input\": \"{considerationInput}\", \"Normalization\": \"Norm.Close\", \"Curve\": \"Curve.Linear\", \"Aggregate\": \"Multiply\" }} ], " +
                    "\"Tasks\": [ \"Task.Attack\" ] } ]");
                File.WriteAllText(Path.Combine(ai, "decision_makers.json"),
                    "[ { \"id\": \"DM.Combat\", \"SelectionMode\": \"FixedPriority\", \"Decisions\": [ \"Decision.Attack\" ] } ]");
                string defaultStanceProperty = string.IsNullOrWhiteSpace(defaultStance)
                    ? string.Empty
                    : $", \"DefaultStance\": \"{defaultStance}\"";
                WriteProfilesJson("[ { \"id\": \"Profile.Basic\", \"DecisionIntervalSteps\": 1, \"MaxCandidates\": 32, \"DecisionMakers\": [ \"DM.Combat\" ]" + defaultStanceProperty + " } ]");
                File.WriteAllText(Path.Combine(ai, "stances.json"), string.IsNullOrWhiteSpace(defaultStance)
                    ? "[]"
                    : $"[ {{ \"id\": \"{defaultStance}\", \"AutoAcquire\": true, \"Retaliate\": true }} ]");
                File.WriteAllText(Path.Combine(ai, "actuators.json"), "[]");
            }

            public void WriteProfilesJson(string json)
            {
                File.WriteAllText(Path.Combine(_core, "AI", "profiles.json"), json);
            }

            public void WriteUtilityInputsJson(string json)
            {
                File.WriteAllText(Path.Combine(_core, "AI", "inputs.json"), json);
            }

            public void WriteUtilityActuatorsJson(string json)
            {
                File.WriteAllText(Path.Combine(_core, "AI", "actuators.json"), json);
            }

            public void WriteUtilityTasksJson(string json)
            {
                File.WriteAllText(Path.Combine(_core, "AI", "tasks.json"), json);
            }

            public void WriteGoapActionsJson(string json)
            {
                File.WriteAllText(Path.Combine(_core, "AI", "goap_actions.json"), json);
            }

            public AiCompiledRuntime Load()
            {
                var atoms = new AtomRegistry(capacity: 256);
                var loader = new AiConfigLoader(_pipeline, atoms, _validation);
                return loader.LoadAndCompile(AiConfigCatalog.CreateDefault());
            }

            public void Dispose()
            {
                OrderBlackboardKeyRegistry.ResetToBuiltins();
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
