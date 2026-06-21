using Ludots.Adapter.Raylib;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibSyntheticKeyboardInputTests
{
    [Test]
    public void SendKeyStroke_RoutesControlKeyDownAndUpThroughFocusedCanvas()
    {
        var sink = new RecordingCanvasKeyboardSink();
        UIRoot root = CreateFocusedCanvasRoot(sink);

        bool handled = RaylibSyntheticKeyboardInput.SendKeyStroke(root, "Backspace");

        Assert.That(handled, Is.True);
        Assert.That(sink.Events.Select(e => e.Action), Is.EqualTo(new[] { KeyboardAction.Down, KeyboardAction.Up }));
        Assert.That(sink.Events.Select(e => e.Key), Is.EqualTo(new[] { "Backspace", "Backspace" }));
    }

    [Test]
    public void SendTextInput_RoutesEachRuneAsCharacterInputThroughFocusedCanvas()
    {
        var sink = new RecordingCanvasKeyboardSink();
        UIRoot root = CreateFocusedCanvasRoot(sink);

        bool handled = RaylibSyntheticKeyboardInput.SendTextInput(root, "a你");

        Assert.That(handled, Is.True);
        Assert.That(sink.Events.Select(e => e.Action), Is.EqualTo(new[] { KeyboardAction.Character, KeyboardAction.Character }));
        Assert.That(sink.Events.Select(e => e.Text), Is.EqualTo(new[] { "a", "你" }));
    }

    private static UIRoot CreateFocusedCanvasRoot(RecordingCanvasKeyboardSink sink)
    {
        var root = new UIRoot(new NullUiRenderer());
        UiScene scene = UiSceneComposer.Compose(
            new FixedTextMeasurer(),
            new NullImageSizeProvider(),
            Ui.Canvas(sink).Width(100f).Height(100f));
        root.MountScene(scene);
        root.Resize(100f, 100f);
        root.HandleInput(new PointerEvent
        {
            DeviceType = InputDeviceType.Mouse,
            PointerId = 0,
            Action = PointerAction.Down,
            X = 8f,
            Y = 8f
        });
        return root;
    }

    private sealed class RecordingCanvasKeyboardSink : IUiCanvasContent, IUiCanvasInputSink, IUiCanvasKeyboardInputSink
    {
        public List<KeyboardEvent> Events { get; } = new();

        public bool HandleInput(UiNode node, PointerEvent pointerEvent)
        {
            return pointerEvent.Action == PointerAction.Down;
        }

        public bool HandleKeyboardInput(UiNode node, KeyboardEvent keyboardEvent)
        {
            Events.Add(new KeyboardEvent
            {
                DeviceType = keyboardEvent.DeviceType,
                Action = keyboardEvent.Action,
                Key = keyboardEvent.Key,
                Code = keyboardEvent.Code,
                Text = keyboardEvent.Text,
                Modifiers = keyboardEvent.Modifiers
            });
            return true;
        }
    }

    private sealed class NullUiRenderer : IUiRenderer
    {
        public void Render(UiScene scene, float width, float height)
        {
        }
    }

    private sealed class FixedTextMeasurer : IUiTextMeasurer
    {
        public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
        {
            float width = MeasureWidth(text, style);
            float lineHeight = Math.Max(1f, style.FontSize);
            return new UiTextLayoutResult(new[] { text ?? string.Empty }, width, lineHeight, lineHeight);
        }

        public float MeasureWidth(string? text, UiStyle style)
        {
            return (text?.Length ?? 0) * 8f;
        }
    }

    private sealed class NullImageSizeProvider : IUiImageSizeProvider
    {
        public bool TryGetSize(string? source, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            return false;
        }
    }
}
