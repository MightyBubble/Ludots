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
using Ludots.Tests.Gas.Graph;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Graph
{
    /// <summary>
    /// Cast-mode, slot-ability, and ground-point ops.
    /// Symbol immediates go through the same config-key patch as SetInteractionMode.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class PrefGraphOpsTests
    {
        private const string Key = "pref.test.gesture";
        private const string Smart = "symbol.test.smart";
        private const string Aim = "symbol.test.aim";
        private const string AbilityName = "ability.cast_mode_graph.ranged";

        [SetUp]
        public void SetUp()
        {
            ConfigKeyRegistry.Clear();
        }

        [Test]
        public void IsPref_MatchAndMismatch_FollowTheAbilityPin([Values("Effect", "Query")] string kind)
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity unit = fixture.Unit(defaultMode: Smart, pinAbility: fixture.AbilityId, pinMode: Aim, withSlot: true);

            Run matched = CompileAndRun(IsPrefDoc(kind, Aim), world, fixture.Api, "check", unit, unit);
            Assert.That(matched.Bools[matched.FeaturedDst], Is.EqualTo(1), "the ability pin wins over the player default");

            Run missed = CompileAndRun(IsPrefDoc(kind, Smart), world, fixture.Api, "check", unit, unit);
            Assert.That(missed.Bools[missed.FeaturedDst], Is.EqualTo(0), "a different installed mode is false, not a throw");
        }

        [Test]
        public void IsPref_Unset_FailsNamingTheKeyAndRep()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity rep = fixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(IsPrefDoc("Effect", Smart), world, fixture.Api, "check", rep, rep));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.IsPrefUnset"));
            Assert.That(error.Message, Does.Contain(rep.ToString()));
        }

        [Test]
        public void IsPref_EmptySlot_FailsNamingActorAndSlot()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity actor = fixture.Unit(defaultMode: Smart, pinAbility: 0, pinMode: null, withSlot: false);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(IsPrefDoc("Effect", Smart), world, fixture.Api, "check", actor, actor));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.IsPrefSlotEmpty"));
            Assert.That(error.Message, Does.Contain(actor.ToString()));
            Assert.That(error.Message, Does.Contain("slot 0"));
        }

        [Test]
        public void IsPref_UnknownValue_FailsNamingTheKeyAndValue()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity unit = fixture.Unit(defaultMode: Smart, pinAbility: 0, pinMode: null, withSlot: true);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(IsPrefDoc("Effect", "symbol.test.missing"), world, fixture.Api, "check", unit, unit));
            Assert.That(error!.Message, Does.Contain("symbol.test.missing"));
        }

        [Test]
        public void SetPref_WritesThePlayerLevel([Values("Effect", "TriggerGraph")] string kind)
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity rep = fixture.Unit(defaultMode: Smart, pinAbility: 0, pinMode: null, withSlot: false);

            CompileAndRun(SetPrefDoc(kind, Aim), world, fixture.Api, "write", rep, rep);

            int keyId = fixture.Catalog.Keys.GetId(Key);
            Assert.That(world.Get<InteractionPref>(rep).ResolvePref(0, keyId), Is.EqualTo(fixture.Catalog.Values.GetId(Aim)));
        }

        [Test]
        public void SetPref_MissingPref_FailsNamingTheRep()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity rep = world.Create();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(SetPrefDoc("Effect", Smart), world, fixture.Api, "write", rep, rep));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.SetPrefRepHasNoPref"));
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

        private static GraphControlFlowDocument IsPrefDoc(string kind, string mode)
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "tests.graph.is-pref." + kind,
                Kind = kind,
                Entry = "slot",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "slot", Op = "ConstInt", IntValue = 0 },
                    new GraphControlFlowNode { Id = "who", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "check", Op = "IsPref", Key = Key, Value = mode },
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

        private static GraphControlFlowDocument SetPrefDoc(string kind, string mode)
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "tests.graph.set-pref." + kind,
                Kind = kind,
                Entry = "rep",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "rep", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "write", Op = "SetPref", Key = Key, Value = mode },
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

            foreach (string path in Directory.GetFiles(aimDir, "graph.aim.*.json"))
            {
                GraphControlFlowDocument named = LoadGraph(path, json);
                GraphIdRegistry.Register(named.Id);
                for (int i = 0; i < named.Nodes.Count; i++)
                {
                    if (string.Equals(named.Nodes[i].Op, "InvokeGraph", StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(named.Nodes[i].FunctionName))
                    {
                        GraphIdRegistry.Register(named.Nodes[i].FunctionName!);
                    }
                }
            }

            string pressPath = Path.Combine(aimDir, "graph.aim.press.json");
            string confirmPath = Path.Combine(aimDir, "graph.aim.confirm.json");
            string releasePath = Path.Combine(aimDir, "graph.aim.release_confirm.json");
            int champion = MeasureWovenPress(pressPath, hookPath: null, invokeTarget: null, json, schemas);
            int interaction = champion;
            int moba = champion;
            int aimSchemes = champion;
            int championConfirm = MeasureWovenPress(confirmPath, hookPath: null, invokeTarget: null, json, schemas);
            int interactionConfirm = championConfirm;
            int championRelease = MeasureWovenPress(releasePath, hookPath: null, invokeTarget: null, json, schemas);
            int interactionRelease = championRelease;
            int ruleNone = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.rule.none.json"), null, null, json, schemas);
            int ruleUnit = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.rule.unit_only.json"), null, null, json, schemas);
            int ruleGround = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.rule.ground_only.json"), null, null, json, schemas);
            int ruleBoth = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.rule.unit_or_ground.json"), null, null, json, schemas);
            int trackPointer = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.track_pointer.json"), "graph.aim.track_pointer.body", json, schemas);
            int trackStick = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.track_stick.json"), "graph.aim.track_stick.body", json, schemas);
            int cancel = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.cancel.json"), null, json, schemas);
            int castMode = MeasureWovenPress(Path.Combine(aimDir, "graph.aim.set_cast_mode.json"), null, json, schemas);

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
                $"rule none={ruleNone} unit={ruleUnit} ground={ruleGround} both={ruleBoth}; " +
                $"track pointer={trackPointer} stick={trackStick}; " +
                $"cancel={cancel} castMode={castMode}");
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
                Assert.That(trackPointer, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "track pointer");
                Assert.That(trackStick, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "track stick");
                Assert.That(cancel, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "cancel");
                Assert.That(castMode, Is.LessThanOrEqualTo(TriggerGraphLimits.SliceBudgetSteps), "cast mode");
            });
        }

        private static int MeasureWovenPress(
            string pressPath,
            string? invokeTarget,
            JsonSerializerOptions json,
            EventSchemaRegistry schemas)
            => MeasureWovenPress(pressPath, hookPath: null, invokeTarget, json, schemas);

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
                int longest = TriggerGraphSliceBudget.CountLongest(woven.Program, entries[i].StartPc);
                if (!string.IsNullOrEmpty(entries[i].Budget))
                {
                    int runtime = SliceStepProbe.Measure(woven.Program, entries[i].StartPc);
                    Assert.That(runtime, Is.EqualTo(longest), $"{graphId} {entries[i].Label}");
                }

                steps = Math.Max(steps, longest);
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
            public InteractionPrefCatalog Catalog = null!;
            public GasGraphRuntimeApi Api = null!;
            public int AbilityId;
            private World _world = null!;

            public static Fixture Create(
                World world,
                bool withTargeting = true)
            {
                var catalog = new InteractionPrefCatalog();
                catalog.DefineKey(Key, contextValued: false, new[] { Smart, Aim }, contextProfiles: null, source: "test");
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
                api.BindInteractionPrefCatalog(catalog);
                api.BindAbilityDefinitions(definitions);
                return new Fixture
                {
                    Catalog = catalog,
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
                    pref.SetPlayerPref(Catalog.Keys.GetId(Key), Catalog.Values.GetId(defaultMode));
                }

                if (pinAbility > 0 && pinMode != null)
                {
                    pref.SetAbilityPref(pinAbility, Catalog.Keys.GetId(Key), Catalog.Values.GetId(pinMode));
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
