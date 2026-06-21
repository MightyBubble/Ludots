using System;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.OffScreen;
using Ludots.UI.Browser;

namespace Ludots.UI.Browser.Cef;

internal sealed class CefBrowserSurface : IBrowserSurface
{
	private readonly object _sync = new();
	private readonly ChromiumWebBrowser _browser;
	private readonly CefBrowserMessageBridge _messages;
	private readonly IBrowserResourceResolver? _resourceResolver;
	private readonly CefBrowserSurfaceRegistry _registry;
	private readonly TaskCompletionSource _browserInitialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private BrowserViewport _viewport;
	private BrowserFrameBuffer _frameBuffer;
	private int? _browserIdentifier;
	private bool _disposed;

	public CefBrowserSurface(
		BrowserViewport viewport,
		IBrowserResourceResolver? resourceResolver,
		CefBrowserSurfaceRegistry registry)
	{
		_viewport = viewport;
		_resourceResolver = resourceResolver;
		_registry = registry ?? throw new ArgumentNullException(nameof(registry));
		_frameBuffer = new BrowserFrameBuffer(viewport, BrowserPixelFormat.Bgra8888Premultiplied);
		Id = BrowserSurfaceId.New();

		var browserSettings = new BrowserSettings
		{
			BackgroundColor = global::CefSharp.Cef.ColorSetARGB(0, 0, 0, 0),
			WindowlessFrameRate = 60
		};

		_browser = new ChromiumWebBrowser(
			"about:blank",
			browserSettings,
			requestContext: null,
			automaticallyCreateBrowser: true,
			onAfterBrowserCreated: OnAfterBrowserCreated);
		_browser.Size = new Size(viewport.Width, viewport.Height);
		_browser.Paint += OnPaint;
		_browser.JavascriptMessageReceived += OnJavascriptMessageReceived;
		_browser.BrowserInitialized += OnBrowserInitialized;
		if (_browser.IsBrowserInitialized)
		{
			_browserInitialized.TrySetResult();
		}

		_messages = new CefBrowserMessageBridge(_browser, this);
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

		if (_browser.IsBrowserInitialized)
		{
			_browserInitialized.TrySetResult();
		}
		await _browserInitialized.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		string navigationUrl = ResolveNavigationUrl(request.Uri);
		LoadUrlAsyncResponse response = await _browser.LoadUrlAsync(navigationUrl).ConfigureAwait(false);
		if (!response.Success)
		{
			throw new InvalidOperationException(
				$"CEF failed to navigate to '{navigationUrl}'. ErrorCode={response.ErrorCode}, HttpStatusCode={response.HttpStatusCode}.");
		}

		_browser.BrowserCore?.GetHost()?.Invalidate(PaintElementType.View);
	}

	public async ValueTask ResizeAsync(BrowserViewport viewport, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		lock (_sync)
		{
			if (_viewport.Equals(viewport))
			{
				return;
			}

			_viewport = viewport;
		}

		_browser.Size = new Size(viewport.Width, viewport.Height);
		if (_browser.IsBrowserInitialized)
		{
			await _browser.ResizeAsync(viewport.Width, viewport.Height).ConfigureAwait(false);
			_browser.BrowserCore?.GetHost()?.WasResized();
		}
	}

	public ValueTask SendInputAsync(BrowserInputEvent inputEvent, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(inputEvent);
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		IBrowserHost? host = _browser.BrowserCore?.GetHost();
		if (host == null)
		{
			return ValueTask.CompletedTask;
		}

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

		host.Invalidate(PaintElementType.View);
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

		_browser.Paint -= OnPaint;
		_browser.JavascriptMessageReceived -= OnJavascriptMessageReceived;
		_browser.BrowserInitialized -= OnBrowserInitialized;

		_browser.Dispose();
		return ValueTask.CompletedTask;
	}

	internal IResourceHandler? ResolveResource(string requestUrl)
	{
		if (_resourceResolver == null)
		{
			return null;
		}

		if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out Uri? uri))
		{
			return null;
		}

		BrowserResource? resource = _resourceResolver.ResolveAsync(uri).AsTask().GetAwaiter().GetResult();
		return resource == null ? null : CefBrowserSchemeHandlerFactory.CreateResourceHandler(resource);
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
		await _browserInitialized.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		IFrame? mainFrame = _browser.GetMainFrame();
		if (mainFrame == null || !mainFrame.IsValid)
		{
			return;
		}

		mainFrame.ExecuteJavaScriptAsync(script);
	}

	private void OnAfterBrowserCreated(IBrowser browser)
	{
		_browserIdentifier = browser.Identifier;
		_registry.Register(browser.Identifier, this);
	}

	private void OnBrowserInitialized(object? sender, EventArgs e)
	{
		_browserInitialized.TrySetResult();
		if (_browser.BrowserCore?.GetHost() is IBrowserHost host)
		{
			host.WasResized();
			host.SetFocus(true);
			host.Invalidate(PaintElementType.View);
		}
	}

	private unsafe void OnPaint(object? sender, OnPaintEventArgs e)
	{
		if (_disposed || e.IsPopup || e.Width <= 0 || e.Height <= 0 || e.BufferHandle == IntPtr.Zero)
		{
			return;
		}

		BrowserFrameReadyEventArgs frameReady;
		lock (_sync)
		{
			if (_frameBuffer.Viewport.Width != e.Width || _frameBuffer.Viewport.Height != e.Height)
			{
				_viewport = new BrowserViewport(e.Width, e.Height, _viewport.DeviceScaleFactor);
				_frameBuffer = new BrowserFrameBuffer(_viewport, BrowserPixelFormat.Bgra8888Premultiplied);
			}

			BrowserDirtyRect? dirtyRect = TryCreateDirtyRect(e.DirtyRect, e.Width, e.Height);
			if (dirtyRect is BrowserDirtyRect rect && !IsWholeViewport(rect, e.Width, e.Height))
			{
				_frameBuffer.ApplyDirtyFrame(
					e.BufferHandle,
					checked(e.Width * BrowserFrameBuffer.BytesPerPixel),
					rect);
			}
			else
			{
				_frameBuffer.ApplyFullFrame(e.BufferHandle);
			}

			frameReady = _frameBuffer.CreateFrameReadyEventArgs();
		}

		FrameReady?.Invoke(this, frameReady);
	}

	private void OnJavascriptMessageReceived(object? sender, JavascriptMessageReceivedEventArgs e)
	{
		string payload = NormalizeMessagePayload(e.Message);
		_messages.RaiseMessage(new BrowserScriptMessage("cefsharp", payload));
	}

	private void SendPointerEvent(IBrowserHost host, BrowserPointerEvent pointer)
	{
		int x = (int)MathF.Round(pointer.X);
		int y = (int)MathF.Round(pointer.Y);
		CefEventFlags flags = pointer.IsPrimaryButtonDown ? CefEventFlags.LeftMouseButton : CefEventFlags.None;

		switch (pointer.EventType)
		{
			case BrowserPointerEventType.Move:
				host.SendMouseMoveEvent(new MouseEvent(x, y, flags), mouseLeave: false);
				break;
			case BrowserPointerEventType.Leave:
				host.SendMouseMoveEvent(new MouseEvent(x, y, flags), mouseLeave: true);
				break;
			case BrowserPointerEventType.Down:
				host.SendMouseClickEvent(new MouseEvent(x, y, flags), ToMouseButton(pointer.Button), mouseUp: false, clickCount: 1);
				break;
			case BrowserPointerEventType.Up:
				host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.None), ToMouseButton(pointer.Button), mouseUp: true, clickCount: 1);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(pointer), pointer, "Unsupported pointer event type.");
		}
	}

	private static void SendWheelEvent(IBrowserHost host, BrowserWheelEvent wheel)
	{
		CefWheelDelta delta = CefBrowserInputTranslator.ToCefWheelDelta(wheel);
		host.SendMouseWheelEvent(
			new MouseEvent((int)MathF.Round(wheel.X), (int)MathF.Round(wheel.Y), CefEventFlags.None),
			delta.DeltaX,
			delta.DeltaY);
	}

	private static void SendKeyEvent(IBrowserHost host, BrowserKeyEvent key)
	{
		int windowsKeyCode = ResolveWindowsKeyCode(key);
		var cefKeyEvent = new KeyEvent
		{
			Type = key.EventType switch
			{
				BrowserKeyEventType.Down => KeyEventType.RawKeyDown,
				BrowserKeyEventType.Up => KeyEventType.KeyUp,
				BrowserKeyEventType.Character => KeyEventType.Char,
				_ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported key event type.")
			},
			WindowsKeyCode = windowsKeyCode,
			NativeKeyCode = windowsKeyCode,
			Modifiers = ToCefEventFlags(key.Modifiers)
		};

		host.SendKeyEvent(cefKeyEvent);
	}

	private static void SendTextInput(IBrowserHost host, string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}

		foreach (char character in text)
		{
			host.SendKeyEvent(new KeyEvent
			{
				Type = KeyEventType.Char,
				WindowsKeyCode = character,
				NativeKeyCode = character
			});
		}
	}

	private static MouseButtonType ToMouseButton(BrowserPointerButton button)
	{
		return button switch
		{
			BrowserPointerButton.Left => MouseButtonType.Left,
			BrowserPointerButton.Middle => MouseButtonType.Middle,
			BrowserPointerButton.Right => MouseButtonType.Right,
			_ => MouseButtonType.Left
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

	private static string ResolveNavigationUrl(Uri uri)
	{
		if (string.Equals(uri.Scheme, "ludots-browser-showcase", StringComparison.OrdinalIgnoreCase))
		{
			string absolutePath = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
			if (!absolutePath.StartsWith("/", StringComparison.Ordinal))
			{
				absolutePath = "/" + absolutePath;
			}

			return $"{CefBrowserRuntime.LocalAppSchemeName}://{CefBrowserRuntime.LocalAppHostName}{absolutePath}{uri.Query}{uri.Fragment}";
		}

		return uri.ToString();
	}

	private static string NormalizeMessagePayload(object? message)
	{
		if (message == null)
		{
			return string.Empty;
		}

		return message switch
		{
			string text => text,
			JsonElement json => json.GetRawText(),
			_ => JsonSerializer.Serialize(message)
		};
	}

	private static BrowserDirtyRect? TryCreateDirtyRect(CefSharp.Structs.Rect rect, int viewportWidth, int viewportHeight)
	{
		if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0)
		{
			return null;
		}

		long right = (long)rect.X + rect.Width;
		long bottom = (long)rect.Y + rect.Height;
		if (right > viewportWidth || bottom > viewportHeight)
		{
			return null;
		}

		return new BrowserDirtyRect(rect.X, rect.Y, rect.Width, rect.Height);
	}

	private static bool IsWholeViewport(BrowserDirtyRect rect, int viewportWidth, int viewportHeight)
	{
		return rect.X == 0 &&
			rect.Y == 0 &&
			rect.Width == viewportWidth &&
			rect.Height == viewportHeight;
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(CefBrowserSurface));
		}
	}
}
