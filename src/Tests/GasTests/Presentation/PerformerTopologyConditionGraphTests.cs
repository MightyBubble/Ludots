using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// RFC-0065 PROV-4b / PROV-2b (performer side): viewer-relative marker conditions are
    /// evaluated as graph topology predicates at event time — no viewerRole enum, no
    /// write-time snapshot. Covers the three §5.9 condition branches (viewer is row domain /
    /// viewer controls row domain / viewer has knowledge grant only), the E[2]=Viewer and
    /// event payload register injection, and the topology-revocation condition flip.
    /// </summary>
    [TestFixture]
    public sealed class PerformerTopologyConditionGraphTests
    {
        private const int ViewerIsRowDomainProgramId = 1;
        private const int ViewerControlsRowDomainProgramId = 2;
        private const int ViewerKnowledgeGrantOnlyProgramId = 3;

        private const int DeepGreenMarkerDefId = 101;
        private const int LightGreenMarkerDefId = 102;
        private const int RefereeMarkerDefId = 103;

        private World _world = null!;
        private PresentationEventStream _events = null!;
        private PerformerCommandBuffer _commands = null!;
        private PerformerDefinitionRegistry _defs = null!;
        private GraphProgramRegistry _programs = null!;
        private PerformerRuleSystem _system = null!;

        private RelationshipRuntime _relationships = null!;
        private RelationshipTypeRegistry _relationshipTypes = null!;
        private OwnershipResolver _ownership = null!;
        private ControlDomainQuery _controlDomains = null!;
        private KnowledgeProjectionStore _knowledgeStore = null!;
        private int _ownsTypeId;
        private int _controlsTypeId;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _events = new PresentationEventStream(64);
            _commands = new PerformerCommandBuffer(64);
            _defs = new PerformerDefinitionRegistry();
            _programs = new GraphProgramRegistry();

            _relationshipTypes = new RelationshipTypeRegistry();
            _relationships = new RelationshipRuntime(
                _world,
                _relationshipTypes,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 16),
                new RelationshipReverseIndex(_world));
            _ownsTypeId = _relationshipTypes.Register("Owns");
            _controlsTypeId = _relationshipTypes.Register("Controls");
            _ownership = new OwnershipResolver(_relationships, _ownsTypeId);
            _controlDomains = new ControlDomainQuery(_world, _relationships, _ownership, _ownsTypeId, _controlsTypeId);
            _knowledgeStore = new KnowledgeProjectionStore();

            var api = new GasGraphRuntimeApi(
                _world,
                spatialQueries: null,
                coords: null,
                eventBus: null,
                effectRequests: null,
                tagOps: null,
                relationshipRuntime: _relationships);
            api.BindTopologyServices(_controlDomains, new KnowledgeProjectionResolver(_knowledgeStore), new DiscreteClock());

            _system = new PerformerRuleSystem(
                _world,
                _events,
                _commands,
                _defs,
                runtime: null,
                _programs,
                api,
                new Dictionary<string, object>());
        }

        [TearDown]
        public void TearDown()
        {
            _system?.Dispose();
            _world?.Dispose();
        }

        // ── §5.9 scenario: P1Rep owns a unit, P2Rep holds a Controls grant on P1Rep,
        //    referee holds only a knowledge grant on the unit ──

        private (Entity P1Rep, Entity P2Rep, Entity Referee, Entity Unit) BuildTopology()
        {
            Entity p1Rep = _world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity p2Rep = _world.Create(new PlayerIdentity { PlayerId = 2 });
            Entity referee = _world.Create();
            Entity unit = _world.Create();
            _ownership.EnsureOwnership(p1Rep, unit);
            _relationships.EnsureLink(p2Rep, p1Rep, _controlsTypeId);
            _knowledgeStore.Upsert(referee, unit, CreateDisclosure(referee));
            return (p1Rep, p2Rep, referee, unit);
        }

        private static KnowledgeDisclosureRecord CreateDisclosure(Entity source)
        {
            return new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                source,
                observedTick: 0,
                expiryTick: 0,
                confidencePermille: 1000,
                revision: 0);
        }

        // Register the three §5.9 condition graphs. Context registers seeded by
        // PerformerRuleSystem: E[0]=Source (member), E[1]=Target (owner), E[2]=Viewer.
        private void RegisterSelectionMarkerRules()
        {
            // graph.cond.viewer_is_row_domain: B[0] = (ControlDomainResolve(source) == viewer)
            _programs.Register(ViewerIsRowDomainProgramId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ControlDomainResolve, A = 0, Dst = 3 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqEntity, A = 3, B = 2, Dst = 0 },
            }, GraphKind.Validation);

            // graph.cond.viewer_controls_row_domain: controls-reachable but not the row domain itself
            _programs.Register(ViewerControlsRowDomainProgramId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ControlDomainControls, A = 2, B = 0, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ControlDomainResolve, A = 0, Dst = 3 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqEntity, A = 3, B = 2, Dst = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 1, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 0 },
            }, GraphKind.Validation);

            // graph.cond.viewer_has_knowledge_grant: knowledge projection without controls reachability
            _programs.Register(ViewerKnowledgeGrantOnlyProgramId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.KnowledgeHasProjection, A = 2, B = 0, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ControlDomainControls, A = 2, B = 0, Dst = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 1, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstBool, Dst = 0, Imm = 0 },
            }, GraphKind.Validation);

            _defs.Register("test.selection_marker.rules", new PerformerDefinition
            {
                Rules = new[]
                {
                    SelectionMarkerRule(ViewerIsRowDomainProgramId, DeepGreenMarkerDefId),
                    SelectionMarkerRule(ViewerControlsRowDomainProgramId, LightGreenMarkerDefId),
                    SelectionMarkerRule(ViewerKnowledgeGrantOnlyProgramId, RefereeMarkerDefId),
                },
            });
        }

        private static PerformerRule SelectionMarkerRule(int graphProgramId, int markerDefId)
        {
            return new PerformerRule
            {
                Event = new EventFilter { Kind = PresentationEventKind.EntityCollectionMemberAdded, KeyId = -1 },
                Condition = new ConditionRef { GraphProgramId = graphProgramId },
                Command = new PerformerCommand
                {
                    CommandKind = PerformerCommandKind.CreatePerformer,
                    PerformerDefinitionId = markerDefId,
                    ScopeTag = 1,
                },
            };
        }

        private void SendMemberAdded(Entity unit, Entity owner, Entity viewer)
        {
            Assert.That(_events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EntityCollectionMemberAdded,
                KeyId = 7,
                Source = unit,
                Target = owner,
                Viewer = viewer,
                PayloadA = 99,
            }), Is.True);
            _system.Update(0.016f);
        }

        private int[] EmittedMarkerDefIds()
        {
            var span = _commands.GetSpan();
            var ids = new int[span.Length];
            for (int i = 0; i < span.Length; i++)
            {
                ids[i] = span[i].PerformerDefinitionId;
            }

            return ids;
        }

        [Test]
        public void ViewerIsRowDomain_OnlyDeepGreenRuleFires()
        {
            RegisterSelectionMarkerRules();
            var (p1Rep, _, _, unit) = BuildTopology();

            SendMemberAdded(unit, p1Rep, viewer: p1Rep);

            Assert.That(EmittedMarkerDefIds(), Is.EqualTo(new[] { DeepGreenMarkerDefId }));
        }

        [Test]
        public void ViewerControlsRowDomain_OnlyLightGreenRuleFires()
        {
            RegisterSelectionMarkerRules();
            var (p1Rep, p2Rep, _, unit) = BuildTopology();

            SendMemberAdded(unit, p1Rep, viewer: p2Rep);

            Assert.That(EmittedMarkerDefIds(), Is.EqualTo(new[] { LightGreenMarkerDefId }));
        }

        [Test]
        public void ViewerWithKnowledgeGrantOnly_OnlyRefereeRuleFires()
        {
            RegisterSelectionMarkerRules();
            var (p1Rep, _, referee, unit) = BuildTopology();

            SendMemberAdded(unit, p1Rep, viewer: referee);

            Assert.That(EmittedMarkerDefIds(), Is.EqualTo(new[] { RefereeMarkerDefId }));
        }

        [Test]
        public void ControlsRevoked_SameEventNoLongerMatchesLightGreenRule()
        {
            RegisterSelectionMarkerRules();
            var (p1Rep, p2Rep, _, unit) = BuildTopology();

            SendMemberAdded(unit, p1Rep, viewer: p2Rep);
            Assert.That(EmittedMarkerDefIds(), Is.EqualTo(new[] { LightGreenMarkerDefId }));

            // PROV-2b: relationship revocation flips the condition on re-evaluation — no
            // write-time snapshot survives the topology change.
            _relationships.RemoveLink(p2Rep, p1Rep, _controlsTypeId);
            _commands.Clear();

            SendMemberAdded(unit, p1Rep, viewer: p2Rep);
            Assert.That(EmittedMarkerDefIds(), Is.Empty);
        }

        [Test]
        public void ViewerRegister_IsSeededIntoEntityRegisterTwo()
        {
            // Condition reads E[2] directly (no LoadViewer op): true only when viewer == source.
            _programs.Register(ViewerIsRowDomainProgramId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqEntity, A = 2, B = 0, Dst = 0 },
            }, GraphKind.Validation);
            _defs.Register("test.viewer_register", new PerformerDefinition
            {
                Rules = new[] { SelectionMarkerRule(ViewerIsRowDomainProgramId, DeepGreenMarkerDefId) },
            });

            Entity unit = _world.Create();
            Entity other = _world.Create();

            SendMemberAdded(unit, other, viewer: other);
            Assert.That(EmittedMarkerDefIds(), Is.Empty);

            SendMemberAdded(unit, other, viewer: unit);
            Assert.That(EmittedMarkerDefIds(), Is.EqualTo(new[] { DeepGreenMarkerDefId }));
        }

        [Test]
        public void EventPayloadRegisters_AreReadableByConditionGraphs()
        {
            // B[0] = (PayloadA == 42) AND-composed with (FloatD > 0.5) via JumpIfFalse.
            _programs.Register(ViewerIsRowDomainProgramId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadEventPayloadInt, Imm = 0, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Imm = 42, Dst = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqInt, A = 0, B = 1, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.JumpIfFalse, A = 0, Imm = 2 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadEventPayloadFloat, Imm = 3, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, ImmF = 0.5f, Dst = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.CompareGtFloat, A = 0, B = 1, Dst = 0 },
            }, GraphKind.Validation);
            _defs.Register("test.payload_registers", new PerformerDefinition
            {
                Rules = new[] { SelectionMarkerRule(ViewerIsRowDomainProgramId, DeepGreenMarkerDefId) },
            });

            Entity unit = _world.Create();

            SendPayloadEvent(unit, payloadA: 42, floatD: 1f);
            Assert.That(EmittedMarkerDefIds(), Is.EqualTo(new[] { DeepGreenMarkerDefId }));

            _commands.Clear();
            SendPayloadEvent(unit, payloadA: 41, floatD: 1f);
            Assert.That(EmittedMarkerDefIds(), Is.Empty);

            SendPayloadEvent(unit, payloadA: 42, floatD: 0.25f);
            Assert.That(EmittedMarkerDefIds(), Is.Empty);
        }

        private void SendPayloadEvent(Entity unit, int payloadA, float floatD)
        {
            Assert.That(_events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.EntityCollectionMemberAdded,
                KeyId = 7,
                Source = unit,
                Target = unit,
                PayloadA = payloadA,
                FloatD = floatD,
            }), Is.True);
            _system.Update(0.016f);
        }

        [Test]
        public void TopologyOps_CompileFromGraphConfig_AndResolveRelationshipTypeAtLoad()
        {
            var cfg = new GraphConfig
            {
                Id = "graph.cond.topology_ops",
                Kind = "Effect",
                Entry = "src",
                Nodes = new List<GraphNodeConfig>
                {
                    new() { Id = "src", Op = "LoadCaster", Next = "tgt" },
                    new() { Id = "tgt", Op = "LoadExplicitTarget", Next = "vwr" },
                    new() { Id = "vwr", Op = "LoadViewer", Next = "domain" },
                    new() { Id = "domain", Op = "ControlDomainResolve", Inputs = { "src" }, Next = "isSelf" },
                    new() { Id = "isSelf", Op = "CompareEqEntity", Inputs = { "domain", "vwr" }, Next = "hasEdge" },
                    new() { Id = "hasEdge", Op = "RelationshipHasLink", Inputs = { "vwr", "tgt" }, RelationshipType = "Controls" },
                },
            };

            var (package, diagnostics) = GraphCompiler.Compile(cfg);
            Assert.That(package.HasValue, Is.True, string.Join("; ", diagnostics.ConvertAll(d => d.Message)));

            var (_, symbols, program, _) = package!.Value;
            GraphProgramSymbolPatcher.Patch(symbols, program, new StubSymbolResolver(_controlsTypeId));

            var (p1Rep, p2Rep, _, unit) = BuildTopology();
            var api = new GasGraphRuntimeApi(
                _world,
                spatialQueries: null,
                coords: null,
                eventBus: null,
                effectRequests: null,
                tagOps: null,
                relationshipRuntime: _relationships);
            api.BindTopologyServices(_controlDomains, new KnowledgeProjectionResolver(_knowledgeStore), new DiscreteClock());

            var floats = new float[GraphVmLimits.MaxFloatRegisters];
            var ints = new int[GraphVmLimits.MaxIntRegisters];
            var bools = new byte[GraphVmLimits.MaxBoolRegisters];
            var entities = new Entity[GraphVmLimits.MaxEntityRegisters];
            var targets = new Entity[GraphVmLimits.MaxTargets];
            var state = new GraphExecutionState
            {
                World = _world,
                Caster = unit,
                ExplicitTarget = p1Rep,
                Viewer = p2Rep,
                TargetPosCm = IntVector2.Zero,
                Api = api,
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = new GraphTargetList(targets),
            CallStack = new int[Ludots.Core.NodeLibraries.GASGraph.GraphVmLimits.MaxCallStackDepth],
            CallStackCount = 0,
        };
            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            // isSelf → bool reg 0: ControlDomainResolve(unit)=P1Rep != viewer P2Rep.
            Assert.That(bools[0], Is.EqualTo(0));
            // hasEdge → bool reg 1: Controls edge P2Rep→P1Rep exists (type resolved at load).
            Assert.That(bools[1], Is.EqualTo(1));
            // ControlDomainResolve output landed in the first dynamic entity register (E[3]).
            Assert.That(entities[3], Is.EqualTo(p1Rep));
        }

        private sealed class StubSymbolResolver : IGraphSymbolResolver
        {
            private readonly int _controlsTypeId;

            public StubSymbolResolver(int controlsTypeId)
            {
                _controlsTypeId = controlsTypeId;
            }

            public int ResolveTag(string name) => throw new NotSupportedException();
            public int ResolveAttribute(string name) => throw new NotSupportedException();
            public int ResolveEffectTemplate(string name) => throw new NotSupportedException();
            public int ResolveRelationshipType(string name)
                => name == "Controls" ? _controlsTypeId : throw new InvalidOperationException($"Unknown relationship type '{name}'.");
            public int ResolveRelationshipMetric(string name) => throw new NotSupportedException();
            public int ResolveRelationshipFlag(string name) => throw new NotSupportedException();
            public int ResolveRelationshipReason(string name) => throw new NotSupportedException();
            public int ResolveTargetDispatchPreset(string name) => throw new NotSupportedException();
            public int ResolveEntityTemplate(string name) => throw new NotSupportedException();
        }
    }
}
