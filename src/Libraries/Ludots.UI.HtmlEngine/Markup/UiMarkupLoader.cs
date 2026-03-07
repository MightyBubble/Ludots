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
            "label" or "span" or "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => UiNodeKind.Text,
            _ => UiNodeKind.Container
        };
    }
}
