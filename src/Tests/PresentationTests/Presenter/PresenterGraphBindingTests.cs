using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterGraphBindingTests
    {
        private const int InputParamKey = 10;
        private const int OutputParamKey = 40;
        private const int ChainedMidParamKey = 11;
        private const int ChainedOutputParamKey = 12;
        private const int DoubleGraphId = 910_001;
        private const int IncrementGraphId = 910_002;
        private const int NoFloatResultGraphId = 910_003;
        private const int UnregisteredGraphId = 910_099;

        private World _world = null!;
        private PresenterEntityRuntime _instances = null!;
        private PresenterDefinitionRegistry _definitions = null!;
        private GraphProgramRegistry _programs = null!;
        private GasGraphRuntimeApi _graphApi = null!;
        private PresentationEventStream _events = null!;
        private PresentationOwnerChangeBuffer _ownerChanges = null!;
        private SoundRequestBuffer _soundRequests = null!;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _instances = new PresenterEntityRuntime(_world);
            _definitions = new PresenterDefinitionRegistry();
            _programs = new GraphProgramRegistry();
            _graphApi = new GasGraphRuntimeApi(_world);
            _events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
            _ownerChanges = new PresentationOwnerChangeBuffer(8);
            _soundRequests = new SoundRequestBuffer();
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        private PresenterBehaviorSystem CreateBehaviorSystem()
        {
            return new PresenterBehaviorSystem(
                _world,
                _instances,
                _definitions,
                _events,
                _ownerChanges,
                _soundRequests,
                graphPrograms: _programs,
                graphApi: _graphApi);
        }

        private static GraphInstruction Instr(GraphNodeOp op, byte dst = 0, byte a = 0, byte b = 0, int imm = 0, float immF = 0f)
        {
            return new GraphInstruction { Op = (ushort)op, Dst = dst, A = a, B = b, Imm = imm, ImmF = immF };
        }

        private void RegisterScoreProgram(int graphId, params GraphInstruction[] program)
        {
            _programs.Register(graphId, program, GraphKind.Score);
        }

        private void RegisterDoubleGraph(int graphId)
        {
            RegisterScoreProgram(
                graphId,
                Instr(GraphNodeOp.ConstFloat, dst: 3, immF: 2f),
                Instr(GraphNodeOp.MulFloat, dst: 0, a: (byte)InputParamKey, b: 3),
                Instr(GraphNodeOp.HaltReturnInt));
        }

        private Entity CreatePresenter(int defId, int stableId)
        {
            Entity owner = _world.Create();
            return _instances.Create(
                defId,
                owner,
                0,
                PresentationAnchorKind.WorldPosition,
                Vector3.Zero,
                stableId,
                Entity.Null,
                _definitions.Get(defId));
        }

        [Test]
        public void GraphBinding_WritesScoreResultToParamBlackboard()
        {
            RegisterDoubleGraph(DoubleGraphId);
            int defId = _definitions.Register("graph.binding.basic", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = OutputParamKey,
                        Value = ValueRef.FromGraph(DoubleGraphId),
                    },
                ],
            });

            Entity presenter = CreatePresenter(defId, 6001);
            _instances.SetParam(presenter, InputParamKey, ParamLane.Float, 3.5f, 0, Vector4.Zero);

            using var system = CreateBehaviorSystem();
            system.Update(0.016f);

            Assert.That(_instances.ResolveFloat(presenter, OutputParamKey, -1f), Is.EqualTo(7f).Within(0.0001f));
        }

        [Test]
        public void GraphBinding_ReevaluatesEveryFrame_WhenBlackboardInputChanges()
        {
            RegisterDoubleGraph(DoubleGraphId);
            int defId = _definitions.Register("graph.binding.perframe", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = OutputParamKey,
                        Value = ValueRef.FromGraph(DoubleGraphId),
                    },
                ],
            });

            Entity presenter = CreatePresenter(defId, 6002);
            _instances.SetParam(presenter, InputParamKey, ParamLane.Float, 1f, 0, Vector4.Zero);

            using var system = CreateBehaviorSystem();
            system.Update(0.016f);
            Assert.That(_instances.ResolveFloat(presenter, OutputParamKey, -1f), Is.EqualTo(2f).Within(0.0001f));

            _instances.SetParam(presenter, InputParamKey, ParamLane.Float, 5f, 0, Vector4.Zero);
            system.Update(0.016f);
            Assert.That(_instances.ResolveFloat(presenter, OutputParamKey, -1f), Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void GraphBinding_ChainEvaluatesInBindingOrder_WithinOneFrame()
        {
            RegisterDoubleGraph(DoubleGraphId);
            RegisterScoreProgram(
                IncrementGraphId,
                Instr(GraphNodeOp.ConstFloat, dst: 3, immF: 1f),
                Instr(GraphNodeOp.AddFloat, dst: 0, a: (byte)ChainedMidParamKey, b: 3),
                Instr(GraphNodeOp.HaltReturnInt));
            int defId = _definitions.Register("graph.binding.chain", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = ChainedMidParamKey,
                        Value = ValueRef.FromGraph(DoubleGraphId),
                    },
                    new PresenterParamBinding
                    {
                        ParamKey = ChainedOutputParamKey,
                        Value = ValueRef.FromGraph(IncrementGraphId),
                    },
                ],
            });

            Entity presenter = CreatePresenter(defId, 6003);
            _instances.SetParam(presenter, InputParamKey, ParamLane.Float, 3f, 0, Vector4.Zero);

            using var system = CreateBehaviorSystem();
            system.Update(0.016f);

            Assert.That(_instances.ResolveFloat(presenter, ChainedMidParamKey, -1f), Is.EqualTo(6f).Within(0.0001f));
            Assert.That(
                _instances.ResolveFloat(presenter, ChainedOutputParamKey, -1f),
                Is.EqualTo(7f).Within(0.0001f),
                "The second graph must read the value the first graph wrote in the same frame.");
        }

        [Test]
        public void GraphBinding_SameFrameTrace_GraphEvaluate_BlackboardWrite_AssetBindingRead()
        {
            RegisterDoubleGraph(DoubleGraphId);
            int defId = _definitions.Register("graph.binding.trace", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = OutputParamKey,
                        Value = ValueRef.FromGraph(DoubleGraphId),
                    },
                ],
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 1201,
                            MaterialId = 2201,
                            RenderPath = VisualRenderPath.InstancedStaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalScale = Vector3.One,
                            ScaleParamKey = OutputParamKey,
                            AssetIdParamKey = -1,
                            AssetSwapParamKey = -1,
                        },
                    },
                ],
            });

            Entity owner = _world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
            Entity presenter = _instances.Create(
                defId,
                owner,
                0,
                PresentationAnchorKind.WorldPosition,
                Vector3.Zero,
                6004,
                Entity.Null,
                _definitions.Get(defId));
            _instances.SetParam(presenter, InputParamKey, ParamLane.Float, 1.25f, 0, Vector4.Zero);

            var requests = new PresentationRequestBuffer();
            using var behavior = CreateBehaviorSystem();
            behavior.Update(0.016f);
            float blackboardAfterBehavior = _instances.ResolveFloat(presenter, OutputParamKey, -1f);

            using var emit = new PresenterEmitSystem(
                _world,
                _instances,
                _definitions,
                requests,
                new Dictionary<string, object>());
            emit.Update(0.016f);

            Assert.That(blackboardAfterBehavior, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(requests.Count, Is.EqualTo(1));
            Assert.That(
                requests.VisualProxyAt(0).VisualProxy.Scale,
                Is.EqualTo(new Vector3(2.5f, 2.5f, 2.5f)),
                "AssetBinding must read the graph result from the blackboard in the same frame, with exact value equality.");
        }

        [Test]
        public void GraphBinding_MissingProgram_WarnsAndKeepsOldValue()
        {
            int defId = _definitions.Register("graph.binding.missing", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = OutputParamKey,
                        Value = ValueRef.FromGraph(UnregisteredGraphId),
                    },
                ],
            });

            Entity presenter = CreatePresenter(defId, 6005);
            _instances.SetParam(presenter, OutputParamKey, ParamLane.Float, 0.25f, 0, Vector4.Zero);

            using var system = CreateBehaviorSystem();
            system.Update(0.016f);
            system.Update(0.016f);

            Assert.That(_instances.ResolveFloat(presenter, OutputParamKey, -1f), Is.EqualTo(0.25f));
        }

        [Test]
        public void GraphBinding_MissingFloatResultRegister_WarnsAndKeepsOldValue()
        {
            RegisterScoreProgram(
                NoFloatResultGraphId,
                Instr(GraphNodeOp.ConstFloat, dst: 3, immF: 9f),
                Instr(GraphNodeOp.HaltReturnInt));
            int defId = _definitions.Register("graph.binding.noresult", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = OutputParamKey,
                        Value = ValueRef.FromGraph(NoFloatResultGraphId),
                    },
                ],
            });

            Entity presenter = CreatePresenter(defId, 6006);
            _instances.SetParam(presenter, OutputParamKey, ParamLane.Float, 0.5f, 0, Vector4.Zero);

            using var system = CreateBehaviorSystem();
            system.Update(0.016f);
            system.Update(0.016f);

            Assert.That(_instances.ResolveFloat(presenter, OutputParamKey, -1f), Is.EqualTo(0.5f));
        }

        [Test]
        public void GraphBinding_IncompleteInput_WarnsAndKeepsOldValue()
        {
            RegisterScoreProgram(
                IncrementGraphId,
                Instr(GraphNodeOp.ConstFloat, dst: 3, immF: 1f),
                Instr(GraphNodeOp.AddFloat, dst: 0, a: (byte)InputParamKey, b: 3),
                Instr(GraphNodeOp.HaltReturnInt));
            int defId = _definitions.Register("graph.binding.missinginput", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = OutputParamKey,
                        Value = ValueRef.FromGraph(IncrementGraphId),
                    },
                ],
            });

            Entity presenter = CreatePresenter(defId, 6007);
            _instances.SetParam(presenter, OutputParamKey, ParamLane.Float, 0.75f, 0, Vector4.Zero);

            using var system = CreateBehaviorSystem();
            system.Update(0.016f);
            system.Update(0.016f);

            Assert.That(
                _instances.ResolveFloat(presenter, OutputParamKey, -1f),
                Is.EqualTo(0.75f),
                "A graph reading an input register without a blackboard key must not produce a half write.");
        }

        [Test]
        public void GraphBinding_WrongKindProgram_WarnsAndKeepsOldValue()
        {
            _programs.Register(IncrementGraphId, BuildIncrementProgram(), GraphKind.Validation);
            int defId = _definitions.Register("graph.binding.wrongkind", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = OutputParamKey,
                        Value = ValueRef.FromGraph(IncrementGraphId),
                    },
                ],
            });

            Entity presenter = CreatePresenter(defId, 6008);
            _instances.SetParam(presenter, OutputParamKey, ParamLane.Float, 0.125f, 0, Vector4.Zero);

            using var system = CreateBehaviorSystem();
            system.Update(0.016f);
            system.Update(0.016f);

            Assert.That(_instances.ResolveFloat(presenter, OutputParamKey, -1f), Is.EqualTo(0.125f));
        }

        [Test]
        public void GraphBinding_WithoutGraphRegistries_ThrowsForConfiguredDefinition()
        {
            RegisterDoubleGraph(DoubleGraphId);
            int defId = _definitions.Register("graph.binding.nowiring", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = OutputParamKey,
                        Value = ValueRef.FromGraph(DoubleGraphId),
                    },
                ],
            });

            CreatePresenter(defId, 6009);

            using var system = new PresenterBehaviorSystem(
                _world,
                _instances,
                _definitions,
                _events,
                _ownerChanges,
                _soundRequests);
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0.016f))!;
            Assert.That(ex.Message, Does.Contain("source=graph"));
            Assert.That(ex.Message, Does.Contain("graph program registry"));
        }

        [Test]
        public void ConstantBinding_AlongsideGraphBinding_StillApplies()
        {
            RegisterDoubleGraph(DoubleGraphId);
            int constantKey = 45;
            int defId = _definitions.Register("graph.binding.mixed", new PresenterDefinition
            {
                Bindings =
                [
                    new PresenterParamBinding
                    {
                        ParamKey = OutputParamKey,
                        Value = ValueRef.FromGraph(DoubleGraphId),
                    },
                    new PresenterParamBinding
                    {
                        ParamKey = constantKey,
                        Value = ValueRef.FromConstant(1.75f),
                    },
                ],
            });

            Entity presenter = CreatePresenter(defId, 6010);
            _instances.SetParam(presenter, InputParamKey, ParamLane.Float, 2f, 0, Vector4.Zero);

            using var system = CreateBehaviorSystem();
            system.Update(0.016f);

            Assert.That(_instances.ResolveFloat(presenter, OutputParamKey, -1f), Is.EqualTo(4f).Within(0.0001f));
            Assert.That(_instances.ResolveFloat(presenter, constantKey, -1f), Is.EqualTo(1.75f));
        }

        private static GraphInstruction[] BuildIncrementProgram()
        {
            return
            [
                Instr(GraphNodeOp.ConstFloat, dst: 3, immF: 1f),
                Instr(GraphNodeOp.AddFloat, dst: 0, a: (byte)InputParamKey, b: 3),
                Instr(GraphNodeOp.HaltReturnInt),
            ];
        }
    }
}
