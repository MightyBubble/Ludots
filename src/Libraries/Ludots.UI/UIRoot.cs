using System;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;

namespace Ludots.UI;

public class UIRoot
{
	private readonly IUiRenderer _renderer;

	private UiNodeId? _pressedNodeId;

	public UiScene? Scene { get; private set; }

	public float Width { get; private set; }

	public float Height { get; private set; }

	public bool IsDirty { get; set; } = true;

	public UIRoot(IUiRenderer renderer)
	{
		_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
	}

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

	public void Render()
	{
		if (Scene == null)
		{
			IsDirty = false;
			return;
		}
		RefreshReactiveSceneRuntime();
		_renderer.Render(Scene, Width, Height);
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
		bool sceneChanged = Scene.IsDirty;
		bool runtimeChanged = false;
		if (flag || sceneChanged)
		{
			runtimeChanged = RefreshReactiveSceneRuntime();
			sceneChanged = Scene.IsDirty;
		}
		if (sceneChanged || runtimeChanged)
		{
			IsDirty = true;
		}
		return flag || runtimeChanged;
	}

	private bool RefreshReactiveSceneRuntime()
	{
		if (Scene == null || Width <= 0f || Height <= 0f)
		{
			return false;
		}
		Scene.Layout(Width, Height);
		if (!Scene.TryRefreshReactiveRuntimeDependencies())
		{
			return false;
		}
		Scene.Layout(Width, Height);
		IsDirty = true;
		return true;
	}
}
