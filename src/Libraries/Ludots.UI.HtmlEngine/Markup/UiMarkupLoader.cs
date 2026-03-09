using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ExCSS;
using Ludots.UI.Runtime;

namespace Ludots.UI.HtmlEngine.Markup;

public sealed class UiMarkupLoader
{
    private readonly HtmlParser _htmlParser = new();

    public UiDocument LoadDocument(string html, string css = "")
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ArgumentException("HTML markup is required.", nameof(html));
        }

        IDocument document = _htmlParser.ParseDocument(html);
        UiElement root = BuildRoot(document.Body);
        UiDocument uiDocument = new(root)
        {
            Title = document.Title
        };

        if (!string.IsNullOrWhiteSpace(css))
        {
            uiDocument.StyleSheets.Add(UiCssParser.ParseStyleSheet(css));
        }

        return uiDocument;
    }

    public UiScene LoadScene(string html, string css = "", object? codeBehind = null, UiThemePack? theme = null)
    {
        UiDocument document = LoadDocument(html, css);
        UiScene scene = new();
        scene.MountDocument(document, theme);
        if (codeBehind != null)
        {
            MarkupBinder.Bind(scene, codeBehind);
        }

        return scene;
    }

    private static UiElement BuildRoot(IElement body)
    {
        List<IElement> childElements = body.Children.ToList();
        if (childElements.Count == 1)
        {
            return BuildElement(childElements[0]);
        }

        UiElement root = new("div", UiNodeKind.Container);
        foreach (INode childNode in body.ChildNodes)
        {
            AppendNode(root, childNode);
        }

        return root;
    }

    private static UiElement BuildElement(IElement element)
    {
        if (TryBuildSpecialElement(element, out UiElement? specialElement))
        {
            return specialElement;
        }

        UiElement uiElement = new(element.LocalName, MapKind(element));
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

        if (element.Children.Length == 0)
        {
            string text = element.TextContent?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                uiElement.TextContent = text;
            }
        }
        else
        {
            foreach (INode childNode in element.ChildNodes)
            {
                AppendNode(uiElement, childNode);
            }
        }

        return uiElement;
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
        if (uiElement.InlineStyle["width"] == null && TryParsePixelAttribute(element, "width", out string width))
        {
            uiElement.InlineStyle.Set("width", width);
        }

        if (uiElement.InlineStyle["height"] == null && TryParsePixelAttribute(element, "height", out string height))
        {
            uiElement.InlineStyle.Set("height", height);
        }
    }

    private static bool TryParsePixelAttribute(IElement element, string attributeName, out string value)
    {
        value = string.Empty;
        string? raw = element.GetAttribute(attributeName);
        if (!float.TryParse(raw, out float pixels) || pixels <= 0.01f)
        {
            return false;
        }

        value = pixels.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
        return true;
    }

    private static string EncodeInlineSvgDataUri(IElement element)
    {
        string markup = element.OuterHtml;
        if (!markup.Contains("xmlns=", StringComparison.OrdinalIgnoreCase))
        {
            markup = markup.Replace("<svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"", StringComparison.OrdinalIgnoreCase);
        }

        return "data:image/svg+xml;utf8," + Uri.EscapeDataString(markup);
    }

    private static void AppendNode(UiElement parent, INode node)
    {
        switch (node)
        {
            case IElement element:
                parent.AddChild(BuildElement(element));
                break;
            case IText textNode:
                string text = textNode.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parent.AddChild(new UiElement("span", UiNodeKind.Text) { TextContent = text });
                }
                break;
        }
    }

    private static UiNodeKind MapKind(IElement element)
    {
        string tagName = element.LocalName.ToLowerInvariant();
        if (tagName == "input")
        {
            string type = element.GetAttribute("type")?.Trim().ToLowerInvariant() ?? string.Empty;
            return type switch
            {
                "button" or "submit" or "reset" => UiNodeKind.Button,
                "checkbox" => UiNodeKind.Checkbox,
                "radio" => UiNodeKind.Radio,
                "range" => UiNodeKind.Slider,
                _ => UiNodeKind.Input
            };
        }

        return tagName switch
        {
            "button" => UiNodeKind.Button,
            "img" => UiNodeKind.Image,
            "select" => UiNodeKind.Select,
            "textarea" => UiNodeKind.TextArea,
            "article" => UiNodeKind.Card,
            "canvas" => UiNodeKind.Custom,
            "table" => UiNodeKind.Table,
            "thead" => UiNodeKind.TableHeader,
            "tbody" => UiNodeKind.TableBody,
            "tfoot" => UiNodeKind.TableFooter,
            "tr" => UiNodeKind.TableRow,
            "td" => UiNodeKind.TableCell,
            "th" => UiNodeKind.TableHeaderCell,
            "label" or "span" or "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => UiNodeKind.Text,
            _ => UiNodeKind.Container
        };
    }
}
