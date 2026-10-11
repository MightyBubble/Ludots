using Ludots.Client.Raylib.Input;
using Ludots.Core.Input.Runtime;
using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibGamepadInputPathTests
{
    [Test]
    public void Parser_MapsLayoutAbstractNamesOntoRaylibControls()
    {
        Assert.That(
            RaylibInputPathParser.ParseGamepadButton("<Gamepad>/buttonSouth"),
            Is.EqualTo(GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_DOWN));
        Assert.That(
            RaylibInputPathParser.ParseGamepadButton("<gamepad>/ButtonSouth"),
            Is.EqualTo(GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_DOWN));
        Assert.That(
            RaylibInputPathParser.ParseGamepadButton("<Gamepad>/dpadUp"),
            Is.EqualTo(GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_UP));
        Assert.That(
            RaylibInputPathParser.ParseGamepadButton("<Gamepad>/rightTrigger"),
            Is.EqualTo(GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_2));

        Assert.That(RaylibInputPathParser.ParseGamepadButton("<Gamepad>/leftStick"), Is.Null);
        Assert.That(RaylibInputPathParser.ParseGamepadAxis("<Gamepad>/leftStick"), Is.Null);

        (int Pad, GamepadAxis Axis) stickX = RaylibInputPathParser.ParseGamepadAxis("<Gamepad>/leftStick/x")!.Value;
        Assert.That(stickX.Pad, Is.EqualTo(0));
        Assert.That(stickX.Axis, Is.EqualTo(GamepadAxis.GAMEPAD_AXIS_LEFT_X));

        (int Pad, GamepadAxis Axis) trigger = RaylibInputPathParser.ParseGamepadAxis("<Gamepad2>/rightTrigger")!.Value;
        Assert.That(trigger.Pad, Is.EqualTo(1));
        Assert.That(trigger.Axis, Is.EqualTo(GamepadAxis.GAMEPAD_AXIS_RIGHT_TRIGGER));
        Assert.That(RaylibInputPathParser.ParseGamepadIndex("<Gamepad2>/buttonSouth"), Is.EqualTo(1));

        Assert.That(RaylibInputPathParser.ParseGamepadButton("<Keyboard>/w"), Is.Null);
        Assert.That(RaylibInputPathParser.ParseGamepadAxis("<Mouse>/Pos"), Is.Null);
        Assert.That(RaylibInputPathParser.ParseGamepadIndex("<Keyboard>/space"), Is.EqualTo(-1));
    }

    [Test]
    public void Parser_UnknownGamepadControl_FailsFast()
    {
        var error = Assert.Throws<InvalidOperationException>(() => RaylibInputPathParser.ParseGamepadButton("<Gamepad>/notARealButton"));
        Assert.That(error!.Message, Does.Contain("LUDOTS_INPUT_GAMEPAD_CONTROL_UNKNOWN"));
        Assert.That(error.Message, Does.Contain("<Gamepad>/notARealButton"));
        Assert.That(error.Message, Does.Contain("buttonSouth"));
        Assert.That(error.Message, Does.Not.Contain("CANCEL"));
        Assert.Throws<InvalidOperationException>(() => RaylibInputPathParser.ParseGamepadButton("<Gamepad>/A"));
        Assert.Throws<InvalidOperationException>(() => RaylibInputPathParser.ParseGamepadButton("<Gamepad>/LB"));
        Assert.Throws<InvalidOperationException>(() => RaylibInputPathParser.ParseGamepadAxis("<Gamepad>/left_stick_x"));
        Assert.Throws<InvalidOperationException>(() => RaylibInputPathParser.ParseGamepadButton("<Gamepad>/CANCEL"));
        Assert.Throws<InvalidOperationException>(() => RaylibInputPathParser.ParseGamepadAxis("<Gamepad9>/leftStick/x"));
    }

    [Test]
    public void Backend_ReadsEngagedSyntheticGamepadWithoutRequiringHardware()
    {
        var device = new SyntheticInputDevice();
        device.SetGamepadAxis("<Gamepad>/leftStick/x", 0.25f);
        device.SetGamepadAxis("<Gamepad>/leftStick/y", -0.5f);
        device.GamepadButtonDown("<Gamepad>/buttonSouth");
        device.AdvanceFrame();

        var backend = new RaylibInputBackend(device);
        Assert.That(backend.GetAxis("<Gamepad>/leftStick/x"), Is.EqualTo(0.25f).Within(0.0001f));
        Assert.That(backend.GetAxis("<Gamepad>/leftStick/y"), Is.EqualTo(-0.5f).Within(0.0001f));
        Assert.That(backend.GetButton("<Gamepad>/buttonSouth"), Is.True);
        Assert.That(backend.GetButton("<Gamepad>/leftStick"), Is.False);
        Assert.Throws<InvalidOperationException>(() => backend.GetButton("<Gamepad>/notARealButton"));
    }
}
