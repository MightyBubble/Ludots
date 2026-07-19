using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class PlayerInputHandlerHotPathTests
    {
        [Test]
        public void PlayerInputHandler_CompiledCompositeAndProcessors_PreserveBehavior()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Move", Type = InputActionType.Axis2D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new()
                            {
                                ActionId = "Move",
                                CompositeType = "Vector2",
                                CompositeParts = new List<InputBindingDef>
                                {
                                    new() { Path = "<Keyboard>/w" },
                                    new() { Path = "<Keyboard>/s" },
                                    new() { Path = "<Keyboard>/a" },
                                    new() { Path = "<Keyboard>/d" },
                                },
                                Processors = new List<InputModifierDef>
                                {
                                    new() { Type = "Normalize" },
                                    new() { Type = "Scale", Parameters = new List<InputParameterDef> { new() { Name = "Factor", Value = 2f } } },
                                }
                            }
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            backend.Buttons["<Keyboard>/w"] = true;
            backend.Buttons["<Keyboard>/d"] = true;

            handler.Update();
            var move = handler.ReadAction<Vector2>("Move");

            Assert.That(move.X, Is.EqualTo(1.4142135f).Within(0.01f));
            Assert.That(move.Y, Is.EqualTo(1.4142135f).Within(0.01f));
        }

        [Test]
        public void PlayerInputHandler_MouseDeltaAndInvertProcessor_ProduceCameraLookSemantics()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "PointerDelta", Type = InputActionType.Axis2D },
                    new() { Id = "Look", Type = InputActionType.Axis2D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Camera",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "PointerDelta", Path = "<Mouse>/Delta" },
                            new()
                            {
                                ActionId = "Look",
                                Path = "<Mouse>/Delta",
                                Processors = new List<InputModifierDef>
                                {
                                    new() { Type = "Invert", Parameters = new List<InputParameterDef> { new() { Name = "Y", Value = 1f } } },
                                }
                            }
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Camera");

            backend.MousePosition = new Vector2(320f, 240f);
            handler.Update();

            backend.MousePosition = new Vector2(356f, 216f);
            handler.Update();

            var pointerDelta = handler.ReadAction<Vector2>("PointerDelta");
            var look = handler.ReadAction<Vector2>("Look");

            Assert.That(pointerDelta.X, Is.EqualTo(36f).Within(0.01f));
            Assert.That(pointerDelta.Y, Is.EqualTo(-24f).Within(0.01f));
            Assert.That(look.X, Is.EqualTo(36f).Within(0.01f));
            Assert.That(look.Y, Is.EqualTo(24f).Within(0.01f));
        }

        [Test]
        public void PlayerInputHandler_ButtonChordComposite_RequiresAllParts()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "RunicAttack", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new()
                            {
                                ActionId = "RunicAttack",
                                CompositeType = "ButtonChord",
                                CompositeParts = new List<InputBindingDef>
                                {
                                    new() { Path = "<Keyboard>/q" },
                                    new() { Path = "<Keyboard>/e" },
                                }
                            }
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            backend.Buttons["<Keyboard>/q"] = true;
            handler.Update();
            Assert.That(handler.IsDown("RunicAttack"), Is.False);

            backend.Buttons["<Keyboard>/e"] = true;
            handler.Update();
            Assert.That(handler.IsDown("RunicAttack"), Is.True);
            Assert.That(handler.PressedThisFrame("RunicAttack"), Is.True);
        }

        [Test]
        public void PlayerInputHandler_UnavailableMouseFrame_ResetsPointerWithoutDeltaSpike()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                    new() { Id = "PointerDelta", Type = InputActionType.Axis2D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Camera",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos" },
                            new() { ActionId = "PointerDelta", Path = "<Mouse>/Delta" },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Camera");

            backend.MousePosition = new Vector2(320f, 240f);
            handler.Update();

            backend.HasMousePosition = false;
            handler.Update();

            Assert.That(handler.ReadAction<Vector2>("PointerPos"), Is.EqualTo(new Vector2(-1f, -1f)));
            Assert.That(handler.ReadAction<Vector2>("PointerDelta"), Is.EqualTo(Vector2.Zero));

            backend.HasMousePosition = true;
            backend.MousePosition = new Vector2(680f, 420f);
            handler.Update();

            Assert.That(handler.ReadAction<Vector2>("PointerPos"), Is.EqualTo(new Vector2(680f, 420f)));
            Assert.That(handler.ReadAction<Vector2>("PointerDelta"), Is.EqualTo(Vector2.Zero));
        }

        [Test]
        public void PlayerInputHandler_DuplicateBindingAcrossActiveContexts_ContributesOnce()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                    new() { Id = "Zoom", Type = InputActionType.Axis1D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 0,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos" },
                            new() { ActionId = "Zoom", Path = "<Mouse>/ScrollY" },
                        }
                    },
                    new()
                    {
                        Id = "MapScoped",
                        Priority = 100,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos" },
                            new() { ActionId = "Zoom", Path = "<Mouse>/ScrollY" },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            handler.PushContext("MapScoped");

            backend.MousePosition = new Vector2(640f, 360f);
            backend.MouseWheel = 2f;
            handler.Update();

            Assert.That(handler.ReadAction<Vector2>("PointerPos"), Is.EqualTo(new Vector2(640f, 360f)));
            Assert.That(handler.ReadAction<float>("Zoom"), Is.EqualTo(2f));
        }

        private sealed class StubInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new();
            public Vector2 MousePosition { get; set; }
            public float MouseWheel { get; set; }
            public bool HasMousePosition { get; set; } = true;

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var down) && down;
            public Vector2 GetMousePosition() => HasMousePosition ? MousePosition : new Vector2(float.NaN, float.NaN);
            public float GetMouseWheel() => MouseWheel;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
