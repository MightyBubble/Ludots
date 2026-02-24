using SkiaSharp;
using Ludots.UI.Input;

namespace Ludots.UI.Widgets;

public class Panel : Widget
{
    private readonly List<Widget> _children = new();

    public void AddChild(Widget child)
    {
        _children.Add(child);
    }
    
    public void RemoveChild(Widget child)
    {
        _children.Remove(child);
    }

    protected override void OnRender(SKCanvas canvas)
    {
        foreach (var child in _children)
        {
            child.Render(canvas);
        }
    }

    public override bool HandleInput(InputEvent e, float parentX, float parentY)
    {
        float globalX = parentX + X;
        float globalY = parentY + Y;
        
        // Children first (reverse Z-order)
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i].HandleInput(e, globalX, globalY))
            {
                return true;
            }
        }
        
        return base.HandleInput(e, parentX, parentY);
    }
}
