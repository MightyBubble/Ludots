using Ludots.UI.Input;
using SkiaSharp;

namespace Ludots.UI.Widgets;

public class Widget
{
	public float X { get; set; }

	public float Y { get; set; }

	public float Width { get; set; }

	public float Height { get; set; }

	public SKColor BackgroundColor { get; set; } = SKColors.Transparent;

	public bool IsDirty { get; set; } = true;

	public virtual void Render(SKCanvas canvas)
	{
		canvas.Save();
		canvas.Translate(X, Y);
		if (BackgroundColor != SKColors.Transparent)
		{
			using SKPaint paint = new SKPaint
			{
				Color = BackgroundColor
			};
			canvas.DrawRect(0f, 0f, Width, Height, paint);
		}
		OnRender(canvas);
		canvas.Restore();
	}

	protected virtual void OnRender(SKCanvas canvas)
	{
	}

	public virtual bool HandleInput(InputEvent e, float parentX, float parentY)
	{
		float num = parentX + X;
		float num2 = parentY + Y;
		if (e is PointerEvent pointerEvent && pointerEvent.X >= num && pointerEvent.X <= num + Width && pointerEvent.Y >= num2 && pointerEvent.Y <= num2 + Height)
		{
			return OnPointerEvent(pointerEvent, pointerEvent.X - num, pointerEvent.Y - num2);
		}
		return false;
	}

	protected virtual bool OnPointerEvent(PointerEvent e, float localX, float localY)
	{
		return false;
	}
}
