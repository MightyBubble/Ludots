using System;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;
using SkiaSharp;

namespace Ludots.UI;

public class UIRoot
{
	private readonly UiSceneRenderer _sceneRenderer = new UiSceneRenderer();

	private UiNodeId? _pressedNodeId;

	public UiScene? Scene { get; private set; }

	public float Width { get; private set; }

	public float Height { get; private set; }

	public bool IsDirty { get; set; } = true;

	public void MountScene(UiScene scene)
	{
		Scene = scene ?? throw new ArgumentNullException("scene");
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
		bool flag = Scene.AdvanceTime(deltaSeconds);
		if (flag)
		{
			IsDirty = true;
		}
		return flag;
	}

	public bool HandleInput(InputEvent e)
	{
		if (Scene == null)
		{
			return false;
		}
		Scene.Layout(Width, Height);
		if (!(e is PointerEvent pointerEvent))
		{
			return false;
		}
		bool flag = false;
		UiNodeId? uiNodeId = Scene.HitTest(pointerEvent.X, pointerEvent.Y)?.Id;
		switch (pointerEvent.Action)
		{
		case PointerAction.Move:
			flag = Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Move, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, uiNodeId)).Handled;
			break;
		case PointerAction.Down:
			_pressedNodeId = uiNodeId;
			flag = Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Down, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, uiNodeId)).Handled;
			break;
		case PointerAction.Up:
		{
			flag = Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Up, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, uiNodeId)).Handled;
			UiNodeId? pressedNodeId = _pressedNodeId;
			if (pressedNodeId.HasValue)
			{
				UiNodeId valueOrDefault = pressedNodeId.GetValueOrDefault();
				if (valueOrDefault.IsValid && uiNodeId == valueOrDefault)
				{
					flag |= Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, valueOrDefault)).Handled;
				}
			}
			_pressedNodeId = null;
			break;
		}
		case PointerAction.Scroll:
			flag = Scene.Dispatch(new UiPointerEvent(UiPointerEventType.Scroll, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, uiNodeId, pointerEvent.DeltaX, pointerEvent.DeltaY)).Handled;
			break;
		}
		if (flag || Scene.IsDirty)
		{
			IsDirty = true;
		}
		return flag;
	}
}
