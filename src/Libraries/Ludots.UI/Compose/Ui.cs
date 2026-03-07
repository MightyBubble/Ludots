using Ludots.UI.Runtime;

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

    public static UiElementBuilder Text(string text)
    {
        return new UiElementBuilder(UiNodeKind.Text, "span").Text(text);
    }

    public static UiElementBuilder Button(string text, Action<Ludots.UI.Runtime.Actions.UiActionContext>? onClick = null)
    {
        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Button, "button").Text(text);
        if (onClick != null)
        {
            builder.OnClick(onClick);
        }

        return builder;
    }

    public static UiElementBuilder Input(string? text = null)
    {
        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Input, "input");
        if (!string.IsNullOrWhiteSpace(text))
        {
            builder.Text(text);
        }

        return builder;
    }

    public static UiElementBuilder Checkbox(string text, bool isChecked = false, Action<Ludots.UI.Runtime.Actions.UiActionContext>? onClick = null)
    {
        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Checkbox, "input").Type("checkbox").Text(text).Checked(isChecked);
        if (onClick != null)
        {
            builder.OnClick(onClick);
        }

        return builder;
    }

    public static UiElementBuilder Radio(string text, string? groupName = null, bool isChecked = false, Action<Ludots.UI.Runtime.Actions.UiActionContext>? onClick = null)
    {
        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Radio, "input").Type("radio").Text(text).Checked(isChecked);
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            builder.Name(groupName);
        }

        if (onClick != null)
        {
            builder.OnClick(onClick);
        }

        return builder;
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
