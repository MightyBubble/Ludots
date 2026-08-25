using System;
using System.Collections.Generic;
using Arch.Core;
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
    public sealed class PresenterVisibilityConvergenceTests
    {
        private VisibilityFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _fixture = new VisibilityFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _fixture.Dispose();
        }

        [Test]
        public void RegisterDefinition_VisibilityParamBackedByIntDefault_Passes()
        {
            int defId = _fixture.RegisterMeshDefinition();
            Assert.That(defId, Is.GreaterThan(0));
        }

        [Test]
        public void ParamDrivenVisibility_ParamZeroHidesSlot_ParamOneShowsSlot()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();

            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(1), "paramDefault=1 时资产应可见");

            _fixture.SetVisibilityParam(presenter, 0);
            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(0), "param=0 时资产应被业务可见性隐藏");

            _fixture.SetVisibilityParam(presenter, 1);
            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(1), "param=1 时资产恢复可见");
        }

        [Test]
        public void CommandDrivenVisibility_DeactivateStopsEmission_ActivateResumes()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();
            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(1));

            _fixture.EnqueueBehaviorCommand(presenter, PresenterCommandKind.DeactivateBehavior);
            _fixture.RuntimeSystem.Update(0.016f);
            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(0), "DeactivateBehavior 后 slot 不再发射");

            _fixture.EnqueueBehaviorCommand(presenter, PresenterCommandKind.ActivateBehavior);
            _fixture.RuntimeSystem.Update(0.016f);
            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(1), "ActivateBehavior 后 slot 恢复发射");
        }

        [Test]
        public void Culling_DoesNotChangeBusinessVisibility()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();

            _fixture.SetCullVisible(presenter, false);
            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(0), "平台裁剪关断可见请求");
            ref PresenterState culledState = ref _fixture.World.Get<PresenterState>(presenter);
            Assert.That(culledState.BehaviorActiveMask & 1u, Is.Not.EqualTo(0u), "裁剪不得改变业务 active mask");
            Assert.That(_fixture.Instances.TryResolveInt(presenter, _fixture.VisibilityKey, out int culledParam), Is.True);
            Assert.That(culledParam, Is.EqualTo(1), "裁剪不得改变业务 visibility param");

            _fixture.SetCullVisible(presenter, true);
            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(1), "裁剪恢复后按业务可见性发射");
        }

        [Test]
        public void CullingOff_StillEmitsHiddenBusinessState_BusinessHiddenStillHiddenWhenCulled()
        {
            _fixture.RegisterMeshDefinition();
            Entity presenter = _fixture.CreatePresenter();

            _fixture.SetVisibilityParam(presenter, 0);
            _fixture.SetCullVisible(presenter, true);
            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(0), "业务隐藏与裁剪无关地阻断可见请求");

            _fixture.SetCullVisible(presenter, false);
            _fixture.Emit.Update(0.016f);
            Assert.That(_fixture.CountNewVisibleRequests(presenter), Is.EqualTo(0));
            ref PresenterState state = ref _fixture.World.Get<PresenterState>(presenter);
            Assert.That(state.BehaviorActiveMask & 1u, Is.Not.EqualTo(0u), "业务状态不受裁剪影响");
        }

        private sealed class VisibilityFixture : IDisposable
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
            public readonly int VisibilityKey;

            public VisibilityFixture()
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
                    Definitions);
                Emit = new PresenterEmitSystem(World, Instances, Definitions, Requests, globals);
                VisibilityKey = PresenterParamKeyRegistry.Register("visconv.visible");
            }

            public int RegisterMeshDefinition()
            {
                return Definitions.Register("visconv.mesh", BuildMeshDefinition(VisibilityKey, BuildDefaults()));
            }

            private ParamDefault[] BuildDefaults()
            {
                return new[]
                {
                    new ParamDefault
                    {
                        ParamKey = VisibilityKey,
                        Lane = ParamLane.Int,
                        IntValue = 1,
                    },
                };
            }

            private static PresenterDefinition BuildMeshDefinition(int visibilityKey, ParamDefault[]? paramDefaults)
            {
                return new PresenterDefinition
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
                                Mobility = VisualMobility.Movable,
                                VisibilityParamKey = visibilityKey,
                            },
                        },
                    },
                    ParamDefaults = paramDefaults ?? Array.Empty<ParamDefault>(),
                };
            }

            public Entity CreatePresenter()
            {
                int defId = Definitions.GetId("visconv.mesh");
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                    RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                    PresenterDefinitionId = defId,
                    ScopeTag = 710,
                    AnchorKind = PresentationAnchorKind.Entity,
                    Source = Owner,
                    Target = Owner,
                });
                RuntimeSystem.Update(0.016f);
                foreach (ref readonly PresentationEvent evt in Events.GetSpan())
                {
                    if (evt.Kind == PresentationEventKind.PresenterCreated && evt.KeyId == defId)
                    {
                        return evt.PresenterEntity;
                    }
                }

                Assert.Fail("presenter 必须创建成功");
                return Entity.Null;
            }

            public void SetVisibilityParam(Entity presenter, int value)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.SetParam,
                    CommandKindId = (byte)PresenterCommandKind.SetParam,
                    RouteStrategy = PresenterCommandRouteStrategy.ExistingInstances,
                    PresenterEntity = presenter,
                    ParamKey = VisibilityKey,
                    ParamLane = ParamLane.Int,
                    IntValue = value,
                    HasParamPayload = true,
                });
                RuntimeSystem.Update(0.016f);
            }

            public void EnqueueBehaviorCommand(Entity presenter, PresenterCommandKind kind)
            {
                Commands.TryAdd(new PresenterCommand
                {
                    CommandKind = kind,
                    CommandKindId = (byte)kind,
                    RouteStrategy = PresenterCommandRouteStrategy.ExistingInstances,
                    PresenterEntity = presenter,
                    TargetBehaviorSlot = 0,
                });
                RuntimeSystem.Update(0.016f);
            }

            public void SetCullVisible(Entity presenter, bool visible)
            {
                ref PresenterCullState cull = ref World.Get<PresenterCullState>(presenter);
                cull.OwnerCullVisible = visible;
                cull.LOD = visible ? LODLevel.High : LODLevel.Culled;
            }

            private int _seenRequestCount;

            public int CountNewVisibleRequests(Entity presenter)
            {
                PresenterState state = World.Get<PresenterState>(presenter);
                int slotStableId = PresenterBehaviorRuntimeUtility.ComposeVisualStableId(
                    state.StableId, 0, AssetKind.Mesh, state.DefId);
                ReadOnlySpan<PresentationRequest> span = Requests.GetSpan();
                int count = 0;
                for (int i = _seenRequestCount; i < span.Length; i++)
                {
                    ref readonly PresentationRequest request = ref span[i];
                    if (request.Kind == PresentationRequestKind.VisualProxy &&
                        request.VisualProxy.StableId == slotStableId &&
                        request.VisualProxy.Visibility == VisualVisibility.Visible)
                    {
                        count++;
                    }
                }

                _seenRequestCount = span.Length;
                return count;
            }

            public void Dispose()
            {
                World.Dispose();
            }
        }
    }
}
