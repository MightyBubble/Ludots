using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Diagnostics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterSinkParamToAssetTests
    {
        private RecordingLogBackend _log;
        private SinkFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingLogBackend();
            Log.Initialize(_log);
            _fixture = new SinkFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _fixture.Dispose();
            Log.Initialize(NullLogBackend.Instance);
            _log.Dispose();
        }

        [Test]
        public void Sink_WritesLaneValueIntoSlotRequestSynchronously()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();
            _fixture.EnqueueSetParam(presenter, ParamLane.Float, 2.5f);
            _fixture.RuntimeSystem.Update(0.016f);

            _fixture.EnqueueSink(presenter, ParamLane.Float, slot: 0);
            Assert.That(_fixture.Requests.Count, Is.EqualTo(0), "sink 前不应有资产请求");
            _fixture.RuntimeSystem.Update(0.016f);

            PresentationRequest request = _fixture.FindSinkVisualRequest(presenter, slot: 0);
            Assert.That(request.Kind, Is.EqualTo(PresentationRequestKind.VisualProxy));
            Assert.That(request.VisualProxy.Scale.X, Is.EqualTo(2.5f), "指定 slot 应得到 lane 当前值");
            Assert.That(request.VisualProxy.Scale.Y, Is.EqualTo(2.5f));
            Assert.That(request.VisualProxy.Scale.Z, Is.EqualTo(2.5f));
        }

        [Test]
        public void Sink_SuccessDiagnosticsAndLogContainTargetLaneSlot()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();
            _fixture.EnqueueSetParam(presenter, ParamLane.Float, 1.75f);
            _fixture.EnqueueSink(presenter, ParamLane.Float, slot: 0);
            _fixture.RuntimeSystem.Update(0.016f);

            Assert.That(_fixture.RuntimeSystem.SinkDiagnostics.TotalRecorded, Is.EqualTo(1));
            PresenterSinkOutcome outcome = _fixture.RuntimeSystem.SinkDiagnostics.GetRecent(0);
            Assert.That(outcome.Accepted, Is.True);
            Assert.That(outcome.Presenter, Is.EqualTo(presenter));
            Assert.That(outcome.Lane, Is.EqualTo(ParamLane.Float));
            Assert.That(outcome.BehaviorSlot, Is.EqualTo(0));
            Assert.That(outcome.CommandKindId, Is.EqualTo((int)PresenterCommandKind.SinkParamToAsset));
            Assert.That(outcome.Message, Does.Contain($"target={presenter}"));
            Assert.That(outcome.Message, Does.Contain("lane=Float"));
            Assert.That(outcome.Message, Does.Contain("slot=0"));

            Assert.That(_log.Infos.Count, Is.EqualTo(1), "成功 sink 必须产出一条 Presentation 通道 Info 日志");
            Assert.That(_log.Infos[0], Does.Contain($"target={presenter}"));
            Assert.That(_log.Infos[0], Does.Contain("lane=Float"));
            Assert.That(_log.Infos[0], Does.Contain("slot=0"));
        }

        [Test]
        public void Sink_TargetPresenterMissing_RejectedThenSubsequentCommandStillProcessed()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();

            _fixture.EnqueueSink(Entity.Null, ParamLane.Float, slot: 0);
            _fixture.EnqueueSetParam(presenter, ParamLane.Float, 4.0f);
            _fixture.RuntimeSystem.Update(0.016f);

            PresenterSinkOutcome outcome = _fixture.RuntimeSystem.SinkDiagnostics.GetRecent(0);
            Assert.That(outcome.Accepted, Is.False);
            Assert.That(outcome.Rejection, Is.EqualTo(PresenterSinkRejection.TargetPresenterMissing));
            Assert.That(outcome.Message, Does.Contain("commandId=7"));
            Assert.That(outcome.Message, Does.Contain("slot=0"));
            Assert.That(_log.Warnings[0], Does.Contain("TargetPresenterMissing"));
            Assert.That(_fixture.Instances.TryResolveFloat(presenter, _fixture.ScaleKey, out float value), Is.True);
            Assert.That(value, Is.EqualTo(4.0f), "拒绝后的后续命令必须继续处理");
        }

        [Test]
        public void Sink_SlotMissing_RejectedWithSlotField()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();
            _fixture.EnqueueSetParam(presenter, ParamLane.Float, 1f);

            _fixture.EnqueueSink(presenter, ParamLane.Float, slot: -1);
            _fixture.RuntimeSystem.Update(0.016f);
            PresenterSinkOutcome outcome = _fixture.RuntimeSystem.SinkDiagnostics.GetRecent(0);
            Assert.That(outcome.Accepted, Is.False);
            Assert.That(outcome.Rejection, Is.EqualTo(PresenterSinkRejection.AssetSlotMissing));
            Assert.That(outcome.Message, Does.Contain("slot=-1"));
        }

        [Test]
        public void Sink_SlotNotAnAssetBinding_Rejected()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();
            _fixture.EnqueueSetParam(presenter, ParamLane.Float, 1f);

            _fixture.EnqueueSink(presenter, ParamLane.Float, slot: 7);
            _fixture.RuntimeSystem.Update(0.016f);
            PresenterSinkOutcome outcome = _fixture.RuntimeSystem.SinkDiagnostics.GetRecent(0);
            Assert.That(outcome.Accepted, Is.False);
            Assert.That(outcome.Rejection, Is.EqualTo(PresenterSinkRejection.AssetSlotNotAssetBinding));
            Assert.That(outcome.Message, Does.Contain("slot=7"));
        }

        [Test]
        public void Sink_DeactivatedSlot_Rejected()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();
            _fixture.EnqueueSetParam(presenter, ParamLane.Float, 1f);
            _fixture.Commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DeactivateBehavior,
                CommandKindId = (byte)PresenterCommandKind.DeactivateBehavior,
                RouteStrategy = PresenterCommandRouteStrategy.ExistingInstances,
                PresenterEntity = presenter,
                TargetBehaviorSlot = 0,
            });
            _fixture.RuntimeSystem.Update(0.016f);

            _fixture.EnqueueSink(presenter, ParamLane.Float, slot: 0);
            _fixture.RuntimeSystem.Update(0.016f);
            PresenterSinkOutcome outcome = _fixture.RuntimeSystem.SinkDiagnostics.GetRecent(0);
            Assert.That(outcome.Accepted, Is.False);
            Assert.That(outcome.Rejection, Is.EqualTo(PresenterSinkRejection.AssetSlotInactive));
        }

        [Test]
        public void Sink_LaneMissing_Rejected()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();

            _fixture.EnqueueSink(presenter, ParamLane.Float, slot: 0, paramKey: _fixture.UnsetKey);
            _fixture.RuntimeSystem.Update(0.016f);
            PresenterSinkOutcome outcome = _fixture.RuntimeSystem.SinkDiagnostics.GetRecent(0);
            Assert.That(outcome.Accepted, Is.False);
            Assert.That(outcome.Rejection, Is.EqualTo(PresenterSinkRejection.LaneMissing));
            Assert.That(outcome.Message, Does.Contain("lane=Float"));
        }

        [Test]
        public void Sink_LaneTypeMismatch_Rejected()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();
            _fixture.EnqueueSetParam(presenter, ParamLane.Int, 3);
            _fixture.RuntimeSystem.Update(0.016f);

            _fixture.EnqueueSink(presenter, ParamLane.Float, slot: 0);
            _fixture.RuntimeSystem.Update(0.016f);
            PresenterSinkOutcome outcome = _fixture.RuntimeSystem.SinkDiagnostics.GetRecent(0);
            Assert.That(outcome.Accepted, Is.False);
            Assert.That(outcome.Rejection, Is.EqualTo(PresenterSinkRejection.LaneTypeMismatch));
            Assert.That(_log.Warnings[0], Does.Contain("LaneTypeMismatch"));
        }

        [Test]
        public void Sink_InSameBatchAsCreateAndSetParam_ClosesLoopInCommandOrder()
        {
            int defId = _fixture.RegisterMeshDefinition();

            _fixture.Commands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                PresenterDefinitionId = defId,
                ScopeTag = 900,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = _fixture.Owner,
                Target = _fixture.Owner,
            });
            _fixture.RuntimeSystem.Update(0.016f);
            Entity presenter = _fixture.FindCreatedPresenter(defId);
            Assert.That(presenter, Is.Not.EqualTo(Entity.Null));

            _fixture.EnqueueSetParam(presenter, ParamLane.Float, 0.5f);
            _fixture.EnqueueSink(presenter, ParamLane.Float, slot: 0);
            _fixture.RuntimeSystem.Update(0.016f);

            PresentationRequest request = _fixture.FindSinkVisualRequest(presenter, slot: 0);
            Assert.That(request.VisualProxy.Scale.X, Is.EqualTo(0.5f));
            Assert.That(_fixture.RuntimeSystem.SinkDiagnostics.TotalRecorded, Is.EqualTo(1));
            Assert.That(_fixture.RuntimeSystem.SinkDiagnostics.GetRecent(0).Accepted, Is.True);
        }

        [Test]
        public void Sink_SameFrameEmitSystemDoesNotOverwriteSinkValue()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();
            _fixture.EnqueueSetParam(presenter, ParamLane.Float, 3.25f);
            _fixture.RuntimeSystem.Update(0.016f);

            _fixture.EnqueueSink(presenter, ParamLane.Float, slot: 0);
            _fixture.RuntimeSystem.Update(0.016f);
            Assert.That(_fixture.Requests.Count, Is.GreaterThan(0), "命令处理阶段必须同步写入，不得推迟到帧尾");

            _fixture.Emit.Update(0.016f);

            int slotStableId = _fixture.ComposeSlotStableId(presenter, slot: 0);
            int matching = 0;
            foreach (ref readonly PresentationRequest request in _fixture.Requests.GetSpan())
            {
                if (request.Kind == PresentationRequestKind.VisualProxy &&
                    request.VisualProxy.StableId == slotStableId)
                {
                    matching++;
                    Assert.That(request.VisualProxy.Scale.X, Is.EqualTo(3.25f), "同帧 emit 不得用旧值覆盖 sink 写入");
                }
            }

            Assert.That(matching, Is.GreaterThanOrEqualTo(2), "sink 请求与同帧 emit 请求都应携带 lane 当前值");
        }

        private sealed class RecordingLogBackend : ILogBackend, IDisposable
        {
            public readonly List<string> Infos = new();
            public readonly List<string> Warnings = new();

            public void Write(LogLevel level, in LogChannel channel, string message)
            {
                if (level == LogLevel.Warning)
                {
                    Warnings.Add(message);
                }
                else if (level == LogLevel.Info)
                {
                    Infos.Add(message);
                }
            }

            public void Flush() { }

            public void Dispose() { }
        }

        private sealed class SinkFixture : IDisposable
        {
            public readonly World World;
            public readonly PresenterCommandBuffer Commands;
            public readonly PresentationEventStream Events;
            public readonly PresentationRequestBuffer Requests;
            public readonly PresenterEntityRuntime Instances;
            public readonly PresenterDefinitionRegistry Definitions;
            public readonly PresenterRuntimeSystem RuntimeSystem;
            public readonly PresenterEmitSystem Emit;
            public readonly Entity Owner;
            public readonly int ScaleKey;
            public readonly int UnsetKey;

            public SinkFixture()
            {
                World = Arch.Core.World.Create();
                Commands = new PresenterCommandBuffer();
                Events = new PresentationEventStream(PresentationTestConstants.EventStreamCapacity);
                Requests = new PresentationRequestBuffer();
                Instances = new PresenterEntityRuntime(World);
                Definitions = new PresenterDefinitionRegistry();
                Owner = this.World.Create();
                var globals = new Dictionary<string, object>();
                RuntimeSystem = new PresenterRuntimeSystem(
                    World,
                    Commands,
                    Events,
                    new TransientMarkerBuffer(),
                    Requests,
                    Instances,
                    new PresentationStableIdAllocator(),
                    Definitions,
                    globals: globals);
                Emit = new PresenterEmitSystem(World, Instances, Definitions, Requests, globals);
                ScaleKey = PresenterParamKeyRegistry.Register("it.sink.scale");
                UnsetKey = PresenterParamKeyRegistry.Register("it.sink.unset");
            }

            public int MeshDefinitionId { get; private set; }

            public int RegisterMeshDefinition()
            {
                MeshDefinitionId = Definitions.Register("it.sink.mesh", new PresenterDefinition
                {
                    Behaviors = new[]
                    {
                        new BehaviorSlot
                        {
                            SlotIndex = 0,
                            Kind = BehaviorKind.AssetBinding,
                            ActiveByDefault = true,
                            AssetBinding = new AssetBindingConfig
                            {
                                AssetKind = AssetKind.Mesh,
                                AssetId = 42,
                                MaterialId = 1,
                                RenderPath = VisualRenderPath.StaticMesh,
                                Mobility = VisualMobility.Static,
                                ScaleParamKey = ScaleKey,
                            },
                        },
                    },
                });
                return MeshDefinitionId;
            }

            public Entity CreatePresenter()
            {
                int defId = MeshDefinitionId;
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                    RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                    PresenterDefinitionId = defId,
                    ScopeTag = 700,
                    AnchorKind = PresentationAnchorKind.Entity,
                    Source = Owner,
                    Target = Owner,
                });
                RuntimeSystem.Update(0.016f);
                Entity presenter = FindCreatedPresenter(defId);
                Assert.That(presenter, Is.Not.EqualTo(Entity.Null), "presenter 必须创建成功");
                return presenter;
            }

            public Entity FindCreatedPresenter(int definitionId)
            {
                foreach (ref readonly PresentationEvent evt in Events.GetSpan())
                {
                    if (evt.Kind == PresentationEventKind.PresenterCreated && evt.KeyId == definitionId)
                    {
                        return evt.PresenterEntity;
                    }
                }

                return Entity.Null;
            }

            public int ComposeSlotStableId(Entity presenter, int slot)
            {
                PresenterState state = World.Get<PresenterState>(presenter);
                return PresenterBehaviorRuntimeUtility.ComposeVisualStableId(
                    state.StableId, slot, AssetKind.Mesh, state.DefId);
            }

            public PresentationRequest FindSinkVisualRequest(Entity presenter, int slot)
            {
                int slotStableId = ComposeSlotStableId(presenter, slot);
                foreach (ref readonly PresentationRequest request in Requests.GetSpan())
                {
                    if (request.Kind == PresentationRequestKind.VisualProxy &&
                        request.VisualProxy.StableId == slotStableId)
                    {
                        return request;
                    }
                }

                Assert.Fail($"未找到 slot {slot} 的 sink 视觉请求");
                return default;
            }

            public void EnqueueSetParam(Entity presenter, ParamLane lane, float value)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.SetParam,
                    CommandKindId = (byte)PresenterCommandKind.SetParam,
                    RouteStrategy = PresenterCommandRouteStrategy.ExistingInstances,
                    PresenterEntity = presenter,
                    ParamKey = ScaleKey,
                    ParamLane = lane,
                    ParamValue = value,
                    IntValue = (int)value,
                    HasParamPayload = true,
                });
            }

            public void EnqueueSink(Entity presenter, ParamLane lane, int slot, int? paramKey = null)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.SinkParamToAsset,
                    CommandKindId = (byte)PresenterCommandKind.SinkParamToAsset,
                    RouteStrategy = PresenterCommandRouteStrategy.SingleRuntime,
                    PresenterEntity = presenter,
                    ParamKey = paramKey ?? ScaleKey,
                    ParamLane = lane,
                    TargetBehaviorSlot = slot,
                });
            }

            public void Dispose()
            {
                World.Dispose();
            }
        }
    }
}
