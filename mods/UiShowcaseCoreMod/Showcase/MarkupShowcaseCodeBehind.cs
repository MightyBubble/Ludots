using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace UiShowcaseCoreMod.Showcase;

internal sealed class MarkupShowcaseCodeBehind
{
    private readonly UiMarkupLoader _loader = new();
    private int _count = 5;
    private string _themeClass = "theme-light";
    private string _densityClass = "density-cozy";
    private bool _checkboxChecked = true;
    private bool _switchEnabled = true;
    private bool _formError = true;
    private string _formStatus = "Waiting validation";
    private int _selectedItem = 2;
    private bool _modalOpen;
    private bool _toastVisible;

    internal UiScene BuildScene()
    {
        UiDocument document = _loader.LoadDocument(BuildHtml(), BuildCss());
        ValidatePrototype(document);
        UiScene scene = new();
        scene.MountDocument(document);
        MarkupBinder.Bind(scene, this);
        return scene;
    }

    private string BuildHtml()
    {
        string diagnostics = string.Join(string.Empty, ScanPrototypeDiagnostics().Select(item => $"<div class=\"prototype-box\">{item}</div>"));
        string modal = _modalOpen
            ? "<div id=\"markup-modal\" class=\"overlay-card\"><div>Modal opened ¡ª code-behind stays in C#.</div><button ui-click=\"ToggleModal\">Close Modal</button></div>"
            : "<div class=\"muted\">Tooltip / Drawer / ContextMenu share the same overlay contract.</div>";
        string toast = _toastVisible
            ? "<div id=\"markup-toast\" class=\"toast-badge\">Toast: markup action committed.</div>"
            : "<div class=\"muted\">Toast hidden.</div>";

        return $$"""
<div class="skin-root {{_themeClass}} {{_densityClass}}">
  <div class="skin-header">Markup + CodeBehind ¡ª HTML/CSS Prototype</div>
  <div class="control-row">
    <button id="markup-theme-light" ui-click="ThemeLight">Light</button>
    <button id="markup-theme-dark" class="skin-primary" ui-click="ThemeDark">Dark</button>
    <button id="markup-theme-hud" ui-click="ThemeHud">GameHUD</button>
  </div>
  <div class="page-grid-row">
    <article id="markup-overview" class="skin-card">
      <div class="page-card-title">OverviewPage</div>
      <div class="page-copy">Prototype-first authoring, native DOM, all behavior in pure C#.</div>
      <div id="markup-count">Counter: {{_count}}</div>
      <div class="control-row">
        <button id="markup-inc" class="skin-primary" ui-click="Increment">Increment</button>
        <button id="markup-reset" ui-click="ResetCounter">Reset</button>
      </div>
      <div class="muted">Theme: {{UiShowcaseScaffolding.ThemeLabel(_themeClass)}} / Density: {{UiShowcaseScaffolding.DensityLabel(_densityClass)}}</div>
    </article>
    <article id="markup-controls" class="skin-card">
      <div class="page-card-title">ControlsPage</div>
      <div class="control-row">
        <button id="markup-checkbox" class="control-chip {{(_checkboxChecked ? "active" : string.Empty)}}" ui-click="ToggleCheckbox">Checkbox: {{(_checkboxChecked ? "Checked" : "Off")}}</button>
        <button id="markup-switch" class="control-chip {{(_switchEnabled ? "active" : string.Empty)}}" ui-click="ToggleSwitch">Switch: {{(_switchEnabled ? "On" : "Off")}}</button>
        <div id="markup-radio" class="control-chip active">Radio: Primary</div>
      </div>
      <div class="control-row">
        <div class="control-chip">Select / Dropdown</div>
        <div class="control-chip">Slider 72%</div>
      </div>
      <div class="page-copy">ProgressBar</div>
      <div class="progress-track"><div class="progress-fill" style="width:72%;"></div></div>
    </article>
    <article id="markup-forms" class="skin-card">
      <div class="page-card-title">FormsPage</div>
      <div class="control-chip">Email: markup@ludots.dev</div>
      <div class="control-chip">Password: ???????</div>
      <div class="control-chip">Textarea / validation summary</div>
      <div id="markup-form-status" class="{{(_formError ? "error-text" : "ok-text")}}">{{_formStatus}}</div>
      <div class="control-row">
        <button ui-click="SubmitInvalid">Invalid</button>
        <button class="skin-primary" ui-click="SubmitValid">Valid</button>
        <button ui-click="ResetForm">Reset</button>
      </div>
    </article>
  </div>
  <div class="page-grid-row">
    <article id="markup-collections" class="skin-card">
      <div class="page-card-title">CollectionsPage</div>
      <div id="markup-selected" class="page-copy">Selected item: #{{_selectedItem}}</div>
      <div class="control-row">
        <button id="markup-item-1" class="control-chip {{(_selectedItem == 1 ? "selected-item" : string.Empty)}}" ui-click="SelectItemOne">Item 1</button>
        <button id="markup-item-2" class="control-chip {{(_selectedItem == 2 ? "selected-item" : string.Empty)}}" ui-click="SelectItemTwo">Item 2</button>
        <button id="markup-item-3" class="control-chip {{(_selectedItem == 3 ? "selected-item" : string.Empty)}}" ui-click="SelectItemThree">Item 3</button>
      </div>
      <div class="muted">Same semantic page as Compose / Reactive.</div>
    </article>
    <article id="markup-overlays" class="skin-card">
      <div class="page-card-title">OverlaysPage</div>
      <div class="control-row">
        <button id="markup-modal-toggle" class="skin-primary" ui-click="ToggleModal">{{(_modalOpen ? "Close Modal" : "Open Modal")}}</button>
        <button id="markup-toast-toggle" ui-click="ToggleToast">{{(_toastVisible ? "Hide Toast" : "Show Toast")}}</button>
      </div>
      {{modal}}
      {{toast}}
    </article>
    <article id="markup-styles" class="skin-card">
      <div class="page-card-title">StylesPage</div>
      <div class="page-copy">Typography / spacing / density / CSS token parity.</div>
      <div class="control-row">
        <button ui-click="DensityCompact">Compact</button>
        <button class="skin-primary" ui-click="DensityCozy">Cozy</button>
        <button ui-click="DensityComfortable">Comfortable</button>
      </div>
      <div id="markup-density" class="muted">Current density: {{UiShowcaseScaffolding.DensityLabel(_densityClass)}}</div>
      <div class="control-row">
        <div class="control-chip state-disabled">Disabled</div>
        <div class="control-chip active">Loading</div>
        <div class="control-chip error-text">Error</div>
      </div>
    </article>
  </div>
  <article id="markup-prototype" class="skin-card">
    <div class="page-card-title">PrototypeImportPage</div>
    <div class="page-copy">Prototype HTML/CSS compiles into native DOM, then binds back to C# methods.</div>
    <div class="muted">Unsupported features are surfaced as diagnostics instead of silently ignored.</div>
    <div class="control-row">{{diagnostics}}</div>
  </article>
</div>
""";
    }

    private static string BuildCss()
    {
        return UiShowcaseStyles.BuildAuthoringCss() + """
#markup-count { font-size:42px; font-weight:700; }
""";
    }

    private void ValidatePrototype(UiDocument document)
    {
        if (document.QuerySelector("#markup-count") == null || document.QuerySelectorAll(".skin-card").Count < 6)
        {
            throw new InvalidOperationException("Markup showcase prototype did not compile into the expected native DOM structure.");
        }
    }

    private IEnumerable<string> ScanPrototypeDiagnostics()
    {
        const string prototypeCss = "grid-template-columns:repeat(3,1fr); animation:fade-in 180ms ease; calc(100% - 24px); position:fixed;";
        if (prototypeCss.Contains("grid-template-columns", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Unsupported: CSS Grid layout";
        }

        if (prototypeCss.Contains("animation:", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Unsupported: CSS animations";
        }

        if (prototypeCss.Contains("calc(", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Unsupported: calc() expressions";
        }

        if (prototypeCss.Contains("position:fixed", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Unsupported: browser fixed positioning";
        }
    }

    private void Rebuild(UiActionContext context)
    {
        context.Scene.Dispatcher.Reset();
        UiDocument document = _loader.LoadDocument(BuildHtml(), BuildCss());
        ValidatePrototype(document);
        context.Scene.MountDocument(document);
        MarkupBinder.Bind(context.Scene, this);
    }

    private void ThemeLight(UiActionContext context) { _themeClass = "theme-light"; Rebuild(context); }
    private void ThemeDark(UiActionContext context) { _themeClass = "theme-dark"; Rebuild(context); }
    private void ThemeHud(UiActionContext context) { _themeClass = "theme-hud"; Rebuild(context); }
    private void DensityCompact(UiActionContext context) { _densityClass = "density-compact"; Rebuild(context); }
    private void DensityCozy(UiActionContext context) { _densityClass = "density-cozy"; Rebuild(context); }
    private void DensityComfortable(UiActionContext context) { _densityClass = "density-comfortable"; Rebuild(context); }
    private void Increment(UiActionContext context) { _count++; Rebuild(context); }
    private void ResetCounter(UiActionContext context) { _count = 0; Rebuild(context); }
    private void ToggleCheckbox(UiActionContext context) { _checkboxChecked = !_checkboxChecked; Rebuild(context); }
    private void ToggleSwitch(UiActionContext context) { _switchEnabled = !_switchEnabled; Rebuild(context); }
    private void SubmitInvalid(UiActionContext context) { _formError = true; _formStatus = "Validation failed: email is invalid"; Rebuild(context); }
    private void SubmitValid(UiActionContext context) { _formError = false; _formStatus = "Form submitted successfully"; Rebuild(context); }
    private void ResetForm(UiActionContext context) { _formError = true; _formStatus = "Waiting validation"; Rebuild(context); }
    private void SelectItemOne(UiActionContext context) { _selectedItem = 1; Rebuild(context); }
    private void SelectItemTwo(UiActionContext context) { _selectedItem = 2; Rebuild(context); }
    private void SelectItemThree(UiActionContext context) { _selectedItem = 3; Rebuild(context); }
    private void ToggleModal(UiActionContext context) { _modalOpen = !_modalOpen; Rebuild(context); }
    private void ToggleToast(UiActionContext context) { _toastVisible = !_toastVisible; Rebuild(context); }
}
