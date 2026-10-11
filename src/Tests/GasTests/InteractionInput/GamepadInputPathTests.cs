using System.Numerics;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class GamepadInputPathTests
    {
        [Test]
        public void Parse_KnownControls_MapSlotKindAndAxis()
        {
            GamepadControlPath south = GamepadControlPath.Parse("<Gamepad>/buttonSouth");
            Assert.That(south.Pad, Is.EqualTo(0));
            Assert.That(south.Kind, Is.EqualTo(GamepadControlKind.Button));
            Assert.That(south.Button, Is.EqualTo(GamepadButtonControl.RightFaceDown));

            GamepadControlPath stick = GamepadControlPath.Parse("<Gamepad>/leftStick");
            Assert.That(stick.Kind, Is.EqualTo(GamepadControlKind.Stick));
            Assert.That(stick.Axis, Is.EqualTo(GamepadAxisControl.LeftX));
            Assert.That(stick.AxisY, Is.EqualTo(GamepadAxisControl.LeftY));

            GamepadControlPath stickX = GamepadControlPath.Parse("<Gamepad>/leftStick/x");
            Assert.That(stickX.Kind, Is.EqualTo(GamepadControlKind.StickAxis));
            Assert.That(stickX.Axis, Is.EqualTo(GamepadAxisControl.LeftX));

            GamepadControlPath trigger = GamepadControlPath.Parse("<Gamepad>/rightTrigger");
            Assert.That(trigger.Kind, Is.EqualTo(GamepadControlKind.Trigger));
            Assert.That(trigger.Axis, Is.EqualTo(GamepadAxisControl.RightTrigger));
            Assert.That(trigger.Button, Is.EqualTo(GamepadButtonControl.RightTrigger));

            GamepadControlPath secondPad = GamepadControlPath.Parse("<Gamepad2>/buttonSouth");
            Assert.That(secondPad.Pad, Is.EqualTo(1));
            Assert.That(secondPad.Button, Is.EqualTo(GamepadButtonControl.RightFaceDown));

            GamepadControlPath primaryAlias = GamepadControlPath.Parse("<Gamepad1>/dpad/up");
            GamepadControlPath faceAlias = GamepadControlPath.Parse("<gamepad>/leftFaceUp");
            Assert.That(primaryAlias.Pad, Is.EqualTo(0));
            Assert.That(primaryAlias.Button, Is.EqualTo(GamepadButtonControl.LeftFaceUp));
            Assert.That(faceAlias.Button, Is.EqualTo(primaryAlias.Button));

            Assert.That(GamepadControlPath.Parse("<Gamepad4>/leftStick").Pad, Is.EqualTo(3));
            Assert.That(GamepadControlPath.TryParse("<Keyboard>/w", out _), Is.False);
            Assert.That(Enum.GetValues<GamepadButtonControl>().Length, Is.EqualTo(GamepadControlPath.ButtonControlCount));
            Assert.That(Enum.GetValues<GamepadAxisControl>().Length, Is.EqualTo(GamepadControlPath.AxisControlCount));
        }

        [Test]
        public void Parse_UnknownControlOrSlot_FailsFastNamingThePath()
        {
            AssertThrowsPath("<Gamepad>/notARealButton", "LUDOTS_INPUT_GAMEPAD_CONTROL_UNKNOWN");
            AssertThrowsPath("notARealButton", "LUDOTS_INPUT_GAMEPAD_CONTROL_UNKNOWN");
            AssertThrowsPath("<Gamepad5>/buttonSouth", "LUDOTS_INPUT_GAMEPAD_INDEX_INVALID");
            AssertThrowsPath("<Gamepad>/", "LUDOTS_INPUT_GAMEPAD_PATH_INVALID");
            Assert.Throws<ArgumentException>(() => GamepadControlPath.Parse("  "));
        }

        [Test]
        public void SyntheticStick_ReachesAxis2D_ThroughDeadzoneAndInvert()
        {
            var device = new SyntheticInputDevice();
            PlayerInputHandler handler = CreateHandler(
                device,
                new InputActionDef { Id = "Aim.Stick", Type = InputActionType.Axis2D },
                new InputBindingDef
                {
                    ActionId = "Aim.Stick",
                    Path = "<Gamepad>/leftStick",
                    Processors = new List<InputModifierDef>
                    {
                        new()
                        {
                            Type = "Deadzone",
                            Parameters = new List<InputParameterDef> { new() { Name = "Min", Value = 0.25f } },
                        },
                        new()
                        {
                            Type = "Invert",
                            Parameters = new List<InputParameterDef> { new() { Name = "Y", Value = 1f } },
                        },
                    },
                });

            device.SetGamepadAxis("LeftStick/X", 0.1f);
            device.SetGamepadAxis("LeftStick/Y", 0.1f);
            device.AdvanceFrame();
            handler.Update(1f / 60f);
            Assert.That(handler.ReadAction<Vector2>("Aim.Stick"), Is.EqualTo(Vector2.Zero));

            device.SetGamepadAxis("<Gamepad>/leftStick/x", 0.6f);
            device.SetGamepadAxis("left_stick/y", 0.8f);
            device.AdvanceFrame();
            handler.Update(1f / 60f);
            Vector2 aim = handler.ReadAction<Vector2>("Aim.Stick");
            Assert.That(aim.X, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(aim.Y, Is.EqualTo(-0.8f).Within(0.0001f));
        }

        [Test]
        public void SyntheticTrigger_ReachesAxis1D_AndStickComponentStaysOneAxis()
        {
            var device = new SyntheticInputDevice();
            PlayerInputHandler handler = CreateHandler(
                device,
                new InputActionDef { Id = "Aim.Trigger", Type = InputActionType.Axis1D },
                new InputBindingDef { ActionId = "Aim.Trigger", Path = "<Gamepad>/rightTrigger" },
                new InputActionDef { Id = "Aim.Yaw", Type = InputActionType.Axis1D },
                new InputBindingDef { ActionId = "Aim.Yaw", Path = "<Gamepad>/rightStick/x" });

            device.SetGamepadAxis("RightTrigger", 0.75f);
            device.SetGamepadAxis("<Gamepad2>/rightStick/x", 0.4f);
            device.SetGamepadAxis("RightStick/X", -0.2f);
            device.AdvanceFrame();
            handler.Update(1f / 60f);

            Assert.That(handler.ReadAction<float>("Aim.Trigger"), Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(handler.ReadAction<float>("Aim.Yaw"), Is.EqualTo(-0.2f).Within(0.0001f));
            Assert.That(device.GetGamepadAxis("<Gamepad2>/rightStick/x"), Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(device.IsGamepadButtonDown("RightTrigger"), Is.False, "an analog trigger write is not a digital button");
        }

        [Test]
        public void SyntheticButtons_ReportPressAndReleaseEdges()
        {
            var device = new SyntheticInputDevice();
            PlayerInputHandler handler = CreateHandler(
                device,
                new InputActionDef { Id = "Aim.Confirm", Type = InputActionType.Button },
                new InputBindingDef { ActionId = "Aim.Confirm", Path = "<Gamepad>/buttonSouth" });

            device.GamepadButtonDown("ButtonSouth");
            device.AdvanceFrame();
            handler.Update(1f / 60f);
            Assert.That(handler.IsDown("Aim.Confirm"), Is.True);
            Assert.That(handler.PressedThisFrame("Aim.Confirm"), Is.True);
            Assert.That(handler.ReleasedThisFrame("Aim.Confirm"), Is.False);

            handler.Update(1f / 60f);
            Assert.That(handler.IsDown("Aim.Confirm"), Is.True);
            Assert.That(handler.PressedThisFrame("Aim.Confirm"), Is.False);

            device.GamepadButtonUp("a");
            device.AdvanceFrame();
            handler.Update(1f / 60f);
            Assert.That(handler.IsDown("Aim.Confirm"), Is.False);
            Assert.That(handler.ReleasedThisFrame("Aim.Confirm"), Is.True);
        }

        [Test]
        public void ReleaseAll_ZeroesGamepadButtonsSticksAndTriggers()
        {
            var device = new SyntheticInputDevice();
            PlayerInputHandler handler = CreateHandler(
                device,
                new InputActionDef { Id = "Aim.Stick", Type = InputActionType.Axis2D },
                new InputBindingDef { ActionId = "Aim.Stick", Path = "<Gamepad>/rightStick" },
                new InputActionDef { Id = "Aim.Trigger", Type = InputActionType.Axis1D },
                new InputBindingDef { ActionId = "Aim.Trigger", Path = "<Gamepad>/leftTrigger" },
                new InputActionDef { Id = "Aim.Confirm", Type = InputActionType.Button },
                new InputBindingDef { ActionId = "Aim.Confirm", Path = "<Gamepad>/start" });

            device.SetGamepadAxis("RightStick/X", 1f);
            device.SetGamepadAxis("RightStick/Y", -1f);
            device.SetGamepadAxis("LT", 1f);
            device.GamepadButtonDown("<Gamepad>/start");
            device.AdvanceFrame();
            handler.Update(1f / 60f);
            Assert.That(handler.ReadAction<Vector2>("Aim.Stick"), Is.EqualTo(new Vector2(1f, -1f)));
            Assert.That(handler.ReadAction<float>("Aim.Trigger"), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(handler.IsDown("Aim.Confirm"), Is.True);

            device.SetGamepadAxis("LeftStick/X", 0f);
            device.AdvanceFrame();
            GamepadControlPath centered = GamepadControlPath.Parse("<Gamepad>/leftStick/x");
            Assert.That(device.TryGetGamepadAxis(centered, out float centeredValue), Is.True);
            Assert.That(centeredValue, Is.EqualTo(0f));

            device.ReleaseAll();
            device.AdvanceFrame();
            handler.Update(1f / 60f);

            Assert.That(device.TryGetGamepadAxis(centered, out _), Is.False);
            Assert.That(handler.ReadAction<Vector2>("Aim.Stick"), Is.EqualTo(Vector2.Zero));
            Assert.That(handler.ReadAction<float>("Aim.Trigger"), Is.EqualTo(0f));
            Assert.That(handler.IsDown("Aim.Confirm"), Is.False);
            Assert.That(handler.ReleasedThisFrame("Aim.Confirm"), Is.True);
        }

        [Test]
        public void UnknownBinding_FailsWhenTheHandlerIsBuilt()
        {
            var device = new SyntheticInputDevice();
            var config = Config(
                new InputActionDef { Id = "Aim.Confirm", Type = InputActionType.Button },
                new InputBindingDef { ActionId = "Aim.Confirm", Path = "<Gamepad>/notARealButton" });

            Assert.That(
                () => new PlayerInputHandler(new DeviceBackend(device), config),
                Throws.InvalidOperationException.With.Message.Contains("LUDOTS_INPUT_GAMEPAD_CONTROL_UNKNOWN")
                    .And.Message.Contains("<Gamepad>/notARealButton"));
            Assert.Throws<InvalidOperationException>(() => device.GamepadButtonDown("leftStick"));
            Assert.Throws<InvalidOperationException>(() => device.SetGamepadAxis("buttonSouth", 1f));
        }

        [Test]
        [NonParallelizable]
        public void PollingKnownGamepadPaths_DoesNotAllocate()
        {
            const string stickX = "<Gamepad>/leftStick/x";
            const string button = "<Gamepad>/buttonSouth";
            GamepadControlPath.Parse(stickX);
            var device = new SyntheticInputDevice();
            device.SetGamepadAxis(stickX, 0.5f);
            device.GamepadButtonDown(button);
            device.AdvanceFrame();
            PlayerInputHandler handler = CreateHandler(
                device,
                new InputActionDef { Id = "Aim.Stick", Type = InputActionType.Axis2D },
                new InputBindingDef { ActionId = "Aim.Stick", Path = "<Gamepad>/leftStick" },
                new InputActionDef { Id = "Aim.Confirm", Type = InputActionType.Button },
                new InputBindingDef { ActionId = "Aim.Confirm", Path = button });

            for (int i = 0; i < 32; i++)
            {
                GamepadControlPath.Parse(stickX);
                device.GetGamepadAxis(stickX);
                handler.Update(1f / 60f);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                GamepadControlPath.Parse(stickX);
                GamepadControlPath.Parse(button);
                device.GetGamepadAxis(stickX);
                device.IsGamepadButtonDown(button);
                handler.Update(1f / 60f);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
        }

        private static void AssertThrowsPath(string path, string code)
        {
            var error = Assert.Throws<InvalidOperationException>(() => GamepadControlPath.Parse(path));
            Assert.That(error!.Message, Does.Contain(code));
            Assert.That(error.Message, Does.Contain(path));
        }

        private static PlayerInputHandler CreateHandler(SyntheticInputDevice device, params object[] actionsThenBindings)
        {
            var actions = new List<InputActionDef>();
            var bindings = new List<InputBindingDef>();
            foreach (object item in actionsThenBindings)
            {
                switch (item)
                {
                    case InputActionDef action:
                        actions.Add(action);
                        break;
                    case InputBindingDef binding:
                        bindings.Add(binding);
                        break;
                    default:
                        throw new ArgumentException($"Unexpected {item.GetType().Name}.");
                }
            }

            var handler = new PlayerInputHandler(new DeviceBackend(device), Config(actions, bindings));
            handler.PushContext("Gameplay");
            return handler;
        }

        private static InputConfigRoot Config(params object[] actionsThenBindings)
        {
            var actions = new List<InputActionDef>();
            var bindings = new List<InputBindingDef>();
            foreach (object item in actionsThenBindings)
            {
                switch (item)
                {
                    case InputActionDef action:
                        actions.Add(action);
                        break;
                    case InputBindingDef binding:
                        bindings.Add(binding);
                        break;
                }
            }

            return Config(actions, bindings);
        }

        private static InputConfigRoot Config(List<InputActionDef> actions, List<InputBindingDef> bindings) =>
            new()
            {
                Actions = actions,
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = bindings,
                    },
                },
            };

        private sealed class DeviceBackend : IInputBackend
        {
            private readonly SyntheticInputDevice _device;

            public DeviceBackend(SyntheticInputDevice device) => _device = device;

            public float GetAxis(string devicePath)
            {
                if (!GamepadControlPath.TryParse(devicePath, out GamepadControlPath parsed))
                {
                    return 0f;
                }

                return _device.TryGetGamepadAxis(parsed, out float value) ? value : 0f;
            }

            public bool GetButton(string devicePath)
            {
                if (!GamepadControlPath.TryParse(devicePath, out GamepadControlPath parsed))
                {
                    return false;
                }

                return _device.IsGamepadButtonDown(parsed);
            }

            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
