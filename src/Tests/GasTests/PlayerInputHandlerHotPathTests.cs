using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Ludots.Core.Input.Automation;
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

        [Test]
        public void InputAutomationBackend_DrivesPointerKeyboardAndWheelThroughPlayerInputHandler()
        {
            var physicalBackend = new StubInputBackend();
            var player = new InputAutomationPlayer(new[]
            {
                new InputAutomationCommand
                {
                    Kind = InputAutomationCommandKind.PointerMove,
                    Frame = 0,
                    DurationFrames = 2,
                    X = 100f,
                    Y = 200f,
                    EndX = 160f,
                    EndY = 230f
                },
                new InputAutomationCommand
                {
                    Kind = InputAutomationCommandKind.KeyStroke,
                    Frame = 1,
                    DurationFrames = 2,
                    Key = "W"
                },
                new InputAutomationCommand
                {
                    Kind = InputAutomationCommandKind.PointerScroll,
                    Frame = 2,
                    X = 160f,
                    Y = 230f,
                    DeltaY = 3f
                }
            });
            var backend = new InputAutomationBackend(physicalBackend, player);
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                    new() { Id = "PointerDelta", Type = InputActionType.Axis2D },
                    new() { Id = "Forward", Type = InputActionType.Button },
                    new() { Id = "Zoom", Type = InputActionType.Axis1D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos" },
                            new() { ActionId = "PointerDelta", Path = "<Mouse>/Delta" },
                            new() { ActionId = "Forward", Path = "<Keyboard>/w" },
                            new() { ActionId = "Zoom", Path = "<Mouse>/ScrollY" },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            backend.AdvanceFrameInput();
            handler.Update();
            Assert.That(handler.ReadAction<Vector2>("PointerPos"), Is.EqualTo(new Vector2(100f, 200f)));
            Assert.That(handler.ReadAction<Vector2>("PointerDelta"), Is.EqualTo(Vector2.Zero));

            backend.AdvanceFrameInput();
            handler.Update();
            Assert.That(handler.ReadAction<Vector2>("PointerPos"), Is.EqualTo(new Vector2(130f, 215f)));
            Assert.That(handler.ReadAction<Vector2>("PointerDelta"), Is.EqualTo(new Vector2(30f, 15f)));
            Assert.That(handler.IsDown("Forward"), Is.True);
            Assert.That(handler.PressedThisFrame("Forward"), Is.True);

            backend.AdvanceFrameInput();
            handler.Update();
            Assert.That(handler.ReadAction<Vector2>("PointerPos"), Is.EqualTo(new Vector2(160f, 230f)));
            Assert.That(handler.ReadAction<float>("Zoom"), Is.EqualTo(3f));
            Assert.That(handler.IsDown("Forward"), Is.True);

            backend.AdvanceFrameInput();
            handler.Update();
            Assert.That(handler.IsDown("Forward"), Is.False);
            Assert.That(handler.ReleasedThisFrame("Forward"), Is.True);
            Assert.That(handler.ReadAction<float>("Zoom"), Is.EqualTo(0f));
        }

        [Test]
        public void InputAutomationBackend_AddsAutomationWithoutSuppressingPhysicalButtons()
        {
            var physicalBackend = new StubInputBackend();
            physicalBackend.Buttons["<Keyboard>/d"] = true;
            physicalBackend.Buttons["<Mouse>/LeftButton"] = true;
            var player = new InputAutomationPlayer(new[]
            {
                new InputAutomationCommand
                {
                    Kind = InputAutomationCommandKind.KeyStroke,
                    Frame = 0,
                    DurationFrames = 1,
                    Key = "W"
                }
            });
            var backend = new InputAutomationBackend(physicalBackend, player);
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Forward", Type = InputActionType.Button },
                    new() { Id = "StrafeRight", Type = InputActionType.Button },
                    new() { Id = "Select", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Forward", Path = "<Keyboard>/w" },
                            new() { ActionId = "StrafeRight", Path = "<Keyboard>/d" },
                            new() { ActionId = "Select", Path = "<Mouse>/LeftButton" },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            backend.AdvanceFrameInput();
            handler.Update();
            Assert.That(handler.IsDown("Forward"), Is.True);
            Assert.That(handler.IsDown("StrafeRight"), Is.True);
            Assert.That(handler.IsDown("Select"), Is.True);

            backend.AdvanceFrameInput();
            handler.Update();
            Assert.That(handler.IsDown("Forward"), Is.False);
            Assert.That(handler.IsDown("StrafeRight"), Is.True);
            Assert.That(handler.IsDown("Select"), Is.True);
        }

        [Test]
        public void InputAutomationPlayer_EmitsHostNeutralPointerKeyboardTextAndScrollEvents()
        {
            var player = new InputAutomationPlayer(new[]
            {
                new InputAutomationCommand
                {
                    Kind = InputAutomationCommandKind.PointerClick,
                    Frame = 0,
                    DurationFrames = 2,
                    X = 10f,
                    Y = 20f,
                    Button = InputAutomationPointerButton.Left,
                    Modifiers = 1
                },
                new InputAutomationCommand
                {
                    Kind = InputAutomationCommandKind.PointerDrag,
                    Frame = 4,
                    DurationFrames = 3,
                    X = 1f,
                    Y = 2f,
                    EndX = 7f,
                    EndY = 8f,
                    Button = InputAutomationPointerButton.Right,
                    Modifiers = 2
                },
                new InputAutomationCommand
                {
                    Kind = InputAutomationCommandKind.Text,
                    Frame = 8,
                    Text = "ab",
                    Modifiers = 4
                },
                new InputAutomationCommand
                {
                    Kind = InputAutomationCommandKind.PointerScroll,
                    Frame = 9,
                    X = 30f,
                    Y = 40f,
                    DeltaY = -120f,
                    Modifiers = 8
                },
                new InputAutomationCommand
                {
                    Kind = InputAutomationCommandKind.KeyStroke,
                    Frame = 10,
                    DurationFrames = 1,
                    Key = "Enter",
                    Modifiers = 16
                }
            });

            player.SetFrame(0);
            Assert.That(player.FrameEvents[0].Kind, Is.EqualTo(InputAutomationFrameEventKind.PointerMove));
            Assert.That(player.FrameEvents[0].Position, Is.EqualTo(new Vector2(10f, 20f)));
            Assert.That(player.FrameEvents[0].Modifiers, Is.EqualTo(1));
            Assert.That(player.FrameEvents[1].Kind, Is.EqualTo(InputAutomationFrameEventKind.PointerDown));
            Assert.That(player.TryGetButton("<Mouse>/LeftButton", out bool leftDown) && leftDown, Is.True);

            player.SetFrame(2);
            Assert.That(player.FrameEvents[0].Kind, Is.EqualTo(InputAutomationFrameEventKind.PointerUp));
            Assert.That(player.TryGetButton("<Mouse>/LeftButton", out leftDown), Is.True);
            Assert.That(leftDown, Is.False);

            player.SetFrame(4);
            Assert.That(player.TryGetButton("<Mouse>/RightButton", out bool rightDown) && rightDown, Is.True);
            Assert.That(player.FrameEvents[^1].Kind, Is.EqualTo(InputAutomationFrameEventKind.PointerDown));
            Assert.That(player.FrameEvents[^1].Modifiers, Is.EqualTo(2));

            player.SetFrame(5);
            Assert.That(player.FrameEvents[0].Kind, Is.EqualTo(InputAutomationFrameEventKind.PointerMove));
            Assert.That(player.FrameEvents[0].Position, Is.EqualTo(new Vector2(3f, 4f)));

            player.SetFrame(7);
            Assert.That(player.FrameEvents[^1].Kind, Is.EqualTo(InputAutomationFrameEventKind.PointerUp));
            Assert.That(player.TryGetButton("<Mouse>/RightButton", out rightDown), Is.True);
            Assert.That(rightDown, Is.False);

            player.SetFrame(8);
            Assert.That(player.ConsumeCharBuffer(), Is.EqualTo("ab"));
            Assert.That(player.FrameEvents[0].Kind, Is.EqualTo(InputAutomationFrameEventKind.Character));
            Assert.That(player.FrameEvents[0].Text, Is.EqualTo("a"));
            Assert.That(player.FrameEvents[0].Modifiers, Is.EqualTo(4));
            Assert.That(player.FrameEvents[1].Text, Is.EqualTo("b"));

            player.SetFrame(9);
            Assert.That(player.MouseWheel, Is.EqualTo(-120f));
            Assert.That(player.FrameEvents[0].Kind, Is.EqualTo(InputAutomationFrameEventKind.PointerScroll));
            Assert.That(player.FrameEvents[0].Delta.Y, Is.EqualTo(-120f));
            Assert.That(player.FrameEvents[0].Modifiers, Is.EqualTo(8));

            player.SetFrame(10);
            Assert.That(player.FrameEvents[0].Kind, Is.EqualTo(InputAutomationFrameEventKind.KeyDown));
            Assert.That(player.FrameEvents[0].Key, Is.EqualTo("Enter"));
            Assert.That(player.FrameEvents[0].Modifiers, Is.EqualTo(16));
            Assert.That(player.TryGetButton("<Keyboard>/enter", out bool enterDown) && enterDown, Is.True);

            player.SetFrame(11);
            Assert.That(player.FrameEvents[0].Kind, Is.EqualTo(InputAutomationFrameEventKind.KeyUp));
            Assert.That(player.TryGetButton("<Keyboard>/enter", out enterDown), Is.True);
            Assert.That(enterDown, Is.False);
        }

        [Test]
        public void InputAutomationScriptLoader_UsesSingleHostNeutralEnvironmentVariable()
        {
            string scriptPath = Path.Combine(
                Path.GetTempPath(),
                "ludots-input-automation-" + Guid.NewGuid().ToString("N") + ".json");
            string? previous = Environment.GetEnvironmentVariable(InputAutomationScriptLoader.ScriptEnvironmentVariable);

            try
            {
                File.WriteAllText(
                    scriptPath,
                    @"{ ""commands"": [ { ""kind"": ""Text"", ""frame"": 3, ""text"": ""ok"" } ] }");
                Environment.SetEnvironmentVariable(InputAutomationScriptLoader.ScriptEnvironmentVariable, scriptPath);

                Assert.That(InputAutomationScriptLoader.TryCreatePlayerFromEnvironment(out var player), Is.True);
                Assert.That(player, Is.Not.Null);

                player!.SetFrame(3);
                Assert.That(player.ConsumeCharBuffer(), Is.EqualTo("ok"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(InputAutomationScriptLoader.ScriptEnvironmentVariable, previous);
                if (File.Exists(scriptPath))
                {
                    File.Delete(scriptPath);
                }
            }
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
