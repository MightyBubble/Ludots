using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;

namespace UiShowcaseCoreMod.Showcase;

internal sealed class ComposeShowcaseController
{
    private string _themeClass = "theme-dark";
    private string _densityClass = "density-cozy";
    private bool _checkboxChecked = true;
    private bool _switchEnabled = true;
    private bool _formError = true;
    private string _formStatus = "Waiting validation";
    private int _selectedItem = 1;
    private bool _modalOpen;
    private bool _toastVisible;

    internal UiScene BuildScene()
    {
        UiScene scene = new();
        RebuildScene(scene);
        return scene;
    }

    private void RebuildScene(UiScene scene)
    {
        scene.Dispatcher.Reset();
        int nextId = 1;
        scene.Mount(BuildRoot().Build(scene.Dispatcher, ref nextId));
        scene.SetStyleSheets(UiShowcaseScaffolding.AuthoringStyleSheet);
    }

    private UiElementBuilder BuildRoot()
    {
        return Ui.Column(
                Ui.Text("Compose Fluent — Official Native C# Style").Class("skin-header"),
                UiShowcaseScaffolding.BuildThemeToolbar(
                    "compose",
                    ctx => ChangeTheme(ctx, "theme-light"),
                    ctx => ChangeTheme(ctx, "theme-dark"),
                    ctx => ChangeTheme(ctx, "theme-hud")),
                Ui.Row(
                    BuildOverviewCard(),
                    BuildControlsCard(),
                    BuildFormsCard())
                    .Class("page-grid-row")
                    .Gap(12)
                    .FlexGrow(1),
                Ui.Row(
                    BuildCollectionsCard(),
                    BuildOverlaysCard(),
                    BuildStylesCard())
                    .Class("page-grid-row")
                    .Gap(12)
                    .FlexGrow(1))
            .Classes("skin-root", _themeClass, _densityClass)
            .Width(1280)
            .Height(720)
            .Gap(12);
    }

    private UiElementBuilder BuildOverviewCard()
    {
        return Ui.Card(
                Ui.Text("OverviewPage").Class("page-card-title"),
                Ui.Text("默认生产主路径：静态布局、HUD、主菜单、配置页。")
                    .Class("page-copy"),
                Ui.Text($"Theme: {UiShowcaseScaffolding.ThemeLabel(_themeClass)} / Density: {UiShowcaseScaffolding.DensityLabel(_densityClass)}")
                    .Id("compose-theme")
                    .Class("muted"),
                Ui.Text("Chain-style builders stay readable without DSL.").Class("muted"))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private UiElementBuilder BuildControlsCard()
    {
        return Ui.Card(
                Ui.Text("ControlsPage").Class("page-card-title"),
                Ui.Row(
                    Ui.Button("Primary").Id("compose-primary").Class("skin-primary"),
                    Ui.Button("Secondary").Id("compose-secondary"),
                    UiShowcaseScaffolding.BuildChip(_checkboxChecked ? "Checkbox: Checked" : "Checkbox: Off", _checkboxChecked, "compose-checkbox", ToggleCheckbox),
                    UiShowcaseScaffolding.BuildChip(_switchEnabled ? "Switch: On" : "Switch: Off", _switchEnabled, "compose-switch", ToggleSwitch))
                    .Class("control-row"),
                Ui.Row(
                    UiShowcaseScaffolding.BuildChip("Radio: Primary", true, "compose-radio"),
                    new UiElementBuilder(UiNodeKind.Select, "select").Text("Select / Dropdown").Class("control-chip").FlexGrow(1),
                    new UiElementBuilder(UiNodeKind.Slider, "slider").Text("Slider 72%").Class("control-chip").FlexGrow(1))
                    .Class("control-row"),
                Ui.Text("ProgressBar").Class("page-copy"),
                UiShowcaseScaffolding.BuildProgressBar(72f))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private UiElementBuilder BuildFormsCard()
    {
        return Ui.Card(
                Ui.Text("FormsPage").Class("page-card-title"),
                new UiElementBuilder(UiNodeKind.Input, "input").Text("Email: designer@ludots.dev").Class("control-chip"),
                new UiElementBuilder(UiNodeKind.Custom, "input").Text("Password: ???????").Class("control-chip"),
                new UiElementBuilder(UiNodeKind.TextArea, "textarea").Text("Textarea / validation summary").Class("control-chip"),
                Ui.Text(_formStatus).Id("compose-form-status").Class(_formError ? "error-text" : "ok-text"),
                Ui.Row(
                    Ui.Button("Invalid", SubmitInvalid),
                    Ui.Button("Valid", SubmitValid).Class("skin-primary"),
                    Ui.Button("Reset", ResetForm))
                    .Class("control-row"))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private UiElementBuilder BuildCollectionsCard()
    {
        return Ui.Card(
                Ui.Text("CollectionsPage").Class("page-card-title"),
                Ui.Text($"Selected item: #{_selectedItem}").Id("compose-selected").Class("page-copy"),
                Ui.Row(
                    UiShowcaseScaffolding.BuildSelectableCard("Item 1", _selectedItem == 1, "compose-item-1", ctx => SelectItem(ctx, 1)),
                    UiShowcaseScaffolding.BuildSelectableCard("Item 2", _selectedItem == 2, "compose-item-2", ctx => SelectItem(ctx, 2)),
                    UiShowcaseScaffolding.BuildSelectableCard("Item 3", _selectedItem == 3, "compose-item-3", ctx => SelectItem(ctx, 3)))
                    .Class("control-row"),
                Ui.Text("ListView / GridView / Tabs 共享同一语义状态模型。")
                    .Class("muted"))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private UiElementBuilder BuildOverlaysCard()
    {
        return Ui.Card(
                Ui.Text("OverlaysPage").Class("page-card-title"),
                Ui.Row(
                    Ui.Button(_modalOpen ? "Close Modal" : "Open Modal", ToggleModal).Id("compose-modal-toggle").Class("skin-primary"),
                    Ui.Button(_toastVisible ? "Hide Toast" : "Show Toast", ToggleToast).Id("compose-toast-toggle"))
                    .Class("control-row"),
                _modalOpen
                    ? Ui.Card(Ui.Text("Modal opened — deterministic action path.")).Id("compose-modal").Class("overlay-card")
                    : Ui.Text("Drawer / Tooltip / Toast share the same overlay semantics.").Class("muted"),
                _toastVisible
                    ? Ui.Text("Toast: compose action committed.").Id("compose-toast").Class("toast-badge")
                    : Ui.Text("Toast hidden.").Class("muted"))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private UiElementBuilder BuildStylesCard()
    {
        return Ui.Card(
                Ui.Text("StylesPage").Class("page-card-title"),
                Ui.Text("Typography / spacing / density / token parity.").Class("page-copy"),
                Ui.Row(
                    Ui.Button("Compact", ctx => ChangeDensity(ctx, "density-compact")),
                    Ui.Button("Cozy", ctx => ChangeDensity(ctx, "density-cozy")).Class("skin-primary"),
                    Ui.Button("Comfortable", ctx => ChangeDensity(ctx, "density-comfortable")))
                    .Class("control-row"),
                Ui.Text($"Current density: {UiShowcaseScaffolding.DensityLabel(_densityClass)}").Id("compose-density").Class("muted"),
                Ui.Row(
                    UiShowcaseScaffolding.BuildChip("Disabled", false).Class("state-disabled"),
                    UiShowcaseScaffolding.BuildChip("Loading", true),
                    UiShowcaseScaffolding.BuildChip("Error", false).Class("error-text"))
                    .Class("control-row"))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private void ChangeTheme(UiActionContext context, string themeClass)
    {
        _themeClass = themeClass;
        RebuildScene(context.Scene);
    }

    private void ChangeDensity(UiActionContext context, string densityClass)
    {
        _densityClass = densityClass;
        RebuildScene(context.Scene);
    }

    private void ToggleCheckbox(UiActionContext context)
    {
        _checkboxChecked = !_checkboxChecked;
        RebuildScene(context.Scene);
    }

    private void ToggleSwitch(UiActionContext context)
    {
        _switchEnabled = !_switchEnabled;
        RebuildScene(context.Scene);
    }

    private void SubmitInvalid(UiActionContext context)
    {
        _formError = true;
        _formStatus = "Validation failed: email is invalid";
        RebuildScene(context.Scene);
    }

    private void SubmitValid(UiActionContext context)
    {
        _formError = false;
        _formStatus = "Form submitted successfully";
        RebuildScene(context.Scene);
    }

    private void ResetForm(UiActionContext context)
    {
        _formError = true;
        _formStatus = "Waiting validation";
        RebuildScene(context.Scene);
    }

    private void SelectItem(UiActionContext context, int selectedItem)
    {
        _selectedItem = selectedItem;
        RebuildScene(context.Scene);
    }

    private void ToggleModal(UiActionContext context)
    {
        _modalOpen = !_modalOpen;
        RebuildScene(context.Scene);
    }

    private void ToggleToast(UiActionContext context)
    {
        _toastVisible = !_toastVisible;
        RebuildScene(context.Scene);
    }
}
