using System;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using SkiaSharp;

namespace Ludots.UI.Compose;

public static class Ui
{
	public static UiElementBuilder Column(params UiElementBuilder[] children)
	{
		return new UiElementBuilder(UiNodeKind.Column, "div").Column().Children(children);
	}

	public static UiElementBuilder Row(params UiElementBuilder[] children)
	{
		return new UiElementBuilder(UiNodeKind.Row, "div").Row().Children(children);
	}

	public static UiElementBuilder Panel(params UiElementBuilder[] children)
	{
		return new UiElementBuilder(UiNodeKind.Panel, "section").Children(children);
	}

	public static UiElementBuilder Card(params UiElementBuilder[] children)
	{
		return new UiElementBuilder(UiNodeKind.Card, "article").Children(children);
	}

	public static UiElementBuilder ScrollView(params UiElementBuilder[] children)
	{
		return new UiElementBuilder(UiNodeKind.Container, "div").Column().Overflow(UiOverflow.Scroll).Children(children);
	}

	public static UiElementBuilder Text(string text)
	{
		return new UiElementBuilder(UiNodeKind.Text, "span").Text(text);
	}

	public static UiElementBuilder Image(string source)
	{
		return new UiElementBuilder(UiNodeKind.Image, "img").Src(source);
	}

	public static UiElementBuilder Canvas(Action<SKCanvas, SKRect> draw)
	{
		return new UiElementBuilder(UiNodeKind.Custom, "canvas").CanvasContent(draw);
	}

	public static UiElementBuilder Button(string text, Action<UiActionContext>? onClick = null)
	{
		UiElementBuilder uiElementBuilder = new UiElementBuilder(UiNodeKind.Button, "button").Text(text);
		if (onClick != null)
		{
			uiElementBuilder.OnClick(onClick);
		}
		return uiElementBuilder;
	}

	public static UiElementBuilder Input(string? text = null)
	{
		UiElementBuilder uiElementBuilder = new UiElementBuilder(UiNodeKind.Input, "input");
		if (!string.IsNullOrWhiteSpace(text))
		{
			uiElementBuilder.Text(text);
		}
		return uiElementBuilder;
	}

	public static UiElementBuilder Checkbox(string text, bool isChecked = false, Action<UiActionContext>? onClick = null)
	{
		UiElementBuilder uiElementBuilder = new UiElementBuilder(UiNodeKind.Checkbox, "input").Type("checkbox").Text(text).Checked(isChecked);
		if (onClick != null)
		{
			uiElementBuilder.OnClick(onClick);
		}
		return uiElementBuilder;
	}

	public static UiElementBuilder Radio(string text, string? groupName = null, bool isChecked = false, Action<UiActionContext>? onClick = null)
	{
		UiElementBuilder uiElementBuilder = new UiElementBuilder(UiNodeKind.Radio, "input").Type("radio").Text(text).Checked(isChecked);
		if (!string.IsNullOrWhiteSpace(groupName))
		{
			uiElementBuilder.Name(groupName);
		}
		if (onClick != null)
		{
			uiElementBuilder.OnClick(onClick);
		}
		return uiElementBuilder;
	}

	public static UiElementBuilder Table(params UiElementBuilder[] children)
	{
		return new UiElementBuilder(UiNodeKind.Table, "table").Children(children);
	}

	public static UiElementBuilder TableRow(params UiElementBuilder[] children)
	{
		return new UiElementBuilder(UiNodeKind.TableRow, "tr").Children(children);
	}

	public static UiElementBuilder TableCell(string text)
	{
		return new UiElementBuilder(UiNodeKind.TableCell, "td").Text(text);
	}

	public static UiElementBuilder TableCell(params UiElementBuilder[] children)
	{
		return new UiElementBuilder(UiNodeKind.TableCell, "td").Children(children);
	}

	public static UiElementBuilder TableHeaderCell(string text)
	{
		return new UiElementBuilder(UiNodeKind.TableHeaderCell, "th").Text(text);
	}

	public static UiElementBuilder TableHeaderCell(params UiElementBuilder[] children)
	{
		return new UiElementBuilder(UiNodeKind.TableHeaderCell, "th").Children(children);
	}
}
