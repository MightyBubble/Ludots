using System.Numerics;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class CameraRuntimeConvergenceTests
    {
        private sealed class PlatformManagedTestDriver : IPlatformManagedCameraDriver
        {
            public Vector2 TargetCm { get; set; }
            public float DistanceCm { get; set; }
            public int PrimeCalls { get; private set; }
            public int UpdateCalls { get; private set; }

            public void PrimeDefinition(VirtualCameraDefinition definition)
            {
                PrimeCalls++;
                definition.MinDistanceCm = 500f;
            }

            public bool Update(PlatformManagedCameraUpdateContext context)
            {
                UpdateCalls++;
                context.State.TargetCm = TargetCm;
                context.State.DistanceCm = DistanceCm;
                return true;
            }
        }

        private sealed class StaticFollowTarget : ICameraFollowTarget
        {
            public Vector2? PositionCm { get; set; }

            public bool TryGetPosition(out Vector2 positionCm)
            {
                if (PositionCm.HasValue)
                {
                    positionCm = PositionCm.Value;
                    return true;
                }

                positionCm = default;
                return false;
            }
        }

        private sealed class TestHeightmap : IVisualHeightmap
        {
            private readonly float _heightCm;
            private readonly bool _hit;

            public TestHeightmap(float heightCm, bool hit = true)
            {
                _heightCm = heightCm;
                _hit = hit;
            }

            public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
            {
                heightCm = _heightCm + (worldXCm * 0.01f) - (worldYCm * 0.005f);
                return _hit && layerIndex == 0;
            }

            public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = 0)
            {
                if (worldXCm.Length != worldYCm.Length || worldXCm.Length != outHeightCm.Length)
                {
                    throw new ArgumentException("TestHeightmap spans must have identical lengths.");
                }

                if (!_hit || layerIndex != 0)
                {
                    return false;
                }

                for (int i = 0; i < worldXCm.Length; i++)
                {
                    outHeightCm[i] = _heightCm + (worldXCm[i] * 0.01f) - (worldYCm[i] * 0.005f);
                }

                return true;
            }

            public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = 0)
            {
                hit = default;
                return false;
            }

            public bool RaycastGroundBatch(
                ReadOnlySpan<float> originXMeters,
                ReadOnlySpan<float> originYMeters,
                ReadOnlySpan<float> originZMeters,
                ReadOnlySpan<float> directionX,
                ReadOnlySpan<float> directionY,
                ReadOnlySpan<float> directionZ,
                Span<float> outWorldXCm,
                Span<float> outWorldYCm,
                Span<float> outHeightCm,
                Span<float> outDistanceMeters,
                Span<float> outNormalX,
                Span<float> outNormalY,
                Span<float> outNormalZ,
                Span<int> outLayerIndex,
                Span<byte> outHitMask,
                int layerIndex = 0)
            {
                outHitMask.Clear();
                return false;
            }
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public Vector2 Resolution => new(1280f, 720f);
            public float Fov => 60f;
            public float AspectRatio => 16f / 9f;
        }

        [Test]
        public void CameraManager_AlwaysFollow_SnapsWhenTargetBecomesAvailable()
        {
            var manager = CreateManagerWithRegistry(new VirtualCameraDefinition
            {
                Id = "FollowCamera",
                Priority = 0,
                RigKind = CameraRigKind.ThirdPerson,
                DistanceCm = 400f,
                Pitch = 15f,
                Yaw = 180f,
                FollowMode = CameraFollowMode.AlwaysFollow,
                FollowTargetKind = CameraFollowTargetKind.LocalPlayer
            });
            var target = new StaticFollowTarget();

            manager.ActivateVirtualCamera("FollowCamera", blendDurationSeconds: 0f, followTarget: target);
            manager.Update(0.016f);

            Assert.That(manager.State.IsFollowing, Is.False);
            Assert.That(manager.State.TargetCm, Is.EqualTo(Vector2.Zero));

            target.PositionCm = new Vector2(3200f, 1800f);
            manager.Update(0.016f);

            Assert.That(manager.State.IsFollowing, Is.True);
            Assert.That(manager.State.TargetCm, Is.EqualTo(target.PositionCm.Value));
            Assert.That(manager.FollowTargetPositionCm, Is.EqualTo(target.PositionCm.Value));
        }

        [Test]
        public void SelectedGroupFollowTarget_UsesViewedSelectionCentroid_AndTracksViewedPrimarySelection()
        {
            using var world = World.Create();
            var globals = new Dictionary<string, object>();
            var collections = new EntityCollectionStore(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));

            Entity selector = world.Create();
            globals[CoreServiceKeys.LocalPlayerEntity.Name] = selector;
            globals[CoreServiceKeys.EntityCollectionStore.Name] = collections;

            Entity light = world.Create(new WorldPositionCm { Value = new Ludots.Core.Mathematics.FixedPoint.Fix64Vec2(1000, 2000) });
            Entity heavy = world.Create(
                new WorldPositionCm { Value = new Ludots.Core.Mathematics.FixedPoint.Fix64Vec2(4000, 5000) },
                new CameraFollowWeight { Value = 3f });

            ReplaceCommandSource(collections, selector, light, heavy);

            var target = new SelectedGroupFollowTarget(world, globals);
            Assert.That(target.TryGetPosition(out var centroid), Is.True);
            Assert.That(centroid.X, Is.EqualTo(3250f).Within(0.01f));
            Assert.That(centroid.Y, Is.EqualTo(4250f).Within(0.01f));

            ReplaceCommandSource(collections, selector, light);
            Assert.That(EntityCollectionContextRuntime.TryGetCurrentPrimary(world, globals, out var primary), Is.True);
            Assert.That(primary, Is.EqualTo(light));

            Assert.That(target.TryGetPosition(out var fallback), Is.True);
            Assert.That(fallback.X, Is.EqualTo(1000f).Within(0.01f));
            Assert.That(fallback.Y, Is.EqualTo(2000f).Within(0.01f));
        }

        private static void ReplaceCommandSource(EntityCollectionStore collections, Entity owner, params Entity[] entities)
        {
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: entities.Length > 0 ? entities[0] : Entity.Null,
                title: "Camera runtime command source",
                summary: "Test-owned command source collection.");
            collections.Replace(owner, in descriptor, entities, owner);
        }

        [Test]
        public void CameraManager_ClearVirtualCamera_FallsBackToNextActiveCamera()
        {
            var manager = CreateManagerWithRegistry(
                new VirtualCameraDefinition
                {
                    Id = "Base",
                    Priority = 0,
                    RigKind = CameraRigKind.Orbit,
                    DistanceCm = 5000f,
                    Pitch = 45f,
                    Yaw = 180f,
                    FovYDeg = 60f
                },
                new VirtualCameraDefinition
                {
                    Id = "FocusEnemy",
                    Priority = 1000,
                    RigKind = CameraRigKind.TopDown,
                    TargetSource = VirtualCameraTargetSource.Fixed,
                    FixedTargetCm = new Vector2(2000f, 1000f),
                    Yaw = 225f,
                    Pitch = 70f,
                    DistanceCm = 12000f,
                    FovYDeg = 40f,
                    BlendCurve = CameraBlendCurve.Cut,
                    AllowUserInput = false
                });

            manager.ActivateVirtualCamera("Base", 0f);
            manager.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = "Base",
                TargetCm = new Vector2(400f, 600f)
            });
            manager.Update(0.016f);
            var baseTarget = manager.State.TargetCm;
            var baseDistance = manager.State.DistanceCm;
            var basePitch = manager.State.Pitch;

            manager.ActivateVirtualCamera("FocusEnemy", 0f);
            manager.Update(0.016f);

            Assert.That(manager.State.TargetCm, Is.EqualTo(new Vector2(2000f, 1000f)));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(12000f));
            Assert.That(manager.State.RigKind, Is.EqualTo(CameraRigKind.TopDown));

            manager.ClearVirtualCamera();
            manager.Update(0.016f);

            Assert.That(manager.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo("Base"));
            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.True);

            manager.Update(0.25f);

            Assert.That(manager.State.TargetCm, Is.EqualTo(baseTarget));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(baseDistance));
            Assert.That(manager.State.Pitch, Is.EqualTo(basePitch));
            Assert.That(manager.State.RigKind, Is.EqualTo(CameraRigKind.Orbit));
        }

        [Test]
        public void CameraManager_ClearVirtualCamera_AfterMultipleFrames_FallsBackToNextActiveCamera()
        {
            var manager = CreateManagerWithRegistry(
                new VirtualCameraDefinition
                {
                    Id = "Base",
                    Priority = 0,
                    RigKind = CameraRigKind.Orbit,
                    DistanceCm = 4200f,
                    Pitch = 40f,
                    Yaw = 135f,
                    FovYDeg = 55f
                },
                new VirtualCameraDefinition
                {
                    Id = "LockFocus",
                    Priority = 1000,
                    RigKind = CameraRigKind.TopDown,
                    TargetSource = VirtualCameraTargetSource.Fixed,
                    FixedTargetCm = new Vector2(8000f, 1200f),
                    Yaw = 200f,
                    Pitch = 75f,
                    DistanceCm = 15000f,
                    FovYDeg = 35f,
                    BlendCurve = CameraBlendCurve.Cut,
                    AllowUserInput = false
                });

            manager.ActivateVirtualCamera("Base", 0f);
            manager.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = "Base",
                TargetCm = new Vector2(900f, 1100f)
            });
            manager.Update(0.016f);
            var baseTarget = manager.State.TargetCm;
            var baseYaw = manager.State.Yaw;
            var basePitch = manager.State.Pitch;
            var baseDistance = manager.State.DistanceCm;

            manager.ActivateVirtualCamera("LockFocus", 0f);
            manager.Update(0.016f);
            manager.Update(0.016f);
            manager.Update(0.016f);

            manager.ClearVirtualCamera();
            manager.Update(0.016f);

            Assert.That(manager.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo("Base"));
            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.True);

            manager.Update(0.25f);

            Assert.That(manager.State.TargetCm, Is.EqualTo(baseTarget));
            Assert.That(manager.State.Yaw, Is.EqualTo(baseYaw));
            Assert.That(manager.State.Pitch, Is.EqualTo(basePitch));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(baseDistance));
            Assert.That(manager.State.RigKind, Is.EqualTo(CameraRigKind.Orbit));
        }

        [Test]
        public void CameraManager_ClearingTopCamera_FallsBackToLatestHigherPriorityBase()
        {
            var manager = CreateManagerWithRegistry(
                new VirtualCameraDefinition
                {
                    Id = "BaseA",
                    Priority = 0,
                    RigKind = CameraRigKind.Orbit,
                    DistanceCm = 3000f,
                    Pitch = 35f,
                    Yaw = 180f,
                    FovYDeg = 60f
                },
                new VirtualCameraDefinition
                {
                    Id = "BaseB",
                    Priority = 100,
                    RigKind = CameraRigKind.ThirdPerson,
                    DistanceCm = 600f,
                    Pitch = 20f,
                    Yaw = 160f,
                    FovYDeg = 50f
                },
                new VirtualCameraDefinition
                {
                    Id = "TacticalLock",
                    Priority = 1000,
                    RigKind = CameraRigKind.TopDown,
                    TargetSource = VirtualCameraTargetSource.Fixed,
                    FixedTargetCm = new Vector2(6400f, 3200f),
                    Yaw = 210f,
                    Pitch = 80f,
                    DistanceCm = 18000f,
                    FovYDeg = 42f,
                    BlendCurve = CameraBlendCurve.Cut,
                    AllowUserInput = false
                });

            manager.ActivateVirtualCamera("BaseA", 0f);
            manager.Update(0.016f);

            manager.ActivateVirtualCamera("TacticalLock", 0f);
            manager.Update(0.016f);

            manager.ActivateVirtualCamera("BaseB", 0f);
            manager.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = "BaseB",
                TargetCm = new Vector2(1500f, 2500f)
            });

            manager.ClearVirtualCamera();
            manager.Update(0.016f);

            Assert.That(manager.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo("BaseB"));
            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.True);

            manager.Update(0.25f);

            Assert.That(manager.State.RigKind, Is.EqualTo(CameraRigKind.ThirdPerson));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(600f));
            Assert.That(manager.State.Pitch, Is.EqualTo(20f));
            Assert.That(manager.State.Yaw, Is.EqualTo(160f));
            Assert.That(manager.State.TargetCm, Is.EqualTo(new Vector2(1500f, 2500f)));
        }

        [Test]
        public void CameraManager_ClearVirtualCamera_AfterFollowTargetResolves_FallsBackToResolvedFollowCamera()
        {
            var manager = CreateManagerWithRegistry(
                new VirtualCameraDefinition
                {
                    Id = "FollowBase",
                    Priority = 0,
                    RigKind = CameraRigKind.ThirdPerson,
                    DistanceCm = 400f,
                    Pitch = 15f,
                    Yaw = 180f,
                    FovYDeg = 60f,
                    FollowMode = CameraFollowMode.AlwaysFollow,
                    FollowTargetKind = CameraFollowTargetKind.LocalPlayer
                },
                new VirtualCameraDefinition
                {
                    Id = "IntroFocus",
                    Priority = 1000,
                    RigKind = CameraRigKind.TopDown,
                    TargetSource = VirtualCameraTargetSource.Fixed,
                    FixedTargetCm = new Vector2(6400f, 3200f),
                    Yaw = 210f,
                    Pitch = 75f,
                    DistanceCm = 18000f,
                    FovYDeg = 42f,
                    BlendCurve = CameraBlendCurve.Cut,
                    AllowUserInput = false
                });
            var target = new StaticFollowTarget();

            manager.ActivateVirtualCamera("FollowBase", 0f, followTarget: target, snapToFollowTargetWhenAvailable: true);
            manager.ActivateVirtualCamera("IntroFocus", 0f);
            manager.Update(0.016f);

            target.PositionCm = new Vector2(1200f, 800f);
            manager.Update(0.016f);

            Assert.That(manager.State.TargetCm, Is.EqualTo(new Vector2(6400f, 3200f)));
            Assert.That(manager.State.RigKind, Is.EqualTo(CameraRigKind.TopDown));

            manager.ClearVirtualCamera();
            manager.Update(0.016f);

            Assert.That(manager.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo("FollowBase"));
            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.True);

            manager.Update(0.25f);

            Assert.That(manager.State.TargetCm, Is.EqualTo(target.PositionCm.Value));
            Assert.That(manager.State.RigKind, Is.EqualTo(CameraRigKind.ThirdPerson));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(400f));
            Assert.That(manager.State.IsFollowing, Is.True);
        }

        [Test]
        public void CameraManager_VirtualCamera_LinearBlendAdvancesByTweenProgress()
        {
            var manager = CreateManagerWithRegistry(
                new VirtualCameraDefinition
                {
                    Id = "Base",
                    Priority = 0,
                    RigKind = CameraRigKind.Orbit,
                    DistanceCm = 3000f,
                    Pitch = 40f,
                    Yaw = 180f,
                    FovYDeg = 60f
                },
                new VirtualCameraDefinition
                {
                    Id = "BlendFocus",
                    Priority = 1000,
                    RigKind = CameraRigKind.TopDown,
                    TargetSource = VirtualCameraTargetSource.Fixed,
                    FixedTargetCm = new Vector2(2000f, 1000f),
                    Yaw = 270f,
                    Pitch = 70f,
                    DistanceCm = 9000f,
                    FovYDeg = 45f,
                    DefaultBlendDuration = 1f,
                    BlendCurve = CameraBlendCurve.Linear,
                    AllowUserInput = false
                });

            manager.ActivateVirtualCamera("Base", 0f);
            manager.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = "Base",
                TargetCm = new Vector2(400f, 200f)
            });

            manager.ActivateVirtualCamera("BlendFocus", 1f);
            manager.Update(0.5f);

            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.True);
            Assert.That(manager.State.TargetCm.X, Is.EqualTo(1200f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(600f).Within(0.01f));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(6000f).Within(0.01f));
            Assert.That(manager.State.Pitch, Is.EqualTo(55f).Within(0.01f));
            Assert.That(manager.State.FovYDeg, Is.EqualTo(52.5f).Within(0.01f));

            manager.Update(0.5f);

            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.False);
            Assert.That(manager.State.TargetCm, Is.EqualTo(new Vector2(2000f, 1000f)));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(9000f));
            Assert.That(manager.State.Pitch, Is.EqualTo(70f));
            Assert.That(manager.State.Yaw, Is.EqualTo(270f));
            Assert.That(manager.State.FovYDeg, Is.EqualTo(45f));
        }

        [Test]
        public void CameraManager_VirtualCameraTargetHeight_SamplesVisualHeightmapAndFeedsRenderTarget()
        {
            var manager = CreateManagerWithRegistry(new VirtualCameraDefinition
            {
                Id = "HeightmapCamera",
                Priority = 0,
                RigKind = CameraRigKind.Orbit,
                TargetSource = VirtualCameraTargetSource.Fixed,
                FixedTargetCm = new Vector2(2000f, 1000f),
                TargetHeightMode = VirtualCameraTargetHeightMode.VisualHeightmap,
                TargetHeightOffsetCm = 125f,
                DistanceCm = 5000f,
                Pitch = 45f,
                Yaw = 180f,
                FovYDeg = 60f
            });

            manager.ConfigureRuntime(
                new PlayerInputHandler(new NullInputBackend(), new InputConfigRoot()),
                new StubViewController(),
                visualHeightmapProvider: () => new TestHeightmap(300f));
            manager.ActivateVirtualCamera("HeightmapCamera", blendDurationSeconds: 0f);
            manager.Update(0.016f);

            Assert.That(manager.State.TargetCm, Is.EqualTo(new Vector2(2000f, 1000f)));
            Assert.That(manager.State.TargetHeightCm, Is.EqualTo(440f).Within(0.001f));

            CameraRenderState3D renderState = CameraViewportUtil.StateToRenderState(manager.State);
            Assert.That(renderState.Target.Y, Is.EqualTo(4.4f).Within(0.0001f));
        }

        [Test]
        public void CameraManager_VirtualCameraTargetHeight_DoesNotCorruptBlendDestination()
        {
            var manager = CreateManagerWithRegistry(
                new VirtualCameraDefinition
                {
                    Id = "BaseHeight",
                    Priority = 0,
                    RigKind = CameraRigKind.Orbit,
                    TargetSource = VirtualCameraTargetSource.Fixed,
                    FixedTargetCm = Vector2.Zero,
                    TargetHeightMode = VirtualCameraTargetHeightMode.VisualHeightmap,
                    DistanceCm = 3000f,
                    Pitch = 40f,
                    Yaw = 180f,
                    FovYDeg = 60f
                },
                new VirtualCameraDefinition
                {
                    Id = "BlendHeight",
                    Priority = 1000,
                    RigKind = CameraRigKind.TopDown,
                    TargetSource = VirtualCameraTargetSource.Fixed,
                    FixedTargetCm = new Vector2(2000f, 1000f),
                    TargetHeightMode = VirtualCameraTargetHeightMode.VisualHeightmap,
                    DistanceCm = 9000f,
                    Pitch = 70f,
                    Yaw = 270f,
                    FovYDeg = 45f,
                    DefaultBlendDuration = 1f,
                    BlendCurve = CameraBlendCurve.Linear,
                    AllowUserInput = false
                });

            manager.ConfigureRuntime(
                new PlayerInputHandler(new NullInputBackend(), new InputConfigRoot()),
                new StubViewController(),
                visualHeightmapProvider: () => new TestHeightmap(300f));
            manager.ActivateVirtualCamera("BaseHeight", 0f);
            manager.Update(0.016f);

            manager.ActivateVirtualCamera("BlendHeight", 1f);
            manager.Update(0.5f);

            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.True);
            Assert.That(manager.State.TargetCm.X, Is.EqualTo(1000f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(500f).Within(0.01f));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(6000f).Within(0.01f));
            Assert.That(manager.State.Pitch, Is.EqualTo(55f).Within(0.01f));

            manager.Update(0.5f);

            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.False);
            Assert.That(manager.State.TargetCm, Is.EqualTo(new Vector2(2000f, 1000f)));
            Assert.That(manager.State.TargetHeightCm, Is.EqualTo(315f).Within(0.001f));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(9000f));
            Assert.That(manager.State.Pitch, Is.EqualTo(70f));
            Assert.That(manager.State.Yaw, Is.EqualTo(270f));
            Assert.That(manager.State.FovYDeg, Is.EqualTo(45f));
        }

        [Test]
        public void CameraManager_VirtualCameraTargetHeight_UpdatesAfterInputTargetChangeBeforeCapture()
        {
            var manager = CreateManagerWithRegistry(new VirtualCameraDefinition
            {
                Id = "InputHeight",
                Priority = 0,
                RigKind = CameraRigKind.Orbit,
                TargetHeightMode = VirtualCameraTargetHeightMode.VisualHeightmap,
                DistanceCm = 5000f,
                Pitch = 45f,
                Yaw = 180f,
                FovYDeg = 60f
            });

            manager.ConfigureRuntime(
                new PlayerInputHandler(new NullInputBackend(), new InputConfigRoot()),
                new StubViewController(),
                visualHeightmapProvider: () => new TestHeightmap(300f));
            manager.ActivateVirtualCamera("InputHeight", blendDurationSeconds: 0f);
            manager.Update(0.016f);

            manager.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = "InputHeight",
                TargetCm = new Vector2(4000f, 2000f)
            });
            manager.Update(0.016f);

            Assert.That(manager.State.TargetCm, Is.EqualTo(new Vector2(4000f, 2000f)));
            Assert.That(manager.State.TargetHeightCm, Is.EqualTo(330f).Within(0.001f));
        }

        [Test]
        public void CameraManager_TargetConfine_DoesNotCorruptBlendDestination()
        {
            var manager = CreateManagerWithRegistry(
                new VirtualCameraDefinition
                {
                    Id = "BaseConfine",
                    Priority = 0,
                    RigKind = CameraRigKind.Orbit,
                    TargetSource = VirtualCameraTargetSource.Fixed,
                    FixedTargetCm = Vector2.Zero,
                    DistanceCm = 3000f,
                    Pitch = 40f,
                    Yaw = 180f,
                    FovYDeg = 60f,
                    ConfineTargetToWorldBounds = true
                },
                new VirtualCameraDefinition
                {
                    Id = "BlendConfine",
                    Priority = 1000,
                    RigKind = CameraRigKind.TopDown,
                    TargetSource = VirtualCameraTargetSource.Fixed,
                    FixedTargetCm = new Vector2(5000f, -5000f),
                    DistanceCm = 9000f,
                    Pitch = 70f,
                    Yaw = 270f,
                    FovYDeg = 45f,
                    DefaultBlendDuration = 1f,
                    BlendCurve = CameraBlendCurve.Linear,
                    AllowUserInput = false,
                    ConfineTargetToWorldBounds = true
                });

            manager.ConfigureRuntime(
                new PlayerInputHandler(new NullInputBackend(), new InputConfigRoot()),
                new StubViewController(),
                targetBoundsProvider: () => new WorldAabbCm(-1000, -500, 2000, 1000));
            manager.ActivateVirtualCamera("BaseConfine", 0f);
            manager.Update(0.016f);

            manager.ActivateVirtualCamera("BlendConfine", 1f);
            manager.Update(0.5f);

            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.True);
            Assert.That(manager.State.TargetCm.X, Is.EqualTo(500f).Within(0.01f));
            Assert.That(manager.State.TargetCm.Y, Is.EqualTo(-250f).Within(0.01f));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(6000f).Within(0.01f));

            manager.Update(0.5f);

            Assert.That(manager.VirtualCameraBrain?.IsBlending, Is.False);
            Assert.That(manager.State.TargetCm, Is.EqualTo(new Vector2(1000f, -500f)));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(9000f));
            Assert.That(manager.State.Pitch, Is.EqualTo(70f));
            Assert.That(manager.State.Yaw, Is.EqualTo(270f));
            Assert.That(manager.State.FovYDeg, Is.EqualTo(45f));
        }

        [Test]
        public void CameraManager_VirtualCameraTargetHeight_ThrowsWhenVisualHeightmapMissingOrOutOfBounds()
        {
            var missing = CreateManagerWithRegistry(new VirtualCameraDefinition
            {
                Id = "MissingHeightmapCamera",
                TargetHeightMode = VirtualCameraTargetHeightMode.VisualHeightmap,
                DistanceCm = 5000f
            });
            missing.ConfigureRuntime(
                new PlayerInputHandler(new NullInputBackend(), new InputConfigRoot()),
                new StubViewController());
            missing.ActivateVirtualCamera("MissingHeightmapCamera", blendDurationSeconds: 0f);
            Assert.That(
                () => missing.Update(0.016f),
                Throws.InvalidOperationException.With.Message.Contains("requires CoreServiceKeys.VisualHeightmap"));

            var miss = CreateManagerWithRegistry(new VirtualCameraDefinition
            {
                Id = "MissHeightmapCamera",
                TargetHeightMode = VirtualCameraTargetHeightMode.VisualHeightmap,
                DistanceCm = 5000f
            });
            miss.ConfigureRuntime(
                new PlayerInputHandler(new NullInputBackend(), new InputConfigRoot()),
                new StubViewController(),
                visualHeightmapProvider: () => new TestHeightmap(0f, hit: false));
            miss.ActivateVirtualCamera("MissHeightmapCamera", blendDurationSeconds: 0f);
            Assert.That(
                () => miss.Update(0.016f),
                Throws.InvalidOperationException.With.Message.Contains("could not sample VisualHeightmap target height"));
        }

        [Test]
        public void CameraViewportUtil_FirstPersonStateToRenderState_DoesNotProduceNaN()
        {
            var state = new CameraState
            {
                RigKind = CameraRigKind.FirstPerson,
                TargetCm = new Vector2(1500f, -300f),
                DistanceCm = 0f,
                Pitch = 0f,
                Yaw = 180f,
                FovYDeg = 90f
            };

            CameraRenderState3D renderState = CameraViewportUtil.StateToRenderState(state);

            Assert.That(float.IsNaN(renderState.Position.X), Is.False);
            Assert.That(float.IsNaN(renderState.Position.Y), Is.False);
            Assert.That(float.IsNaN(renderState.Position.Z), Is.False);
            Assert.That(float.IsNaN(renderState.Target.X), Is.False);
            Assert.That(float.IsNaN(renderState.Target.Y), Is.False);
            Assert.That(float.IsNaN(renderState.Target.Z), Is.False);
            Assert.That(Vector3.DistanceSquared(renderState.Position, renderState.Target), Is.GreaterThan(0.1f));
        }

        [Test]
        public void CameraViewportUtil_ResolveClipPlanes_ScalesForLargeWorldStrategicCamera()
        {
            var state = new CameraState
            {
                TargetCm = Vector2.Zero,
                DistanceCm = 180_000f,
                Pitch = 62f,
                Yaw = 45f,
                FovYDeg = 60f
            };

            CameraRenderState3D renderState = CameraViewportUtil.StateToRenderState(state);
            CameraClipPlanes clipPlanes = CameraViewportUtil.ResolveClipPlanes(in renderState);

            Assert.That(clipPlanes.NearMeters, Is.EqualTo(CameraViewportUtil.DefaultNearPlaneMeters).Within(0.0001f));
            Assert.That(clipPlanes.FarMeters, Is.GreaterThan(Vector3.Distance(renderState.Position, renderState.Target) * 4f));
            Assert.That(clipPlanes.FarMeters, Is.GreaterThan(CameraViewportUtil.DefaultFarPlaneMeters));
        }

        private static CameraManager CreateManagerWithRegistry(params VirtualCameraDefinition[] definitions)
        {
            var manager = new CameraManager();
            var registry = new VirtualCameraRegistry();
            for (int i = 0; i < definitions.Length; i++)
            {
                registry.Register(definitions[i]);
            }

            manager.SetVirtualCameraRegistry(registry);
            return manager;
        }
    }
}
