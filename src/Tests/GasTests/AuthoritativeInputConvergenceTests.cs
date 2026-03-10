using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class AuthoritativeInputConvergenceTests
    {
        [Test]
        public void AuthoritativeInputAccumulator_PreservesEdgesUntilConsumed()
        {
            var (backend, handler) = BuildHandler();
            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();

            handler.Update();
            accumulator.CaptureVisualFrame(handler);
            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.False);
            Assert.That(snapshot.IsDown("Attack"), Is.False);

            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.True);
            Assert.That(snapshot.IsDown("Attack"), Is.True);

            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.False);
            Assert.That(snapshot.IsDown("Attack"), Is.True);

            backend.Buttons["<Keyboard>/a"] = false;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.ReleasedThisFrame("Attack"), Is.True);
            Assert.That(snapshot.IsDown("Attack"), Is.False);
        }

        [Test]
        public void InputOrderMapping_HeldQuickTap_EmitsStartAndEndOnSameLogicTick()
        {
            var (backend, handler) = BuildHandler();
            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Attack",
                        Trigger = InputTriggerType.Held,
                        HeldPolicy = HeldPolicy.StartEnd,
                        OrderTypeKey = "beam",
                        SelectionType = OrderSelectionType.None,
                        RequireSelection = false,
                        IsSkillMapping = false,
                    }
                }
            };

            var system = new InputOrderMappingSystem(snapshot, config);
            var orders = new List<Order>();
            system.SetOrderTypeKeyResolver(key => key switch
            {
                "beam.Start" => 101,
                "beam.End" => 102,
                "beam" => 100,
                _ => 0
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));

            using var world = World.Create();
            system.SetLocalPlayer(world.Create(), 1);

            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            backend.Buttons["<Keyboard>/a"] = false;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            accumulator.BuildTickSnapshot(snapshot);
            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(2));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(101));
            Assert.That(orders[1].OrderTypeId, Is.EqualTo(102));
        }

        [Test]
        public void GasInputResponseSystem_UsesAuthoritativeSnapshotInsteadOfLiveHandler()
        {
            using var world = World.Create();

            var liveInput = BuildHandler().handler;
            var authoritativeInput = new FrozenInputActionReader();
            authoritativeInput.SetActionState("Confirm", Vector3.One, isDown: true, pressedThisFrame: true, releasedThisFrame: false);

            var target = world.Create();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = liveInput,
                [CoreServiceKeys.AuthoritativeInput.Name] = authoritativeInput,
                [CoreServiceKeys.AbilityInputRequestQueue.Name] = new InputRequestQueue(),
                [CoreServiceKeys.InputResponseBuffer.Name] = new InputResponseBuffer(),
                [CoreServiceKeys.SelectedEntity.Name] = target,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };

            var system = new GasInputResponseSystem(world, globals);
            var requests = (InputRequestQueue)globals[CoreServiceKeys.AbilityInputRequestQueue.Name];
            var responses = (InputResponseBuffer)globals[CoreServiceKeys.InputResponseBuffer.Name];
            requests.TryEnqueue(new InputRequest { RequestId = 7, RequestTagId = 700 });

            system.Update(0f);

            Assert.That(responses.TryConsume(7, out var response), Is.True);
            Assert.That(response.Target, Is.EqualTo(target));
            Assert.That(response.ResponseTagId, Is.EqualTo(700));
        }

        private static (TestInputBackend backend, PlayerInputHandler handler) BuildHandler()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Attack", Type = InputActionType.Button },
                    new() { Id = "Confirm", Type = InputActionType.Button },
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
                            new() { ActionId = "Confirm", Path = "<Keyboard>/enter", Processors = new() },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            return (backend, handler);
        }

        private sealed class TestInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new Dictionary<string, bool>();

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var down) && down;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
