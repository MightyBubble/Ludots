using SkiaSharp;
using Ludots.UI.Input;
using Ludots.UI.Widgets;

namespace Ludots.UI;

public class UIRoot
{
    public Widget? Content { get; set; }
    public float Width { get; private set; }
    public float Height { get; private set; }
    public bool IsDirty { get; set; } = true;

    public void Resize(float width, float height)
    {
        Width = width;
        Height = height;
        IsDirty = true;
    }

    public void Render(SKCanvas canvas)
    {
        if (Content != null)
        {
            // Ensure Root Content fills the screen
            if (Content.Width != Width || Content.Height != Height)
            {
                Content.Width = Width;
                Content.Height = Height;
                // If it's a FlexNodeWidget, this change might need to trigger MarkDirty or Layout?
                // But Render will call CalculateLayout if it's root.
            }

            Content.Render(canvas);
        }
        IsDirty = false;
    }
    
    public bool HandleInput(InputEvent e)
    {
        if (Content != null)
        {
            // Propagate dirty state if input handled (assuming interaction might change visual state)
            // Ideally widgets should set IsDirty themselves, but for now we can be safe
            // Actually, let's not auto-dirty on input unless widget says so?
            // But Widget.HandleInput returns bool 'handled'.
            // Let's assume handled input might change state.
            bool handled = Content.HandleInput(e, 0, 0);
            if (handled)
            {
                IsDirty = true;
            }
            // We can't know if handled input caused visual change without Widget reporting it.
            // But for this simple demo, let's assume if it returns true, we might need redraw.
            // Wait, HandleInput in Widget.cs returns false by default.
            // HtmlWidget currently doesn't override HandleInput except OnPointerEvent.
            // Let's check HtmlWidget.
            return handled;
        }
        return false;
    }
}
