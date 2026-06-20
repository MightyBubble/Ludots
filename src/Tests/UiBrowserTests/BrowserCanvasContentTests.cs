using Ludots.UI;
using Ludots.UI.Browser;
using Ludots.UI.Compose;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using NUnit.Framework;
using SkiaSharp;
using Ludots.UI.Browser.Skia;
using SkiaBrowserCanvasContent = Ludots.UI.Browser.Skia.BrowserCanvasContent;

namespace Ludots.Tests.UiBrowser;

[TestFixture]
public sealed class BrowserCanvasContentTests
{
	[Test]
	public void DrawFrame_RendersBgraFrameIntoSkiaCanvas()
	{
		BrowserFrame frame = CreateSolidFrame(2, 2, b: 10, g: 20, r: 200, a: 255);
		var renderer = new SkiaBrowserFrameRenderer();
		using var bitmap = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul));
		using var canvas = new SKCanvas(bitmap);
		canvas.Clear(SKColors.Transparent);

		renderer.DrawFrame(canvas, new SKRect(0, 0, 4, 4), frame);

		SKColor pixel = bitmap.GetPixel(2, 2);
		Assert.That(pixel.Red, Is.EqualTo(200));
		Assert.That(pixel.Green, Is.EqualTo(20));
		Assert.That(pixel.Blue, Is.EqualTo(10));
		Assert.That(pixel.Alpha, Is.EqualTo(255));
	}

	[Test]
	public void BrowserCanvasContent_RendersThroughExistingUiCanvasNode()
	{
		BrowserFrame frame = CreateSolidFrame(2, 2, b: 30, g: 120, r: 220, a: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new SkiaBrowserCanvasContent(surface);
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			Ui.Canvas(content).Width(16).Height(16));

		using var bitmap = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul));
		using var canvas = new SKCanvas(bitmap);
		canvas.Clear(SKColors.Transparent);
		var renderer = new SkiaUiRenderer();
		renderer.RenderToCanvas(scene, canvas, 16, 16);

		SKColor pixel = bitmap.GetPixel(8, 8);
		Assert.That(pixel.Red, Is.EqualTo(220));
		Assert.That(pixel.Green, Is.EqualTo(120));
		Assert.That(pixel.Blue, Is.EqualTo(30));
		Assert.That(pixel.Alpha, Is.EqualTo(255));
	}

	[Test]
	public void DrawFrame_CompositesTransparentBrowserPixelsOverHostCanvas()
	{
		var renderer = new SkiaBrowserFrameRenderer();
		var frame = new BrowserFrame(
			new BrowserViewport(2, 2),
			BrowserPixelFormat.Bgra8888Premultiplied,
			new byte[]
			{
				0, 0, 128, 128,
				0, 0, 128, 128,
				0, 0, 128, 128,
				0, 0, 128, 128
			},
			2 * BrowserFrameBuffer.BytesPerPixel,
			new[] { new BrowserDirtyRect(0, 0, 2, 2) },
			1);

		using var bitmap = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul));
		using var canvas = new SKCanvas(bitmap);
		canvas.Clear(SKColors.Blue);

		renderer.DrawFrame(canvas, new SKRect(0, 0, 4, 4), frame);

		SKColor pixel = bitmap.GetPixel(2, 2);
		Assert.That(pixel.Red, Is.InRange(127, 129));
		Assert.That(pixel.Green, Is.EqualTo(0));
		Assert.That(pixel.Blue, Is.InRange(126, 128));
		Assert.That(pixel.Alpha, Is.EqualTo(255));
	}

	[Test]
	public void HandleInput_DownThenMove_KeepsPrimaryButtonStateForBrowserDrag()
	{
		BrowserFrame frame = CreateSolidFrame(200, 100, b: 10, g: 20, r: 30, a: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new BrowserSurfaceCanvasContent(surface);
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			Ui.Canvas(content).Width(200).Height(100));
		var root = new UIRoot(new NullUiRenderer());
		root.MountScene(scene);
		root.Resize(200, 100);

		bool downHandled = root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Down,
			X = 40,
			Y = 30
		});
		bool moveHandled = root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Move,
			X = 60,
			Y = 45
		});

		Assert.That(downHandled, Is.True);
		Assert.That(moveHandled, Is.True);
		Assert.That(surface.InputEvents.Count, Is.EqualTo(3));
		Assert.That(surface.InputEvents[0], Is.TypeOf<BrowserFocusEvent>());
		Assert.That(surface.InputEvents[1], Is.EqualTo(new BrowserPointerEvent(BrowserPointerEventType.Down, 0, 40, 30, BrowserPointerButton.Left, true)));
		Assert.That(surface.InputEvents[2], Is.EqualTo(new BrowserPointerEvent(BrowserPointerEventType.Move, 0, 60, 45, BrowserPointerButton.None, true)));
	}

	[Test]
	public void HandleInput_DownThenUpOutsideCanvas_StillDeliversBrowserPointerUp()
	{
		BrowserFrame frame = CreateSolidFrame(200, 100, b: 10, g: 20, r: 30, a: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new BrowserSurfaceCanvasContent(surface);
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			Ui.Canvas(content).Width(200).Height(100));
		var root = new UIRoot(new NullUiRenderer());
		root.MountScene(scene);
		root.Resize(200, 100);

		bool downHandled = root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Down,
			X = 40,
			Y = 30
		});
		bool upHandled = root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Up,
			X = 260,
			Y = 180
		});

		Assert.That(downHandled, Is.True);
		Assert.That(upHandled, Is.True);
		Assert.That(surface.InputEvents[^1], Is.EqualTo(new BrowserPointerEvent(BrowserPointerEventType.Up, 0, 199, 99, BrowserPointerButton.Left, false)));
	}

	[Test]
	public void Draw_WhenCanvasSizeChanges_ResizesBrowserSurfaceToMatchLayout()
	{
		BrowserFrame frame = CreateSolidFrame(2, 2, b: 30, g: 120, r: 220, a: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new SkiaBrowserCanvasContent(surface);

		using var bitmap = new SKBitmap(new SKImageInfo(320, 180, SKColorType.Bgra8888, SKAlphaType.Premul));
		using var canvas = new SKCanvas(bitmap);
		content.Draw(canvas, new SKRect(0, 0, 320, 180));

		Assert.That(surface.ResizeEvents.Count, Is.EqualTo(1));
		Assert.That(surface.ResizeEvents[0], Is.EqualTo(new BrowserViewport(320, 180)));
	}

	[Test]
	public void HitTest_AlphaMode_IgnoresTransparentBrowserPixels()
	{
		BrowserFrame frame = CreateTwoPixelAlphaFrame(leftAlpha: 0, rightAlpha: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new BrowserSurfaceCanvasContent(surface, hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			Ui.Canvas(content).Width(200).Height(100));
		scene.Layout(200, 100);

		UiNode? transparentHit = scene.HitTest(40, 50);
		UiNode? opaqueHit = scene.HitTest(160, 50);

		Assert.That(transparentHit?.TagName, Is.Not.EqualTo("canvas"));
		Assert.That(opaqueHit?.TagName, Is.EqualTo("canvas"));
	}

	[Test]
	public void HandleInput_AlphaMode_DownOnTransparentPixel_DoesNotSendBrowserInput()
	{
		BrowserFrame frame = CreateTwoPixelAlphaFrame(leftAlpha: 0, rightAlpha: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new BrowserSurfaceCanvasContent(surface, hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			Ui.Canvas(content).Width(200).Height(100));
		var root = new UIRoot(new NullUiRenderer());
		root.MountScene(scene);
		root.Resize(200, 100);

		bool handled = root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Down,
			X = 40,
			Y = 50
		});

		Assert.That(handled, Is.False);
		Assert.That(surface.InputEvents, Is.Empty);
	}

	[Test]
	public void HandleInput_AlphaMode_DownOnOpaquePixel_SendsBrowserInput()
	{
		BrowserFrame frame = CreateTwoPixelAlphaFrame(leftAlpha: 0, rightAlpha: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new BrowserSurfaceCanvasContent(surface, hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			Ui.Canvas(content).Width(200).Height(100));
		var root = new UIRoot(new NullUiRenderer());
		root.MountScene(scene);
		root.Resize(200, 100);

		bool handled = root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Down,
			X = 160,
			Y = 50
		});

		Assert.That(handled, Is.True);
		Assert.That(surface.InputEvents.Count, Is.EqualTo(2));
		Assert.That(surface.InputEvents[0], Is.TypeOf<BrowserFocusEvent>());
		Assert.That(surface.InputEvents[1], Is.EqualTo(new BrowserPointerEvent(BrowserPointerEventType.Down, 0, 160, 50, BrowserPointerButton.Left, true)));
	}

	[Test]
	public void KeyboardInput_FocusedBrowserCanvas_RoutesThroughUiRoot()
	{
		BrowserFrame frame = CreateSolidFrame(200, 100, b: 10, g: 20, r: 30, a: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new BrowserSurfaceCanvasContent(surface);
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			Ui.Canvas(content).Width(200).Height(100));
		var root = new UIRoot(new NullUiRenderer());
		root.MountScene(scene);
		root.Resize(200, 100);

		root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Down,
			X = 40,
			Y = 30
		});
		bool handled = root.HandleInput(new KeyboardEvent
		{
			DeviceType = InputDeviceType.Keyboard,
			Action = KeyboardAction.Character,
			Key = "a",
			Code = "KeyA",
			Text = "a"
		});

		Assert.That(handled, Is.True);
		Assert.That(surface.InputEvents[^1], Is.EqualTo(new BrowserTextInputEvent("a")));
	}

	[Test]
	public void PointerDownOutsideAlphaHitBrowser_ClearsBrowserFocus()
	{
		BrowserFrame frame = CreateTwoPixelAlphaFrame(leftAlpha: 0, rightAlpha: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new BrowserSurfaceCanvasContent(surface, hitTestOptions: BrowserSurfaceHitTestOptions.Alpha());
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			Ui.Canvas(content).Width(200).Height(100));
		var root = new UIRoot(new NullUiRenderer());
		root.MountScene(scene);
		root.Resize(200, 100);

		root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Down,
			X = 160,
			Y = 50
		});
		root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Up,
			X = 160,
			Y = 50
		});

		bool handled = root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Down,
			X = 40,
			Y = 50
		});

		Assert.That(handled, Is.False);
		Assert.That(surface.InputEvents[^1], Is.EqualTo(new BrowserFocusEvent(false)));
	}

	[Test]
	public void TryReadLatestFrame_UsesCurrentSurfaceFrameWithoutCloneContractChange()
	{
		BrowserFrame frame = CreateSolidFrame(4, 2, b: 11, g: 22, r: 33, a: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new BrowserSurfaceCanvasContent(surface);
		var state = new FrameReadProbe();

		bool read = content.TryReadLatestFrame(state, static (in BrowserFrameAccess access, FrameReadProbe probe) =>
		{
			probe.Sequence = access.Sequence;
			probe.RowBytes = access.RowBytes;
			probe.FirstPixelBlue = access.Pixels.Span[0];
		});

		Assert.That(read, Is.True);
		Assert.That(state.Sequence, Is.EqualTo(frame.Sequence));
		Assert.That(state.RowBytes, Is.EqualTo(frame.RowBytes));
		Assert.That(state.FirstPixelBlue, Is.EqualTo(11));
	}

	[Test]
	public void Dispose_WhenFocusedSurfaceWasAlreadyDisposed_DoesNotThrow()
	{
		BrowserFrame frame = CreateSolidFrame(200, 100, b: 10, g: 20, r: 30, a: 255);
		var surface = new TestBrowserSurface(frame);
		var content = new BrowserSurfaceCanvasContent(surface);
		UiScene scene = UiSceneComposer.Compose(
			new SkiaTextMeasurer(),
			new SkiaImageSizeProvider(),
			Ui.Canvas(content).Width(200).Height(100));
		var root = new UIRoot(new NullUiRenderer());
		root.MountScene(scene);
		root.Resize(200, 100);
		root.HandleInput(new PointerEvent
		{
			PointerId = 0,
			Action = PointerAction.Down,
			X = 40,
			Y = 30
		});

		surface.DisposeAsync().AsTask().GetAwaiter().GetResult();

		Assert.DoesNotThrow(() => content.Dispose());
	}

	private static BrowserFrame CreateSolidFrame(int width, int height, byte b, byte g, byte r, byte a)
	{
		byte[] pixels = new byte[width * height * BrowserFrameBuffer.BytesPerPixel];
		for (int i = 0; i < pixels.Length; i += BrowserFrameBuffer.BytesPerPixel)
		{
			pixels[i] = b;
			pixels[i + 1] = g;
			pixels[i + 2] = r;
			pixels[i + 3] = a;
		}

		return new BrowserFrame(
			new BrowserViewport(width, height),
			BrowserPixelFormat.Bgra8888Premultiplied,
			pixels,
			width * BrowserFrameBuffer.BytesPerPixel,
			new[] { new BrowserDirtyRect(0, 0, width, height) },
			1);
	}

	private static BrowserFrame CreateTwoPixelAlphaFrame(byte leftAlpha, byte rightAlpha)
	{
		return new BrowserFrame(
			new BrowserViewport(2, 1),
			BrowserPixelFormat.Bgra8888Premultiplied,
			new byte[]
			{
				0, 0, 255, leftAlpha,
				0, 255, 0, rightAlpha
			},
			2 * BrowserFrameBuffer.BytesPerPixel,
			new[] { new BrowserDirtyRect(0, 0, 2, 1) },
			1);
	}

	private sealed class TestBrowserSurface : IBrowserSurface
	{
		private BrowserFrame _frame;
		private bool _disposed;

		public TestBrowserSurface(BrowserFrame frame)
		{
			_frame = frame;
			Id = BrowserSurfaceId.New();
			Viewport = frame.Viewport;
			Messages = new TestBrowserMessageBridge();
			InputEvents = new List<BrowserInputEvent>();
			ResizeEvents = new List<BrowserViewport>();
		}

		public event EventHandler<BrowserFrameReadyEventArgs>? FrameReady;

		public BrowserSurfaceId Id { get; }

		public BrowserViewport Viewport { get; private set; }

		public IBrowserMessageBridge Messages { get; }

		public List<BrowserInputEvent> InputEvents { get; }

		public List<BrowserViewport> ResizeEvents { get; }

		public ValueTask NavigateAsync(BrowserNavigationRequest request, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(request);
			return ValueTask.CompletedTask;
		}

		public ValueTask ResizeAsync(BrowserViewport viewport, CancellationToken cancellationToken = default)
		{
			Viewport = viewport;
			ResizeEvents.Add(viewport);
			return ValueTask.CompletedTask;
		}

		public ValueTask SendInputAsync(BrowserInputEvent inputEvent, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(inputEvent);
			if (_disposed)
			{
				throw new ObjectDisposedException(nameof(TestBrowserSurface));
			}

			InputEvents.Add(inputEvent);
			return ValueTask.CompletedTask;
		}

		public BrowserFrame? TryGetLatestFrame() => _frame;

		public bool TryReadLatestFrame<TState>(TState state, BrowserFrameReadAction<TState> readFrame)
		{
			ArgumentNullException.ThrowIfNull(readFrame);
			readFrame(BrowserFrameAccess.FromFrame(_frame), state);
			return true;
		}

		public ValueTask DisposeAsync()
		{
			_disposed = true;
			return ValueTask.CompletedTask;
		}

		public void SetFrame(BrowserFrame frame)
		{
			_frame = frame;
			FrameReady?.Invoke(this, new BrowserFrameReadyEventArgs(
				frame.Viewport,
				frame.PixelFormat,
				frame.DirtyRects,
				frame.Sequence));
		}
	}

	private sealed class NullUiRenderer : IUiRenderer
	{
		public void Render(UiScene scene, float width, float height)
		{
		}
	}

	private sealed class FrameReadProbe
	{
		public long Sequence { get; set; }

		public int RowBytes { get; set; }

		public byte FirstPixelBlue { get; set; }
	}

	private sealed class TestBrowserMessageBridge : IBrowserMessageBridge
	{
		public event EventHandler<BrowserScriptMessage>? MessageReceived;

		public ValueTask PostMessageAsync(BrowserScriptMessage message, CancellationToken cancellationToken = default)
		{
			MessageReceived?.Invoke(this, message);
			return ValueTask.CompletedTask;
		}

		public ValueTask ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(script);
			return ValueTask.CompletedTask;
		}
	}
}
