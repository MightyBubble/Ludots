using System;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;

namespace Ludots.UI;

public class UIRoot
{
	private readonly IUiRenderer _renderer;

	private UiNodeId? _pressedNodeId;

	private UiNodeId? _capturedCanvasNodeId;

	private UiNodeId? _focusedCanvasNodeId;

	public UiScene? Scene { get; private set; }

	public float Width { get; private set; }

	public float Height { get; private set; }

	public bool IsDirty { get; set; } = true;

	public bool HasFocusedCanvas => _focusedCanvasNodeId.HasValue;

	public UIRoot(IUiRenderer renderer)
	{
		_renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
	}

	internal void MountSceneFromHost(UiScene scene)
	{
		SetFocusedCanvas(null);
		_pressedNodeId = null;
		_capturedCanvasNodeId = null;
		Scene = scene ?? throw new ArgumentNullException("scene");
		IsDirty = true;
	}

	internal void ClearSceneFromHost()
	{
		SetFocusedCanvas(null);
		_pressedNodeId = null;
		_capturedCanvasNodeId = null;
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
		UiScene scene = Scene;
		if (scene == null)
		{
			return false;
		}
		scene.Layout(Width, Height);
		if (e is KeyboardEvent keyboardEvent)
		{
			return HandleKeyboardInput(keyboardEvent);
		}

		if (!(e is PointerEvent pointerEvent))
		{
			return false;
		}
		bool flag = false;
		if (_capturedCanvasNodeId.HasValue &&
			scene.FindNode(_capturedCanvasNodeId.Value) is UiNode capturedCanvasNode &&
			capturedCanvasNode.CanvasContent is IUiCanvasInputSink capturedInputSink)
		{
			bool capturedHandled = capturedInputSink.HandleInput(capturedCanvasNode, pointerEvent);
			if (pointerEvent.Action is PointerAction.Up or PointerAction.Cancel)
			{
				_capturedCanvasNodeId = null;
			}

			if (capturedHandled)
			{
				IsDirty = true;
				return true;
			}
		}
		else
		{
			_capturedCanvasNodeId = null;
		}

		UiNodeId? uiNodeId = scene.HitTest(pointerEvent.X, pointerEvent.Y)?.Id;
		if (uiNodeId.HasValue)
		{
			UiNodeId valueOrDefault = uiNodeId.GetValueOrDefault();
			if (valueOrDefault.IsValid &&
				scene.FindNode(valueOrDefault) is UiNode hitNode &&
				TryHandleCanvasInput(hitNode, pointerEvent, out UiNodeId canvasNodeId))
			{
				if (pointerEvent.Action == PointerAction.Down)
				{
					_capturedCanvasNodeId = canvasNodeId;
					SetFocusedCanvas(canvasNodeId);
				}

				if (pointerEvent.Action is PointerAction.Up or PointerAction.Cancel)
				{
					_capturedCanvasNodeId = null;
				}

				IsDirty = true;
				return true;
			}
		}

		if (pointerEvent.Action == PointerAction.Down)
		{
			SetFocusedCanvas(null);
		}

		switch (pointerEvent.Action)
		{
		case PointerAction.Move:
			flag = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Move, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, uiNodeId)).Handled;
			break;
		case PointerAction.Down:
		{
			PointerButton button = RequirePointerButton(pointerEvent);
			_pressedNodeId = button == PointerButton.Left ? uiNodeId : null;
			flag = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Down, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, uiNodeId)).Handled;
			break;
		}
		case PointerAction.Up:
		{
			flag = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Up, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, uiNodeId)).Handled;
			UiNodeId? pressedNodeId = _pressedNodeId;
			PointerButton button = RequirePointerButton(pointerEvent);
			if (pressedNodeId.HasValue && button == PointerButton.Left)
			{
				UiNodeId valueOrDefault = pressedNodeId.GetValueOrDefault();
				if (valueOrDefault.IsValid && uiNodeId == valueOrDefault)
				{
					flag |= scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, valueOrDefault)).Handled;
				}
			}
			_pressedNodeId = null;
			_capturedCanvasNodeId = null;
			break;
		}
		case PointerAction.Cancel:
			_pressedNodeId = null;
			_capturedCanvasNodeId = null;
			break;
		case PointerAction.Scroll:
			flag = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Scroll, pointerEvent.PointerId, pointerEvent.X, pointerEvent.Y, uiNodeId, pointerEvent.DeltaX, pointerEvent.DeltaY)).Handled;
			break;
		}
		return FinishInputDispatch(scene, flag);
	}

	public bool ScrollNode(UiNodeId targetNodeId, float deltaX, float deltaY)
	{
		if (!targetNodeId.IsValid)
		{
			throw new ArgumentException("ScrollNode requires a valid target node id.", nameof(targetNodeId));
		}

		UiScene scene = Scene;
		if (scene == null)
		{
			return false;
		}

		scene.Layout(Width, Height);
		UiNode target = scene.FindNode(targetNodeId)
			?? throw new InvalidOperationException($"ScrollNode target '{targetNodeId.Value}' is not mounted in the current scene.");
		bool handled = scene.Dispatch(new UiPointerEvent(
			UiPointerEventType.Scroll,
			0,
			target.LayoutRect.X,
			target.LayoutRect.Y,
			targetNodeId,
			deltaX,
			deltaY)).Handled;
		return FinishInputDispatch(scene, handled);
	}

	private static bool TryHandleCanvasInput(UiNode node, PointerEvent pointerEvent, out UiNodeId canvasNodeId)
	{
		for (UiNode? current = node; current != null; current = current.Parent)
		{
			if (current.CanvasContent is IUiCanvasInputSink inputSink &&
				inputSink.HandleInput(current, pointerEvent))
			{
				canvasNodeId = current.Id;
				return true;
			}
		}

		canvasNodeId = default;
		return false;
	}

	private bool HandleKeyboardInput(KeyboardEvent keyboardEvent)
	{
		if (!_focusedCanvasNodeId.HasValue)
		{
			return false;
		}

		UiNodeId focusedCanvasNodeId = _focusedCanvasNodeId.Value;
		if (!focusedCanvasNodeId.IsValid ||
			Scene?.FindNode(focusedCanvasNodeId) is not UiNode focusedCanvasNode ||
			focusedCanvasNode.CanvasContent is not IUiCanvasKeyboardInputSink keyboardSink)
		{
			_focusedCanvasNodeId = null;
			return false;
		}

		bool handled = keyboardSink.HandleKeyboardInput(focusedCanvasNode, keyboardEvent);
		if (handled)
		{
			IsDirty = true;
		}

		return handled;
	}

	private bool FinishInputDispatch(UiScene scene, bool handled)
	{
		if (!ReferenceEquals(Scene, scene))
		{
			IsDirty = true;
			return handled;
		}

		bool sceneChanged = scene.IsDirty;
		bool runtimeChanged = false;
		if (handled || sceneChanged)
		{
			runtimeChanged = RefreshReactiveSceneRuntime(scene);
			sceneChanged = scene.IsDirty;
		}
		if (sceneChanged || runtimeChanged)
		{
			IsDirty = true;
		}
		return handled || runtimeChanged;
	}

	private void SetFocusedCanvas(UiNodeId? canvasNodeId)
	{
		if (_focusedCanvasNodeId == canvasNodeId)
		{
			return;
		}

		if (_focusedCanvasNodeId.HasValue)
		{
			UiNodeId previousId = _focusedCanvasNodeId.Value;
			if (previousId.IsValid &&
				Scene?.FindNode(previousId) is UiNode previousNode &&
				previousNode.CanvasContent is IUiCanvasFocusSink previousFocusSink)
			{
				previousFocusSink.SetCanvasFocus(previousNode, false);
			}
		}

		_focusedCanvasNodeId = canvasNodeId;
		if (canvasNodeId.HasValue)
		{
			UiNodeId nextId = canvasNodeId.Value;
			if (nextId.IsValid &&
				Scene?.FindNode(nextId) is UiNode nextNode &&
				nextNode.CanvasContent is IUiCanvasFocusSink nextFocusSink)
			{
				nextFocusSink.SetCanvasFocus(nextNode, true);
			}
		}
	}

	private bool RefreshReactiveSceneRuntime()
	{
		UiScene scene = Scene;
		return scene != null && RefreshReactiveSceneRuntime(scene);
	}

	private bool RefreshReactiveSceneRuntime(UiScene scene)
	{
		if (Width <= 0f || Height <= 0f)
		{
			return false;
		}
		scene.Layout(Width, Height);
		if (!scene.TryRefreshReactiveRuntimeDependencies())
		{
			return false;
		}
		scene.Layout(Width, Height);
		IsDirty = true;
		return true;
	}

	private static PointerButton RequirePointerButton(PointerEvent pointerEvent)
	{
		return pointerEvent.Button
			?? throw new InvalidOperationException("Pointer Down/Up input must include an explicit button.");
	}
}
