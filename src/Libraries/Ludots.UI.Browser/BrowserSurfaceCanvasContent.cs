using System;
using Ludots.UI.Input;
using Ludots.UI.Runtime;

namespace Ludots.UI.Browser;

public class BrowserSurfaceCanvasContent : IUiCanvasContent, IUiBrowserCanvasContent, IUiCanvasInputSink, IUiCanvasKeyboardInputSink, IUiCanvasHitTestSink, IUiCanvasFocusSink, IDisposable
{
	private readonly IBrowserSurface _surface;
	private readonly BrowserSurfaceHitTestOptions _hitTestOptions;
	private PointerButton? _activePointerButton;
	private bool _focused;
	private bool _disposed;
	private bool _hasPointerMapping;
	private UiRect _pointerMappingContentRect;
	private BrowserViewport _pointerMappingViewport;
	private float _pointerMappingScaleX;
	private float _pointerMappingScaleY;

	public BrowserSurfaceCanvasContent(
		IBrowserSurface surface,
		BrowserSurfaceHitTestOptions? hitTestOptions = null)
	{
		_surface = surface ?? throw new ArgumentNullException(nameof(surface));
		_hitTestOptions = hitTestOptions ?? BrowserSurfaceHitTestOptions.Bounds;
		_hitTestOptions.Validate();
	}

	public IBrowserSurface Surface => _surface;

	public BrowserFrame? LatestFrame => _surface.TryGetLatestFrame();

	public bool HitTest(UiNode node, float x, float y)
	{
		ArgumentNullException.ThrowIfNull(node);
		ThrowIfDisposed();

		UiRect contentRect = GetContentRect(node);
		if (!contentRect.Contains(x, y))
		{
			return false;
		}

		if (_activePointerButton.HasValue || _hitTestOptions.Mode == BrowserSurfaceHitTestMode.Bounds)
		{
			return true;
		}

		var state = new AlphaHitTestState(contentRect, x, y, _hitTestOptions.AlphaThreshold);
		TryReadLatestFrame(state, static (in BrowserFrameAccess frame, AlphaHitTestState hitTestState) =>
		{
			hitTestState.IsOpaque = IsFramePixelOpaque(frame, hitTestState.ContentRect, hitTestState.X, hitTestState.Y, hitTestState.AlphaThreshold);
		});
		return state.IsOpaque;
	}

	public bool HandleInput(UiNode node, PointerEvent pointerEvent)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentNullException.ThrowIfNull(pointerEvent);
		ThrowIfDisposed();

		if (pointerEvent.Action is PointerAction.Down or PointerAction.Up &&
			!pointerEvent.Button.HasValue)
		{
			throw new InvalidOperationException("Browser pointer Down/Up input must include an explicit button.");
		}
		PointerButton? pointerButton = pointerEvent.Button;

		UiRect contentRect = GetContentRect(node);
		if (!contentRect.Contains(pointerEvent.X, pointerEvent.Y) &&
			pointerEvent.Action != PointerAction.Up &&
			pointerEvent.Action != PointerAction.Cancel &&
			!_activePointerButton.HasValue)
		{
			return false;
		}

		BrowserPointerMapping pointerMapping = GetPointerMapping(contentRect);
		float localX = Math.Clamp((pointerEvent.X - contentRect.X) * pointerMapping.ScaleX, 0f, pointerMapping.Viewport.Width - 1f);
		float localY = Math.Clamp((pointerEvent.Y - contentRect.Y) * pointerMapping.ScaleY, 0f, pointerMapping.Viewport.Height - 1f);
		if (pointerEvent.Action == PointerAction.Down)
		{
			_activePointerButton = pointerButton;
			SetBrowserFocus(true);
		}

		PointerButton? activeButton = pointerEvent.Action switch
		{
			PointerAction.Move => _activePointerButton,
			PointerAction.Down => pointerButton,
			PointerAction.Up => pointerButton,
			PointerAction.Cancel => _activePointerButton,
			_ => null
		};
		BrowserPointerButton browserButton = activeButton switch
		{
			PointerButton.Left => BrowserPointerButton.Left,
			PointerButton.Middle => BrowserPointerButton.Middle,
			PointerButton.Right => BrowserPointerButton.Right,
			_ => BrowserPointerButton.None
		};
		bool buttonDown = activeButton.HasValue && pointerEvent.Action != PointerAction.Up && pointerEvent.Action != PointerAction.Cancel;
		BrowserInputEvent? browserEvent = pointerEvent.Action switch
		{
			PointerAction.Move => new BrowserPointerEvent(BrowserPointerEventType.Move, pointerEvent.PointerId, localX, localY, browserButton, buttonDown),
			PointerAction.Down => new BrowserPointerEvent(BrowserPointerEventType.Down, pointerEvent.PointerId, localX, localY, browserButton, true),
			PointerAction.Up => new BrowserPointerEvent(BrowserPointerEventType.Up, pointerEvent.PointerId, localX, localY, browserButton, false),
			PointerAction.Scroll => new BrowserWheelEvent(localX, localY, pointerEvent.DeltaX * pointerMapping.ScaleX, pointerEvent.DeltaY * pointerMapping.ScaleY),
			PointerAction.Cancel => new BrowserPointerEvent(BrowserPointerEventType.Leave, pointerEvent.PointerId, localX, localY, BrowserPointerButton.None, false),
			_ => null
		};

		if (browserEvent == null)
		{
			return false;
		}

		_ = _surface.SendInputAsync(browserEvent);
		if (pointerEvent.Action is PointerAction.Up or PointerAction.Cancel)
		{
			_activePointerButton = null;
		}

		return true;
	}

	public bool HandleKeyboardInput(UiNode node, KeyboardEvent keyboardEvent)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentNullException.ThrowIfNull(keyboardEvent);
		ThrowIfDisposed();

		BrowserInputEvent? browserEvent = keyboardEvent.Action switch
		{
			KeyboardAction.Down => new BrowserKeyEvent(BrowserKeyEventType.Down, keyboardEvent.Key, keyboardEvent.Code, (BrowserInputModifiers)keyboardEvent.Modifiers),
			KeyboardAction.Up => new BrowserKeyEvent(BrowserKeyEventType.Up, keyboardEvent.Key, keyboardEvent.Code, (BrowserInputModifiers)keyboardEvent.Modifiers),
			KeyboardAction.Character => string.IsNullOrEmpty(keyboardEvent.Text)
				? null
				: new BrowserTextInputEvent(keyboardEvent.Text),
			_ => null
		};

		if (browserEvent == null)
		{
			return false;
		}

		_ = _surface.SendInputAsync(browserEvent);
		return true;
	}

	public void SetCanvasFocus(UiNode node, bool isFocused)
	{
		ArgumentNullException.ThrowIfNull(node);
		ThrowIfDisposed();
		SetBrowserFocus(isFocused);
	}

	public void EnsureSurfaceViewport(float width, float height)
	{
		if (width <= 0.01f || height <= 0.01f)
		{
			return;
		}

		BrowserViewport viewport = _surface.Viewport;
		int desiredWidth = Math.Max(1, (int)MathF.Ceiling(width * viewport.DeviceScaleFactor));
		int desiredHeight = Math.Max(1, (int)MathF.Ceiling(height * viewport.DeviceScaleFactor));
		if (viewport.Width == desiredWidth && viewport.Height == desiredHeight)
		{
			return;
		}

		_hasPointerMapping = false;
		_ = _surface.ResizeAsync(new BrowserViewport(desiredWidth, desiredHeight, viewport.DeviceScaleFactor));
	}

	public virtual void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		TryClearBrowserFocus();
		_disposed = true;
	}

	public UiRect GetContentRect(UiNode node)
	{
		return ResolveContentRect(node);
	}

	public bool TryReadLatestFrame<TState>(TState state, BrowserFrameReadAction<TState> readFrame)
	{
		ArgumentNullException.ThrowIfNull(readFrame);
		ThrowIfDisposed();
		return _surface.TryReadLatestFrame(state, readFrame);
	}

	public static UiRect ResolveContentRect(UiNode node)
	{
		ArgumentNullException.ThrowIfNull(node);
		UiRect rect = node.LayoutRect;
		UiThickness padding = node.RenderStyle.Padding;
		float x = rect.X + padding.Left;
		float y = rect.Y + padding.Top;
		float width = Math.Max(0f, rect.Width - padding.Horizontal);
		float height = Math.Max(0f, rect.Height - padding.Vertical);
		return new UiRect(x, y, width, height);
	}

	protected void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(BrowserSurfaceCanvasContent));
		}
	}

	private static bool IsFramePixelOpaque(
		in BrowserFrameAccess frame,
		UiRect contentRect,
		float x,
		float y,
		byte alphaThreshold)
	{
		if (frame.Viewport.Width <= 0 || frame.Viewport.Height <= 0 ||
			contentRect.Width <= 0.01f || contentRect.Height <= 0.01f)
		{
			return false;
		}

		int frameX = Math.Clamp(
			(int)MathF.Floor((x - contentRect.X) * frame.Viewport.Width / contentRect.Width),
			0,
			frame.Viewport.Width - 1);
		int frameY = Math.Clamp(
			(int)MathF.Floor((y - contentRect.Y) * frame.Viewport.Height / contentRect.Height),
			0,
			frame.Viewport.Height - 1);
		int offset = checked((frameY * frame.RowBytes) + (frameX * BrowserFrameBuffer.BytesPerPixel));
		ReadOnlySpan<byte> pixels = frame.Pixels.Span;
		if (offset + 3 >= pixels.Length)
		{
			return false;
		}

		return pixels[offset + 3] >= alphaThreshold;
	}

	private BrowserPointerMapping GetPointerMapping(UiRect contentRect)
	{
		if (_hasPointerMapping && _pointerMappingContentRect == contentRect)
		{
			return new BrowserPointerMapping(_pointerMappingViewport, _pointerMappingScaleX, _pointerMappingScaleY);
		}

		BrowserViewport viewport = EnsureSurfaceViewportResolved(contentRect.Width, contentRect.Height);
		float scaleX = viewport.Width / Math.Max(1f, contentRect.Width);
		float scaleY = viewport.Height / Math.Max(1f, contentRect.Height);

		_hasPointerMapping = true;
		_pointerMappingContentRect = contentRect;
		_pointerMappingViewport = viewport;
		_pointerMappingScaleX = scaleX;
		_pointerMappingScaleY = scaleY;
		return new BrowserPointerMapping(viewport, scaleX, scaleY);
	}

	private BrowserViewport EnsureSurfaceViewportResolved(float width, float height)
	{
		BrowserViewport viewport = _surface.Viewport;
		int desiredWidth = Math.Max(1, (int)MathF.Ceiling(width * viewport.DeviceScaleFactor));
		int desiredHeight = Math.Max(1, (int)MathF.Ceiling(height * viewport.DeviceScaleFactor));
		if (viewport.Width == desiredWidth && viewport.Height == desiredHeight)
		{
			return viewport;
		}

		var desiredViewport = new BrowserViewport(desiredWidth, desiredHeight, viewport.DeviceScaleFactor);
		_ = _surface.ResizeAsync(desiredViewport);
		return desiredViewport;
	}

	private void SetBrowserFocus(bool isFocused)
	{
		if (_focused == isFocused)
		{
			return;
		}

		_focused = isFocused;
		_ = _surface.SendInputAsync(new BrowserFocusEvent(isFocused));
	}

	private void TryClearBrowserFocus()
	{
		if (!_focused)
		{
			return;
		}

		_focused = false;
		try
		{
			_ = _surface.SendInputAsync(new BrowserFocusEvent(false));
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private sealed class AlphaHitTestState
	{
		public AlphaHitTestState(UiRect contentRect, float x, float y, byte alphaThreshold)
		{
			ContentRect = contentRect;
			X = x;
			Y = y;
			AlphaThreshold = alphaThreshold;
		}

		public UiRect ContentRect { get; }

		public float X { get; }

		public float Y { get; }

		public byte AlphaThreshold { get; }

		public bool IsOpaque { get; set; }
	}

	private readonly record struct BrowserPointerMapping(BrowserViewport Viewport, float ScaleX, float ScaleY);
}
