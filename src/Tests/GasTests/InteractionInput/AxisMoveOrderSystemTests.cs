using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Systems;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 INT-6 (DEC-15): WASD axis intent to throttled move order kernel, driven by the
    /// active control scheme's <c>axisMove</c> declaration. Orders always go through
    /// <see cref="OrderQueue"/> and never write <see cref="WorldPositionCm"/> directly.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class AxisMoveOrderSystemTests
    {
        private const int MoveToOrderTypeId = 2;
        private const int StartXcm = 1000;
        private const int StartYcm = 2000;
        private const string Intent = "intent.test.default";
        private const string AxisScheme = "scheme.test.axis";
        private const string PlainScheme = "scheme.test.plain";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
            ContextGroupIdRegistry.Clear();
        }

        [Test]
        public void Update_UninstalledRuntime_SubmitsNothingEvenWithAxisInput()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            var system = harness.CreateSystem();
            harness.Input.SetActionValue("Move", new Vector3(1f, 0f, 0f));

            for (int i = 0; i < 10; i++)
            {
                system.Update(0f);
            }

            Assert.That(harness.Orders.Count, Is.EqualTo(0), "before scheme install there is no active axisMove declaration.");
        }

        [Test]
        public void Update_ActiveSchemeWithoutAxisMove_SubmitsNothingEvenWithAxisInput()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallSchemes();
            harness.Switch(PlainScheme);
            var system = harness.CreateSystem();
            harness.Input.SetActionValue("Move", new Vector3(1f, 0f, 0f));

            for (int i = 0; i < 10; i++)
            {
                system.Update(0f);
            }

            Assert.That(harness.Orders.Count, Is.EqualTo(0), "a scheme without axisMove is topology, not fallback.");
        }

        [Test]
        public void Update_ZeroAxis_SubmitsNothing()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallSchemes();
            harness.Switch(AxisScheme);
            var system = harness.CreateSystem();

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
            harness.InstallSchemes(axisThrottleTicks: 6, axisStepDistanceCm: 400);
            harness.Switch(AxisScheme);
            var system = harness.CreateSystem();
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
            Assert.That(first.Args.Spatial.WorldCm.Z, Is.EqualTo(0f).Within(0.01f));

            Assert.That(harness.Orders.TryDequeue(out Order second), Is.True);
            Assert.That(second.Args.Spatial.WorldCm, Is.EqualTo(first.Args.Spatial.WorldCm));
        }

        [Test]
        public void Update_AxisRelease_RearmsThrottleForImmediateResubmit()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallSchemes(axisThrottleTicks: 6);
            harness.Switch(AxisScheme);
            var system = harness.CreateSystem();

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
        public void Update_HotSwitch_StopsAndResumesOrderFlow()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallSchemes(axisThrottleTicks: 2);
            harness.Switch(AxisScheme);
            var system = harness.CreateSystem();
            harness.Input.SetActionValue("Move", new Vector3(1f, 0f, 0f));

            system.Update(0f);
            Assert.That(harness.Orders.Count, Is.EqualTo(1), "the declared scheme submits while the axis is held.");

            harness.Switch(PlainScheme);
            for (int i = 0; i < 10; i++)
            {
                system.Update(0f);
            }

            Assert.That(harness.Orders.Count, Is.EqualTo(1), "a scheme without axisMove submits nothing after the hot switch.");

            harness.Switch(AxisScheme);
            system.Update(0f);
            Assert.That(harness.Orders.Count, Is.EqualTo(2), "switching back to the declared scheme resumes order flow.");
        }

        [Test]
        public void Install_UnknownOrderTypeKey_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);

            Assert.Throws<InvalidOperationException>(
                () => harness.Schemes.Install(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition>
                    {
                        Harness.Scheme("scheme.test.bad", new ControlSchemeAxisMove
                        {
                            ActionId = "Move",
                            OrderTypeKey = "orders.test.unknown",
                            ThrottleTicks = 6,
                            StepDistanceCm = 400,
                        }),
                    },
                }));
        }

        [Test]
        public void Update_DeclaredScheme_MissingAuthoritativeInputService_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallSchemes();
            harness.Switch(AxisScheme);
            var system = harness.CreateSystem();
            harness.Globals.Remove(CoreServiceKeys.AuthoritativeInput.Name);

            Assert.Throws<InvalidOperationException>(
                () => system.Update(0f),
                "a declared axis move without the authoritative input snapshot is a wiring error.");

            harness.Switch(PlainScheme);
            Assert.DoesNotThrow(() => system.Update(0f), "a scheme without the declaration keeps zero work per tick.");
        }

        [Test]
        public void Update_MissingLocalPlayerOrPosition_SubmitsNothing()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallSchemes(axisThrottleTicks: 1);
            harness.Switch(AxisScheme);
            var system = harness.CreateSystem();
            harness.Input.SetActionValue("Move", new Vector3(1f, 0f, 0f));

            harness.Globals.Remove(CoreServiceKeys.LocalPlayerEntity.Name);
            system.Update(0f);
            Assert.That(harness.Orders.Count, Is.EqualTo(0), "no resolved local player entity: nothing to move.");

            Entity positionless = harness.World.Create();
            harness.Globals[CoreServiceKeys.LocalPlayerEntity.Name] = positionless;
            system.Update(0f);
            Assert.That(harness.Orders.Count, Is.EqualTo(0), "a rep without WorldPositionCm has no movable anchor.");
        }

        [Test]
        public void Update_SteadyState_IsAllocationFree()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.InstallSchemes(axisThrottleTicks: 4);
            harness.Switch(AxisScheme);
            var system = harness.CreateSystem();
            harness.Input.SetActionValue("Move", new Vector3(1f, 1f, 0f));

            for (int i = 0; i < 64; i++)
            {
                RunLogicStep(system, harness.AdmissionResults, harness.Orders);
            }

            long allocated = MeasureUpdateAllocations(system, harness.Orders, harness.AdmissionResults);
            allocated = Math.Min(allocated, MeasureUpdateAllocations(system, harness.Orders, harness.AdmissionResults));
            Assert.That(allocated, Is.EqualTo(0), "Steady-state axis move ticks must be allocation free.");
        }

        private static long MeasureUpdateAllocations(
            AxisMoveOrderSystem system,
            OrderQueue orders,
            OrderAdmissionResultBuffer admissionResults)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                RunLogicStep(system, admissionResults, orders);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static void RunLogicStep(
            AxisMoveOrderSystem system,
            OrderAdmissionResultBuffer admissionResults,
            OrderQueue? orders = null)
        {
            admissionResults.BeginLogicStep();
            system.Update(0f);
            if (orders != null && orders.TryDequeue(out Order order))
            {
                var outcome = new OrderAdmissionOutcome(
                    order.OrderId,
                    order.OrderTypeId,
                    OrderAdmissionStage.EntityIntake,
                    OrderSubmitResult.Activated);
                if (!admissionResults.TryWrite(in outcome))
                {
                    throw new InvalidOperationException(
                        $"Axis move test failed to write EntityIntake for orderId={order.OrderId}.");
                }
            }

            admissionResults.EndEntityIntake();
            admissionResults.EndLogicStep();
        }

        private sealed class Harness
        {
            public World World = null!;
            public Dictionary<string, object> Globals = null!;
            public FrozenInputActionReader Input = null!;
            public OrderQueue Orders = null!;
            public OrderAdmissionResultBuffer AdmissionResults = null!;
            public ControlSchemeRuntime Schemes = null!;
            public Entity Avatar;
            private StringIntRegistry _schemeIds = null!;
            private const string DispatchProfileId = "dispatch.test.axis";

            public static Harness Create(World world)
            {
                Entity avatar = world.Create(WorldPositionCm.FromCm(StartXcm, StartYcm));

                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                orderTypes.Register(new OrderTypeConfig { Key = "moveTo", OrderTypeId = MoveToOrderTypeId });

                CommandIntentProfileTests.Harness intents = CommandIntentProfileTests.Harness.Create(world);
                intents.Intents.Install(CommandIntentProfileTests.Harness.Config(new CommandIntentProfileDefinition
                {
                    Id = Intent,
                    GroupPolicy = new CommandIntentGroupPolicyDefinition { Kind = "independent" },
                    Rules = new List<CommandIntentRuleDefinition>
                    {
                        CommandIntentProfileTests.Harness.GroundRule(priority: 10, orderTypeKey: "moveTo"),
                    },
                }));

                var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var schemeIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var dispatch = new CastDispatchProfileRegistry(
                    new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                    new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
                dispatch.Install(CastDispatchProfileTests.Harness.Config(new CastDispatchProfileDefinition
                {
                    Id = DispatchProfileId,
                    Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                    Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
                }));
                var schemes = new ControlSchemeRuntime(
                    schemeIds,
                    new InteractionContextStack(collectionKeys),
                    intents.Intents,
                    dispatch,
                    orderTypes);

                var input = new FrozenInputActionReader();
                var admissionResults = new OrderAdmissionResultBuffer(64, 64);
                return new Harness
                {
                    World = world,
                    Input = input,
                    Orders = new OrderQueue(64, admissionResults),
                    AdmissionResults = admissionResults,
                    Schemes = schemes,
                    Avatar = avatar,
                    _schemeIds = schemeIds,
                    Globals = new Dictionary<string, object>
                    {
                        [CoreServiceKeys.AuthoritativeInput.Name] = input,
                        [CoreServiceKeys.LocalPlayerId.Name] = 1,
                        [CoreServiceKeys.LocalPlayerEntity.Name] = avatar,
                    },
                };
            }

            public AxisMoveOrderSystem CreateSystem()
            {
                return new AxisMoveOrderSystem(World, Globals, Schemes, Orders);
            }

            public void InstallSchemes(int axisThrottleTicks = 6, int axisStepDistanceCm = 400)
            {
                Schemes.Install(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition>
                    {
                        Scheme(AxisScheme, new ControlSchemeAxisMove
                        {
                            ActionId = "Move",
                            OrderTypeKey = "moveTo",
                            ThrottleTicks = axisThrottleTicks,
                            StepDistanceCm = axisStepDistanceCm,
                        }),
                        Scheme(PlainScheme, axisMove: null),
                    },
                });
            }

            public void Switch(string schemeId)
            {
                Assert.That(Schemes.TrySwitch(_schemeIds.GetId(schemeId)), Is.True, $"switch to '{schemeId}' must succeed.");
            }

            public static ControlSchemeDefinition Scheme(string id, ControlSchemeAxisMove axisMove)
            {
                return new ControlSchemeDefinition
                {
                    Id = id,
                    InputContexts = new List<string>(),
                    Defaults = new ControlSchemeDefaults
                    {
                        CommandIntentId = Intent,
                        CastDispatchProfileId = DispatchProfileId,
                    },
                    AxisMove = axisMove,
                };
            }
        }
    }
}
