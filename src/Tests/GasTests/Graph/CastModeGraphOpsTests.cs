using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Cast-mode and ground-point ops: compile on the kinds Slice E will author, then execute.
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

        [Test]
        public void AbilityAcceptsTargetKind_FollowsTheAuthoredSet([Values("Effect", "Query")] string kind)
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world, targetKinds: AbilityTargetKinds.UnitBit);
            Entity actor = fixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);

            Run unit = CompileAndRun(AcceptsKindDoc(kind, AbilityTargetKinds.Unit), world, fixture.Api, "check", actor, actor);
            Assert.That(unit.Bools[unit.FeaturedDst], Is.EqualTo(1));

            Run ground = CompileAndRun(AcceptsKindDoc(kind, AbilityTargetKinds.Ground), world, fixture.Api, "check", actor, actor);
            Assert.That(ground.Bools[ground.FeaturedDst], Is.EqualTo(0));

            Run none = CompileAndRun(AcceptsKindDoc(kind, AbilityTargetKinds.None), world, fixture.Api, "check", actor, actor);
            Assert.That(none.Bools[none.FeaturedDst], Is.EqualTo(0));
        }

        [Test]
        public void AbilityAcceptsTargetKind_NoneFollowsItsOwnBit([Values("Effect", "Query")] string kind)
        {
            using var noneWorld = World.Create();
            Fixture noneFixture = Fixture.Create(noneWorld, targetKinds: AbilityTargetKinds.NoneBit);
            Entity noneActor = noneFixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);

            Run none = CompileAndRun(AcceptsKindDoc(kind, AbilityTargetKinds.None), noneWorld, noneFixture.Api, "check", noneActor, noneActor);
            Assert.That(none.Bools[none.FeaturedDst], Is.EqualTo(1));
            Run unit = CompileAndRun(AcceptsKindDoc(kind, AbilityTargetKinds.Unit), noneWorld, noneFixture.Api, "check", noneActor, noneActor);
            Assert.That(unit.Bools[unit.FeaturedDst], Is.EqualTo(0));
            Run ground = CompileAndRun(AcceptsKindDoc(kind, AbilityTargetKinds.Ground), noneWorld, noneFixture.Api, "check", noneActor, noneActor);
            Assert.That(ground.Bools[ground.FeaturedDst], Is.EqualTo(0));

            using var bothWorld = World.Create();
            Fixture bothFixture = Fixture.Create(bothWorld);
            Entity bothActor = bothFixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);
            Run noneOnBoth = CompileAndRun(AcceptsKindDoc(kind, AbilityTargetKinds.None), bothWorld, bothFixture.Api, "check", bothActor, bothActor);
            Assert.That(noneOnBoth.Bools[noneOnBoth.FeaturedDst], Is.EqualTo(0));
        }

        [Test]
        public void AbilityAcceptsTargetKind_UnknownKind_FailsNamingTheSymbol()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world, targetKinds: AbilityTargetKinds.GroundBit);
            Entity actor = fixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(AcceptsKindDoc("Effect", "self"), world, fixture.Api, "check", actor, actor));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.AbilityAcceptsTargetKindUnknown"));
            Assert.That(error.Message, Does.Contain("self"));
            Assert.That(error.Message, Does.Contain(AbilityTargetKinds.None));
        }

        [Test]
        public void AbilityAcceptsTargetKind_MissingTargeting_FailsNamingTheAbility()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world, withTargeting: false);
            Entity actor = fixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: true);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(AcceptsKindDoc("Effect", AbilityTargetKinds.Unit), world, fixture.Api, "check", actor, actor));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.AbilityAcceptsTargetKindNoTargeting"));
            Assert.That(error.Message, Does.Contain(AbilityName));
        }

        [Test]
        public void AbilityAcceptsTargetKind_EmptySlot_FailsNamingActorAndSlot()
        {
            using var world = World.Create();
            Fixture fixture = Fixture.Create(world);
            Entity actor = fixture.Unit(defaultMode: null, pinAbility: 0, pinMode: null, withSlot: false);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                CompileAndRun(AcceptsKindDoc("Effect", AbilityTargetKinds.Unit), world, fixture.Api, "check", actor, actor));
            Assert.That(error!.Message, Does.Contain("GAS.GRAPH.ERR.AbilityAcceptsTargetKindSlotEmpty"));
            Assert.That(error.Message, Does.Contain(actor.ToString()));
            Assert.That(error.Message, Does.Contain("slot 0"));
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

        private static GraphControlFlowDocument AcceptsKindDoc(string kind, string symbol)
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "tests.graph.accepts-kind." + kind,
                Kind = kind,
                Entry = "slot",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "slot", Op = "ConstInt", IntValue = 0 },
                    new GraphControlFlowNode { Id = "actor", Op = "LoadCaster" },
                    new GraphControlFlowNode { Id = "check", Op = "AbilityAcceptsTargetKind", Symbol = symbol },
                },
                ControlEdges =
                {
                    new GraphControlFlowEdge("slot", "next", "actor"),
                    new GraphControlFlowEdge("actor", "next", "check"),
                },
                ValueEdges =
                {
                    new GraphControlFlowValueEdge("actor", "value", "check", "source"),
                    new GraphControlFlowValueEdge("slot", "value", "check", "value"),
                },
            };
            if (kind == "TriggerGraph")
            {
                doc.Entries.Add(new TriggerGraphEntryConfig { Label = "probe", Event = "EntityAliveCountChanged", Start = "slot" });
            }

            return doc;
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
            return new Run(featuredDst, frame.B.ToArray(), frame.F.ToArray(), frame.TargetPosCm);
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

        private readonly record struct Run(byte FeaturedDst, byte[] Bools, float[] Floats, IntVector2 TargetPosCm);

        private sealed class Fixture
        {
            public StringIntRegistry Modes = null!;
            public GasGraphRuntimeApi Api = null!;
            public int AbilityId;
            private World _world = null!;

            public static Fixture Create(
                World world,
                bool withTargeting = true,
                byte targetKinds = AbilityTargetKinds.UnitBit | AbilityTargetKinds.GroundBit)
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
                        TargetKinds = targetKinds,
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
