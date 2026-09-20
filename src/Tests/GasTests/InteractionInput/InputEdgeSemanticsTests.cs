using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Systems;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Button press/release semantics across the two input rhythms: the live handler's
    /// pressed/released-this-frame spans one visual frame, the frozen snapshot's
    /// pressed/released-this-tick folds every visual frame since the previous freeze.
    /// When the pacemaker skips logic ticks between visual frames, only the tick reads
    /// still reach fixed-step consumers.
    /// </summary>
    [TestFixture]
    public sealed class InputEdgeSemanticsTests
    {
        [Test]
        public void TickSnapshot_PressEdgeSurvivesVisualFramesWithoutLogicTick()
        {
            var (backend, handler, _, frameSystem, snapshotSystem, snapshot) = BuildGlobalChain();

            backend.Buttons["<Keyboard>/a"] = true;
            frameSystem.Update(1f / 60f);

            frameSystem.Update(1f / 60f);
            frameSystem.Update(1f / 60f);

            Assert.That(handler.PressedThisFrame("Attack"), Is.False,
                "the live pressed-this-frame has already expired by the time the skipped logic tick runs");
            snapshotSystem.Update(1f / 50f);

            Assert.That(snapshot.PressedThisTick("Attack"), Is.True,
                "the tick snapshot must fold presses from every visual frame since the previous freeze");
            Assert.That(snapshot.IsDown("Attack"), Is.True);
        }

        [Test]
        public void TickSnapshot_PressAndReleaseFoldIntoTheSameSkippedTick()
        {
            var (backend, _, _, frameSystem, snapshotSystem, snapshot) = BuildGlobalChain();

            backend.Buttons["<Keyboard>/a"] = true;
            frameSystem.Update(1f / 60f);
            backend.Buttons["<Keyboard>/a"] = false;
            frameSystem.Update(1f / 60f);
            frameSystem.Update(1f / 60f);
            frameSystem.Update(1f / 60f);

            snapshotSystem.Update(1f / 50f);

            Assert.That(snapshot.PressedThisTick("Attack"), Is.True);
            Assert.That(snapshot.ReleasedThisTick("Attack"), Is.True);
            Assert.That(snapshot.IsDown("Attack"), Is.False);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.True,
                "the IInputActionReader path over the frozen snapshot returns the same pressed-this-tick");

            snapshotSystem.Update(1f / 50f);
            Assert.That(snapshot.PressedThisTick("Attack"), Is.False,
                "pressed-this-tick is consumed by the freeze, not repeated on the next tick");
            Assert.That(snapshot.ReleasedThisTick("Attack"), Is.False);
        }

        [Test]
        public void PerSeatChannelReader_TickEdgeSurvivesVisualFramesWithoutLogicTick()
        {
            using ChannelHarness harness = ChannelHarness.CreateDualSeat();
            ClientLocalSeatInputChannel channelZero = harness.Channel("seat.0");
            ClientLocalSeatInputChannel channelOne = harness.Channel("seat.1");

            channelZero.Handler.InjectButtonPress("CmdA");
            harness.Runtime.UpdateVisualFrame(1f / 60f);
            harness.Runtime.UpdateVisualFrame(1f / 60f);
            harness.Runtime.UpdateVisualFrame(1f / 60f);

            Assert.That(channelZero.Handler.PressedThisFrame("CmdA"), Is.False,
                "the channel handler's pressed-this-frame has expired before the freeze");
            harness.Runtime.FreezeSnapshots(discardLiveInput: false);

            Assert.That(channelZero.Reader.PressedThisTick("CmdA"), Is.True,
                "the seat channel's frozen reader must deliver pressed-this-tick across skipped logic ticks");
            Assert.That(channelOne.Reader.PressedThisTick("CmdA"), Is.False,
                "the other seat's channel keeps its own snapshot; presses never cross seats");
        }

        private static (TestInputBackend backend, PlayerInputHandler handler, Dictionary<string, object> globals, InputRuntimeSystem frameSystem, AuthoritativeInputSnapshotSystem snapshotSystem, FrozenInputActionReader snapshot) BuildGlobalChain()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Attack", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Attack", Path = "<Keyboard>/a", Processors = new() },
                        }
                    }
                }
            };
            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };
            var frameSystem = new InputRuntimeSystem(globals, accumulator);
            var snapshotSystem = new AuthoritativeInputSnapshotSystem(snapshot, accumulator);
            frameSystem.Initialize();
            snapshotSystem.Initialize();

            // Baseline freeze so the first asserted tick starts from a consumed snapshot.
            frameSystem.Update(1f / 60f);
            snapshotSystem.Update(1f / 50f);
            return (backend, handler, globals, frameSystem, snapshotSystem, snapshot);
        }

        private sealed class ChannelHarness : IDisposable
        {
            public ClientLocalSeatInputRuntime Runtime = null!;
            public World World = null!;

            public ClientLocalSeatInputChannel Channel(string seatId)
            {
                Assert.That(Runtime.TryGetChannel(seatId, out ClientLocalSeatInputChannel channel), Is.True,
                    $"seat channel '{seatId}' must exist after publishing the dual-seat table");
                return channel;
            }

            public static ChannelHarness CreateDualSeat()
            {
                var world = World.Create();
                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
                var schemes = new ControlSchemeRuntime(
                    new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                    orderTypes);
                var inputConfig = new InputConfigRoot
                {
                    Actions = new List<InputActionDef>
                    {
                        new() { Id = "CmdA", Type = InputActionType.Button },
                    },
                    Contexts = new List<InputContextDef>
                    {
                        new()
                        {
                            Id = "imc.perseat",
                            Priority = 1,
                            Bindings = new List<InputBindingDef>
                            {
                                new() { ActionId = "CmdA", Path = "<Keyboard>/q", Processors = new() },
                            },
                        },
                    },
                };
                var globals = new Dictionary<string, object>();
                var runtime = new ClientLocalSeatInputRuntime(globals, schemes, inputConfig);
                var seats = new ClientLocalSeatRegistry();
                seats.Add(new ClientLocalSeat("seat.0"));
                seats.Add(new ClientLocalSeat("seat.1"));
                runtime.PublishSeats(seats);

                return new ChannelHarness { Runtime = runtime, World = world };
            }

            public void Dispose()
            {
                World.Destroy(World);
            }
        }

        private sealed class TestInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new Dictionary<string, bool>();
            public Vector2 MousePosition { get; set; }
            public float MouseWheel { get; set; }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var down) && down;
            public Vector2 GetMousePosition() => MousePosition;
            public float GetMouseWheel() => MouseWheel;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
