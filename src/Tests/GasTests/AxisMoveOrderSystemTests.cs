using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 INT-6 (DEC-15): WASD axis intent → throttled move order kernel. Orders always go
    /// through the <see cref="OrderQueue"/> (never direct <see cref="WorldPositionCm"/> writes),
    /// the throttle re-arms on axis release, disabled config means zero work, and the steady-state
    /// tick is allocation free.
    /// </summary>
    [TestFixture]
    public sealed class AxisMoveOrderSystemTests
    {
        private const int MoveToOrderTypeId = 2;
        private const int StartXcm = 1000;
        private const int StartYcm = 2000;

        [Test]
        public void Update_Disabled_SubmitsNothingEvenWithAxisInput()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            var system = harness.CreateSystem(Harness.Config(enabled: false));
            harness.Input.SetActionValue("Move", new Vector3(1f, 0f, 0f));

            for (int i = 0; i < 10; i++)
            {
                system.Update(0f);
            }

            Assert.That(harness.Orders.Count, Is.EqualTo(0), "enabled=false is explicit configuration: zero work per tick.");
        }

        [Test]
        public void Update_ZeroAxis_SubmitsNothing()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            var system = harness.CreateSystem(Harness.Config(enabled: true, throttleTicks: 6));

            for (int i = 0; i < 10; i++)
            {
                system.Update(0f);
            }

            Assert.That(harness.Orders.Count, Is.EqualTo(0));
        }

        [Test]
        public void Update_HeldAxis_SubmitsThrottledMoveOrdersTowardDirection()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            var system = harness.CreateSystem(Harness.Config(enabled: true, throttleTicks: 6, stepDistanceCm: 400));
            // Diagonal (3,4) normalizes to (0.6, 0.8): step lands at +240/+320 cm.
            harness.Input.SetActionValue("Move", new Vector3(3f, 4f, 0f));

            for (int i = 0; i < 12; i++)
            {
                system.Update(0f);
            }

            Assert.That(harness.Orders.Count, Is.EqualTo(2), "12 held ticks at throttleTicks=6 submit exactly two orders.");
            Assert.That(harness.Orders.TryDequeue(out Order first), Is.True);
            Assert.That(first.OrderTypeId, Is.EqualTo(MoveToOrderTypeId));
            Assert.That(first.PlayerId, Is.EqualTo(1));
            Assert.That(first.Actor, Is.EqualTo(harness.Avatar));
            Assert.That(first.SubmitMode, Is.EqualTo(OrderSubmitMode.Immediate));
            Assert.That(first.Args.Spatial.Kind, Is.EqualTo(OrderSpatialKind.WorldCm));
            Assert.That(first.Args.Spatial.WorldCm.X, Is.EqualTo(StartXcm + 240f).Within(0.01f));
            Assert.That(first.Args.Spatial.WorldCm.Y, Is.EqualTo(StartYcm + 320f).Within(0.01f));

            // Nothing moved the avatar between ticks, so the second target is identical.
            Assert.That(harness.Orders.TryDequeue(out Order second), Is.True);
            Assert.That(second.Args.Spatial.WorldCm, Is.EqualTo(first.Args.Spatial.WorldCm));
        }

        [Test]
        public void Update_AxisRelease_RearmsThrottleForImmediateResubmit()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            var system = harness.CreateSystem(Harness.Config(enabled: true, throttleTicks: 6));

            harness.Input.SetActionValue("Move", new Vector3(1f, 0f, 0f));
            system.Update(0f);
            Assert.That(harness.Orders.Count, Is.EqualTo(1), "a fresh press submits on the first tick.");

            system.Update(0f);
            system.Update(0f);
            Assert.That(harness.Orders.Count, Is.EqualTo(1), "held ticks inside the throttle window submit nothing.");

            harness.Input.SetActionValue("Move", Vector3.Zero);
            system.Update(0f);

            harness.Input.SetActionValue("Move", new Vector3(0f, 1f, 0f));
            system.Update(0f);
            Assert.That(harness.Orders.Count, Is.EqualTo(2), "release re-arms the throttle so a fresh press submits immediately.");
        }

        [Test]
        public void Ctor_UnknownOrderTypeKey_FailsFastOnlyWhenEnabled()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Assert.Throws<KeyNotFoundException>(() => harness.CreateSystem(new AxisMoveConfig
            {
                Enabled = true,
                ActionId = "Move",
                OrderTypeKey = "orders.test.unknown",
                ThrottleTicks = 6,
                StepDistanceCm = 400,
            }));

            Assert.DoesNotThrow(() => harness.CreateSystem(new AxisMoveConfig
            {
                Enabled = false,
                OrderTypeKey = "orders.test.unknown",
            }));
        }

        [Test]
        public void Update_Enabled_MissingAuthoritativeInputService_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            var system = harness.CreateSystem(Harness.Config(enabled: true));
            harness.Globals.Remove(CoreServiceKeys.AuthoritativeInput.Name);

            Assert.Throws<InvalidOperationException>(
                () => system.Update(0f),
                "enabled without the authoritative input snapshot is a wiring error, never a state to wait out.");

            var disabled = harness.CreateSystem(Harness.Config(enabled: false));
            Assert.DoesNotThrow(() => disabled.Update(0f), "disabled config keeps zero work per tick.");
        }

        [Test]
        public void Update_MissingLocalPlayerOrPosition_SubmitsNothing()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            var system = harness.CreateSystem(Harness.Config(enabled: true, throttleTicks: 1));
            harness.Input.SetActionValue("Move", new Vector3(1f, 0f, 0f));

            harness.Globals.Remove(CoreServiceKeys.LocalPlayerEntity.Name);
            system.Update(0f);
            Assert.That(harness.Orders.Count, Is.EqualTo(0), "no resolved local player entity: nothing to move.");

            Entity positionless = harness.World.Create();
            harness.Globals[CoreServiceKeys.LocalPlayerEntity.Name] = positionless;
            system.Update(0f);
            Assert.That(harness.Orders.Count, Is.EqualTo(0), "a rep without WorldPositionCm has no movable anchor (RTS wiring lands later).");
        }

        [Test]
        public void Update_SteadyState_IsAllocationFree()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            var system = harness.CreateSystem(Harness.Config(enabled: true, throttleTicks: 4));
            harness.Input.SetActionValue("Move", new Vector3(1f, 1f, 0f));

            for (int i = 0; i < 64; i++)
            {
                system.Update(0f);
                harness.Orders.TryDequeue(out _);
            }

            long allocated = MeasureUpdateAllocations(system, harness.Orders);
            allocated = Math.Min(allocated, MeasureUpdateAllocations(system, harness.Orders));
            Assert.That(allocated, Is.EqualTo(0), "Steady-state axis move ticks must be allocation free.");
        }

        private static long MeasureUpdateAllocations(AxisMoveOrderSystem system, OrderQueue orders)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                system.Update(0f);
                orders.TryDequeue(out _);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private sealed class Harness
        {
            public World World = null!;
            public Dictionary<string, object> Globals = null!;
            public FrozenInputActionReader Input = null!;
            public OrderQueue Orders = null!;
            public Entity Avatar;
            private OrderTypeRegistry _orderTypes = null!;

            public static Harness Create(World world)
            {
                Entity avatar = world.Create(WorldPositionCm.FromCm(StartXcm, StartYcm));

                var orderTypes = new OrderTypeRegistry();
                orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = MoveToOrderTypeId });

                var input = new FrozenInputActionReader();
                return new Harness
                {
                    World = world,
                    Input = input,
                    Orders = new OrderQueue(),
                    Avatar = avatar,
                    _orderTypes = orderTypes,
                    Globals = new Dictionary<string, object>
                    {
                        [CoreServiceKeys.AuthoritativeInput.Name] = input,
                        [CoreServiceKeys.LocalPlayerId.Name] = 1,
                        [CoreServiceKeys.LocalPlayerEntity.Name] = avatar,
                    },
                };
            }

            public AxisMoveOrderSystem CreateSystem(AxisMoveConfig config)
            {
                return new AxisMoveOrderSystem(World, Globals, config, Orders, _orderTypes);
            }

            public static AxisMoveConfig Config(bool enabled, int throttleTicks = 6, int stepDistanceCm = 400)
            {
                return new AxisMoveConfig
                {
                    Enabled = enabled,
                    ActionId = "Move",
                    OrderTypeKey = "moveTo",
                    ThrottleTicks = throttleTicks,
                    StepDistanceCm = stepDistanceCm,
                };
            }
        }
    }
}
