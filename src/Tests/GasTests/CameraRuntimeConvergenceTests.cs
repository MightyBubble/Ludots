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
            var manager = new CameraManager();
            var target = new StaticFollowTarget();
            var preset = new CameraPreset
            {
                Id = "FollowPreset",
                RigKind = CameraRigKind.ThirdPerson,
                DistanceCm = 400f,
                Pitch = 15f,
                Yaw = 180f,
                FollowMode = CameraFollowMode.AlwaysFollow,
                FollowTargetKind = CameraFollowTargetKind.LocalPlayer
            };

            manager.ApplyPreset(preset, target, snapToFollowTargetWhenAvailable: true);
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
        public void CameraManager_VirtualCamera_ClearRestoresBaseState()
        {
            var manager = new CameraManager();
            var registry = new VirtualCameraRegistry();
            registry.Register(new VirtualCameraDefinition
            {
                Id = "FocusEnemy",
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
            manager.SetVirtualCameraRegistry(registry);
            manager.ApplyPreset(new CameraPreset
            {
                Id = "Base",
                RigKind = CameraRigKind.Orbit,
                DistanceCm = 5000f,
                Pitch = 45f,
                Yaw = 180f,
                FovYDeg = 60f
            });
            manager.ApplyPose(new CameraPoseRequest
            {
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

            Assert.That(manager.State.TargetCm, Is.EqualTo(baseTarget));
            Assert.That(manager.State.DistanceCm, Is.EqualTo(baseDistance));
            Assert.That(manager.State.Pitch, Is.EqualTo(basePitch));
            Assert.That(manager.State.RigKind, Is.EqualTo(CameraRigKind.Orbit));
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
    }
}
