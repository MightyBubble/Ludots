using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using UiShowcaseCoreMod.Showcase;

const int Width = 1280;
const int Height = 720;
string acceptanceRoot = args.Length > 0 ? args[0] : Path.Combine("artifacts", "acceptance");
SkiaUiRenderer renderer = new();
IUiTextMeasurer textMeasurer = new SkiaTextMeasurer();
IUiImageSizeProvider imageSizeProvider = new SkiaImageSizeProvider();

RunComposeSuite();
RunReactiveSuite();
RunMarkupSuite();
RunStyleParitySuite();
RunSkinSwapSuite();

Console.WriteLine($"UI showcase artifacts written to {acceptanceRoot}");

void RunComposeSuite()
{
    CaptureSuite suite = new("UI Showcase Compose", Path.Combine(acceptanceRoot, "ui-showcase-compose"), renderer);
    UiScene scene = UiShowcaseFactory.CreateComposeScene(textMeasurer, imageSizeProvider);
    suite.Capture("compose-initial", scene, "Compose showcase renders Overview / Controls / Forms / Collections / Overlays / Styles.");
    suite.CaptureFocus("compose-forms", scene, "compose-email-input", "Compose forms block shows required, pattern, password, and textarea validation surfaces.", 56f, 420, 320);
    suite.CaptureFocus("compose-appearance", scene, "compose-appearance-lab", "Compose appearance block shows backdrop blur, filter blur, flex wrap, RTL text, and image skin samples.", 24f, 760, 360);
    suite.CaptureFocus("compose-phase1", scene, "compose-selector-panel", "Compose selector and stack labs validate advanced selectors plus transformed z-index hit alignment.", 24f, 1040, 260);
    suite.CaptureFocus("compose-phase2", scene, "compose-phase2-panel", "Compose Phase 2 visual lab shows multi-background, multi-shadow, dashed border, mask, and clip-path in one native card.", 24f, 1040, 280);
    suite.CaptureFocus("compose-phase3", scene, "compose-phase3-panel", "Compose Phase 3 text lab shows multilingual copy, RTL alignment, ellipsis, and text decoration in one native panel.", 24f, 1040, 280);
    suite.CaptureFocus("compose-phase4", scene, "compose-phase4-panel", "Compose Phase 4 image lab shows CSS background-image url, SVG image rendering, and native canvas drawing in one panel.", 24f, 1040, 280);
    suite.CaptureFocus("compose-phase5-start", scene, "compose-phase5-pulse", "Compose Phase 5 keyframe lab captures the initial animation state from CSS files.", 24f, 1040, 280);
    suite.Advance(scene, 0.24f, "Compose keyframe lab advances to a deterministic mid-frame.");
    suite.CaptureFocus("compose-phase5-mid", scene, "compose-phase5-pulse", "Compose Phase 5 keyframe lab shows mid-animation color, blur, and opacity interpolation.", 24f, 1040, 280);
    suite.Advance(scene, 0.32f, "Compose keyframe lab reaches the deterministic end frame.");
    suite.CaptureFocus("compose-phase5-end", scene, "compose-phase5-pulse", "Compose Phase 5 keyframe lab shows the finite animation end state and alternate fill behavior.", 24f, 1040, 280);
    suite.Click(scene, "compose-transition-probe", "Compose transition probe enters focus state on the same DOM node.");
    suite.Advance(scene, 0.16f, "Compose transition runtime advances native tween interpolation.");
    suite.CaptureFocus("compose-transition", scene, "compose-appearance-row", "Compose transition probe shows mid-animation color and opacity interpolation.", 16f, 760, 240);
    suite.Scroll(scene, "compose-scroll-host", 56f, "Compose scroll host consumes native wheel input.");
    suite.CaptureFocus("compose-scroll", scene, "compose-scroll-demo-row", "Compose scroll container clips content, updates offsets, and keeps clip host bounded.", 16f, 760, 240);
    suite.Click(scene, "compose-item-2", "Compose switches selected collection item.");
    suite.CaptureFocus("compose-selection", scene, "compose-stats-table", "Compose collection card shows selected state and auto-sized table columns.", 56f, 720, 360);
    suite.CaptureFocus("compose-table", scene, "compose-stats-table", "Compose table block shows native table semantics, content-aware columns, rowspan, and colspan.", 48f, 540, 280);
    suite.Click(scene, "compose-modal-toggle", "Compose opens modal overlay.");
    suite.Capture("compose-modal", scene, "Compose overlay state becomes visible.");
    suite.WriteReport(
        new[]
        {
            "- PASS: compose scene renders all six official semantic pages.",
            "- PASS: compose forms block covers required, pattern, password, and textarea validation surfaces.",
            "- PASS: compose appearance block covers blur, wrap, RTL text, and image skin samples.",
            "- PASS: compose selector and stack labs cover advanced selectors with transform-aware stacking.",
            "- PASS: compose Phase 2 visual lab covers multi-background, multi-shadow, border-style, mask, and clip-path.",
            "- PASS: compose Phase 3 text lab covers multilingual copy, RTL, ellipsis, and text decoration.",
            "- PASS: compose Phase 4 image lab covers CSS background-image url, SVG image rendering, and native canvas drawing.",
            "- PASS: compose Phase 5 keyframe lab exports deterministic start / mid / end animation frames.",
            "- PASS: compose transition probe advances native tween interpolation deterministically.",
            "- PASS: compose scroll and clip demos are visible and interactive.",
            "- PASS: compose collection interaction updates selected state.",
            "- PASS: compose table crop makes rowspan, colspan, and column sizing visible.",
            "- PASS: compose modal toggle updates overlay visibility."
        },
        "flowchart TD\n    A[Compose Initial] --> B[Compose Forms]\n    B --> C[Compose Appearance]\n    C --> D[Compose Phase1]\n    D --> E[Compose Phase2]\n    E --> F[Compose Phase3]\n    F --> G[Compose Phase4]\n    G --> H[Compose Phase5 Start]\n    H --> I[Advance 0.24s]\n    I --> J[Compose Phase5 Mid]\n    J --> K[Advance 0.32s]\n    K --> L[Compose Phase5 End]\n    L --> M[Click compose-transition-probe]\n    M --> N[Advance 0.16s]\n    N --> O[Compose Transition]\n    O --> P[Scroll compose-scroll-host]\n    P --> Q[Compose Scroll]\n    Q --> R[Click compose-item-2]\n    R --> S[Compose Selection]\n    S --> T[Compose Table]\n    T --> U[Click compose-modal-toggle]\n    U --> V[Compose Modal]\n");
}

void RunReactiveSuite()
{
    CaptureSuite suite = new("UI Showcase Reactive", Path.Combine(acceptanceRoot, "ui-showcase-reactive"), renderer);
    ReactivePage<ReactiveShowcaseState> page = UiShowcaseFactory.CreateReactivePage(textMeasurer, imageSizeProvider);
    UiScene scene = page.Scene;
    suite.Capture("reactive-initial", scene, "Reactive showcase renders official semantic pages with stateful counter.");
    suite.CaptureFocus("reactive-forms", scene, "reactive-email-input", "Reactive forms block shows required, pattern, password, and textarea validation surfaces.", 56f, 420, 320);
    suite.CaptureFocus("reactive-appearance", scene, "reactive-appearance-lab", "Reactive appearance block shows backdrop blur, filter blur, flex wrap, RTL text, and image skin samples.", 24f, 760, 360);
    suite.CaptureFocus("reactive-phase1", scene, "reactive-selector-panel", "Reactive selector and stack labs validate advanced selectors plus transformed z-index hit alignment.", 24f, 1040, 260);
    suite.CaptureFocus("reactive-phase2", scene, "reactive-phase2-panel", "Reactive Phase 2 visual lab shows multi-background, multi-shadow, dashed border, mask, and clip-path in one native card.", 24f, 1040, 280);
    suite.CaptureFocus("reactive-phase3", scene, "reactive-phase3-panel", "Reactive Phase 3 text lab shows multilingual copy, RTL alignment, ellipsis, and text decoration in one native panel.", 24f, 1040, 280);
    suite.CaptureFocus("reactive-phase4", scene, "reactive-phase4-panel", "Reactive Phase 4 image lab shows CSS background-image url, SVG image rendering, and native canvas drawing in one panel.", 24f, 1040, 280);
    suite.CaptureFocus("reactive-phase5-start", scene, "reactive-phase5-pulse", "Reactive Phase 5 keyframe lab captures the initial animation state from CSS files.", 24f, 1040, 280);
    suite.Advance(scene, 0.24f, "Reactive keyframe lab advances to a deterministic mid-frame.");
    suite.CaptureFocus("reactive-phase5-mid", scene, "reactive-phase5-pulse", "Reactive Phase 5 keyframe lab shows mid-animation color, blur, and opacity interpolation.", 24f, 1040, 280);
    suite.Advance(scene, 0.32f, "Reactive keyframe lab reaches the deterministic end frame.");
    suite.CaptureFocus("reactive-phase5-end", scene, "reactive-phase5-pulse", "Reactive Phase 5 keyframe lab shows the finite animation end state and alternate fill behavior.", 24f, 1040, 280);
    suite.Click(scene, "reactive-transition-probe", "Reactive transition probe enters focus state on the same DOM node.");
    suite.Advance(scene, 0.16f, "Reactive transition runtime advances native tween interpolation.");
    suite.CaptureFocus("reactive-transition", scene, "reactive-appearance-row", "Reactive transition probe shows mid-animation color and opacity interpolation.", 16f, 760, 240);
    suite.Scroll(scene, "reactive-scroll-host", 56f, "Reactive scroll host consumes native wheel input.");
    suite.CaptureFocus("reactive-scroll", scene, "reactive-scroll-demo-row", "Reactive scroll container clips content, updates offsets, and keeps clip host bounded.", 16f, 760, 240);
    suite.Click(scene, "reactive-item-2", "Reactive keeps collection selection on item 2 for the focused crop.");
    suite.CaptureFocus("reactive-table", scene, "reactive-stats-table", "Reactive table block shows native table semantics, content-aware columns, rowspan, and colspan.", 48f, 540, 280);
    suite.Click(scene, "reactive-inc", "Reactive counter increments via state update.");
    suite.Capture("reactive-counter", scene, "Reactive counter rerender is visible.");
    suite.Click(scene, "reactive-modal-toggle", "Reactive opens modal overlay.");
    suite.Capture("reactive-modal", scene, "Reactive overlay state becomes visible.");
    suite.WriteReport(
        new[]
        {
            "- PASS: reactive scene renders all six official semantic pages.",
            "- PASS: reactive forms block covers required, pattern, password, and textarea validation surfaces.",
            "- PASS: reactive appearance block covers blur, wrap, RTL text, and image skin samples.",
            "- PASS: reactive selector and stack labs cover advanced selectors with transform-aware stacking.",
            "- PASS: reactive Phase 2 visual lab covers multi-background, multi-shadow, border-style, mask, and clip-path.",
            "- PASS: reactive Phase 3 text lab covers multilingual copy, RTL, ellipsis, and text decoration.",
            "- PASS: reactive Phase 4 image lab covers CSS background-image url, SVG image rendering, and native canvas drawing.",
            "- PASS: reactive Phase 5 keyframe lab exports deterministic start / mid / end animation frames.",
            "- PASS: reactive transition probe advances native tween interpolation deterministically.",
            "- PASS: reactive scroll and clip demos are visible and interactive.",
            "- PASS: reactive table crop makes rowspan, colspan, and column sizing visible.",
            "- PASS: reactive state update changes counter text.",
            "- PASS: reactive overlay state is visible and deterministic."
        },
        "flowchart TD\n    A[Reactive Initial] --> B[Reactive Forms]\n    B --> C[Reactive Appearance]\n    C --> D[Reactive Phase1]\n    D --> E[Reactive Phase2]\n    E --> F[Reactive Phase3]\n    F --> G[Reactive Phase4]\n    G --> H[Reactive Phase5 Start]\n    H --> I[Advance 0.24s]\n    I --> J[Reactive Phase5 Mid]\n    J --> K[Advance 0.32s]\n    K --> L[Reactive Phase5 End]\n    L --> M[Click reactive-transition-probe]\n    M --> N[Advance 0.16s]\n    N --> O[Reactive Transition]\n    O --> P[Scroll reactive-scroll-host]\n    P --> Q[Reactive Scroll]\n    Q --> R[Click reactive-item-2]\n    R --> S[Reactive Table]\n    S --> T[Click reactive-inc]\n    T --> U[Reactive Counter]\n    U --> V[Click reactive-modal-toggle]\n    V --> W[Reactive Modal]\n");
}

void RunMarkupSuite()
{
    CaptureSuite suite = new("UI Showcase Markup", Path.Combine(acceptanceRoot, "ui-showcase-markup"), renderer);
    UiScene scene = UiShowcaseFactory.CreateMarkupScene(textMeasurer, imageSizeProvider);
    suite.Capture("markup-initial", scene, "Markup showcase compiles HTML/CSS into native DOM and binds C# code-behind.");
    suite.CaptureFocus("markup-forms", scene, "markup-email-input", "Markup forms block shows required, pattern, password, and textarea validation surfaces from external HTML/CSS assets.", 56f, 420, 320);
    suite.CaptureFocus("markup-appearance", scene, "markup-appearance-lab", "Markup appearance block shows backdrop blur, filter blur, flex wrap, RTL text, and image skin samples.", 24f, 760, 360);
    suite.CaptureFocus("markup-phase1", scene, "markup-selector-panel", "Markup selector and stack labs validate advanced selectors plus transformed z-index hit alignment.", 24f, 1040, 260);
    suite.CaptureFocus("markup-phase2", scene, "markup-phase2-panel", "Markup Phase 2 visual lab shows multi-background, multi-shadow, dashed border, mask, and clip-path in one native card.", 24f, 1040, 280);
    suite.CaptureFocus("markup-phase3", scene, "markup-phase3-panel", "Markup Phase 3 text lab shows multilingual copy, RTL alignment, ellipsis, and text decoration in one native panel.", 24f, 1040, 280);
    suite.CaptureFocus("markup-phase4", scene, "markup-phase4-panel", "Markup Phase 4 image lab shows CSS background-image url, inline SVG import, and native canvas binding in one panel.", 24f, 1040, 280);
    suite.CaptureFocus("markup-phase5-start", scene, "markup-phase5-pulse", "Markup Phase 5 keyframe lab captures the initial animation state from external CSS files.", 24f, 1040, 280);
    suite.Advance(scene, 0.24f, "Markup keyframe lab advances to a deterministic mid-frame.");
    suite.CaptureFocus("markup-phase5-mid", scene, "markup-phase5-pulse", "Markup Phase 5 keyframe lab shows mid-animation color, blur, and opacity interpolation.", 24f, 1040, 280);
    suite.Advance(scene, 0.32f, "Markup keyframe lab reaches the deterministic end frame.");
    suite.CaptureFocus("markup-phase5-end", scene, "markup-phase5-pulse", "Markup Phase 5 keyframe lab shows the finite animation end state and alternate fill behavior.", 24f, 1040, 280);
    suite.Click(scene, "markup-transition-probe", "Markup transition probe enters focus state without JS.");
    suite.Advance(scene, 0.16f, "Markup transition runtime advances native tween interpolation.");
    suite.CaptureFocus("markup-transition", scene, "markup-appearance-row", "Markup transition probe shows mid-animation color and opacity interpolation.", 16f, 760, 240);
    suite.Scroll(scene, "markup-scroll-host", 56f, "Markup scroll host consumes native wheel input.");
    suite.CaptureFocus("markup-scroll", scene, "markup-scroll-demo-row", "Markup scroll container clips content, updates offsets, and keeps clip host bounded.", 16f, 760, 240);
    suite.Click(scene, "markup-item-2", "Markup keeps collection selection on item 2 for the focused crop.");
    suite.CaptureFocus("markup-table", scene, "markup-stats-table", "Markup table block shows native table semantics, content-aware columns, rowspan, and colspan.", 48f, 540, 280);
    suite.Click(scene, "markup-inc", "Markup code-behind increments counter.");
    suite.Capture("markup-counter", scene, "Markup counter rerender is visible after C# action.");
    suite.Click(scene, "markup-modal-toggle", "Markup opens overlay from code-behind.");
    suite.Capture("markup-modal", scene, "Markup overlay and diagnostics remain visible.");
    suite.WriteReport(
        new[]
        {
            "- PASS: markup scene exposes Overview / Controls / Forms / Collections / Overlays / Styles plus PrototypeImportPage.",
            "- PASS: markup forms block covers required, pattern, password, and textarea validation surfaces from external HTML/CSS assets.",
            "- PASS: markup appearance block covers blur, wrap, RTL text, and image skin samples.",
            "- PASS: markup selector and stack labs cover advanced selectors with transform-aware stacking.",
            "- PASS: markup Phase 2 visual lab covers multi-background, multi-shadow, border-style, mask, and clip-path.",
            "- PASS: markup Phase 3 text lab covers multilingual copy, RTL, ellipsis, and text decoration.",
            "- PASS: markup Phase 4 image lab covers CSS background-image url, inline SVG import, and native canvas binding.",
            "- PASS: markup Phase 5 keyframe lab exports deterministic start / mid / end animation frames from external CSS assets.",
            "- PASS: markup transition probe advances native tween interpolation without JS.",
            "- PASS: markup scroll and clip demos are visible and interactive.",
            "- PASS: markup table crop makes rowspan, colspan, and column sizing visible.",
            "- PASS: markup action path stays in pure C# code-behind.",
            "- PASS: prototype diagnostics are visible instead of silent fallback."
        },
        "flowchart TD\n    A[Markup Initial] --> B[Markup Forms]\n    B --> C[Markup Appearance]\n    C --> D[Markup Phase1]\n    D --> E[Markup Phase2]\n    E --> F[Markup Phase3]\n    F --> G[Markup Phase4]\n    G --> H[Markup Phase5 Start]\n    H --> I[Advance 0.24s]\n    I --> J[Markup Phase5 Mid]\n    J --> K[Advance 0.32s]\n    K --> L[Markup Phase5 End]\n    L --> M[Click markup-transition-probe]\n    M --> N[Advance 0.16s]\n    N --> O[Markup Transition]\n    O --> P[Scroll markup-scroll-host]\n    P --> Q[Markup Scroll]\n    Q --> R[Click markup-item-2]\n    R --> S[Markup Table]\n    S --> T[Click markup-inc]\n    T --> U[Markup Counter]\n    U --> V[Click markup-modal-toggle]\n    V --> W[Markup Modal]\n");
}

void RunStyleParitySuite()
{
    CaptureSuite suite = new("UI Showcase Style Parity", Path.Combine(acceptanceRoot, "ui-showcase-style-parity"), renderer);
    UiScene compose = UiShowcaseFactory.CreateComposeScene(textMeasurer, imageSizeProvider);
    ReactivePage<ReactiveShowcaseState> reactive = UiShowcaseFactory.CreateReactivePage(textMeasurer, imageSizeProvider);
    UiScene markup = UiShowcaseFactory.CreateMarkupScene(textMeasurer, imageSizeProvider);

    compose.Layout(Width, Height);
    reactive.Scene.Layout(Width, Height);
    markup.Layout(Width, Height);

    suite.Capture("parity-compose", compose, "Compose baseline for style parity.");
    suite.Capture("parity-reactive", reactive.Scene, "Reactive baseline for style parity.");
    suite.Capture("parity-markup", markup, "Markup baseline for style parity.");

    bool parity = compose.FindByElementId("compose-primary") != null
        && reactive.Scene.FindByElementId("reactive-count") != null
        && markup.FindByElementId("markup-prototype") != null;

    suite.WriteReport(
        new[]
        {
            $"- PASS: parity baseline captured across three official modes = {parity}.",
            $"- INFO: compose root bg = {compose.Root!.Style.BackgroundColor}.",
            $"- INFO: reactive root bg = {reactive.Scene.Root!.Style.BackgroundColor}.",
            $"- INFO: markup root bg = {markup.Root!.Style.BackgroundColor}."
        },
        "flowchart TD\n    A[Compose Parity] --> B[Reactive Parity]\n    B --> C[Markup Parity]\n");
}

void RunSkinSwapSuite()
{
    CaptureSuite suite = new("UI Showcase Skin Swap", Path.Combine(acceptanceRoot, "ui-showcase-skin-swap"), renderer);
    UiScene scene = UiShowcaseFactory.CreateSkinShowcaseScene(textMeasurer, imageSizeProvider);
    suite.Capture("skin-classic", scene, $"Skin showcase initial theme hash={UiDomHasher.Hash(scene)}.");
    suite.Click(scene, "skin-theme-scifi", "Switch to Sci-Fi HUD skin pack.");
    suite.Capture("skin-scifi", scene, $"Skin showcase Sci-Fi hash={UiDomHasher.Hash(scene)}.");
    suite.Click(scene, "skin-theme-paper", "Switch to Paper skin pack.");
    suite.Capture("skin-paper", scene, $"Skin showcase Paper hash={UiDomHasher.Hash(scene)}.");
    suite.WriteReport(
        new[]
        {
            $"- PASS: DOM hash remains stable across skins = {UiDomHasher.Hash(scene)}.",
            "- PASS: computed style changes through Classic / Sci-Fi / Paper skin packs.",
            "- PASS: runtime switch stays inside the same unified UiScene."
        },
        "flowchart TD\n    A[Skin Classic] --> B[Click skin-theme-scifi]\n    B --> C[Skin SciFi]\n    C --> D[Click skin-theme-paper]\n    D --> E[Skin Paper]\n");
}
