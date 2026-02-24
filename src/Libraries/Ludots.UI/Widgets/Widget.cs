using SkiaSharp;
using Ludots.UI.Input;

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
            using var paint = new SKPaint { Color = BackgroundColor };
            canvas.DrawRect(0, 0, Width, Height, paint);
        }
        
        OnRender(canvas);
        
        canvas.Restore();
    }

    protected virtual void OnRender(SKCanvas canvas)
    {
    }

    public virtual bool HandleInput(InputEvent e, float parentX, float parentY)
    {
        float globalX = parentX + X;
        float globalY = parentY + Y;

        if (e is PointerEvent pe)
        {
            // Simple Hit Test
            bool inside = pe.X >= globalX && pe.X <= globalX + Width && 
                          pe.Y >= globalY && pe.Y <= globalY + Height;
            
            if (inside)
            {
                // We could transform event to local coords here if needed
                // For now just pass it through
                return OnPointerEvent(pe, pe.X - globalX, pe.Y - globalY);
            }
        }
        return false;
    }

    protected virtual bool OnPointerEvent(PointerEvent e, float localX, float localY)
    {
        return false;
    }
}
