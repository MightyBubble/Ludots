using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Binding Interactions runtime: the same physical key bound to different actions
    /// with different time-sequence judges (Tap / Hold / Drag / MultiTap) produces
    /// different action edges, judged on the visual-frame cadence per the input/command
    /// constitution: Tap = press then release at the same position; Drag = press, move,
    /// release (press implied, never stacked with Hold); a zero-travel press completes
    /// Tap, not Drag; MultiTap completes inside its tap window.
    /// </summary>
    [TestFixture]
    public sealed class BindingInteractionTests
    {
        private const float FrameSeconds = 1f / 60f;

        [Test]
        public void SameKey_TapAndDrag_FireTheirOwnActions()
        {
            var (backend, handler) = Build(TapBinding("TapSelect"), DragBinding("BoxSelect"));

            // Press, drag beyond threshold, release: only BoxSelect fires.
            backend.Buttons["<Mouse>/leftButton"] = true;
            backend.MousePosition = new Vector2(100f, 100f);
            handler.Update(FrameSeconds);

            backend.MousePosition = new Vector2(140f, 100f);
            handler.Update(FrameSeconds);

            Assert.That(handler.PressedThisFrame("TapSelect"), Is.False, "no release yet: no Tap edge");
            Assert.That(handler.PressedThisFrame("BoxSelect"), Is.False, "no release yet: no Drag edge");

            backend.Buttons["<Mouse>/leftButton"] = false;
            handler.Update(FrameSeconds);

            Assert.That(handler.PressedThisFrame("TapSelect"), Is.False,
                "a moved press is a drag, not a tap");
            Assert.That(handler.PressedThisFrame("BoxSelect"), Is.True,
                "press-move-release past the threshold must complete the Drag judge");

            handler.Update(FrameSeconds);
            Assert.That(handler.PressedThisFrame("BoxSelect"), Is.False,
                "the completion is a one-frame pulse, not a held level");
        }

        [Test]
        public void ZeroTravelPress_CompletesTapNotDrag()
        {
            var (backend, handler) = Build(TapBinding("TapSelect"), DragBinding("BoxSelect"));

            backend.Buttons["<Mouse>/leftButton"] = true;
            backend.MousePosition = new Vector2(100f, 100f);
            handler.Update(FrameSeconds);

            backend.MousePosition = new Vector2(102f, 101f);
            handler.Update(FrameSeconds);

            backend.Buttons["<Mouse>/leftButton"] = false;
            handler.Update(FrameSeconds);

            Assert.That(handler.PressedThisFrame("TapSelect"), Is.True,
                "release within the tap slop completes Tap (zero-length drag resolves to tap)");
            Assert.That(handler.PressedThisFrame("BoxSelect"), Is.False,
                "release without reaching the drag threshold never completes Drag");
        }

        [Test]
        public void Tap_DoesNotFireWhileHeldOrOnReleaseWithoutPress()
        {
            var (backend, handler) = Build(TapBinding("TapSelect"));

            backend.Buttons["<Mouse>/leftButton"] = true;
            backend.MousePosition = new Vector2(10f, 10f);
            handler.Update(FrameSeconds);
            handler.Update(FrameSeconds);
            handler.Update(FrameSeconds);

            Assert.That(handler.PressedThisFrame("TapSelect"), Is.False,
                "Tap completes on release, not on the press edge");
            Assert.That(handler.IsDown("TapSelect"), Is.False,
                "an interaction-gated action is not down while its judge is pending");
        }

        [Test]
        public void Hold_FiresAfterDuration_WhileStillHeld()
        {
            var (backend, handler) = Build(new InputBindingDef
            {
                ActionId = "ChargedShot",
                Path = "<Mouse>/leftButton",
                Interactions = new List<InputModifierDef>
                {
                    new() { Type = "Hold", Parameters = { new InputParameterDef { Name = "DurationSeconds", Value = 0.2f } } },
                },
            });

            backend.Buttons["<Mouse>/leftButton"] = true;
            backend.MousePosition = new Vector2(0f, 0f);
            int frames = 0;
            while (frames < 12 && !handler.PressedThisFrame("ChargedShot"))
            {
                handler.Update(FrameSeconds);
                frames++;
            }

            Assert.That(handler.PressedThisFrame("ChargedShot"), Is.True,
                "Hold must complete while the key is still held once the duration elapses");
            Assert.That(frames, Is.EqualTo(12),
                "0.2s at 60fps completes on the 12th held update");

            backend.Buttons["<Mouse>/leftButton"] = false;
            handler.Update(FrameSeconds);
            backend.Buttons["<Mouse>/leftButton"] = true;
            handler.Update(FrameSeconds);
            Assert.That(handler.PressedThisFrame("ChargedShot"), Is.False,
                "a fresh press restarts the hold timer instead of re-firing");
        }

        [Test]
        public void Hold_ReleaseBeforeDuration_FiresNothing()
        {
            var (backend, handler) = Build(new InputBindingDef
            {
                ActionId = "ChargedShot",
                Path = "<Mouse>/leftButton",
                Interactions = new List<InputModifierDef>
                {
                    new() { Type = "Hold", Parameters = { new InputParameterDef { Name = "DurationSeconds", Value = 0.5f } } },
                },
            });

            backend.Buttons["<Mouse>/leftButton"] = true;
            handler.Update(FrameSeconds);
            handler.Update(FrameSeconds);
            backend.Buttons["<Mouse>/leftButton"] = false;
            handler.Update(FrameSeconds);

            Assert.That(handler.PressedThisFrame("ChargedShot"), Is.False,
                "releasing before the duration completes nothing");

            backend.Buttons["<Mouse>/leftButton"] = true;
            handler.Update(FrameSeconds);
            Assert.That(handler.PressedThisFrame("ChargedShot"), Is.False,
                "the released hold does not leak into the next press");
        }

        [Test]
        public void MultiTap_TwoTapsInsideWindow_CompleteOnSecondRelease()
        {
            var (backend, handler) = Build(new InputBindingDef
            {
                ActionId = "DoubleCommand",
                Path = "<Mouse>/leftButton",
                Interactions = new List<InputModifierDef>
                {
                    new() { Type = "MultiTap", Parameters = { new InputParameterDef { Name = "TapCount", Value = 2 }, new InputParameterDef { Name = "TapWindowSeconds", Value = 0.5f } } },
                },
            });

            PressAndRelease(backend, handler, at: new Vector2(30f, 30f), holdFrames: 2);
            Assert.That(handler.PressedThisFrame("DoubleCommand"), Is.False,
                "the first tap alone does not complete a double-tap judge");

            PressAndRelease(backend, handler, at: new Vector2(31f, 30f), holdFrames: 2);
            Assert.That(handler.PressedThisFrame("DoubleCommand"), Is.True,
                "the second release inside the window completes MultiTap");
        }

        [Test]
        public void MultiTap_WindowExpiry_RestartsTheChain()
        {
            var (backend, handler) = Build(new InputBindingDef
            {
                ActionId = "DoubleCommand",
                Path = "<Mouse>/leftButton",
                Interactions = new List<InputModifierDef>
                {
                    new() { Type = "MultiTap", Parameters = { new InputParameterDef { Name = "TapCount", Value = 2 }, new InputParameterDef { Name = "TapWindowSeconds", Value = 0.25f } } },
                },
            });

            PressAndRelease(backend, handler, at: new Vector2(30f, 30f), holdFrames: 2);

            // Idle past the tap window (0.25s at 60fps = 15 idle frames).
            for (int i = 0; i < 20; i++)
            {
                handler.Update(FrameSeconds);
            }

            PressAndRelease(backend, handler, at: new Vector2(31f, 30f), holdFrames: 2);
            Assert.That(handler.PressedThisFrame("DoubleCommand"), Is.False,
                "a tap landing after the window expiry counts as a new first tap, not a completion");
        }

        [Test]
        public void PlainBinding_WithoutInteractions_KeepsPressEdgeSemantics()
        {
            var (backend, handler) = Build(new InputBindingDef { ActionId = "PlainPress", Path = "<Mouse>/leftButton" });

            backend.Buttons["<Mouse>/leftButton"] = true;
            handler.Update(FrameSeconds);

            Assert.That(handler.PressedThisFrame("PlainPress"), Is.True,
                "bindings without interactions keep the raw press edge");
            Assert.That(handler.IsDown("PlainPress"), Is.True);
        }

        [Test]
        public void UnknownInteractionType_FailsClosedAtCompile()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = { new InputActionDef { Id = "Bad" } },
                Contexts =
                {
                    new InputContextDef
                    {
                        Id = "Gameplay",
                        Bindings =
                        {
                            new InputBindingDef
                            {
                                ActionId = "Bad",
                                Path = "<Mouse>/leftButton",
                                Interactions = { new InputModifierDef { Type = "SlowTap" } },
                            },
                        },
                    },
                },
            };

            Assert.That(
                () => new PlayerInputHandler(backend, config),
                Throws.InvalidOperationException.With.Message.Contains("LUDOTS_INPUT_INTERACTION_UNKNOWN"));
        }

        [Test]
        public void InteractionOnAxisSource_FailsClosedAtCompile()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = { new InputActionDef { Id = "Look", Type = InputActionType.Axis2D } },
                Contexts =
                {
                    new InputContextDef
                    {
                        Id = "Gameplay",
                        Bindings =
                        {
                            new InputBindingDef
                            {
                                ActionId = "Look",
                                Path = "<Mouse>/Delta",
                                Interactions = { new InputModifierDef { Type = "Tap" } },
                            },
                        },
                    },
                },
            };

            Assert.That(
                () => new PlayerInputHandler(backend, config),
                Throws.InvalidOperationException.With.Message.Contains("LUDOTS_INPUT_INTERACTION_UNSUPPORTED_SOURCE"));
        }

        private static InputBindingDef TapBinding(string actionId) => new()
        {
            ActionId = actionId,
            Path = "<Mouse>/leftButton",
            Interactions = { new InputModifierDef { Type = "Tap" } },
        };

        private static InputBindingDef DragBinding(string actionId) => new()
        {
            ActionId = actionId,
            Path = "<Mouse>/leftButton",
            Interactions = { new InputModifierDef { Type = "Drag" } },
        };

        private static void PressAndRelease(TestInputBackend backend, PlayerInputHandler handler, Vector2 at, int holdFrames)
        {
            backend.MousePosition = at;
            backend.Buttons["<Mouse>/leftButton"] = true;
            handler.Update(FrameSeconds);
            for (int i = 1; i < holdFrames; i++)
            {
                handler.Update(FrameSeconds);
            }

            backend.Buttons["<Mouse>/leftButton"] = false;
            handler.Update(FrameSeconds);
        }

        private static (TestInputBackend backend, PlayerInputHandler handler) Build(params InputBindingDef[] bindings)
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot { Contexts = { new InputContextDef { Id = "Gameplay", Priority = 1 } } };
            for (int i = 0; i < bindings.Length; i++)
            {
                config.Actions.Add(new InputActionDef { Id = bindings[i].ActionId });
                config.Contexts[0].Bindings.Add(bindings[i]);
            }

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            return (backend, handler);
        }

        private sealed class TestInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new();
            public Vector2 MousePosition { get; set; }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var down) && down;
            public Vector2 GetMousePosition() => MousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
