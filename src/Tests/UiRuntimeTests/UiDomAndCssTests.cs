using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Events;
using NUnit.Framework;
using SkiaSharp;

namespace Ludots.Tests.UIRuntime;

[TestFixture]
public sealed class UiDomAndCssTests
{
    [Test]
    public void UiDocument_QuerySelector_SupportsChildDescendant_AndSelectorLists()
    {
        UiElement root = new("div");
        root.Attributes["id"] = "root";

        UiElement toolbar = new("div");
        toolbar.Attributes["class"] = "toolbar";
        UiElement button = new("button", UiNodeKind.Button);
        button.Attributes["id"] = "cta";
        button.Attributes["class"] = "primary";
        toolbar.AddChild(button);

        UiElement body = new("section");
        body.Attributes["class"] = "body";
        UiElement detail = new("span", UiNodeKind.Text) { TextContent = "detail" };
        detail.Attributes["id"] = "detail";
        body.AddChild(detail);

        root.AddChild(toolbar);
        root.AddChild(body);
        UiDocument document = new(root);

        Assert.That(document.QuerySelector(".toolbar > button"), Is.SameAs(button));
        Assert.That(document.QuerySelector("#root .body > span"), Is.SameAs(detail));
        Assert.That(document.QuerySelectorAll("button, span").Count, Is.EqualTo(2));
    }

    [Test]
    public void UiDocument_QuerySelector_SupportsAttributeSelectors_AndCheckedPseudoState()
    {
        UiElement root = new("div");
        root.Attributes["id"] = "root";

        UiElement checkbox = new("input", UiNodeKind.Checkbox);
        checkbox.Attributes["id"] = "consent";
        checkbox.Attributes["type"] = "checkbox";
        checkbox.Attributes["checked"] = "true";
        root.AddChild(checkbox);

        UiDocument document = new(root);
        Assert.That(document.QuerySelector("input[type=checkbox]"), Is.SameAs(checkbox));

        UiScene scene = new();
        scene.MountDocument(document);
        scene.Layout(320, 200);

        UiNode node = scene.FindByElementId("consent")!;
        Assert.That((node.PseudoState & UiPseudoState.Checked) == UiPseudoState.Checked, Is.True);
    }

    [Test]
    public void UiScene_QuerySelectorAndBubble_UseUnifiedRuntimeTree()
    {
        UiDispatcher dispatcher = new();
        int calls = 0;
        UiActionHandle parentHandle = dispatcher.Register(_ => calls++);
        UiNode child = new(new UiNodeId(2), UiNodeKind.Text, textContent: "child", tagName: "span", classNames: new[] { "label" });
        UiNode parent = new(new UiNodeId(1), UiNodeKind.Container, children: new[] { child }, actionHandles: new[] { parentHandle }, tagName: "div", classNames: new[] { "panel" }, elementId: "panel");
        UiScene scene = new(dispatcher);
        scene.Mount(parent);

        Assert.That(scene.QuerySelector("#panel > span.label"), Is.SameAs(child));

        UiEventResult result = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, 0, 0, child.Id));

        Assert.That(result.Handled, Is.True);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void UiStyleResolver_SupportsCssVariables_Fallbacks_AndInheritance()
    {
        UiElement root = new("div");
        root.Attributes["class"] = "theme-root";

        UiElement accent = new("span", UiNodeKind.Text) { TextContent = "accent" };
        accent.Attributes["id"] = "accent";
        accent.Attributes["class"] = "accent";

        UiElement inherit = new("span", UiNodeKind.Text) { TextContent = "inherit" };
        inherit.Attributes["id"] = "inherit";
        inherit.Attributes["class"] = "inherit";

        root.AddChild(accent);
        root.AddChild(inherit);

        UiStyleSheet sheet = new UiStyleSheet()
            .AddRule(":root", style =>
            {
                style.Set("--accent", "#ff5500");
                style.Set("color", "#123456");
                style.Set("font-size", "24px");
            })
            .AddRule(".accent", style =>
            {
                style.Set("color", "var(--accent)");
                style.Set("background-color", "var(--missing, #00ff00)");
            });

        UiDocument document = new(root);
        document.StyleSheets.Add(sheet);
        UiScene scene = new();
        scene.MountDocument(document);
        scene.Layout(1280, 720);

        UiNode accentNode = scene.FindByElementId("accent")!;
        UiNode inheritNode = scene.FindByElementId("inherit")!;

        Assert.That(accentNode.Style.Color, Is.EqualTo(SKColor.Parse("#ff5500")));
        Assert.That(accentNode.Style.BackgroundColor, Is.EqualTo(SKColor.Parse("#00ff00")));
        Assert.That(inheritNode.Style.Color, Is.EqualTo(SKColor.Parse("#123456")));
        Assert.That(inheritNode.Style.FontSize, Is.EqualTo(24f));
    }

    [Test]
    public void UiStyleResolver_AndFlexLayout_SupportAbsolutePosition_AndOverflow()
    {
        UiElement root = new("div");
        root.Attributes["id"] = "root";

        UiElement overlay = new("div");
        overlay.Attributes["id"] = "overlay";
        root.AddChild(overlay);

        UiStyleSheet sheet = new UiStyleSheet()
            .AddRule("#overlay", style =>
            {
                style.Set("position", "absolute");
                style.Set("left", "32px");
                style.Set("top", "24px");
                style.Set("width", "120px");
                style.Set("height", "48px");
                style.Set("overflow", "hidden");
            });

        UiDocument document = new(root);
        document.StyleSheets.Add(sheet);
        UiScene scene = new();
        scene.MountDocument(document);
        scene.Layout(400, 300);

        UiNode overlayNode = scene.FindByElementId("overlay")!;
        Assert.That(overlayNode.LayoutRect.X, Is.EqualTo(32f).Within(0.5f));
        Assert.That(overlayNode.LayoutRect.Y, Is.EqualTo(24f).Within(0.5f));
        Assert.That(overlayNode.LayoutRect.Width, Is.EqualTo(120f).Within(0.5f));
        Assert.That(overlayNode.LayoutRect.Height, Is.EqualTo(48f).Within(0.5f));
        Assert.That(overlayNode.Style.Overflow, Is.EqualTo(UiOverflow.Hidden));
        Assert.That(overlayNode.Style.ClipContent, Is.True);
    }

    [Test]
    public void UiStyleResolver_ParsesAdvancedAppearance_AndTypographyProperties()
    {
        UiElement root = new("div");
        UiElement card = new("div");
        card.Attributes["id"] = "card";
        root.AddChild(card);

        UiStyleSheet sheet = new UiStyleSheet()
            .AddRule("#card", style =>
            {
                style.Set("background-image", "linear-gradient(to bottom, #ff0000 0%, #0000ff 100%)");
                style.Set("box-shadow", "6px 8px 12px rgba(0,0,0,0.5)");
                style.Set("text-shadow", "1px 2px 3px rgba(255,255,255,0.25)");
                style.Set("outline", "3px solid #00ff00");
                style.Set("font-family", "Segoe UI, sans-serif");
                style.Set("white-space", "pre-wrap");
            });

        UiDocument document = new(root);
        document.StyleSheets.Add(sheet);
        UiScene scene = new();
        scene.MountDocument(document);
        scene.Layout(320, 200);

        UiNode node = scene.FindByElementId("card")!;
        Assert.That(node.Style.BackgroundGradient, Is.Not.Null);
        Assert.That(node.Style.BackgroundGradient!.Stops.Count, Is.EqualTo(2));
        Assert.That(node.Style.BackgroundGradient.Stops[0].Color, Is.EqualTo(SKColor.Parse("#ff0000")));
        Assert.That(node.Style.BoxShadow, Is.Not.Null);
        Assert.That(node.Style.BoxShadow!.Value.OffsetX, Is.EqualTo(6f).Within(0.01f));
        Assert.That(node.Style.BoxShadow.Value.OffsetY, Is.EqualTo(8f).Within(0.01f));
        Assert.That(node.Style.BoxShadow.Value.BlurRadius, Is.EqualTo(12f).Within(0.01f));
        Assert.That(node.Style.TextShadow, Is.Not.Null);
        Assert.That(node.Style.OutlineWidth, Is.EqualTo(3f).Within(0.01f));
        Assert.That(node.Style.OutlineColor, Is.EqualTo(SKColor.Parse("#00ff00")));
        Assert.That(node.Style.FontFamily, Is.EqualTo("Segoe UI, sans-serif"));
        Assert.That(node.Style.WhiteSpace, Is.EqualTo(UiWhiteSpace.PreWrap));
    }

    [Test]
    public void UiScene_Click_UpdatesFocusAndCheckedPseudoStates_ForCheckboxAndRadioGroup()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            """
            <div id="root">
              <input id="consent" type="checkbox" />
              <input id="mode-a" type="radio" name="mode" checked="true" />
              <input id="mode-b" type="radio" name="mode" />
            </div>
            """,
            """
            input:focus { outline-width: 2px; outline-color: #00ff00; }
            input:checked { background-color: #2244ff; }
            """);

        scene.Layout(320, 200);
        UiNode consent = scene.FindByElementId("consent")!;
        UiNode modeA = scene.FindByElementId("mode-a")!;
        UiNode modeB = scene.FindByElementId("mode-b")!;

        UiEventResult checkboxClick = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, consent.LayoutRect.X + 1, consent.LayoutRect.Y + 1, consent.Id));
        scene.Layout(320, 200);
        consent = scene.FindByElementId("consent")!;

        Assert.That(checkboxClick.Handled, Is.True);
        Assert.That(consent.PseudoState.HasFlag(UiPseudoState.Focus), Is.True);
        Assert.That(consent.PseudoState.HasFlag(UiPseudoState.Checked), Is.True);
        Assert.That(consent.Style.OutlineWidth, Is.EqualTo(2f).Within(0.01f));
        Assert.That(consent.Style.BackgroundColor, Is.EqualTo(SKColor.Parse("#2244ff")));
        Assert.That(scene.FocusedNodeId, Is.EqualTo(consent.Id));

        UiEventResult radioClick = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, modeB.LayoutRect.X + 1, modeB.LayoutRect.Y + 1, modeB.Id));
        scene.Layout(320, 200);
        modeA = scene.FindByElementId("mode-a")!;
        modeB = scene.FindByElementId("mode-b")!;

        Assert.That(radioClick.Handled, Is.True);
        Assert.That(modeA.PseudoState.HasFlag(UiPseudoState.Checked), Is.False);
        Assert.That(modeA.Attributes.Contains("checked"), Is.False);
        Assert.That(modeB.PseudoState.HasFlag(UiPseudoState.Checked), Is.True);
        Assert.That(modeB.PseudoState.HasFlag(UiPseudoState.Focus), Is.True);
        Assert.That(modeB.Attributes["checked"], Is.EqualTo("true"));
    }

    [Test]
    public void UiMarkupLoader_MapsRadioAndTableSemantics_AndLayoutsTableCells()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            """
            <table id="stats-table">
              <thead>
                <tr>
                  <th id="head-name">Name</th>
                  <th id="head-role">Role</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td id="cell-name">Alice</td>
                  <td id="cell-role">Designer</td>
                </tr>
              </tbody>
            </table>
            <input id="mode" type="radio" name="mode" />
            """,
            """
            #stats-table { width: 320px; }
            th, td { padding: 6px; border-width: 1px; border-color: #445566; }
            """);

        scene.Layout(400, 220);

        UiNode table = scene.FindByElementId("stats-table")!;
        UiNode headerName = scene.FindByElementId("head-name")!;
        UiNode headerRole = scene.FindByElementId("head-role")!;
        UiNode cellName = scene.FindByElementId("cell-name")!;
        UiNode cellRole = scene.FindByElementId("cell-role")!;
        UiNode radio = scene.FindByElementId("mode")!;

        Assert.That(table.Kind, Is.EqualTo(UiNodeKind.Table));
        Assert.That(headerName.Kind, Is.EqualTo(UiNodeKind.TableHeaderCell));
        Assert.That(cellName.Kind, Is.EqualTo(UiNodeKind.TableCell));
        Assert.That(radio.Kind, Is.EqualTo(UiNodeKind.Radio));
        Assert.That(headerName.LayoutRect.Width, Is.EqualTo(headerRole.LayoutRect.Width).Within(1.5f));
        Assert.That(cellName.LayoutRect.Width, Is.EqualTo(cellRole.LayoutRect.Width).Within(1.5f));
        Assert.That(cellName.LayoutRect.Y, Is.GreaterThan(headerName.LayoutRect.Y));
        Assert.That(table.LayoutRect.Width, Is.EqualTo(320f).Within(1.5f));
    }

}
