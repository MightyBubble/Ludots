using System.Numerics;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class CameraRuntimeConvergenceTests
    {
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
                FollowMode = CameraFollowMode.AlwaysFollow
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
                    FollowMode = CameraFollowMode.AlwaysFollow
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
