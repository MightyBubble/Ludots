using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Scripting;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Graph
{
    /// <summary>
    /// Cast-mode, slot-ability, and ground-point ops.
    /// Symbol immediates go through the same config-key patch as SetInteractionMode.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CastModeGraphOpsTests
    {
        private const string Smart = "castMode.test.smart";
        private const string Aim = "castMode.test.aim";
        private const string AbilityName = "ability.cast_mode_graph.ranged";

        [SetUp]
        public void SetUp()
        {
            ConfigKeyRegistry.Clear();
        }

        [Test]
        public void IsCastMode_MatchAndMismatch_FollowTheAbilityPin([Values("Effect", "Query")] string kind)
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity unit = fixture.Unit(defaultMode: Smart, pinAbility: fixture.AbilityId, pinMode: Aim, withSlot: true);

            Run matched = CompileAndRun(IsCastModeDoc(kind, Aim), world, fixture.Api, "check", unit, unit);
            Assert.That(matched.Bools[matched.FeaturedDst], Is.EqualTo(1), "the ability pin wins over the player default");

            Run missed = CompileAndRun(IsCastModeDoc(kind, Smart), world, fixture.Api, "check", unit, unit);
            Assert.That(missed.Bools[missed.FeaturedDst], Is.EqualTo(0), "a different installed mode is false, not a throw");
        }

        [Test]
        public void IsCastMode_UnseededResolvedMode_FailsNamingTheRep()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity rep = fixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(IsCastModeDoc("Effect", Smart), world, fixture.Api, "check", rep, rep));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.IsCastModeUnseeded"));
            Assert.That(error.Message, Does.Contain(rep.ToString()));
        }

        [Test]
        public void IsCastMode_EmptySlot_FailsNamingActorAndSlot()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity actor = fixture.Unit(defaultMode: Smart, pinAbility: 0, pinMode: null, withSlot: false);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(IsCastModeDoc("Effect", Smart), world, fixture.Api, "check", actor, actor));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.IsCastModeSlotEmpty"));
            Assert.That(error.Message, Does.Contain(actor.ToString()));
            Assert.That(error.Message, Does.Contain("slot 0"));
        }

        [Test]
        public void IsCastMode_UnknownMode_FailsNamingTheMode()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity unit = fixture.Unit(defaultMode: Smart, pinAbility: 0, pinMode: null, withSlot: true);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(IsCastModeDoc("Effect", "castMode.test.missing"), world, fixture.Api, "check", unit, unit));
            Assert.That(error!.Message, Does.Contain("castMode.test.missing"));
        }

        [Test]
        public void SetCastMode_WritesThePlayerDefault([Values("Effect", "TriggerGraph")] string kind)
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity rep = fixture.Unit(defaultMode: Smart, pinAbility: 0, pinMode: null, withSlot: false);

            CompileAndRun(SetCastModeDoc(kind, Aim), world, fixture.Api, "write", rep, rep);

            Assert.That(world.Get<InteractionPref>(rep).DefaultCastModeId, Is.EqualTo(fixture.Modes.GetId(Aim)));
        }

        [Test]
        public void SetCastMode_MissingPref_FailsNamingTheRep()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity rep = world.Create();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(SetCastModeDoc("Effect", Smart), world, fixture.Api, "write", rep, rep));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.SetCastModeRepHasNoPref"));
            Assert.That(error.Message, Does.Contain(rep.ToString()));
        }

        [Test]
        public void SetTargetPos_RoundsLikeClampTargetToRange([Values("Effect", "Query")] string kind)
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            (int x, int y) expected = Fix64Vec2.FromFloat(1.6f, -2.4f).RoundToInt();

            Run run = CompileAndRun(SetTargetPosDoc(kind, 1.6f, -2.4f), world, fixture.Api, "point", default, default);

            Assert.That(run.TargetPosCm.X, Is.EqualTo(expected.x));
            Assert.That(run.TargetPosCm.Y, Is.EqualTo(expected.y));
        }

        [Test]
        public void LoadAbilityCastRange_ReadsTargetingAndRejectsAnEmptySlot([Values("Effect", "Query")] string kind)
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity actor = fixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);

            Run run = CompileAndRun(LoadRangeDoc(kind), world, fixture.Api, "range", actor, actor);
            Assert.That(run.Floats[run.FeaturedDst], Is.EqualTo(640f));

            Entity empty = world.Create();
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(LoadRangeDoc(kind), world, fixture.Api, "range", empty, empty));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.LoadAbilityCastRangeSlotEmpty"));
            Assert.That(error.Message, Does.Contain("slot 0"));
        }

        [Test]
        public void LoadAbilityCastRange_MissingTargeting_FailsNamingTheField()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world, withTargeting: false);
            Entity actor = fixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(LoadRangeDoc("Effect"), world, fixture.Api, "range", actor, actor));
            Assert.That(error!.Message, Does.Contain("targeting.castRangeCm"));
            Assert.That(error.Message, Does.Contain(AbilityName));
        }

        private static GraphControlFlowDocument IsCastModeDoc(string kind, string mode)
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "tests.graph.is-cast-mode." + kind,
                Kind = kind,
                Entry = "slot",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "slot", Op = "ConstInt", IntValue = 0 },
                    new GraphControlFlowNode { Id = "who", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "check", Op = "IsCastMode", Mode = mode },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("slot", "next", "who"),
                    new GraphControlFlowEdge("who", "next", "check"),
                },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("who", "value", "check", "source"),
                    new GraphControlFlowValueEdge("who", "value", "check", "target"),
                    new GraphControlFlowValueEdge("slot", "value", "check", "value"),
                },
            };
            if (kind == "TriggerGraph")
            {
                doc.Entries.Add(new TriggerGraphEntryConfig { Label = "probe", Event = "EntityAliveCountChanged", Start = "slot" });
            }

            return doc;
        }

        private static GraphControlFlowDocument SetCastModeDoc(string kind, string mode)
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "tests.graph.set-cast-mode." + kind,
                Kind = kind,
                Entry = "rep",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "rep", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "write", Op = "SetCastMode", Mode = mode },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("rep", "next", "write"),
                },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("rep", "value", "write", "source"),
                },
            };
            if (kind == "TriggerGraph")
            {
                doc.Entry = string.Empty;
                doc.Entries.Add(new TriggerGraphEntryConfig { Label = "probe", Event = "EntityAliveCountChanged", Start = "rep" });
            }

            return doc;
        }

        private static GraphControlFlowDocument SetTargetPosDoc(string kind, float x, float y)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.graph.set-target-pos." + kind,
                Kind = kind,
                Entry = "x",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "x", Op = "ConstFloat", FloatValue = x },
                    new GraphControlFlowNode { Id = "y", Op = "ConstFloat", FloatValue = y },
                    new GraphControlFlowNode { Id = "point", Op = "SetTargetPos" },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("x", "next", "y"),
                    new GraphControlFlowEdge("y", "next", "point"),
                },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("x", "value", "point", "a"),
                    new GraphControlFlowValueEdge("y", "value", "point", "b"),
                },
            };
        }

        private static GraphControlFlowDocument LoadSlotAbilityDoc(string kind)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.graph.load-slot-ability." + kind,
                Kind = kind,
                Entry = "slot",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "slot", Op = "ConstInt", IntValue = 0 },
                    new GraphControlFlowNode { Id = "actor", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "ability", Op = "LoadSlotAbilityId" },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("slot", "next", "actor"),
                    new GraphControlFlowEdge("actor", "next", "ability"),
                },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("actor", "value", "ability", "source"),
                    new GraphControlFlowValueEdge("slot", "value", "ability", "value"),
                },
            };
        }

        private static GraphControlFlowDocument LoadRangeDoc(string kind)
        {
            return new GraphControlFlowDocument
            {
                Id = "tests.graph.load-cast-range." + kind,
                Kind = kind,
                Entry = "slot",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "slot", Op = "ConstInt", IntValue = 0 },
                    new GraphControlFlowNode { Id = "actor", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "range", Op = "LoadAbilityCastRange" },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("slot", "next", "actor"),
                    new GraphControlFlowEdge("actor", "next", "range"),
                },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("actor", "value", "range", "source"),
                    new GraphControlFlowValueEdge("slot", "value", "range", "value"),
                },
            };
        }

        private static Run CompileAndRun(
            GraphControlFlowDocument doc,
            World world,
            GasGraphRuntimeApi api,
            string featuredNodeId,
            Entity caster,
            Entity target)
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);
            Assert.That(result.Diagnostics, Is.Empty, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
            GraphProgramPackage package = result.Package!.Value;
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, SymbolResolver.Instance);
            byte featuredDst = RequireFeaturedDst(result, featuredNodeId);
            GraphKind kind = doc.Kind switch
            {
                "Query" => GraphKind.Query,
                "TriggerGraph" => GraphKind.TriggerGraph,
                _ => GraphKind.Effect,
            };
            var frame = GraphFrame.Bind(
                kind,
                GraphEntityPreset.None,
                world,
                caster,
                target,
                default,
                api,
                programs: null,
                new float[GraphVmLimits.MaxFloatRegisters],
                new int[GraphVmLimits.MaxIntRegisters],
                new byte[GraphVmLimits.MaxBoolRegisters],
                new Entity[GraphVmLimits.MaxEntityRegisters],
                new Entity[GraphVmLimits.MaxTargets],
                new int[GraphVmLimits.MaxIntIds],
                new int[GraphVmLimits.MaxCallStackDepth]);
            GraphExecutor.Execute(ref frame, package.Program, programAlreadyValidated: false);
            return new Run(featuredDst, frame.B.ToArray(), frame.F.ToArray(), frame.I.ToArray(), frame.TargetPosCm);
        }

        private static byte RequireFeaturedDst(GraphControlFlowCompileResult compiled, string featuredNodeId)
        {
            GraphProgramPackage package = compiled.Package!.Value;
            for (int i = 0; i < package.Program.Length; i++)
            {
                if (compiled.SourceMap.TryGetSource(i, out GraphInstructionSource source) &&
                    string.Equals(source.NodeId, featuredNodeId, StringComparison.Ordinal))
                {
                    return package.Program[i].Dst;
                }
            }

            Assert.Fail($"Featured node '{featuredNodeId}' was not emitted.");
            return 0;
        }

        private readonly record struct Run(byte FeaturedDst, byte[] Bools, float[] Floats, int[] Ints, IntVector2 TargetPosCm);

        [Test]
        public void LoadSlotAbilityId_ReadsTheResolvedIdAndRejectsAnEmptySlot([Values("Effect", "Query")] string kind)
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world, withTargeting: false);
            Entity actor = fixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);

            Run run = CompileAndRun(LoadSlotAbilityDoc(kind), world, fixture.Api, "ability", actor, actor);
            Assert.That(run.Ints[run.FeaturedDst], Is.EqualTo(fixture.AbilityId));

            Entity empty = world.Create();
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(LoadSlotAbilityDoc(kind), world, fixture.Api, "ability", empty, empty));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.LoadSlotAbilityIdSlotEmpty"));
            Assert.That(error.Message, Does.Contain("slot 0"));
        }

        [Test]
        public void AimPress_RuleHandoff_StaysInsideTheSlice()
        {
            string repo = FindRepoRoot();
            JsonSerializerOptions json = StrictJsonOptions.CreateCamelCase(includeFields: true);
            string aimDir = Path.Combine(repo, "mods/capabilities/input/AimInteractionMod/assets/GAS/graphs");
            var schemas = new EventSchemaRegistry();
            var modifiers = new EventParamSchema("modifiers", EventParamType.Int, "Aim.Modifiers");
            schemas.RegisterCustom(new EventSchema("Aim.Rule.Press", EventScope.Map, new[] { modifiers }));
            schemas.RegisterCustom(new EventSchema("Aim.Rule.Resolve", EventScope.Map, new[] { modifiers }));

            string pressPath = Path.Combine(aimDir, "graph.aim.press.json");
            string confirmPath = Path.Combine(aimDir, "graph.aim.confirm.json");
            string releasePath = Path.Combine(aimDir, "graph.aim.release_confirm.json");
            string championHook = Path.Combine(
                repo,
                "mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/graphs/graph.champion.bind_aim_actor.json");
            string interactionHook = Path.Combine(
                repo,
                "mods/showcases/interaction/InteractionShowcaseMod/assets/GAS/graphs/graph.interaction.bind_aim_actor.json");
            int champion = MeasureWovenPress(pressPath, championHook, "graph.champion.rebind_aim_actor", json, schemas);
            int interaction = MeasureWovenPress(pressPath, interactionHook, "graph.interaction.rebind_aim_actor", json, schemas);
            int moba = MeasureWovenPress(pressPath, hookPath: null, invokeTarget: null, json, schemas);
            int aimSchemes = MeasureWovenPress(pressPath, hookPath: null, invokeTarget: null, json, schemas);
            int championConfirm = MeasureWovenPress(confirmPath, championHook, "graph.champion.rebind_aim_actor", json, schemas);
            int interactionConfirm = MeasureWovenPress(confirmPath, interactionHook, "graph.interaction.rebind_aim_actor", json, schemas);
            int championRelease = MeasureWovenPress(releasePath, championHook, "graph.champion.rebind_aim_actor", json, schemas);
            int interactionRelease = MeasureWovenPress(releasePath, interactionHook, "graph.interaction.rebind_aim_actor", json, schemas);
            int ruleNone = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.rule.none.json"), null, null, json, schemas);
            int ruleUnit = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.rule.unit_only.json"), null, null, json, schemas);
            int ruleGround = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.rule.ground_only.json"), null, null, json, schemas);
            int ruleBoth = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.rule.unit_or_ground.json"), null, null, json, schemas);

            foreach (string path in Directory.GetFiles(aimDir, "graph.aim.*.json"))
            {
                GraphControlFlowDocument doc = LoadGraph(path, json);
                for (int i = 0; i < doc.Nodes.Count; i++)
                {
                    if (string.Equals(doc.Nodes[i].Op, "InvokeGraph", StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(doc.Nodes[i].FunctionName))
                    {
                        GraphIdRegistry.Register(doc.Nodes[i].FunctionName!);
                    }
                }

                RegisterGraph(new GraphProgramRegistry(), doc, schemas);
            }

            TestContext.Out.WriteLine(
                "woven aim host paths: " +
                $"press champion={champion} interaction={interaction} moba={moba} aim_schemes={aimSchemes}; " +
                $"confirm champion={championConfirm} interaction={interactionConfirm}; " +
                $"release champion={championRelease} interaction={interactionRelease}; " +
                $"rule none={ruleNone} unit={ruleUnit} ground={ruleGround} both={ruleBoth}");
            Assert.Multiple(() =>
            {
                Assert.That(champion, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "press champion");
                Assert.That(interaction, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "press interaction");
                Assert.That(moba, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "press moba");
                Assert.That(aimSchemes, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "press aim_schemes");
                Assert.That(championConfirm, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "confirm champion");
                Assert.That(interactionConfirm, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "confirm interaction");
                Assert.That(championRelease, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "release champion");
                Assert.That(interactionRelease, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "release interaction");
                Assert.That(ruleNone, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "rule none");
                Assert.That(ruleUnit, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "rule unit");
                Assert.That(ruleGround, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "rule ground");
                Assert.That(ruleBoth, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "rule both");
            });
        }

        private static int MeasureWovenPress(
            string pressPath,
            string? hookPath,
            string? invokeTarget,
            JsonSerializerOptions json,
            EventSchemaRegistry schemas)
        {
            GraphControlFlowDocument press = LoadGraph(pressPath, json);
            if (invokeTarget != null)
            {
                GraphIdRegistry.Register(invokeTarget);
            }

            var programs = new GraphProgramRegistry();
            RegisterGraph(programs, press, schemas);
            var docs = new List<KeyValuePair<string, GraphControlFlowDocument>>
            {
                new(press.Id, press),
            };
            if (hookPath != null)
            {
                GraphControlFlowDocument hook = LoadGraph(hookPath, json);
                hook.Entries = hook.Entries
                    .Where(entry => string.Equals(entry.HookNodeBefore?.GraphId, press.Id, StringComparison.Ordinal))
                    .ToList();
                RegisterGraph(programs, hook, schemas);
                docs.Add(new(hook.Id, hook));
            }

            var collections = new EntityCollectionStore(new StringIntRegistry(
                capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            TriggerGraphHookWeaver.Weave(programs, docs, SymbolResolver.Instance, schemas, collections);
            return LongestWovenPress(programs, press.Id);
        }

        private static int LongestWovenPress(GraphProgramRegistry programs, string graphId)
        {
            Assert.That(programs.TryGetRegistration(GraphIdRegistry.GetId(graphId), out GraphProgramRegistration woven), Is.True, graphId);
            int steps = 0;
            IReadOnlyList<TriggerGraphEntry> entries = woven.TriggerGraphEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                steps = Math.Max(steps, LongestHostPath(woven.Program, entries[i].StartPc));
            }

            return steps;
        }

        private static GraphControlFlowDocument LoadGraph(string path, JsonSerializerOptions json)
        {
            List<GraphControlFlowDocument> docs = JsonSerializer.Deserialize<List<GraphControlFlowDocument>>(File.ReadAllText(path), json)
                ?? throw new InvalidOperationException($"Failed to read {path}.");
            return docs[0];
        }

        private static void RegisterGraph(GraphProgramRegistry programs, GraphControlFlowDocument doc, EventSchemaRegistry schemas)
        {
            GraphControlFlowCompileResult compiled = GraphControlFlowCompiler.Compile(doc, schemas);
            List<GraphDiagnostic> errors = compiled.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error).ToList();
            Assert.That(errors, Is.Empty, () => string.Join("\n", errors.Select(d => d.Message)));
            GraphProgramPackage package = compiled.Package!.Value;
            int id = GraphIdRegistry.Register(doc.Id);
            programs.Register(id, package.Program, GraphKind.TriggerGraph, compiled.SourceMap, package.Symbols, package.TriggerGraphEntries);
        }

        private static int LongestHostPath(GraphInstruction[] program, int startPc)
        {
            var memo = new int[program.Length];
            Array.Fill(memo, -1);
            var stack = new HashSet<int>();
            return Walk(startPc);

            int Walk(int pc)
            {
                if ((uint)pc >= (uint)program.Length)
                {
                    return 0;
                }

                if (memo[pc] >= 0)
                {
                    return memo[pc];
                }

                if (!stack.Add(pc))
                {
                    throw new InvalidOperationException($"graph.aim.press cycles at pc {pc}.");
                }

                GraphInstruction ins = program[pc];
                var op = (GraphNodeOp)ins.Op;
                int rest = op switch
                {
                    GraphNodeOp.HaltReturnInt or GraphNodeOp.Fail => 0,
                    GraphNodeOp.Jump => Walk(pc + 1 + ins.Imm),
                    GraphNodeOp.JumpIfFalse => Math.Max(Walk(pc + 1), Walk(pc + 1 + ins.Imm)),
                    _ => Walk(pc + 1),
                };
                stack.Remove(pc);
                memo[pc] = 1 + rest;
                return memo[pc];
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                    File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root.");
        }

        private sealed class Fixture
        {
            public StringIntRegistry Modes = null!;
            public GasGraphRuntimeApi Api = null!;
            public int AbilityId;
            private World _world = null!;

            public static Fixture Create(
                World world,
                bool withTargeting = true)
            {
                var modes = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                modes.Register(Smart);
                modes.Register(Aim);
                int abilityId = AbilityIdRegistry.Register(AbilityName);
                var definitions = new AbilityDefinitionRegistry();
                var definition = new AbilityDefinition();
                if (withTargeting)
                {
                    definition.HasTargeting = true;
                    definition.Targeting = new AbilityTargetingConfig
                    {
                        CastRangeCm = 640f,
                        ImpactEffectTemplateId = 1,
                    };
                }

                definitions.Register(abilityId, definition);
                var api = new GasGraphRuntimeApi(world);
                api.BindCastModeRegistry(modes);
                api.BindAbilityDefinitions(definitions);
                return new Fixture
                {
                    Modes = modes,
                    Api = api,
                    AbilityId = abilityId,
                    _world = world,
                };
            }

            public Entity Unit(string? defaultMode, int pinAbility, string? pinMode, bool withSlot)
            {
                InteractionPref pref = default;
                pref.SetPlayerDefault(1, 1);
                if (defaultMode != null)
                {
                    pref.SetDefaultCastMode(Modes.GetId(defaultMode));
                }

                if (pinAbility > 0 && pinMode != null)
                {
                    pref.SetAbilityOverride(pinAbility, 0, 0, Modes.GetId(pinMode));
                }

                if (!withSlot)
                {
                    return _world.Create(pref);
                }

                var slots = default(AbilityStateBuffer);
                slots.AddAbility(AbilityId);
                return _world.Create(pref, slots);
            }
        }

        private sealed class SymbolResolver : IGraphSymbolResolver
        {
            public static readonly SymbolResolver Instance = new();

            public int ResolveTag(string name) => TagRegistry.Register(name);
            public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
            public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
            public int ResolveRelationshipType(string name) => 1;
            public int ResolveRelationshipMetric(string name) => 1;
            public int ResolveRelationshipFlag(string name) => 1;
            public int ResolveTargetDispatchPreset(string name) => 1;
            public int ResolveEntityTemplate(string name) => 1;
        }
    }
}
