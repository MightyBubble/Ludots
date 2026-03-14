using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ludots.UI.Runtime;

namespace Ludots.UI.HtmlEngine.Markup;

public sealed class UiMarkupLoader
{
	private readonly HtmlParser _htmlParser = new HtmlParser();

	public UiDocument LoadDocument(string html, string css = "")
	{
		if (string.IsNullOrWhiteSpace(html))
		{
			throw new ArgumentException("HTML markup is required.", "html");
		}
		IDocument document = _htmlParser.ParseDocument(html);
		UiElement root = BuildRoot(document.Body);
		UiDocument uiDocument = new UiDocument(root)
		{
			Title = document.Title
		};
		if (!string.IsNullOrWhiteSpace(css))
		{
			uiDocument.StyleSheets.Add(UiCssParser.ParseStyleSheet(css));
		}
		return uiDocument;
	}

	public UiScene LoadScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, string html, string css = "", object? codeBehind = null, UiThemePack? theme = null)
	{
		UiDocument document = LoadDocument(html, css);
		UiScene uiScene = new UiScene(textMeasurer, imageSizeProvider);
		uiScene.MountDocument(document, theme);
		if (codeBehind != null)
		{
			MarkupBinder.Bind(uiScene, codeBehind);
		}
		return uiScene;
	}

	private static UiElement BuildRoot(IElement body)
	{
		List<IElement> list = body.Children.ToList();
		if (list.Count == 1)
		{
			return BuildElement(list[0]);
		}
		UiElement uiElement = new UiElement("div");
		foreach (INode childNode in body.ChildNodes)
		{
			AppendNode(uiElement, childNode);
		}
		return uiElement;
	}

	private static UiElement BuildElement(IElement element)
	{
		if (TryBuildSpecialElement(element, out UiElement uiElement))
		{
			return uiElement;
		}
		UiElement uiElement2 = new UiElement(element.LocalName, MapKind(element));
		foreach (IAttr attribute in element.Attributes)
		{
			if (attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase))
			{
				uiElement2.InlineStyle.Merge(UiCssParser.ParseInline(attribute.Value));
			}
			else
			{
				uiElement2.Attributes[attribute.Name] = attribute.Value;
			}
		}
		ApplyIntrinsicSizing(element, uiElement2);
		if (element.Children.Length == 0)
		{
			string text = element.TextContent?.Trim() ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(text))
			{
				uiElement2.TextContent = text;
			}
		}
		else
		{
			foreach (INode childNode in element.ChildNodes)
			{
				AppendNode(uiElement2, childNode);
			}
		}
		return uiElement2;
	}

	private static bool TryBuildSpecialElement(IElement element, out UiElement? uiElement)
	{
		uiElement = null;
		if (!string.Equals(element.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		uiElement = new UiElement("svg", UiNodeKind.Image);
		foreach (IAttr attribute in element.Attributes)
		{
			if (attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase))
			{
				uiElement.InlineStyle.Merge(UiCssParser.ParseInline(attribute.Value));
			}
			else
			{
				uiElement.Attributes[attribute.Name] = attribute.Value;
			}
		}
		ApplyIntrinsicSizing(element, uiElement);
		uiElement.Attributes["src"] = EncodeInlineSvgDataUri(element);
		return true;
	}

	private static void ApplyIntrinsicSizing(IElement element, UiElement uiElement)
	{
		if (uiElement.InlineStyle["width"] == null && TryParsePixelAttribute(element, "width", out string value))
		{
			uiElement.InlineStyle.Set("width", value);
		}
		if (uiElement.InlineStyle["height"] == null && TryParsePixelAttribute(element, "height", out string value2))
		{
			uiElement.InlineStyle.Set("height", value2);
		}
	}

	private static bool TryParsePixelAttribute(IElement element, string attributeName, out string value)
	{
		value = string.Empty;
		string attribute = element.GetAttribute(attributeName);
		if (!float.TryParse(attribute, out var result) || result <= 0.01f)
		{
			return false;
		}
		value = result.ToString(CultureInfo.InvariantCulture) + "px";
		return true;
	}

	private static string EncodeInlineSvgDataUri(IElement element)
	{
		string text = element.OuterHtml;
		if (!text.Contains("xmlns=", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Replace("<svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"", StringComparison.OrdinalIgnoreCase);
		}
		return "data:image/svg+xml;utf8," + Uri.EscapeDataString(text);
	}

	private static void AppendNode(UiElement parent, INode node)
	{
		if (!(node is IElement element))
		{
			if (node is IText text)
			{
				string text2 = text.Text?.Trim() ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(text2))
				{
					parent.AddChild(new UiElement("span", UiNodeKind.Text)
					{
						TextContent = text2
					});
				}
			}
		}
		else
		{
			parent.AddChild(BuildElement(element));
		}
	}

	private static UiNodeKind MapKind(IElement element)
	{
		string text = element.LocalName.ToLowerInvariant();
		UiNodeKind result;
		if (text == "input")
		{
			string text2 = element.GetAttribute("type")?.Trim().ToLowerInvariant() ?? string.Empty;
			if (1 == 0)
			{
			}
			switch (text2)
			{
			case "button":
			case "submit":
			case "reset":
				result = UiNodeKind.Button;
				break;
			case "checkbox":
				result = UiNodeKind.Checkbox;
				break;
			case "radio":
				result = UiNodeKind.Radio;
				break;
			case "range":
				result = UiNodeKind.Slider;
				break;
			default:
				result = UiNodeKind.Input;
				break;
			}
			if (1 == 0)
			{
			}
			return result;
		}
		if (1 == 0)
		{
		}
		switch (text)
		{
		case "button":
			result = UiNodeKind.Button;
			break;
		case "img":
			result = UiNodeKind.Image;
			break;
		case "select":
			result = UiNodeKind.Select;
			break;
		case "textarea":
			result = UiNodeKind.TextArea;
			break;
		case "article":
			result = UiNodeKind.Card;
			break;
		case "canvas":
			result = UiNodeKind.Custom;
			break;
		case "table":
			result = UiNodeKind.Table;
			break;
		case "thead":
			result = UiNodeKind.TableHeader;
			break;
		case "tbody":
			result = UiNodeKind.TableBody;
			break;
		case "tfoot":
			result = UiNodeKind.TableFooter;
			break;
		case "tr":
			result = UiNodeKind.TableRow;
			break;
		case "td":
			result = UiNodeKind.TableCell;
			break;
		case "th":
			result = UiNodeKind.TableHeaderCell;
			break;
		case "label":
		case "span":
		case "p":
		case "h1":
		case "h2":
		case "h3":
		case "h4":
		case "h5":
		case "h6":
			result = UiNodeKind.Text;
			break;
		default:
			result = UiNodeKind.Container;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}
}
