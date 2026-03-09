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

	private int _selectedMode = 1;

	private bool _modalOpen;

	private bool _toastVisible;

	internal UiScene BuildScene()
	{
		UiScene uiScene = new UiScene();
		RebuildScene(uiScene);
		return uiScene;
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
		return Ui.Column(Ui.Text("Compose Fluent - Official Native C# Style").Class("skin-header"), UiShowcaseScaffolding.BuildThemeToolbar("compose", delegate(UiActionContext ctx)
		{
			ChangeTheme(ctx, "theme-light");
		}, delegate(UiActionContext ctx)
		{
			ChangeTheme(ctx, "theme-dark");
		}, delegate(UiActionContext ctx)
		{
			ChangeTheme(ctx, "theme-hud");
		}), Ui.Row(BuildOverviewCard(), BuildControlsCard(), BuildFormsCard()).Class("page-grid-row").Gap(12f)
			.FlexGrow(1f), Ui.Row(BuildCollectionsCard(), BuildOverlaysCard(), BuildStylesCard()).Class("page-grid-row").Gap(12f)
			.FlexGrow(1f)).Classes("skin-root", _themeClass, _densityClass).Width(1280f)
			.Height(720f)
			.Gap(12f);
	}

	private UiElementBuilder BuildOverviewCard()
	{
		return Ui.Card(Ui.Text("OverviewPage").Class("page-card-title"), Ui.Text("Official production path for stable layouts, HUD, menus, and settings pages.").Class("page-copy"), Ui.Text("Theme: " + UiShowcaseScaffolding.ThemeLabel(_themeClass) + " / Density: " + UiShowcaseScaffolding.DensityLabel(_densityClass)).Id("compose-theme").Class("muted"), Ui.Text("Chain-style builders stay readable without DSL.").Class("muted")).Class("skin-card").FlexGrow(1f);
	}

	private UiElementBuilder BuildControlsCard()
	{
		return Ui.Card(Ui.Text("ControlsPage").Class("page-card-title"), Ui.Row(Ui.Button("Primary").Id("compose-primary").Class("skin-primary"), Ui.Button("Secondary").Id("compose-secondary"), UiShowcaseScaffolding.BuildChip(_checkboxChecked ? "Checkbox: Checked" : "Checkbox: Off", _checkboxChecked, "compose-checkbox", ToggleCheckbox), UiShowcaseScaffolding.BuildChip(_switchEnabled ? "Switch: On" : "Switch: Off", _switchEnabled, "compose-switch", ToggleSwitch)).Class("control-row"), Ui.Row(Ui.Radio("Radio: Primary", "compose-mode", _selectedMode == 1, delegate(UiActionContext ctx)
		{
			SelectMode(ctx, 1);
		}).Id("compose-radio-primary").Class("control-chip"), Ui.Radio("Radio: Secondary", "compose-mode", _selectedMode == 2, delegate(UiActionContext ctx)
		{
			SelectMode(ctx, 2);
		}).Id("compose-radio-secondary").Class("control-chip"), new UiElementBuilder(UiNodeKind.Select, "select").Text("Select / Dropdown").Class("control-chip").FlexGrow(1f), new UiElementBuilder(UiNodeKind.Slider, "slider").Text("Slider 72%").Class("control-chip").FlexGrow(1f)).Class("control-row"), Ui.Text("ProgressBar").Class("page-copy"), UiShowcaseScaffolding.BuildProgressBar(72f)).Class("skin-card").FlexGrow(1f);
	}

	private UiElementBuilder BuildFormsCard()
	{
		return Ui.Card(Ui.Text("FormsPage").Class("page-card-title"), Ui.Input().Id("compose-email-input").Class("control-chip")
			.Type("email")
			.Placeholder("Email / required / @ludots.dev")
			.Required()
			.Pattern("^[^@\\s]+@ludots\\.dev$")
			.Value(_formError ? string.Empty : "designer@ludots.dev"), Ui.Input().Id("compose-password-input").Class("control-chip")
			.Type("password")
			.Placeholder("Password / required / min 8")
			.Required()
			.MinLength(8)
			.Value(_formError ? string.Empty : "hunter22"), new UiElementBuilder(UiNodeKind.TextArea, "textarea").Id("compose-notes-input").Class("control-chip").Placeholder("Validation summary / notes / max 64")
			.Required()
			.MaxLength(64)
			.Value(_formError ? string.Empty : "Textarea / validation summary"), Ui.Text(_formStatus).Id("compose-form-status").Class(_formError ? "error-text" : "ok-text"), Ui.Row(Ui.Button("Invalid", SubmitInvalid), Ui.Button("Valid", SubmitValid).Class("skin-primary"), Ui.Button("Reset", ResetForm)).Class("control-row")).Class("skin-card").FlexGrow(1f);
	}

	private UiElementBuilder BuildCollectionsCard()
	{
		return Ui.Card(Ui.Text("CollectionsPage").Class("page-card-title"), Ui.Text($"Selected item: #{_selectedItem}").Id("compose-selected").Class("page-copy"), Ui.Row(UiShowcaseScaffolding.BuildSelectableCard("Item 1", _selectedItem == 1, "compose-item-1", delegate(UiActionContext ctx)
		{
			SelectItem(ctx, 1);
		}), UiShowcaseScaffolding.BuildSelectableCard("Item 2", _selectedItem == 2, "compose-item-2", delegate(UiActionContext ctx)
		{
			SelectItem(ctx, 2);
		}), UiShowcaseScaffolding.BuildSelectableCard("Item 3", _selectedItem == 3, "compose-item-3", delegate(UiActionContext ctx)
		{
			SelectItem(ctx, 3);
		})).Class("control-row"), Ui.Table(Ui.TableRow(Ui.TableHeaderCell("Prototype Identifier"), Ui.TableHeaderCell("Role")), Ui.TableRow(Ui.TableCell("Sentinel Vanguard Frame").RowSpan(2), Ui.TableCell("Guardian")), Ui.TableRow(Ui.TableCell("Escort")), Ui.TableRow(Ui.TableCell("Status: Active").ColSpan(2).Class("muted"))).Id("compose-stats-table"), Ui.Text("ListView / GridView / Tabs share one unified state model.").Class("muted")).Class("skin-card").FlexGrow(1f);
	}

	private UiElementBuilder BuildOverlaysCard()
	{
		return Ui.Card(Ui.Text("OverlaysPage").Class("page-card-title"), Ui.Row(Ui.Button(_modalOpen ? "Close Modal" : "Open Modal", ToggleModal).Id("compose-modal-toggle").Class("skin-primary"), Ui.Button(_toastVisible ? "Hide Toast" : "Show Toast", ToggleToast).Id("compose-toast-toggle")).Class("control-row"), _modalOpen ? Ui.Card(Ui.Text("Modal opened - deterministic action path.")).Id("compose-modal").Class("overlay-card") : Ui.Text("Drawer / Tooltip / Toast share the same overlay semantics.").Class("muted"), _toastVisible ? Ui.Text("Toast: compose action committed.").Id("compose-toast").Class("toast-badge") : Ui.Text("Toast hidden.").Class("muted")).Class("skin-card").FlexGrow(1f);
	}

	private UiElementBuilder BuildStylesCard()
	{
		return Ui.Card(Ui.Text("AppearancePage").Class("page-card-title"), Ui.Text("Backdrop blur / filter blur / flex wrap / structural pseudo / scroll / clip / density tokens.").Class("page-copy"), new UiElementBuilder(UiNodeKind.Container, "div").Id("compose-appearance-lab").Class("appearance-lab").Children(new UiElementBuilder(UiNodeKind.Container, "div").Id("compose-appearance-host").Class("appearance-host").Children(Ui.Row(new UiElementBuilder(UiNodeKind.Container, "div").Class("appearance-pane-left"), new UiElementBuilder(UiNodeKind.Container, "div").Class("appearance-pane-right")).Class("appearance-background"), Ui.Card(Ui.Text("Frosted Glass").Class("page-card-title"), Ui.Text("Simplified backdrop blur on Skia scene.").Class("muted"), Ui.Text("Blur badge").Id("compose-blur-chip").Classes("control-chip", "appearance-blur-chip")).Id("compose-frosted-card").Class("frosted-glass")
			.Absolute(18f, 18f)), new UiElementBuilder(UiNodeKind.Container, "div").Id("compose-wrap-demo").Class("wrap-demo").Children(UiShowcaseScaffolding.BuildChip("First", active: true), UiShowcaseScaffolding.BuildChip("Second", active: false), UiShowcaseScaffolding.BuildChip("Third", active: true), UiShowcaseScaffolding.BuildChip("Fourth", active: false), UiShowcaseScaffolding.BuildChip("Fifth", active: true), UiShowcaseScaffolding.BuildChip("Sixth", active: false)), UiShowcaseScaffolding.BuildAdvancedAppearanceRow("compose")), UiShowcaseScaffolding.BuildPhaseOneRow("compose"), UiShowcaseScaffolding.BuildPhaseTwoPanel("compose"), UiShowcaseScaffolding.BuildPhaseThreePanel("compose"), UiShowcaseScaffolding.BuildPhaseFourPanel("compose"), UiShowcaseScaffolding.BuildPhaseFivePanel("compose"), UiShowcaseScaffolding.BuildScrollClipRow("compose"), Ui.Row(Ui.Button("Compact", delegate(UiActionContext ctx)
		{
			ChangeDensity(ctx, "density-compact");
		}), Ui.Button("Cozy", delegate(UiActionContext ctx)
		{
			ChangeDensity(ctx, "density-cozy");
		}).Class("skin-primary"), Ui.Button("Comfortable", delegate(UiActionContext ctx)
		{
			ChangeDensity(ctx, "density-comfortable");
		})).Class("control-row"), Ui.Text("Current density: " + UiShowcaseScaffolding.DensityLabel(_densityClass)).Id("compose-density").Class("muted"), Ui.Row(UiShowcaseScaffolding.BuildChip("Disabled", active: false).Class("state-disabled"), UiShowcaseScaffolding.BuildChip("Loading", active: true), UiShowcaseScaffolding.BuildChip("Error", active: false).Class("error-text")).Class("control-row")).Class("skin-card").FlexGrow(1f);
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

	private void SelectMode(UiActionContext context, int selectedMode)
	{
		_selectedMode = selectedMode;
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
