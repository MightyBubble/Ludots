using System.Collections.Generic;
using Ludots.UI.Input;
using SkiaSharp;

namespace Ludots.UI.Widgets;

public class Panel : Widget
{
	private readonly List<Widget> _children = new List<Widget>();

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
		foreach (Widget child in _children)
		{
			child.Render(canvas);
		}
	}

	public override bool HandleInput(InputEvent e, float parentX, float parentY)
	{
		float parentX2 = parentX + base.X;
		float parentY2 = parentY + base.Y;
		for (int num = _children.Count - 1; num >= 0; num--)
		{
			if (_children[num].HandleInput(e, parentX2, parentY2))
			{
				return true;
			}
		}
		return base.HandleInput(e, parentX, parentY);
	}
}
