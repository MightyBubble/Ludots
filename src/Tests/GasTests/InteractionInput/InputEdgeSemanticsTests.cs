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
using Ludots.Core.Persistence;
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

        [Test]
        public void BindingInt_SecondSlotWhileFirstHeld_FoldsBothPressesIntoTheTick()
        {
            var backend = new TestInputBackend();
            PlayerInputHandler handler = SlotHandler(backend);
            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();

            backend.Buttons["<Keyboard>/q"] = true;
            handler.Update(1f / 60f);
            handler.CaptureFrame(accumulator);
            backend.Buttons["<Keyboard>/w"] = true;
            handler.Update(1f / 60f);
            handler.CaptureFrame(accumulator);
            accumulator.BuildTickSnapshot(snapshot);

            var fires = new List<InputBindingIntFire>();
            snapshot.CopyBindingIntFires("Aim.Slot", fires);
            Assert.That(fires, Is.EqualTo(new[]
            {
                new InputBindingIntFire(0, Pressed: true, Released: false),
                new InputBindingIntFire(1, Pressed: true, Released: false),
            }));
            Assert.That(snapshot.PressedThisTick("Aim.Slot"), Is.True);

            backend.Buttons["<Keyboard>/w"] = false;
            handler.Update(1f / 60f);
            handler.CaptureFrame(accumulator);
            accumulator.BuildTickSnapshot(snapshot);
            snapshot.CopyBindingIntFires("Aim.Slot", fires);
            Assert.That(fires, Is.EqualTo(new[]
            {
                new InputBindingIntFire(1, Pressed: false, Released: true),
            }));
            Assert.That(snapshot.ReleasedThisTick("Aim.Slot"), Is.False,
                "the summed action stays down while another slot key is held");
        }

        [Test]
        public void BindingInt_Injection_PressThenReleaseOnTheFollowingUpdate()
        {
            PlayerInputHandler handler = SlotHandler(new TestInputBackend());
            handler.InjectButtonPress("Aim.Slot", 5);
            handler.Update(1f / 60f);
            var fires = new List<InputBindingIntFire>();
            handler.CopyBindingIntFires("Aim.Slot", fires);
            Assert.That(fires, Is.EqualTo(new[]
            {
                new InputBindingIntFire(5, Pressed: true, Released: false),
            }));

            handler.Update(1f / 60f);
            handler.CopyBindingIntFires("Aim.Slot", fires);
            Assert.That(fires, Is.EqualTo(new[]
            {
                new InputBindingIntFire(5, Pressed: false, Released: true),
            }));
            Assert.That(handler.ReleasedThisFrame("Aim.Slot"), Is.True);
        }

        [Test]
        public void BindingInt_MixedWithPlainBinding_FailsClosed()
        {
            var config = SlotConfig();
            config.Contexts[0].Bindings.Add(new InputBindingDef
            {
                ActionId = "Aim.Slot",
                Path = "<Keyboard>/t",
            });

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => InputConfigPipelineLoader.Validate(config, "slots.json", requireDeclaredActionForBindingInt: true))!;
            Assert.That(ex.Message, Does.Contain("LUDOTS_INPUT_BINDING_INT_MIXED"));
            Assert.That(ex.Message, Does.Contain("Aim.Slot"));
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
                    orderTypes,
                    new ClientControlSchemePreferenceStore(Path.Combine(Path.GetTempPath(), $"ludots_scheme_{Guid.NewGuid():N}.json")));
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
                schemes.Install(new ControlSchemesConfig
                {
                    Schemes = new List<ControlSchemeDefinition>
                    {
                        new()
                        {
                            Id = "scheme.perseat",
                            InputContexts = new List<string> { "imc.perseat" },
                        },
                    },
                });
                var runtime = new ClientLocalSeatInputRuntime(globals, schemes, inputConfig);
                var seats = new ClientLocalSeatRegistry();
                seats.Add(new ClientLocalSeat("seat.0", "scheme.perseat"));
                seats.Add(new ClientLocalSeat("seat.1", "scheme.perseat"));
                runtime.PublishSeats(seats);

                return new ChannelHarness { Runtime = runtime, World = world };
            }

            public void Dispose()
            {
                World.Destroy(World);
            }
        }

        private static InputConfigRoot SlotConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Aim.Slot", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Aim.Kbm",
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Aim.Slot", Path = "<Keyboard>/q", IntValue = 0 },
                            new() { ActionId = "Aim.Slot", Path = "<Keyboard>/w", IntValue = 1 },
                        },
                    },
                },
            };
        }

        private static PlayerInputHandler SlotHandler(TestInputBackend backend)
        {
            var handler = new PlayerInputHandler(backend, SlotConfig());
            handler.PushContext("Aim.Kbm");
            return handler;
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
