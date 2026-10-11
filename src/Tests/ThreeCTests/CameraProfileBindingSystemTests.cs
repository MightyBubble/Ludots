using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics.FixedPoint;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    /// <summary>
    /// The possessed rep's CameraProfileBinding is the sole-seat perspective authority: switching
    /// possession switches the active virtual camera profile; no binding means no preference;
    /// empty or unknown profile ids fail fast.
    /// </summary>
    [TestFixture]
    public sealed class CameraProfileBindingSystemTests
    {
        private const string TpsProfileId = "Camera.Test.Tps";
        private const string TopdownProfileId = "Camera.Test.Topdown";
        private const string ViewId = "logicview.test.sole";

        [Test]
        public void PossessionSwitch_AdoptsBoundProfile()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            harness.System.Update(0f);
            Assert.That(harness.ActiveCameraId, Is.EqualTo(TpsProfileId),
                "the initial rep's binding adopts the TPS profile on the first tick.");

            harness.AssignPossession(harness.LiSi);
            harness.System.Update(0f);
            Assert.That(harness.ActiveCameraId, Is.EqualTo(TopdownProfileId),
                "the switched-to rep's binding adopts the top-down profile.");

            harness.AssignPossession(harness.ZhangSan);
            harness.System.Update(0f);
            Assert.That(harness.ActiveCameraId, Is.EqualTo(TpsProfileId),
                "switching back restores the first rep's profile.");
        }

        [Test]
        public void RepWithoutBinding_KeepsCurrentProfile()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.System.Update(0f);
            Assert.That(harness.ActiveCameraId, Is.EqualTo(TpsProfileId));

            harness.AssignPossession(harness.NoBinding);
            harness.System.Update(0f);
            Assert.That(harness.ActiveCameraId, Is.EqualTo(TpsProfileId),
                "a rep without a binding declares no perspective preference — the profile stays.");
        }

        [Test]
        public void UnknownProfileId_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.AssignPossession(harness.CreateRep("Camera.Test.Missing"));

            Assert.Throws<InvalidOperationException>(
                () => harness.System.Update(0f),
                "a binding referencing an unknown profile is a wiring error, not a silent keep.");
        }

        [Test]
        public void EmptyProfileId_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.AssignPossession(harness.CreateRep("  "));

            Assert.Throws<InvalidOperationException>(() => harness.System.Update(0f));
        }

        private sealed class Harness
        {
            private readonly ClientLocalSeatRegistry _seats = new();
            private readonly LogicViewRegistry _views = new();
            private readonly Dictionary<string, object> _globals = new();
            private readonly World _world;

            private Harness(World world)
            {
                _world = world;
            }

            public CameraProfileBindingSystem System = null!;
            public Entity ZhangSan;
            public Entity LiSi;
            public Entity NoBinding;

            public static Harness Create(World world)
            {
                var registry = new VirtualCameraRegistry();
                registry.Register(new VirtualCameraDefinition
                {
                    Id = TpsProfileId,
                    FacingMode = CameraFacingMode.FollowTarget,
                    FollowMode = CameraFollowMode.AlwaysFollow,
                    FollowTargetKind = CameraFollowTargetKind.SolePossessedRep,
                    Pitch = 18f,
                    DistanceCm = 500f,
                });
                registry.Register(new VirtualCameraDefinition
                {
                    Id = TopdownProfileId,
                    FacingMode = CameraFacingMode.None,
                    FollowMode = CameraFollowMode.AlwaysFollow,
                    FollowTargetKind = CameraFollowTargetKind.SolePossessedRep,
                    Pitch = 62f,
                    DistanceCm = 1500f,
                });

                var harness = new Harness(world)
                {
                    ZhangSan = CreateRep(world, TpsProfileId, new Vector2(0f, 0f)),
                    LiSi = CreateRep(world, TopdownProfileId, new Vector2(800f, 0f)),
                };
                harness.NoBinding = world.Create(
                    new WorldPositionCm { Value = Fix64Vec2.FromFloat(1600f, 0f) },
                    new Name { Value = "NoBinding" });

                harness._views.EnsureDefaultView(harness.ZhangSan, ViewId);
                harness._views.Require(ViewId).Camera.SetVirtualCameraRegistry(registry);
                harness._seats.Add(new ClientLocalSeat("seat.0"));
                harness._seats.SetPossession("seat.0", 1, harness.ZhangSan);
                harness._seats.SetPresentBinding(
                    "seat.0",
                    PresentBinding.FullScreen(ViewId, new System.Numerics.Vector2(1280f, 720f)));
                harness._globals[CoreServiceKeys.ClientLocalSeatRegistry.Name] = harness._seats;
                harness._globals[CoreServiceKeys.LogicViewRegistry.Name] = harness._views;

                harness.System = new CameraProfileBindingSystem(world, harness._globals, registry);
                return harness;
            }

            public Entity CreateRep(string profileId) =>
                CreateRep(_world, profileId, new System.Numerics.Vector2(400f, 400f));

            public void AssignPossession(Entity rep) => _seats.SetPossession("seat.0", 1, rep);

            public string ActiveCameraId =>
                _views.Require(ViewId).Camera.VirtualCameraBrain?.ActiveCameraId ?? string.Empty;

            private static Entity CreateRep(World world, string profileId, System.Numerics.Vector2 at) =>
                world.Create(
                    new WorldPositionCm { Value = Fix64Vec2.FromFloat(at.X, at.Y) },
                    new Name { Value = $"Rep_{profileId}" },
                    new CameraProfileBinding { ProfileId = profileId });
        }
    }
}
