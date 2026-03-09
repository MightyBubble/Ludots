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
    public void UiDocument_QuerySelector_SupportsStructuralPseudoClasses_AndNthChildFormulas()
    {
        UiElement root = new("div");

        UiElement first = new("span", UiNodeKind.Text) { TextContent = "first" };
        first.Attributes["id"] = "first";
        first.Attributes["class"] = "item";

        UiElement second = new("span", UiNodeKind.Text) { TextContent = "second" };
        second.Attributes["id"] = "second";
        second.Attributes["class"] = "item";

        UiElement third = new("span", UiNodeKind.Text) { TextContent = "third" };
        third.Attributes["id"] = "third";
        third.Attributes["class"] = "item";

        root.AddChild(first);
        root.AddChild(second);
        root.AddChild(third);

        UiDocument document = new(root);

        Assert.That(document.QuerySelector(".item:first-child"), Is.SameAs(first));
        Assert.That(document.QuerySelector(".item:last-child"), Is.SameAs(third));
        Assert.That(document.QuerySelector(".item:nth-child(2)"), Is.SameAs(second));
        Assert.That(document.QuerySelectorAll(".item:nth-child(odd)").Count, Is.EqualTo(2));
        Assert.That(document.QuerySelectorAll(".item:nth-child(2n+1)").Count, Is.EqualTo(2));
    }

    [Test]
    public void UiDocument_QuerySelector_SupportsSiblingSelectors_LogicalPseudos_AndAdvancedAttributeOperators()
    {
        UiElement root = new("div");

        UiElement alpha = new("div") { TextContent = "Alpha" };
        alpha.Attributes["id"] = "alpha";
        alpha.Attributes["class"] = "pill";
        alpha.Attributes["data-role"] = "hero-main";
        alpha.Attributes["data-tone"] = "warm-ember";
        alpha.Attributes["data-flags"] = "featured lead";
        alpha.Attributes["lang"] = "zh-CN";

        UiElement beta = new("div") { TextContent = "Beta" };
        beta.Attributes["id"] = "beta";
        beta.Attributes["class"] = "pill";
        beta.Attributes["data-role"] = "support-core";
        beta.Attributes["data-tone"] = "neutral-cold";
        beta.Attributes["data-flags"] = "support secondary";

        UiElement gamma = new("div") { TextContent = "Gamma" };
        gamma.Attributes["id"] = "gamma";
        gamma.Attributes["class"] = "pill";
        gamma.Attributes["data-role"] = "support-ops";
        gamma.Attributes["data-tone"] = "cobalt-cold";
        gamma.Attributes["data-flags"] = "secondary fallback";

        UiElement delta = new("div") { TextContent = "Delta" };
        delta.Attributes["id"] = "delta";
        delta.Attributes["class"] = "pill";
        delta.Attributes["data-role"] = "utility";
        delta.Attributes["data-tone"] = "warm-gold";
        delta.Attributes["data-flags"] = "auxiliary";

        root.AddChild(alpha);
        root.AddChild(beta);
        root.AddChild(gamma);
        root.AddChild(delta);
        UiDocument document = new(root);

        Assert.That(document.QuerySelector(".pill[data-flags~=featured] + .pill"), Is.SameAs(beta));
        Assert.That(document.QuerySelector(".pill[data-flags~=featured] ~ .pill[data-tone$=cold]"), Is.SameAs(beta));
        Assert.That(document.QuerySelectorAll(".pill[data-tone$=cold]").Count, Is.EqualTo(2));
        Assert.That(document.QuerySelector(".pill:not([data-tone*=cold])"), Is.SameAs(alpha));
        Assert.That(document.QuerySelector(".pill:is([data-role^=hero], [data-role^=support])"), Is.SameAs(alpha));
        Assert.That(document.QuerySelectorAll(".pill:where([data-role^=support])").Count, Is.EqualTo(2));
        Assert.That(document.QuerySelector(".pill:nth-last-child(2)"), Is.SameAs(gamma));
        Assert.That(document.QuerySelector(".pill[lang|=zh]"), Is.SameAs(alpha));
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
    public void UiStyleResolver_ParsesFlexWrapAlignContentAndBlurFilters()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            "<div id=\"card\"></div>",
            "#card { display:flex; flex-wrap:wrap; align-content:space-between; filter:blur(6px); backdrop-filter:blur(10px); gap:8px 12px; }");

        scene.Layout(320, 200);
        UiNode node = scene.FindByElementId("card")!;

        Assert.That(node.Style.FlexWrap, Is.EqualTo(UiFlexWrap.Wrap));
        Assert.That(node.Style.AlignContent, Is.EqualTo(UiAlignContent.SpaceBetween));
        Assert.That(node.Style.FilterBlurRadius, Is.EqualTo(6f).Within(0.01f));
        Assert.That(node.Style.BackdropBlurRadius, Is.EqualTo(10f).Within(0.01f));
        Assert.That(node.Style.RowGap, Is.EqualTo(8f).Within(0.01f));
        Assert.That(node.Style.ColumnGap, Is.EqualTo(12f).Within(0.01f));
    }

    [Test]
    public void UiStyleResolver_ParsesBackgroundImageUrl_Size_Position_AndRepeat()
    {
        const string svgDataUri = "data:image/svg+xml;utf8,%3Csvg%20xmlns%3D%22http%3A//www.w3.org/2000/svg%22%20viewBox%3D%220%200%2040%2030%22%3E%3Crect%20width%3D%2240%22%20height%3D%2230%22%20fill%3D%22%232563eb%22/%3E%3C/svg%3E";
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            "<div id=\"card\"></div>",
            $"#card {{ width:160px; height:120px; background-image:url('{svgDataUri}'); background-size:cover; background-position:center; background-repeat:no-repeat; }}");

        scene.Layout(320, 200);
        UiNode node = scene.FindByElementId("card")!;

        Assert.That(node.Style.BackgroundLayers.Count, Is.EqualTo(1));
        Assert.That(node.Style.BackgroundLayers[0].ImageSource, Is.EqualTo(svgDataUri));
        Assert.That(node.Style.BackgroundSizes.Count, Is.EqualTo(1));
        Assert.That(node.Style.BackgroundSizes[0].Mode, Is.EqualTo(UiBackgroundSizeMode.Cover));
        Assert.That(node.Style.BackgroundPositions.Count, Is.EqualTo(1));
        Assert.That(node.Style.BackgroundPositions[0], Is.EqualTo(UiBackgroundPosition.Center));
        Assert.That(node.Style.BackgroundRepeats.Count, Is.EqualTo(1));
        Assert.That(node.Style.BackgroundRepeats[0], Is.EqualTo(UiBackgroundRepeat.NoRepeat));
    }

    [Test]
    public void UiMarkupLoader_MapsInlineSvg_AndBindsCanvasContent()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            """
            <div id="root">
              <svg id="badge" viewBox="0 0 32 24" width="32" height="24">
                <rect x="0" y="0" width="32" height="24" fill="#22d3ee" />
              </svg>
              <canvas id="spark" width="180" height="90" ui-canvas="BuildCanvas"></canvas>
            </div>
            """,
            string.Empty,
            new CanvasMarkupCodeBehind());

        scene.Layout(320, 200);
        UiNode svg = scene.FindByElementId("badge")!;
        UiNode canvas = scene.FindByElementId("spark")!;

        Assert.That(svg.Kind, Is.EqualTo(UiNodeKind.Image));
        Assert.That(svg.Attributes["src"], Does.StartWith("data:image/svg+xml"));
        Assert.That(canvas.TagName, Is.EqualTo("canvas"));
        Assert.That(canvas.CanvasContent, Is.Not.Null);
        Assert.That(canvas.LayoutRect.Width, Is.EqualTo(180f).Within(1f));
        Assert.That(canvas.LayoutRect.Height, Is.EqualTo(90f).Within(1f));
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
                  <th id="head-name">Prototype Identifier</th>
                  <th id="head-role">Role</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td id="cell-name">Sentinel Vanguard Frame</td>
                  <td id="cell-role">Guardian</td>
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
        Assert.That(headerName.LayoutRect.Width, Is.GreaterThan(headerRole.LayoutRect.Width));
        Assert.That(cellName.LayoutRect.Width, Is.GreaterThan(cellRole.LayoutRect.Width));
        Assert.That(cellName.LayoutRect.Width, Is.EqualTo(headerName.LayoutRect.Width).Within(1.5f));
        Assert.That(cellRole.LayoutRect.Width, Is.EqualTo(headerRole.LayoutRect.Width).Within(1.5f));
        Assert.That(cellName.LayoutRect.Y, Is.GreaterThan(headerName.LayoutRect.Y));
        Assert.That(table.LayoutRect.Width, Is.EqualTo(320f).Within(1.5f));
    }

    [Test]
    public void UiStyleResolver_TracksRequiredAndInvalidPseudoClasses_ForInputsAndRadioGroups()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            """
            <input id="email" type="email" required placeholder="Email / required" />
            <textarea id="notes" required></textarea>
            <input id="display-name" type="text" required value="Operator" />
            <div>
              <input id="mode-a" type="radio" name="mode" required />
              <input id="mode-b" type="radio" name="mode" />
            </div>
            """,
            """
            input:required, textarea:required { border-width: 1px; border-color: #445566; }
            input:invalid, textarea:invalid { border-width: 2px; border-color: #ff3355; outline-width: 1px; outline-color: #ff3355; }
            """);

        scene.Layout(320, 240);

        UiNode email = scene.FindByElementId("email")!;
        UiNode notes = scene.FindByElementId("notes")!;
        UiNode displayName = scene.FindByElementId("display-name")!;
        UiNode modeA = scene.FindByElementId("mode-a")!;
        UiNode modeB = scene.FindByElementId("mode-b")!;

        Assert.That(email.PseudoState.HasFlag(UiPseudoState.Required), Is.True);
        Assert.That(email.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(email.Style.BorderWidth, Is.EqualTo(2f).Within(0.01f));
        Assert.That(email.Style.BorderColor, Is.EqualTo(SKColor.Parse("#ff3355")));
        Assert.That(notes.PseudoState.HasFlag(UiPseudoState.Required), Is.True);
        Assert.That(notes.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(displayName.PseudoState.HasFlag(UiPseudoState.Required), Is.True);
        Assert.That(displayName.PseudoState.HasFlag(UiPseudoState.Invalid), Is.False);
        Assert.That(displayName.Style.BorderWidth, Is.EqualTo(1f).Within(0.01f));
        Assert.That(modeA.PseudoState.HasFlag(UiPseudoState.Required), Is.True);
        Assert.That(modeB.PseudoState.HasFlag(UiPseudoState.Required), Is.True);
        Assert.That(modeA.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(modeB.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(scene.QuerySelectorAll("input:invalid, textarea:invalid").Count, Is.EqualTo(4));

        UiEventResult radioClick = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, modeB.LayoutRect.X + 1f, modeB.LayoutRect.Y + 1f, modeB.Id));
        scene.Layout(320, 240);

        modeA = scene.FindByElementId("mode-a")!;
        modeB = scene.FindByElementId("mode-b")!;

        Assert.That(radioClick.Handled, Is.True);
        Assert.That(modeA.PseudoState.HasFlag(UiPseudoState.Invalid), Is.False);
        Assert.That(modeB.PseudoState.HasFlag(UiPseudoState.Invalid), Is.False);
        Assert.That(modeB.PseudoState.HasFlag(UiPseudoState.Checked), Is.True);
        Assert.That(scene.QuerySelectorAll("input:invalid, textarea:invalid").Count, Is.EqualTo(2));
    }

    [Test]
    public void UiScene_ValidatesPatternLengthAndNumericConstraints_OnOptionalInputs()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            """
            <input id="email" type="email" value="invalid-email" />
            <input id="password" type="password" minlength="8" value="hunter2" />
            <input id="code" pattern="[A-Z]{3}-\d{2}" value="ab-12" />
            <input id="count" type="number" min="2" max="10" step="2" value="5" />
            <input id="ok" type="number" min="2" max="10" step="2" value="6" />
            <input id="optional-empty" pattern="[A-Z]{3}" value="" />
            """,
            """
            input:invalid { border-width: 2px; border-color: #ff3355; }
            """);

        scene.Layout(420, 220);

        UiNode email = scene.FindByElementId("email")!;
        UiNode password = scene.FindByElementId("password")!;
        UiNode code = scene.FindByElementId("code")!;
        UiNode count = scene.FindByElementId("count")!;
        UiNode ok = scene.FindByElementId("ok")!;
        UiNode optionalEmpty = scene.FindByElementId("optional-empty")!;

        Assert.That(email.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(password.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(code.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(count.PseudoState.HasFlag(UiPseudoState.Invalid), Is.True);
        Assert.That(ok.PseudoState.HasFlag(UiPseudoState.Invalid), Is.False);
        Assert.That(optionalEmpty.PseudoState.HasFlag(UiPseudoState.Invalid), Is.False);
        Assert.That(email.Style.BorderWidth, Is.EqualTo(2f).Within(0.01f));
        Assert.That(count.Attributes["aria-invalid"], Is.EqualTo("true"));
        Assert.That(ok.Attributes["aria-invalid"], Is.EqualTo("false"));
        Assert.That(scene.QuerySelectorAll("input:invalid").Count, Is.EqualTo(4));
    }

    [Test]
    public void UiLayoutEngine_SupportsColspanAndRowspan_InNativeTableLayout()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            """
            <table id="stats-table">
              <tbody>
                <tr>
                  <td id="cell-prototype" rowspan="2">Sentinel Vanguard Frame</td>
                  <td id="cell-guardian">Guardian</td>
                </tr>
                <tr>
                  <td id="cell-escort">Escort</td>
                </tr>
                <tr>
                  <td id="cell-status" colspan="2">Status: Active</td>
                </tr>
              </tbody>
            </table>
            """,
            """
            #stats-table { width: 320px; }
            td { padding: 6px; border-width: 1px; border-color: #445566; }
            """);

        scene.Layout(400, 260);

        UiNode table = scene.FindByElementId("stats-table")!;
        UiNode prototype = scene.FindByElementId("cell-prototype")!;
        UiNode guardian = scene.FindByElementId("cell-guardian")!;
        UiNode escort = scene.FindByElementId("cell-escort")!;
        UiNode status = scene.FindByElementId("cell-status")!;

        Assert.That(prototype.LayoutRect.Width, Is.GreaterThan(guardian.LayoutRect.Width));
        Assert.That(escort.LayoutRect.X, Is.EqualTo(guardian.LayoutRect.X).Within(1.5f));
        Assert.That(escort.LayoutRect.Y, Is.GreaterThan(guardian.LayoutRect.Y));
        Assert.That(prototype.LayoutRect.Bottom, Is.EqualTo(escort.LayoutRect.Bottom).Within(1.5f));
        Assert.That(status.LayoutRect.X, Is.EqualTo(prototype.LayoutRect.X).Within(1.5f));
        Assert.That(status.LayoutRect.Width, Is.EqualTo(prototype.LayoutRect.Width + guardian.LayoutRect.Width).Within(1.5f));
        Assert.That(table.LayoutRect.Width, Is.EqualTo(320f).Within(1.5f));
    }

    [Test]
    public void UiStyleResolver_ParsesDirectionTextAlignObjectFitAndImageSlice()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            """
            <div id="rtl-text">مرحبا</div>
            <img id="cover-image" src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO6r0ioAAAAASUVORK5CYII=" />
            """,
            """
            #rtl-text { direction: rtl; text-align: start; }
            #cover-image { object-fit: cover; image-slice: 12 10 12 10; }
            """);

        scene.Layout(320, 200);

        UiNode text = scene.FindByElementId("rtl-text")!;
        UiNode image = scene.FindByElementId("cover-image")!;

        Assert.That(text.Style.Direction, Is.EqualTo(UiTextDirection.Rtl));
        Assert.That(text.Style.TextAlign, Is.EqualTo(UiTextAlign.Start));
        Assert.That(image.Style.ObjectFit, Is.EqualTo(UiObjectFit.Cover));
        Assert.That(image.Style.ImageSlice.Left, Is.EqualTo(10f).Within(0.01f));
        Assert.That(image.Style.ImageSlice.Top, Is.EqualTo(12f).Within(0.01f));
    }

    [Test]
    public void UiStyleResolver_TreatsWhereAsZeroSpecificity_AndParsesTransformAndZIndex()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            "<div id='card' class='card hero'>Card</div>",
            """
            .card.hero { background-color:#3366ff; }
            .card:where(.hero) { background-color:#22aa22; z-index:6; transform:translate(12px, 8px) rotate(10deg); }
            """);

        scene.Layout(220, 140);

        UiNode card = scene.FindByElementId("card")!;

        Assert.That(card.Style.BackgroundColor, Is.EqualTo(SKColor.Parse("#3366ff")));
        Assert.That(card.Style.ZIndex, Is.EqualTo(6));
        Assert.That(card.Style.Transform.HasOperations, Is.True);
        Assert.That(card.Style.Transform.Operations.Count, Is.EqualTo(2));
    }

        [Test]
    public void UiStyleResolver_ParsesPhaseTwoLayeringBorderMaskAndClipPath()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            """
            <div id="phase-two">Phase Two</div>
            <div id="phase-two-inset">Inset Clip</div>
            """,
            """
            #phase-two {
                background-image: linear-gradient(90deg, rgba(255,0,0,0.9) 0%, rgba(255,128,0,0.85) 100%), linear-gradient(180deg, rgba(0,0,255,0.55) 0%, rgba(0,255,255,0.35) 100%);
                box-shadow: 0px 6px 12px rgba(0,0,0,0.28), 0px 18px 28px rgba(37,99,235,0.22);
                border-style: dashed;
                mask-image: linear-gradient(90deg, rgba(255,255,255,0.1) 0%, rgba(255,255,255,1) 32%, rgba(255,255,255,1) 68%, rgba(255,255,255,0.1) 100%);
                clip-path: circle(42% at 50% 50%);
            }
            #phase-two-inset {
                border-style: dotted;
                clip-path: inset(8px 12px 16px 20px);
            }
            """);

        scene.Layout(320, 180);

        UiNode layered = scene.FindByElementId("phase-two")!;
        UiNode inset = scene.FindByElementId("phase-two-inset")!;

        Assert.That(layered.Style.BackgroundLayers.Count, Is.EqualTo(2));
        Assert.That(layered.Style.BoxShadows.Count, Is.EqualTo(2));
        Assert.That(layered.Style.BorderStyle, Is.EqualTo(UiBorderStyle.Dashed));
        Assert.That(layered.Style.MaskGradient, Is.Not.Null);
        Assert.That(layered.Style.ClipPath, Is.Not.Null);
        Assert.That(layered.Style.ClipPath!.Kind, Is.EqualTo(UiClipPathKind.Circle));

        Assert.That(inset.Style.BorderStyle, Is.EqualTo(UiBorderStyle.Dotted));
        Assert.That(inset.Style.ClipPath, Is.Not.Null);
        Assert.That(inset.Style.ClipPath!.Kind, Is.EqualTo(UiClipPathKind.Inset));
        Assert.That(inset.Style.ClipPath.Inset.Left, Is.EqualTo(20f).Within(0.01f));
        Assert.That(inset.Style.ClipPath.Inset.Top, Is.EqualTo(8f).Within(0.01f));
    }

    [Test]
    public void UiStyleResolver_ParsesTextOverflowAndTextDecorationProperties()
    {
        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene(
            """
            <div id="ellipsis">Long showcase headline for narrow HUD slots</div>
            <div id="decorated">Decorated text</div>
            """,
            """
            #ellipsis { width: 96px; white-space: nowrap; text-overflow: ellipsis; overflow: hidden; }
            #decorated { text-decoration: underline line-through; direction: rtl; text-align: end; }
            """);

        scene.Layout(320, 180);
        UiNode ellipsis = scene.FindByElementId("ellipsis")!;
        UiNode decorated = scene.FindByElementId("decorated")!;

        Assert.That(ellipsis.Style.WhiteSpace, Is.EqualTo(UiWhiteSpace.NoWrap));
        Assert.That(ellipsis.Style.TextOverflow, Is.EqualTo(UiTextOverflow.Ellipsis));
        Assert.That(decorated.Style.TextDecorationLine.HasFlag(UiTextDecorationLine.Underline), Is.True);
        Assert.That(decorated.Style.TextDecorationLine.HasFlag(UiTextDecorationLine.LineThrough), Is.True);
        Assert.That(decorated.Style.Direction, Is.EqualTo(UiTextDirection.Rtl));
        Assert.That(decorated.Style.TextAlign, Is.EqualTo(UiTextAlign.End));
    }

    [Test]
    public void UiCssParser_ParsesKeyframes_AndAnimationShorthand()
    {
        const string css = """
            @keyframes pulse {
                from { background-color:#112233; opacity:1; }
                50% { opacity:0.6; }
                to { background-color:#445566; opacity:0.2; filter:blur(4px); }
            }

            .probe {
                animation: pulse 400ms linear 120ms 2 alternate both;
            }
            """;

        UiStyleSheet sheet = UiCssParser.ParseStyleSheet(css);

        Assert.That(sheet.TryGetKeyframes("pulse", out UiKeyframeDefinition? keyframes), Is.True);
        Assert.That(keyframes, Is.Not.Null);
        Assert.That(keyframes!.Stops.Count, Is.EqualTo(3));
        Assert.That(keyframes.Stops[^1].Declaration["filter"], Is.EqualTo("blur(4px)"));

        UiMarkupLoader loader = new();
        UiScene scene = loader.LoadScene("<div id='probe' class='probe'>Probe</div>", css);
        scene.Layout(220, 120);

        UiNode probe = scene.FindByElementId("probe")!;
        Assert.That(probe.Style.Animation, Is.Not.Null);
        Assert.That(probe.Style.Animation!.Entries.Count, Is.EqualTo(1));
        Assert.That(probe.Style.Animation.Entries[0].Name, Is.EqualTo("pulse"));
        Assert.That(probe.RenderStyle.BackgroundColor, Is.EqualTo(SKColor.Parse("#112233")));
        Assert.That(probe.RenderStyle.Opacity, Is.EqualTo(1f).Within(0.01f));
    }

    private sealed class CanvasMarkupCodeBehind
    {
        private UiCanvasContent BuildCanvas()
        {
            return new UiCanvasContent((canvas, rect) =>
            {
                using SKPaint paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("#f59e0b") };
                canvas.DrawRoundRect(rect, 10f, 10f, paint);
            });
        }
    }
}
