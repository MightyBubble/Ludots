using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Config;
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
            Assert.That(runtime.UtilityRuntime.Tasks[0].OrderTypeId, Is.EqualTo(AttackOrderTypeId));
            Assert.That(runtime.UtilityRuntime.Decisions[0].AbilityId, Is.EqualTo(fixture.AttackAbilityId));
            Assert.That(runtime.UtilityRuntime.Decisions[0].DecisionRepeatDelaySteps, Is.EqualTo(2));
            Assert.That(runtime.UtilityRuntime.Profiles[0].MaxCandidates, Is.EqualTo(32));
            Assert.That(runtime.UtilityRuntime.Profiles[0].MaxGraphScoreInstructions, Is.EqualTo(256));
        }

        [Test]
        public void AiConfigLoader_AcceptsDecisionWithSingleSubmitOrderTask()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();

            AiCompiledRuntime runtime = fixture.Load();

            Assert.That(runtime.UtilityRuntime.Decisions[0].TaskIndex, Is.Zero);
            Assert.That(runtime.UtilityRuntime.Tasks[0].Kind, Is.EqualTo(UtilityAiTaskKind.SubmitOrder));
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
            fixture.WriteProfilesJson("[ { \"id\": \"Profile.Basic\", \"DecisionIntervalSteps\": 1, \"MaxCandidates\": 32, \"MaxGraphScoreInstructions\": 256, \"DecisionMakers\": [ \"DM.Combat\" ], \"DefaultStanceId\": 0 } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("DefaultStanceId"));
            Assert.That(ex.Message, Does.Contain("DefaultStance"));
        }

        [Test]
        public void AiConfigLoader_RejectsMissingUtilityAiGraphScoreInstructionBudget()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteProfilesJson(
                "[ { \"id\": \"Profile.Basic\", \"DecisionIntervalSteps\": 1, \"MaxCandidates\": 32, \"DecisionMakers\": [ \"DM.Combat\" ] } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("MaxGraphScoreInstructions"));
            Assert.That(ex.Message, Does.Contain("required"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void AiConfigLoader_RejectsNonPositiveUtilityAiGraphScoreInstructionBudget(int budget)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteProfilesJson(
                $"[ {{ \"id\": \"Profile.Basic\", \"DecisionIntervalSteps\": 1, \"MaxCandidates\": 32, \"MaxGraphScoreInstructions\": {budget}, \"DecisionMakers\": [ \"DM.Combat\" ] }} ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("MaxGraphScoreInstructions"));
            Assert.That(ex.Message, Does.Contain("positive"));
        }

        [TestCase("GraphScoreInstructionBudget")]
        [TestCase("MaxGraphInstructions")]
        public void AiConfigLoader_RejectsUnknownUtilityAiGraphScoreBudgetAliases(string alias)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteProfilesJson(
                $"[ {{ \"id\": \"Profile.Basic\", \"DecisionIntervalSteps\": 1, \"MaxCandidates\": 32, \"MaxGraphScoreInstructions\": 256, \"{alias}\": 256, \"DecisionMakers\": [ \"DM.Combat\" ] }} ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain(alias));
            Assert.That(ex.Message, Does.Contain("Unsupported profile field"));
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

        [TestCase("Sequence")]
        [TestCase("Parallel")]
        [TestCase("ParallelComplete")]
        [TestCase("UnexpectedTask")]
        [TestCase("submitorder")]
        public void AiConfigLoader_RejectsUnsupportedUtilityAiTaskKind(string kind)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteTasksJson($"[ {{ \"id\": \"Task.Attack\", \"Kind\": \"{kind}\" }} ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("AI/tasks.json[0].Kind"));
            Assert.That(ex.Message, Does.Contain($"Task kind '{kind}' is not supported"));
            Assert.That(ex.Message, Does.Contain("SubmitOrder"));
        }

        [Test]
        public void AiConfigLoader_RejectsDecisionWithMultipleTasks()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(
                decisionTasksJson: "\"Task.Attack\", \"Task.Second\"");
            fixture.WriteTasksJson(
                "[ " +
                "{ \"id\": \"Task.Attack\", \"Kind\": \"SubmitOrder\", \"OrderTypeKey\": \"attackTarget\" }, " +
                "{ \"id\": \"Task.Second\", \"Kind\": \"SubmitOrder\", \"OrderTypeKey\": \"attackTarget\" } " +
                "]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("AI/decisions.json:Decision.Attack.Tasks"));
            Assert.That(ex.Message, Does.Contain("exactly one task"));
        }

        [Test]
        public void AiConfigLoader_RejectsUtilityAiUnknownAbility()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(decisionAbilityKey: "Ability.Missing");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("unknown ability key 'Ability.Missing'"));
        }

        [Test]
        public void AiConfigLoader_RejectsDecisionAndSubmittedTaskAbilityConflict()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteTasksJson(
                "[ { \"id\": \"Task.Attack\", \"Kind\": \"SubmitOrder\", " +
                "\"OrderTypeKey\": \"attackTarget\", \"AbilityKey\": \"Ability.Test.Defend\", " +
                "\"AbilitySlotIndex\": 0, \"SubmitMode\": 0, \"PlayerId\": 0 } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("conflicts with submitted task ability id"));
            Assert.That(ex.Message, Does.Contain("AI/decisions.json[0]"));
        }

        [Test]
        public void AiConfigLoader_RejectsDecisionAndSubmittedTaskSlotConflict()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteTasksJson(
                "[ { \"id\": \"Task.Attack\", \"Kind\": \"SubmitOrder\", " +
                "\"OrderTypeKey\": \"attackTarget\", \"AbilityKey\": \"Ability.Test.Attack\", " +
                "\"AbilitySlotIndex\": 1, \"SubmitMode\": 0, \"PlayerId\": 0 } ]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("conflicts with submitted task ability slot"));
            Assert.That(ex.Message, Does.Contain("AI/decisions.json[0]"));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void AiConfigLoader_RejectsPartialDecisionAndTaskAbilityIntent(
            bool includeAbility,
            bool includeSlot)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(
                decisionAbilityKey: includeAbility ? "Ability.Test.Attack" : null,
                decisionAbilitySlotIndex: includeSlot ? 0 : null);

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("complete ability/slot intent"));
            Assert.That(ex.Message, Does.Contain("AI/decisions.json[0]"));
        }

        [Test]
        public void UtilityActuatorGate_ResolvesFormalJsonActuatorTableEntryToGasAbility()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig();
            fixture.WriteActuatorsJson(
                "[ { \"id\": \"Actuator.Attack\", \"AbilityKey\": \"Ability.Test.Attack\" } ]");
            AiCompiledRuntime compiled = fixture.Load();

            Assert.That(compiled.UtilityRuntime.Actuators, Has.Length.EqualTo(1));
            Assert.That(compiled.UtilityRuntime.Actuators[0].Id, Is.Zero);
            Assert.That(compiled.UtilityRuntime.Actuators[0].AbilityId, Is.EqualTo(fixture.AttackAbilityId));
            Assert.That(fixture.AttackAbilityId, Is.Not.EqualTo(compiled.UtilityRuntime.Actuators[0].Id));

            using var world = World.Create();
            Entity actor = world.Create(new ActuatorReadiness
            {
                ActuatorId = compiled.UtilityRuntime.Actuators[0].Id,
                Ready01 = 0f
            });
            var gate = new UtilityAiAbilityActuatorGate(world, compiled.UtilityRuntime);

            Assert.That(
                gate.Evaluate(actor, fixture.AttackAbilityId),
                Is.EqualTo(AbilityActivationRefusalReason.ActuatorNotReady));
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
        public void AiConfigLoader_GraphScoreMissingGraphRegistry_FailsDuringCompile()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(includeGraphInput: true, considerationInput: "Input.Graph");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.LoadWithoutGraphRegistry());

            Assert.That(ex!.Message, Does.Contain("GraphScore"));
            Assert.That(ex.Message, Does.Contain("GraphProgramRegistry"));
        }

        [TestCase((ushort)GraphNodeOp.ApplyEffectTemplate, GraphKindOperationPolicy.OperationNotAllowedError, "ApplyEffectTemplate")]
        [TestCase((ushort)GraphNodeOp.RelationshipSetMetric, GraphKindOperationPolicy.OperationNotAllowedError, "RelationshipSetMetric")]
        [TestCase((ushort)GraphNodeOp.WriteBlackboardFloat, GraphKindOperationPolicy.OperationNotAllowedError, "WriteBlackboardFloat")]
        [TestCase((ushort)GraphNodeOp.BeginLifecycleTransaction, GraphKindOperationPolicy.OperationNotAllowedError, "BeginLifecycleTransaction")]
        [TestCase((ushort)GraphNodeOp.InvokeBuiltin, GraphKindOperationPolicy.OperationNotAllowedError, "InvokeBuiltin")]
        [TestCase(ushort.MaxValue, GraphKindOperationPolicy.MissingOperationMetadataError, "65535")]
        public void AiConfigLoader_RejectsUtilityAiGraphScoreForbiddenOpcode(
            ushort rawOp,
            string expectedErrorCode,
            string expectedOperation)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.RegisterScoreGraph(new GraphInstruction { Op = rawOp });
            fixture.WriteUtilityConfig(includeGraphInput: true, considerationInput: "Input.Graph");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain(expectedErrorCode));
            Assert.That(ex.Message, Does.Contain("kind='Score'"));
            Assert.That(ex.Message, Does.Contain($"operation='{expectedOperation}'"));
            Assert.That(ex.Message, Does.Contain("instructionIndex=0"));
        }

        [Test]
        public void UtilityAiGraphScoreSafety_DelegatesToTypedGraphOperationPolicy()
        {
            var program = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat }
            };

            InvalidOperationException policyError = Assert.Throws<InvalidOperationException>(() =>
                GraphKindOperationPolicy.RequireAllowed(
                    GraphKind.Score,
                    program,
                    GasGraphOpHandlerTable.Instance,
                    graphId: 41,
                    entrypoint: "AI test"))!;
            InvalidOperationException utilityError = Assert.Throws<InvalidOperationException>(() =>
                UtilityAiGraphSafety.ValidateScoreProgram(program, "AI test", graphId: 41))!;

            Assert.That(utilityError.Message, Is.EqualTo(policyError.Message));
        }

        [Test]
        public void AiConfigLoader_FreezesValidatedGraphScoreProgramInCompiledRuntime()
        {
            using var fixture = AiConfigFixture.Create();
            var authoredProgram = new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 0, ImmF = 0.75f }
            };
            fixture.RegisterScoreGraph(authoredProgram);
            fixture.WriteUtilityConfig(includeGraphInput: true, considerationInput: "Input.Graph");

            UtilityAiCompiledRuntime runtime = fixture.Load().UtilityRuntime;
            authoredProgram[0] = new GraphInstruction { Op = (ushort)GraphNodeOp.InvokeBuiltin };
            fixture.RegisterScoreGraph(new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardFloat });

            Assert.That(runtime.GraphScorePrograms, Has.Length.EqualTo(1));
            Assert.That(runtime.GraphScorePrograms[0].GraphId, Is.EqualTo(fixture.ScoreGraphId));
            Assert.That(runtime.GraphScorePrograms[0].Program[0].Op, Is.EqualTo((ushort)GraphNodeOp.ConstFloat));
            Assert.That(runtime.Inputs[1].Arg0, Is.EqualTo(0));
        }

        [TestCase("Shared" + "Cooldown" + "Tag", "\"Cooldown.Global.Attack\"")]
        [TestCase("Cool" + "downSteps", "30")]
        public void AiConfigLoader_RejectsRemovedUtilityDecisionTimingFields(string field, string value)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(extraDecisionJson: $", \"{field}\": {value}");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain($"Unsupported decision field '{field}'"));
        }

        [TestCase("Autocast")]
        [TestCase("OrdinaryAttack")]
        [TestCase("RequiresTarget")]
        [TestCase("ExplicitOrderOnly")]
        public void AiConfigLoader_RejectsRemovedUtilityDecisionFlagField(string field)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(extraDecisionJson: $", \"{field}\": true");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain($"Unsupported decision field '{field}'"));
        }

        [TestCase("Autocast")]
        [TestCase("OrdinaryAttack")]
        [TestCase("RequiresTarget")]
        [TestCase("ExplicitOrderOnly")]
        [TestCase("KeepRunningUntilFinished")]
        public void AiConfigLoader_RejectsLegacyUtilityDecisionFlagsArray(string flag)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(extraDecisionJson: $", \"Flags\": [\"{flag}\"]");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("Unsupported decision field 'Flags'"));
        }

        [Test]
        public void AiConfigLoader_CompilesCanonicalKeepRunningUntilFinishedProperty()
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(
                extraDecisionJson: ", \"KeepRunningUntilFinished\": true");

            UtilityAiCompiledRuntime runtime = fixture.Load().UtilityRuntime;

            Assert.That(runtime.Decisions[0].KeepRunningUntilFinished, Is.True);
        }

        [TestCase("\"true\"")]
        [TestCase("1")]
        [TestCase("null")]
        public void AiConfigLoader_RejectsNonBooleanKeepRunningUntilFinished(string authoredValue)
        {
            using var fixture = AiConfigFixture.Create();
            fixture.WriteUtilityConfig(
                extraDecisionJson: $", \"KeepRunningUntilFinished\": {authoredValue}");

            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Load());

            Assert.That(ex!.Message, Does.Contain("KeepRunningUntilFinished"));
            Assert.That(ex.Message, Does.Contain("JSON boolean"));
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
                int attackAbilityId,
                int scoreGraphId)
            {
                _root = root;
                _core = core;
                _pipeline = pipeline;
                _validation = validation;
                _graphs = graphs;
                AttackAbilityId = attackAbilityId;
                ScoreGraphId = scoreGraphId;
            }

            public int AttackAbilityId { get; }

            public int ScoreGraphId { get; }

            public static AiConfigFixture Create(string? orderJson = null, string? projectionJson = null)
            {
                string root = Path.Combine(Path.GetTempPath(), "Ludots_AiConfigLoaderTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);

                string core = Path.Combine(root, "Core");
                string mod = Path.Combine(root, "ModA");
                Directory.CreateDirectory(Path.Combine(core, "Configs", "AI"));
                Directory.CreateDirectory(Path.Combine(mod, "assets", "Configs", "AI"));

                orderJson ??= "{ \"OrderTypeKey\": \"attackTarget\", \"SubmitMode\": 0, \"PlayerId\": 0 }";
                projectionJson ??= "[ { \"id\": \"R0\", \"Atom\": \"HasEnemy\", \"Op\": \"EntityIsNonNull\", \"EntityKey\": \"Attack.TargetEntity\" } ]";

                File.WriteAllText(Path.Combine(core, "Configs", "AI", "atoms.json"), "[ { \"id\": \"HasEnemy\" } ]");
                File.WriteAllText(Path.Combine(core, "Configs", "AI", "projection.json"), projectionJson);
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

                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                orderTypes.Register(new OrderTypeConfig
                {
                    Key = "attackTarget",
                    OrderTypeId = AttackOrderTypeId
                });

                AbilityIdRegistry.Clear();
                TagRegistry.Clear();
                GraphIdRegistry.Clear();
                OrderBlackboardKeyRegistry.ResetToBuiltins();
                OrderBlackboardKeyRegistry.Register("Attack.TargetEntity");
                int abilityId = AbilityIdRegistry.Register("Ability.Test.Attack");
                int defendAbilityId = AbilityIdRegistry.Register("Ability.Test.Defend");
                var abilities = new AbilityDefinitionRegistry();
                abilities.Register(abilityId, new AbilityDefinition());
                abilities.Register(defendAbilityId, new AbilityDefinition());

                var graphs = new GraphProgramRegistry();
                int graphId = GraphIdRegistry.Register("Graph.AI.Score");
                graphs.Register(graphId, Array.Empty<GraphInstruction>(), GraphKind.Score);

                return new AiConfigFixture(root, core, pipeline, new AiConfigValidationContext(orderTypes, abilities, graphs), graphs, abilityId, graphId);
            }

            public void RegisterScoreGraph(params GraphInstruction[] program)
            {
                _graphs.Clear();
                _graphs.Register(ScoreGraphId, program, GraphKind.Score);
            }

            public void WriteUtilityConfig(
                string decisionTargetFilter = "TF.Hostile",
                string considerationInput = "Input.Distance",
                string taskOrderTypeKey = "attackTarget",
                string? decisionAbilityKey = "Ability.Test.Attack",
                bool includeGraphInput = false,
                string graphKey = "Graph.AI.Score",
                string? defaultStance = null,
                string extraDecisionJson = "",
                string decisionTasksJson = "\"Task.Attack\"",
                int? decisionAbilitySlotIndex = 0)
            {
                string ai = Path.Combine(_core, "Configs", "AI");
                File.WriteAllText(Path.Combine(ai, "target_filters.json"),
                    "[ { \"id\": \"TF.Hostile\", \"MaxResults\": 32, \"Ops\": [ " +
                    "{ \"Kind\": \"SpatialRadius\", \"RadiusCm\": 900 }, " +
                    "{ \"Kind\": \"Relationship\", \"Value\": \"Hostile\" } ] } ]");

                string inputGraph = includeGraphInput
                    ? $", {{ \"id\": \"Input.Graph\", \"Kind\": \"GraphScore\", \"GraphKey\": \"{graphKey}\" }}"
                    : string.Empty;
                File.WriteAllText(Path.Combine(ai, "inputs.json"),
                    "[ { \"id\": \"Input.Distance\", \"Kind\": \"DistanceToTarget\" }" + inputGraph + " ]");
                File.WriteAllText(Path.Combine(ai, "normalizations.json"),
                    "[ { \"id\": \"Norm.Close\", \"Kind\": \"RangeInverse\", \"Min\": 0, \"Max\": 900 } ]");
                File.WriteAllText(Path.Combine(ai, "curves.json"),
                    "[ { \"id\": \"Curve.Linear\", \"Kind\": \"Linear\" } ]");
                File.WriteAllText(Path.Combine(ai, "tasks.json"),
                    $"[ {{ \"id\": \"Task.Attack\", \"Kind\": \"SubmitOrder\", \"OrderTypeKey\": \"{taskOrderTypeKey}\", \"SubmitMode\": 0, \"PlayerId\": 0 }} ]");
                string decisionAbilityJson = decisionAbilityKey == null
                    ? string.Empty
                    : $", \"AbilityKey\": \"{decisionAbilityKey}\"";
                string decisionAbilitySlotJson = decisionAbilitySlotIndex.HasValue
                    ? $", \"AbilitySlotIndex\": {decisionAbilitySlotIndex.Value}"
                    : string.Empty;
                File.WriteAllText(Path.Combine(ai, "decisions.json"),
                    "[ { \"id\": \"Decision.Attack\", " +
                    $"\"TargetFilter\": \"{decisionTargetFilter}\", " +
                    "\"Priority\": 10, \"BaseScore\": 1, \"Weight\": 1, \"DecisionRepeatDelaySteps\": 2" +
                    decisionAbilityJson + decisionAbilitySlotJson + extraDecisionJson + ", " +
                    "\"Considerations\": [ { " +
                    $"\"Input\": \"{considerationInput}\", \"Normalization\": \"Norm.Close\", \"Curve\": \"Curve.Linear\", \"Aggregate\": \"Multiply\" }} ], " +
                    $"\"Tasks\": [ {decisionTasksJson} ] }} ]");
                File.WriteAllText(Path.Combine(ai, "decision_makers.json"),
                    "[ { \"id\": \"DM.Combat\", \"SelectionMode\": \"FixedPriority\", \"Decisions\": [ \"Decision.Attack\" ] } ]");
                string defaultStanceProperty = string.IsNullOrWhiteSpace(defaultStance)
                    ? string.Empty
                    : $", \"DefaultStance\": \"{defaultStance}\"";
                WriteProfilesJson("[ { \"id\": \"Profile.Basic\", \"DecisionIntervalSteps\": 1, \"MaxCandidates\": 32, \"MaxGraphScoreInstructions\": 256, \"DecisionMakers\": [ \"DM.Combat\" ]" + defaultStanceProperty + " } ]");
                File.WriteAllText(Path.Combine(ai, "stances.json"), string.IsNullOrWhiteSpace(defaultStance)
                    ? "[]"
                    : $"[ {{ \"id\": \"{defaultStance}\", \"AutoAcquire\": true, \"Retaliate\": true }} ]");
                File.WriteAllText(Path.Combine(ai, "actuators.json"), "[]");
            }

            public void WriteProfilesJson(string json)
            {
                File.WriteAllText(Path.Combine(_core, "Configs", "AI", "profiles.json"), json);
            }

            public void WriteTasksJson(string json)
            {
                File.WriteAllText(Path.Combine(_core, "Configs", "AI", "tasks.json"), json);
            }

            public void WriteActuatorsJson(string json)
            {
                File.WriteAllText(Path.Combine(_core, "Configs", "AI", "actuators.json"), json);
            }

            public AiCompiledRuntime Load()
            {
                var atoms = new AtomRegistry(capacity: 256);
                var loader = new AiConfigLoader(_pipeline, atoms, _validation);
                return loader.LoadAndCompile(AiConfigCatalog.CreateDefault());
            }

            public AiCompiledRuntime LoadWithoutGraphRegistry()
            {
                var atoms = new AtomRegistry(capacity: 256);
                var validation = new AiConfigValidationContext(
                    _validation.OrderTypes,
                    _validation.Abilities,
                    graphs: null);
                var loader = new AiConfigLoader(_pipeline, atoms, validation);
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
