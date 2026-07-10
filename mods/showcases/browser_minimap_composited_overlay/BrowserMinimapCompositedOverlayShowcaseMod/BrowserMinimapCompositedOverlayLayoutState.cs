using Ludots.Core.Presentation;

namespace BrowserMinimapCompositedOverlayShowcaseMod;

internal readonly record struct BrowserMinimapCompositedOverlayRect(
	int X,
	int Y,
	int Width,
	int Height,
	PresentationClipShapeKind ClipKind,
	long Sequence);

internal readonly record struct BrowserMinimapCompositedOverlayCanvasRect(
	int X,
	int Y,
	int Width,
	int Height);

internal sealed class BrowserMinimapCompositedOverlayLayoutState
{
	private readonly object _sync = new();
	private BrowserMinimapCompositedOverlayRect _rect;
	private BrowserMinimapCompositedOverlayCanvasRect _canvasRect;
	private int _screenWidth;
	private int _screenHeight;
	private bool _hasRect;

	public void ConfigureCanvas(int x, int y, int width, int height, int screenWidth, int screenHeight)
	{
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width), "Canvas size must be positive.");
		}

		lock (_sync)
		{
			_canvasRect = new BrowserMinimapCompositedOverlayCanvasRect(x, y, width, height);
			_screenWidth = Math.Max(width, screenWidth);
			_screenHeight = Math.Max(height, screenHeight);
		}
	}

	public BrowserMinimapCompositedOverlayCanvasRect GetCanvasRect()
	{
		lock (_sync)
		{
			return _canvasRect;
		}
	}

	public void UpdateScreenBounds(int screenWidth, int screenHeight)
	{
		lock (_sync)
		{
			_screenWidth = Math.Max(_canvasRect.Width, screenWidth);
			_screenHeight = Math.Max(_canvasRect.Height, screenHeight);
			_canvasRect = ClampCanvasRect(_canvasRect);
		}
	}

	public bool ApplyViewportMessage(
		float localX,
		float localY,
		float width,
		float height,
		float coordinateSpaceWidth,
		float coordinateSpaceHeight,
		PresentationClipShapeKind clipKind,
		long sequence,
		float dragDeltaX,
		float dragDeltaY)
	{
		if (width <= 0 || height <= 0)
		{
			return false;
		}

		lock (_sync)
		{
			if (_hasRect && sequence < _rect.Sequence)
			{
				return false;
			}

			float scaleX = ResolveCoordinateSpaceScale(_canvasRect.Width, coordinateSpaceWidth);
			float scaleY = ResolveCoordinateSpaceScale(_canvasRect.Height, coordinateSpaceHeight);
			int dragDeltaUiX = ScaleToUiPixels(dragDeltaX, scaleX);
			int dragDeltaUiY = ScaleToUiPixels(dragDeltaY, scaleY);
			bool canvasMoved = false;
			if (dragDeltaUiX != 0 || dragDeltaUiY != 0)
			{
				BrowserMinimapCompositedOverlayCanvasRect previous = _canvasRect;
				BrowserMinimapCompositedOverlayCanvasRect next = ClampCanvasRect(previous with
				{
					X = previous.X + dragDeltaUiX,
					Y = previous.Y + dragDeltaUiY
				});
				int nextX = next.X;
				int nextY = next.Y;
				if (nextX != previous.X || nextY != previous.Y)
				{
					_canvasRect = next;
					canvasMoved = true;
				}
			}

			_rect = new BrowserMinimapCompositedOverlayRect(
				_canvasRect.X + ScaleToUiPixels(localX, scaleX),
				_canvasRect.Y + ScaleToUiPixels(localY, scaleY),
				Math.Max(1, ScaleToUiPixels(width, scaleX)),
				Math.Max(1, ScaleToUiPixels(height, scaleY)),
				clipKind,
				sequence);
			_hasRect = true;
			return canvasMoved;
		}
	}

	private static float ResolveCoordinateSpaceScale(int uiPixels, float coordinateSpacePixels)
	{
		return coordinateSpacePixels > 0.001f && float.IsFinite(coordinateSpacePixels)
			? uiPixels / coordinateSpacePixels
			: 1f;
	}

	private static int ScaleToUiPixels(float value, float scale)
	{
		if (!float.IsFinite(value) || !float.IsFinite(scale))
		{
			return 0;
		}

		return (int)MathF.Round(value * scale);
	}

	private BrowserMinimapCompositedOverlayCanvasRect ClampCanvasRect(BrowserMinimapCompositedOverlayCanvasRect rect)
	{
		const int Margin = 8;
		int maxX = Math.Max(Margin, _screenWidth - rect.Width - Margin);
		int maxY = Math.Max(Margin, _screenHeight - rect.Height - Margin);
		return rect with
		{
			X = Math.Clamp(rect.X, Margin, maxX),
			Y = Math.Clamp(rect.Y, Margin, maxY)
		};
	}

	public bool TryGetRect(out BrowserMinimapCompositedOverlayRect rect)
	{
		lock (_sync)
		{
			rect = _rect;
			return _hasRect;
		}
	}
}
