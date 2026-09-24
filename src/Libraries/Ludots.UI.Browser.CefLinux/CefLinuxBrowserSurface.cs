using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using global::CefNet;
using Ludots.UI.Browser;
using Ludots.UI.Browser.CefLinux.Core;

namespace Ludots.UI.Browser.CefLinux;

internal sealed class CefLinuxBrowserSurface : IBrowserSurface
{
	private readonly object _sync = new();
	private readonly CefLinuxBrowserClient _client;
	private readonly SurfaceRenderHandler _renderHandler;
	private readonly SurfaceLifeSpanHandler _lifeSpanHandler;
	private readonly SurfaceLoadHandler _loadHandler;
	private readonly CefLinuxBrowserMessageBridge _messages;
	private readonly IBrowserResourceResolver? _resourceResolver;
	private readonly CefLinuxBrowserSurfaceRegistry _registry;
	private readonly TaskCompletionSource _browserInitialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private CefBrowser? _browser;
	private BrowserViewport _viewport;
	private BrowserFrameBuffer _frameBuffer;
	private int? _browserIdentifier;
	private bool _disposed;

	public CefLinuxBrowserSurface(
		BrowserViewport viewport,
		IBrowserResourceResolver? resourceResolver,
		CefLinuxBrowserSurfaceRegistry registry)
	{
		_viewport = viewport;
		_resourceResolver = resourceResolver;
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
		_frameBuffer = new BrowserFrameBuffer(viewport, BrowserPixelFormat.Bgra8888Premultiplied);
		Id = BrowserSurfaceId.New();

		_renderHandler = new SurfaceRenderHandler(this);
		_lifeSpanHandler = new SurfaceLifeSpanHandler(this);
		_loadHandler = new SurfaceLoadHandler(this);
		_client = new CefLinuxBrowserClient(this);
		_messages = new CefLinuxBrowserMessageBridge(this);

		CreateBrowserOnUi();
	}

	public event EventHandler<BrowserFrameReadyEventArgs>? FrameReady;

	public BrowserSurfaceId Id { get; }

	public BrowserViewport Viewport
	{
		get
		{
			lock (_sync)
			{
				return _viewport;
			}
		}
	}

	public IBrowserMessageBridge Messages => _messages;

	public async ValueTask NavigateAsync(BrowserNavigationRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		await _browserInitialized.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		string navigationUrl = request.Uri.ToString();
		CefLinuxUiThread.Run(() =>
		{
			if (_disposed)
			{
				return;
			}

			_browser?.MainFrame?.LoadUrl(navigationUrl);
			_browser?.Host?.Invalidate(CefPaintElementType.View);
		});
	}

	public ValueTask ResizeAsync(BrowserViewport viewport, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		lock (_sync)
		{
			if (_viewport.Equals(viewport))
			{
				return ValueTask.CompletedTask;
			}

			_viewport = viewport;
		}

		CefLinuxUiThread.Run(() =>
		{
			if (_disposed)
			{
				return;
			}

			_browser?.Host?.WasResized();
			_browser?.Host?.Invalidate(CefPaintElementType.View);
		});
		return ValueTask.CompletedTask;
	}

	public ValueTask SendInputAsync(BrowserInputEvent inputEvent, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(inputEvent);
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		CefLinuxUiThread.Run(() =>
		{
			if (_disposed)
			{
				return;
			}

			CefBrowserHost? host = _browser?.Host;
			if (host == null)
			{
				return;
			}

			SendInputOnUi(host, inputEvent);
		});
		return ValueTask.CompletedTask;
	}

	public BrowserFrame? TryGetLatestFrame()
	{
		lock (_sync)
		{
			return _disposed ? null : _frameBuffer.Snapshot();
		}
	}

	public bool TryReadLatestFrame<TState>(TState state, BrowserFrameReadAction<TState> readFrame)
	{
		ArgumentNullException.ThrowIfNull(readFrame);
		BrowserFrameBuffer frameBuffer;
		lock (_sync)
		{
			if (_disposed)
			{
				return false;
			}

			frameBuffer = _frameBuffer;
		}

		frameBuffer.ReadLatestFrame(state, readFrame);
		return true;
	}

	public ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		if (_browserIdentifier is int browserIdentifier)
		{
			_registry.Unregister(browserIdentifier);
		}

		CefLinuxUiThread.Run(() => _browser?.Host?.CloseBrowser(forceClose: true));
		return ValueTask.CompletedTask;
	}

	internal BrowserResource? ResolveResource(string requestUrl)
	{
		if (_resourceResolver == null)
		{
			return null;
		}

		if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri? uri))
		{
			return null;
		}

		return _resourceResolver.ResolveAsync(uri).AsTask().GetAwaiter().GetResult();
	}

	internal ValueTask PostHostMessageAsync(BrowserScriptMessage message, CancellationToken cancellationToken)
	{
		string payloadJson = JsonSerializer.Serialize(new
		{
			channel = message.Channel,
			payload = message.Payload
		});

		string script = $"window.dispatchEvent(new MessageEvent('message', {{ data: {payloadJson} }}));";
		return ExecuteScriptAsync(script, cancellationToken);
	}

	internal async ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		await _browserInitialized.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		CefLinuxUiThread.Run(() =>
		{
			if (_disposed)
			{
				return;
			}

			CefFrame? mainFrame = _browser?.MainFrame;
			if (mainFrame is null || !mainFrame.IsValid)
			{
				return;
			}

			mainFrame.ExecuteJavaScript(script, "ludots:host", 0);
		});
	}

	private void CreateBrowserOnUi()
	{
		using var created = new AutoResetEvent(initialState: false);
		Exception? failure = null;

		CefLinuxUiThread.Run(() =>
		{
			try
			{
				var windowInfo = new CefWindowInfo();
				windowInfo.SetAsWindowless(IntPtr.Zero);
				var browserSettings = new CefBrowserSettings
				{
					BackgroundColor = default,
					WindowlessFrameRate = 60
				};
				try
				{
					_browser = CefApi.CreateBrowserSync(windowInfo, _client, "about:blank", browserSettings, null, null);
				}
				finally
				{
					browserSettings.Dispose();
					windowInfo.Dispose();
				}
			}
			catch (Exception exception)
			{
				failure = exception;
			}
			finally
			{
				created.Set();
			}
		});

		created.WaitOne();
		if (failure != null)
		{
			throw new InvalidOperationException("CEF Linux failed to create an offscreen browser.", failure);
		}
	}

	private void OnAfterCreated(CefBrowser browser)
	{
		_browserIdentifier = browser.Identifier;
		_registry.Register(browser.Identifier, ResolveResource);
		_browserInitialized.TrySetResult();
	}

	private void OnLoadEnd(CefBrowser browser, CefFrame frame)
	{
		if (frame.IsMain)
		{
			QueueDataPlaneFacadeInjection();
		}
	}

	private void QueueDataPlaneFacadeInjection()
	{
		_ = InjectDataPlaneFacadeAndObserveFailureAsync();
	}

	private async Task InjectDataPlaneFacadeAndObserveFailureAsync()
	{
		try
		{
			await ExecuteScriptAsync(CefLinuxFacadeScript.Create(), CancellationToken.None).ConfigureAwait(false);
		}
		catch (ObjectDisposedException) when (_disposed)
		{
		}
	}

	private void OnHostScriptMessage(string payload)
	{
		_messages.RaiseMessage(CefLinuxBrowserMessageNormalizer.Normalize(payload));
	}

	private void OnPaint(CefBrowser browser, CefPaintElementType type, CefRect[] dirtyRects, IntPtr buffer, int width, int height)
	{
		if (_disposed || type != CefPaintElementType.View || width <= 0 || height <= 0 || buffer == IntPtr.Zero)
		{
			return;
		}

		BrowserFrameReadyEventArgs frameReady;
		lock (_sync)
		{
			if (_frameBuffer.Viewport.Width != width || _frameBuffer.Viewport.Height != height)
			{
				_viewport = new BrowserViewport(width, height, _viewport.DeviceScaleFactor);
				_frameBuffer = new BrowserFrameBuffer(_viewport, BrowserPixelFormat.Bgra8888Premultiplied);
			}

			BrowserDirtyRect[] rects = MapDirtyRects(dirtyRects, width, height);
			if (rects.Length == 1 && IsWholeViewport(rects[0], width, height))
			{
				_frameBuffer.ApplyFullFrame(buffer);
			}
			else if (rects.Length > 0)
			{
				int sourceRowBytes = checked(width * BrowserFrameBuffer.BytesPerPixel);
				unsafe
				{
					_frameBuffer.ApplyDirtyFrame(
						new ReadOnlySpan<byte>(buffer.ToPointer(), checked(sourceRowBytes * height)),
						sourceRowBytes,
						rects);
				}
			}
			else
			{
				_frameBuffer.ApplyFullFrame(buffer);
			}

			frameReady = _frameBuffer.CreateFrameReadyEventArgs();
		}

		FrameReady?.Invoke(this, frameReady);
	}

	private static BrowserDirtyRect[] MapDirtyRects(CefRect[] dirtyRects, int viewportWidth, int viewportHeight)
	{
		int count = 0;
		var mapped = new BrowserDirtyRect[dirtyRects.Length];
		foreach (CefRect rect in dirtyRects)
		{
			if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0)
			{
				continue;
			}

			if (rect.Right > viewportWidth || rect.Bottom > viewportHeight)
			{
				continue;
			}

			mapped[count++] = new BrowserDirtyRect(rect.X, rect.Y, rect.Width, rect.Height);
		}

		if (count == mapped.Length)
		{
			return mapped;
		}

		var trimmed = new BrowserDirtyRect[count];
		Array.Copy(mapped, trimmed, count);
		return trimmed;
	}

	private static bool IsWholeViewport(BrowserDirtyRect rect, int viewportWidth, int viewportHeight)
	{
		return rect.X == 0 &&
			rect.Y == 0 &&
			rect.Width == viewportWidth &&
			rect.Height == viewportHeight;
	}

	private static void SendInputOnUi(CefBrowserHost host, BrowserInputEvent inputEvent)
	{
		switch (inputEvent)
		{
			case BrowserPointerEvent pointer:
				SendPointerEvent(host, pointer);
				break;
			case BrowserWheelEvent wheel:
				SendWheelEvent(host, wheel);
				break;
			case BrowserKeyEvent key:
				SendKeyEvent(host, key);
				break;
			case BrowserFocusEvent focus:
				host.SetFocus(focus.IsFocused);
				break;
			case BrowserTextInputEvent textInput:
				SendTextInput(host, textInput.Text);
				break;
			case BrowserImeCompositionEvent:
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(inputEvent), inputEvent, "Unsupported browser input event.");
		}

		host.Invalidate(CefPaintElementType.View);
	}

	private static void SendPointerEvent(CefBrowserHost host, BrowserPointerEvent pointer)
	{
		int x = (int)MathF.Round(pointer.X);
		int y = (int)MathF.Round(pointer.Y);
		uint flags = pointer.IsPrimaryButtonDown ? (uint)ToMouseButtonFlag(pointer.Button) : 0u;

		switch (pointer.EventType)
		{
			case BrowserPointerEventType.Move:
				host.SendMouseMoveEvent(CreateMouseEvent(x, y, flags), mouseLeave: false);
				break;
			case BrowserPointerEventType.Leave:
				host.SendMouseMoveEvent(CreateMouseEvent(x, y, flags), mouseLeave: true);
				break;
			case BrowserPointerEventType.Down:
				host.SendMouseClickEvent(CreateMouseEvent(x, y, flags), ToMouseButton(pointer.Button), mouseUp: false, clickCount: 1);
				break;
			case BrowserPointerEventType.Up:
				host.SendMouseClickEvent(CreateMouseEvent(x, y, 0u), ToMouseButton(pointer.Button), mouseUp: true, clickCount: 1);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(pointer), pointer, "Unsupported pointer event type.");
		}
	}

	private static CefMouseEvent CreateMouseEvent(int x, int y, uint modifiers)
	{
		return new CefMouseEvent { X = x, Y = y, Modifiers = modifiers };
	}

	private static void SendWheelEvent(CefBrowserHost host, BrowserWheelEvent wheel)
	{
		CefLinuxWheelDelta delta = CefLinuxBrowserInputTranslator.ToCefWheelDelta(wheel);
		host.SendMouseWheelEvent(
			CreateMouseEvent((int)MathF.Round(wheel.X), (int)MathF.Round(wheel.Y), 0u),
			delta.DeltaX,
			delta.DeltaY);
	}

	private static void SendKeyEvent(CefBrowserHost host, BrowserKeyEvent key)
	{
		int windowsKeyCode = ResolveWindowsKeyCode(key);
		host.SendKeyEvent(new CefKeyEvent
		{
			Type = key.EventType switch
			{
				BrowserKeyEventType.Down => CefKeyEventType.RawKeyDown,
				BrowserKeyEventType.Up => CefKeyEventType.KeyUp,
				BrowserKeyEventType.Character => CefKeyEventType.Char,
				_ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported key event type.")
			},
			WindowsKeyCode = windowsKeyCode,
			NativeKeyCode = windowsKeyCode,
			Modifiers = (uint)ToCefEventFlags(key.Modifiers)
		});
	}

	private static void SendTextInput(CefBrowserHost host, string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		foreach (char character in text)
		{
			host.SendKeyEvent(new CefKeyEvent
			{
				Type = CefKeyEventType.Char,
				WindowsKeyCode = character,
				NativeKeyCode = character
			});
		}
	}

	private static CefMouseButtonType ToMouseButton(BrowserPointerButton button)
	{
		return button switch
		{
			BrowserPointerButton.Left => CefMouseButtonType.Left,
			BrowserPointerButton.Middle => CefMouseButtonType.Middle,
			BrowserPointerButton.Right => CefMouseButtonType.Right,
			_ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported browser pointer button.")
		};
	}

	private static CefEventFlags ToMouseButtonFlag(BrowserPointerButton button)
	{
		return button switch
		{
			BrowserPointerButton.Left => CefEventFlags.LeftMouseButton,
			BrowserPointerButton.Middle => CefEventFlags.MiddleMouseButton,
			BrowserPointerButton.Right => CefEventFlags.RightMouseButton,
			_ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported browser pointer button.")
		};
	}

	private static int ResolveWindowsKeyCode(BrowserKeyEvent key)
	{
		if (string.IsNullOrWhiteSpace(key.Key))
		{
			return 0;
		}

		return key.Key.Length == 1
			? char.ToUpperInvariant(key.Key[0])
			: key.Key switch
			{
				"Enter" => 0x0D,
				"Escape" => 0x1B,
				"Tab" => 0x09,
				"Backspace" => 0x08,
				"Space" => 0x20,
				"ArrowLeft" => 0x25,
				"ArrowUp" => 0x26,
				"ArrowRight" => 0x27,
				"ArrowDown" => 0x28,
				"Delete" => 0x2E,
				"Home" => 0x24,
				"End" => 0x23,
				"PageUp" => 0x21,
				"PageDown" => 0x22,
				_ => 0
			};
	}

	private static CefEventFlags ToCefEventFlags(BrowserInputModifiers modifiers)
	{
		CefEventFlags flags = CefEventFlags.None;
		if ((modifiers & BrowserInputModifiers.Shift) != 0)
		{
			flags |= CefEventFlags.ShiftDown;
		}
		if ((modifiers & BrowserInputModifiers.Control) != 0)
		{
			flags |= CefEventFlags.ControlDown;
		}
		if ((modifiers & BrowserInputModifiers.Alt) != 0)
		{
			flags |= CefEventFlags.AltDown;
		}
		if ((modifiers & BrowserInputModifiers.Meta) != 0)
		{
			flags |= CefEventFlags.CommandDown;
		}

		return flags;
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(CefLinuxBrowserSurface));
		}
	}

	private sealed class CefLinuxBrowserClient : CefClient
	{
		private readonly CefLinuxBrowserSurface _surface;

		public CefLinuxBrowserClient(CefLinuxBrowserSurface surface)
		{
			_surface = surface;
		}

		protected override CefRenderHandler GetRenderHandler()
		{
			return _surface._renderHandler;
		}

		protected override CefLifeSpanHandler GetLifeSpanHandler()
		{
			return _surface._lifeSpanHandler;
		}

		protected override CefLoadHandler GetLoadHandler()
		{
			return _surface._loadHandler;
		}

		protected override bool OnProcessMessageReceived(
			CefBrowser browser,
			CefFrame frame,
			CefProcessId sourceProcess,
			CefProcessMessage message)
		{
			if (!string.Equals(message.Name, CefLinuxProcessMessages.HostMessage, StringComparison.Ordinal))
			{
				return false;
			}

			string payload = message.ArgumentList.GetString(0) ?? string.Empty;
			_surface.OnHostScriptMessage(payload);
			return true;
		}
	}

	private sealed class SurfaceRenderHandler : CefRenderHandler
	{
		private readonly CefLinuxBrowserSurface _surface;

		public SurfaceRenderHandler(CefLinuxBrowserSurface surface)
		{
			_surface = surface;
		}

		protected override void GetViewRect(CefBrowser browser, ref CefRect rect)
		{
			BrowserViewport viewport = _surface.Viewport;
			rect = new CefRect(0, 0, viewport.Width, viewport.Height);
		}

		protected override void OnPaint(
			CefBrowser browser,
			CefPaintElementType type,
			CefRect[] dirtyRects,
			IntPtr buffer,
			int width,
			int height)
		{
			_surface.OnPaint(browser, type, dirtyRects, buffer, width, height);
		}
	}

	private sealed class SurfaceLifeSpanHandler : CefLifeSpanHandler
	{
		private readonly CefLinuxBrowserSurface _surface;

		public SurfaceLifeSpanHandler(CefLinuxBrowserSurface surface)
		{
			_surface = surface;
		}

		protected override void OnAfterCreated(CefBrowser browser)
		{
			_surface.OnAfterCreated(browser);
		}
	}

	private sealed class SurfaceLoadHandler : CefLoadHandler
	{
		private readonly CefLinuxBrowserSurface _surface;

		public SurfaceLoadHandler(CefLinuxBrowserSurface surface)
		{
			_surface = surface;
		}

		protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
		{
			_surface.OnLoadEnd(browser, frame);
		}
	}
}
