using Ludots.Core.Input.Automation;
using Ludots.UI;
using Ludots.UI.Input;

namespace Ludots.Adapter.Raylib;

internal static class InputAutomationUiForwarder
{
    public static bool Forward(UIRoot uiRoot, InputAutomationPlayer player, out bool pointerCaptured, out bool wheelCaptured)
    {
        pointerCaptured = false;
        wheelCaptured = false;
        bool handled = false;
        IReadOnlyList<InputAutomationFrameEvent> events = player.FrameEvents;
        for (int i = 0; i < events.Count; i++)
        {
            var frameEvent = events[i];
            switch (frameEvent.Kind)
            {
                case InputAutomationFrameEventKind.PointerMove:
                case InputAutomationFrameEventKind.PointerDown:
                case InputAutomationFrameEventKind.PointerUp:
                case InputAutomationFrameEventKind.PointerCancel:
                case InputAutomationFrameEventKind.PointerScroll:
                {
                    var pointer = new PointerEvent
                    {
                        DeviceType = InputDeviceType.Mouse,
                        PointerId = 0,
                        Action = ToPointerAction(frameEvent.Kind),
                        Button = ToUiPointerButton(frameEvent.Button),
                        X = frameEvent.Position.X,
                        Y = frameEvent.Position.Y,
                        DeltaX = frameEvent.Delta.X,
                        DeltaY = frameEvent.Delta.Y
                    };
                    bool eventHandled = uiRoot.HandleInput(pointer);
                    handled |= eventHandled;
                    if (frameEvent.Kind == InputAutomationFrameEventKind.PointerDown && eventHandled)
                    {
                        pointerCaptured = true;
                    }
                    if (frameEvent.Kind == InputAutomationFrameEventKind.PointerScroll && eventHandled)
                    {
                        wheelCaptured = true;
                    }
                    break;
                }
                case InputAutomationFrameEventKind.KeyDown:
                case InputAutomationFrameEventKind.KeyUp:
                    handled |= uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = frameEvent.Kind == InputAutomationFrameEventKind.KeyDown ? KeyboardAction.Down : KeyboardAction.Up,
                        Key = frameEvent.Key,
                        Code = frameEvent.Key,
                        Modifiers = frameEvent.Modifiers
                    });
                    break;
                case InputAutomationFrameEventKind.Character:
                    handled |= uiRoot.HandleInput(new KeyboardEvent
                    {
                        DeviceType = InputDeviceType.Keyboard,
                        Action = KeyboardAction.Character,
                        Key = string.IsNullOrEmpty(frameEvent.Key) ? frameEvent.Text : frameEvent.Key,
                        Text = frameEvent.Text,
                        Modifiers = frameEvent.Modifiers
                    });
                    break;
            }
        }

        return handled;
    }

    private static PointerAction ToPointerAction(InputAutomationFrameEventKind kind)
    {
        return kind switch
        {
            InputAutomationFrameEventKind.PointerDown => PointerAction.Down,
            InputAutomationFrameEventKind.PointerUp => PointerAction.Up,
            InputAutomationFrameEventKind.PointerCancel => PointerAction.Cancel,
            InputAutomationFrameEventKind.PointerScroll => PointerAction.Scroll,
            _ => PointerAction.Move
        };
    }

    private static PointerButton? ToUiPointerButton(InputAutomationPointerButton? button)
    {
        return button switch
        {
            InputAutomationPointerButton.Left => PointerButton.Left,
            InputAutomationPointerButton.Middle => PointerButton.Middle,
            InputAutomationPointerButton.Right => PointerButton.Right,
            _ => null
        };
    }
}
