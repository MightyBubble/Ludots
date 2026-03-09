using SkiaSharp;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;

namespace Ludots.UI;

public class UIRoot
{
    private readonly UiSceneRenderer _sceneRenderer = new();
    private UiNodeId? _pressedNodeId;

    public UiScene? Scene { get; private set; }
    public float Width { get; private set; }
    public float Height { get; private set; }
    public bool IsDirty { get; set; } = true;

    public void MountScene(UiScene scene)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        IsDirty = true;
    }

    public void ClearScene()
    {
        Scene = null;
        IsDirty = true;
    }

    public void Resize(float width, float height)
    {
        Width = width;
        Height = height;
        IsDirty = true;
    }

    public void Render(SKCanvas canvas)
    {
        if (Scene == null)
        {
            IsDirty = false;
            return;
        }

        _sceneRenderer.Render(Scene, canvas, Width, Height);
        IsDirty = false;
    }

    public bool Update(float deltaSeconds)
    {
        if (Scene == null)
        {
            return false;
        }

        bool changed = Scene.AdvanceTime(deltaSeconds);
        if (changed)
        {
            IsDirty = true;
        }

        return changed;
    }

    public bool HandleInput(InputEvent e)
    {
        if (Scene == null)
        {
            return false;
        }

        Scene.Layout(Width, Height);
        if (e is not PointerEvent pointerEvent)
        {
            return false;
        }

        bool handled = false;
        UiNode? currentTarget = Scene.HitTest(pointerEvent.X, pointerEvent.Y);
        UiNodeId? targetNodeId = currentTarget?.Id;

        switch (pointerEvent.Action)
        {
            case PointerAction.Move:
                handled = Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Move, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, targetNodeId)).Handled;
                break;
            case PointerAction.Down:
                _pressedNodeId = targetNodeId;
                handled = Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Down, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, targetNodeId)).Handled;
                break;
            case PointerAction.Up:
                handled = Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Up, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, targetNodeId)).Handled;
                if (_pressedNodeId is UiNodeId pressedId && pressedId.IsValid && targetNodeId == pressedId)
                {
                    handled |= Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, pressedId)).Handled;
                }

                _pressedNodeId = null;
                break;
            case PointerAction.Scroll:
                handled = Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Scroll, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, targetNodeId, pointerEvent.DeltaX, pointerEvent.DeltaY)).Handled;
                break;
        }

        if (handled || Scene.IsDirty)
        {
            IsDirty = true;
        }

        return handled;
    }
}
