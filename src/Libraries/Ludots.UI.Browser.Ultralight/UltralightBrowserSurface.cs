using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ludots.UI.Browser;
using UltralightNet;

namespace Ludots.UI.Browser.Ultralight;

internal sealed class UltralightBrowserSurface : IBrowserSurface, IBrowserSharedBufferSurface
{
	private const string MessagePrefix = "__LUDOTS_MSG__:";
	private const string SharedReadChannel = "ludots.dataplane.shared-read";

	private readonly object _sync = new();
	private readonly View _view;
	private readonly UltralightBrowserMessageBridge _messages;
	private readonly BrowserSharedBufferBridge _sharedBuffers = new();
	private readonly IBrowserResourceResolver? _resourceResolver;
	private readonly string _surfaceKey;
	private readonly string _stagingRoot;
	private readonly CancellationTokenSource _pumpCts = new();
	private readonly Task _pumpTask;

	private BrowserViewport _viewport;
	private BrowserFrameBuffer _frameBuffer;
	private bool _disposed;
	private string? _lastNavigationFailure;

	public UltralightBrowserSurface(
		BrowserViewport viewport,
		IBrowserResourceResolver? resourceResolver,
		UltralightBrowserRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		_viewport = viewport;
		_resourceResolver = resourceResolver;
		_frameBuffer = new BrowserFrameBuffer(viewport, BrowserPixelFormat.Bgra8888Premultiplied);
		Id = BrowserSurfaceId.New();
		_surfaceKey = "surface-" + Id.Value.ToString("N");
		_stagingRoot = Path.Combine(options.CacheRootPath, "surfaces", _surfaceKey);
		Directory.CreateDirectory(_stagingRoot);
		_messages = new UltralightBrowserMessageBridge(this);
		_messages.MessageReceived += OnBridgeMessageReceived;

		_view = UltralightProcessRuntime.Renderer.CreateView(
			(uint)Math.Max(1, viewport.Width),
			(uint)Math.Max(1, viewport.Height),
			new ULViewConfig
			{
				IsAccelerated = false,
				IsTransparent = true,
				InitialFocus = true
			});
		_view.OnAddConsoleMessage += OnConsoleMessage;
		_view.OnDOMReady += OnDomReady;
		_view.OnFinishLoading += OnFinishLoading;
		_view.OnFailLoading += OnFailLoading;
		_pumpTask = Task.Run(() => PumpLoopAsync(_pumpCts.Token));
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

	public BrowserSharedBufferBridge SharedBuffers => _sharedBuffers;

	public async ValueTask NavigateAsync(BrowserNavigationRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		string navigationUrl = await ResolveNavigationUrlAsync(request.Uri, cancellationToken).ConfigureAwait(false);
		_lastNavigationFailure = null;
		_view.URL = navigationUrl;
		await WaitUntilIdleAsync(cancellationToken).ConfigureAwait(false);
		if (!string.IsNullOrWhiteSpace(_lastNavigationFailure))
		{
			throw new InvalidOperationException(
				$"Ultralight navigation to '{navigationUrl}' failed: {_lastNavigationFailure}");
		}

		QueueFacadeInjection();
		CaptureFrame();
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
			_frameBuffer = new BrowserFrameBuffer(viewport, BrowserPixelFormat.Bgra8888Premultiplied);
		}

		uint width = (uint)Math.Max(1, viewport.Width);
		uint height = (uint)Math.Max(1, viewport.Height);
		_view.Resize(in width, in height);
		UltralightProcessRuntime.UpdateAndRender();
		CaptureFrame();
		return ValueTask.CompletedTask;
	}

	public ValueTask SendInputAsync(BrowserInputEvent inputEvent, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(inputEvent);
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();

		switch (inputEvent)
		{
			case BrowserPointerEvent pointer:
				_view.FireMouseEvent(UltralightBrowserInputTranslator.ToMouseEvent(pointer));
				break;
			case BrowserWheelEvent wheel:
				_view.FireScrollEvent(UltralightBrowserInputTranslator.ToScrollEvent(wheel));
				break;
			case BrowserKeyEvent key:
				_view.FireKeyEvent(UltralightBrowserInputTranslator.ToKeyEvent(key));
				break;
			case BrowserFocusEvent focus:
				if (focus.IsFocused)
				{
					_view.Focus();
				}
				else
				{
					_view.Unfocus();
				}
				break;
			case BrowserTextInputEvent textInput:
				_view.FireKeyEvent(UltralightBrowserInputTranslator.ToTextInputEvent(textInput.Text));
				break;
			case BrowserImeCompositionEvent:
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(inputEvent), inputEvent, "Unsupported browser input event.");
		}

		UltralightProcessRuntime.UpdateAndRender();
		CaptureFrame();
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
		_messages.MessageReceived -= OnBridgeMessageReceived;
		_pumpCts.Cancel();
		try
		{
			_pumpTask.Wait(TimeSpan.FromSeconds(2));
		}
		catch (AggregateException)
		{
		}

		_view.OnAddConsoleMessage -= OnConsoleMessage;
		_view.OnDOMReady -= OnDomReady;
		_view.OnFinishLoading -= OnFinishLoading;
		_view.OnFailLoading -= OnFailLoading;
		_view.Dispose();
		_pumpCts.Dispose();
		return ValueTask.CompletedTask;
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

	internal ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		string? exception;
		_ = _view.EvaluateScript(script, out exception);
		if (!string.IsNullOrWhiteSpace(exception))
		{
			throw new InvalidOperationException($"Ultralight script execution failed: {exception}");
		}

		UltralightProcessRuntime.UpdateAndRender();
		CaptureFrame();
		return ValueTask.CompletedTask;
	}

	private async Task PumpLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				UltralightProcessRuntime.UpdateAndRender();
				CaptureFrame();
				await Task.Delay(16, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (ObjectDisposedException) when (_disposed)
			{
				return;
			}
		}
	}

	private async Task WaitUntilIdleAsync(CancellationToken cancellationToken)
	{
		for (int i = 0; i < 180; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			UltralightProcessRuntime.UpdateAndRender();
			if (!_view.IsLoading)
			{
				return;
			}

			await Task.Delay(16, cancellationToken).ConfigureAwait(false);
		}
	}

	private void OnDomReady(ulong frameId, bool isMainFrame, string url)
	{
		if (isMainFrame)
		{
			QueueFacadeInjection();
		}
	}

	private void OnFinishLoading(ulong frameId, bool isMainFrame, string url)
	{
		if (isMainFrame)
		{
			QueueFacadeInjection();
			CaptureFrame();
		}
	}

	private void OnFailLoading(
		ulong frameId,
		bool isMainFrame,
		string url,
		string description,
		string errorDomain,
		int errorCode)
	{
		if (!isMainFrame)
		{
			return;
		}

		_lastNavigationFailure =
			$"domain={errorDomain} code={errorCode} description={description} url={url}";
	}

	private void OnConsoleMessage(
		ULMessageSource source,
		ULMessageLevel level,
		string message,
		uint lineNumber,
		uint columnNumber,
		string sourceId)
	{
		if (string.IsNullOrWhiteSpace(message) ||
		    !message.StartsWith(MessagePrefix, StringComparison.Ordinal))
		{
			return;
		}

		string json = message.Substring(MessagePrefix.Length);
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		string channel = root.TryGetProperty("channel", out JsonElement channelElement)
			? channelElement.GetString() ?? BrowserMessageChannels.Application
			: BrowserMessageChannels.Application;
		string payload = root.TryGetProperty("payload", out JsonElement payloadElement)
			? payloadElement.ValueKind == JsonValueKind.String
				? payloadElement.GetString() ?? string.Empty
				: payloadElement.GetRawText()
			: string.Empty;
		_messages.RaiseMessage(new BrowserScriptMessage(channel, payload));
	}

	private void OnBridgeMessageReceived(object? sender, BrowserScriptMessage message)
	{
		if (!string.Equals(message.Channel, SharedReadChannel, StringComparison.Ordinal))
		{
			return;
		}

		_ = HandleSharedReadAsync(message.Payload);
	}

	private async Task HandleSharedReadAsync(string payloadJson)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(payloadJson);
			JsonElement root = document.RootElement;
			string requestId = root.GetProperty("requestId").GetString()
				?? throw new InvalidOperationException("Shared-read requestId is required.");
			string bufferId = root.GetProperty("bufferId").GetString()
				?? throw new InvalidOperationException("Shared-read bufferId is required.");
			int byteOffset = root.GetProperty("byteOffset").GetInt32();
			int byteLength = root.GetProperty("byteLength").GetInt32();
			long sequence = root.GetProperty("sequence").GetInt64();
			byte[] bytes = _sharedBuffers.ReadSharedBuffer(bufferId, byteOffset, byteLength, sequence);
			string base64 = Convert.ToBase64String(bytes);
			string script =
				$"window.__ludotsResolveSharedRead({JsonSerializer.Serialize(requestId)}, {JsonSerializer.Serialize(base64)}, null);";
			await ExecuteScriptAsync(script, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			string requestId = "unknown";
			try
			{
				using JsonDocument document = JsonDocument.Parse(payloadJson);
				requestId = document.RootElement.GetProperty("requestId").GetString() ?? "unknown";
			}
			catch
			{
			}

			string script =
				$"window.__ludotsResolveSharedRead({JsonSerializer.Serialize(requestId)}, null, {JsonSerializer.Serialize(ex.Message)});";
			await ExecuteScriptAsync(script, CancellationToken.None).ConfigureAwait(false);
		}
	}

	private void QueueFacadeInjection()
	{
		_ = InjectFacadeAsync();
	}

	private async Task InjectFacadeAsync()
	{
		try
		{
			await ExecuteScriptAsync(UltralightDataPlaneFacadeScript.Create(_surfaceKey), CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (ObjectDisposedException) when (_disposed)
		{
		}
		catch (InvalidOperationException) when (_disposed)
		{
		}
	}

	private unsafe void CaptureFrame()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			ULSurface? maybeSurface = _view.Surface;
			if (maybeSurface is null)
			{
				return;
			}

			ULSurface surface = maybeSurface.Value;
			ULBitmap? bitmap = surface.Bitmap;
			if (bitmap == null || bitmap.IsEmpty)
			{
				return;
			}

			int width = checked((int)bitmap.Width);
			int height = checked((int)bitmap.Height);
			if (width <= 0 || height <= 0)
			{
				return;
			}

			byte* pixels = bitmap.LockPixels();
			try
			{
				int rowBytes = checked((int)bitmap.RowBytes);
				if (_frameBuffer.Viewport.Width != width || _frameBuffer.Viewport.Height != height)
				{
					_viewport = new BrowserViewport(width, height, _viewport.DeviceScaleFactor);
					_frameBuffer = new BrowserFrameBuffer(_viewport, BrowserPixelFormat.Bgra8888Premultiplied);
				}

				ULIntRect dirty = surface.DirtyBounds;
				bool hasPartialDirty =
					!dirty.IsEmpty &&
					dirty.Left >= 0 &&
					dirty.Top >= 0 &&
					dirty.Right <= width &&
					dirty.Bottom <= height &&
					dirty.Right > dirty.Left &&
					dirty.Bottom > dirty.Top &&
					!(dirty.Left == 0 && dirty.Top == 0 && dirty.Right == width && dirty.Bottom == height);

				if (hasPartialDirty)
				{
					var rect = new BrowserDirtyRect(
						dirty.Left,
						dirty.Top,
						dirty.Right - dirty.Left,
						dirty.Bottom - dirty.Top);
					_frameBuffer.ApplyDirtyFrame((IntPtr)pixels, rowBytes, rect);
				}
				else if (rowBytes == _frameBuffer.RowBytes)
				{
					_frameBuffer.ApplyFullFrame((IntPtr)pixels);
				}
				else
				{
					var packed = new byte[checked(_frameBuffer.RowBytes * height)];
					for (int y = 0; y < height; y++)
					{
						new ReadOnlySpan<byte>(pixels + (y * rowBytes), _frameBuffer.RowBytes)
							.CopyTo(packed.AsSpan(y * _frameBuffer.RowBytes, _frameBuffer.RowBytes));
					}

					_frameBuffer.ApplyFullFrame(packed);
				}

				BrowserFrameReadyEventArgs frameReady = _frameBuffer.CreateFrameReadyEventArgs();
				surface.ClearDirtyBounds();
				FrameReady?.Invoke(this, frameReady);
			}
			finally
			{
				bitmap.UnlockPixels();
			}
		}
	}

	private async Task<string> ResolveNavigationUrlAsync(Uri uri, CancellationToken cancellationToken)
	{
		if (string.Equals(uri.Scheme, BrowserLocalAppUri.Scheme, StringComparison.OrdinalIgnoreCase))
		{
			if (_resourceResolver == null)
			{
				throw new InvalidOperationException(
					$"Local app navigation to '{uri}' requires an IBrowserResourceResolver.");
			}

			return await UltralightLocalAppStager
				.StageAsync(_stagingRoot, uri, _resourceResolver, cancellationToken)
				.ConfigureAwait(false);
		}

		return uri.AbsoluteUri;
	}

	private void ThrowIfDisposed()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(UltralightBrowserSurface));
		}
	}
}
