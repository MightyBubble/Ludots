using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace UiShowcaseCoreMod.Showcase;

internal static class ReactiveShowcasePageFactory
{
	internal static ReactivePage<ReactiveShowcaseState> CreatePage(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
	{
		return new ReactivePage<ReactiveShowcaseState>(textMeasurer, imageSizeProvider, new ReactiveShowcaseState(3, "theme-hud", "density-cozy", CheckboxChecked: true, SwitchEnabled: true, FormError: true, "Waiting validation", 2, 1, ModalOpen: false, ToastVisible: false), BuildRoot, null, UiShowcaseScaffolding.AuthoringStyleSheet);
	}

	private static UiElementBuilder BuildRoot(ReactiveContext<ReactiveShowcaseState> context)
	{
		ReactiveShowcaseState state = context.State;
		return Ui.Column(Ui.Text("Reactive Fluent - State Drives UI").Class("skin-header"), UiShowcaseScaffolding.BuildThemeToolbar("reactive", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				ThemeClass = "theme-light"
			});
		}, delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				ThemeClass = "theme-dark"
			});
		}, delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				ThemeClass = "theme-hud"
			});
		}), Ui.Row(BuildOverviewCard(context), BuildControlsCard(context), BuildFormsCard(context)).Class("page-grid-row").Gap(12f)
			.FlexGrow(1f), Ui.Row(BuildCollectionsCard(context), BuildOverlaysCard(context), BuildStylesCard(context)).Class("page-grid-row").Gap(12f)
			.FlexGrow(1f)).Classes("skin-root", state.ThemeClass, state.DensityClass).Width(1280f)
			.Height(720f)
			.Gap(12f);
	}

	private static UiElementBuilder BuildOverviewCard(ReactiveContext<ReactiveShowcaseState> context)
	{
		ReactiveShowcaseState state = context.State;
		return Ui.Card(Ui.Text("OverviewPage").Class("page-card-title"), Ui.Text("React-like state rendering in pure C#.").Class("page-copy"), Ui.Text($"Counter: {state.Counter}").Id("reactive-count").FontSize(36f)
			.Bold(), Ui.Row(Ui.Button("-1", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				Counter = current.Counter - 1
			});
		}).Id("reactive-dec"), Ui.Button("+1", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				Counter = current.Counter + 1
			});
		}).Id("reactive-inc").Class("skin-primary"), Ui.Button("Reset", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				Counter = 0
			});
		})).Class("control-row"), Ui.Text("Theme: " + UiShowcaseScaffolding.ThemeLabel(state.ThemeClass) + " / Density: " + UiShowcaseScaffolding.DensityLabel(state.DensityClass)).Class("muted")).Class("skin-card").FlexGrow(1f);
	}

	private static UiElementBuilder BuildControlsCard(ReactiveContext<ReactiveShowcaseState> context)
	{
		ReactiveShowcaseState state = context.State;
		return Ui.Card(Ui.Text("ControlsPage").Class("page-card-title"), Ui.Row(UiShowcaseScaffolding.BuildChip(state.CheckboxChecked ? "Checkbox: Checked" : "Checkbox: Off", state.CheckboxChecked, "reactive-checkbox", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				CheckboxChecked = !current.CheckboxChecked
			});
		}), UiShowcaseScaffolding.BuildChip(state.SwitchEnabled ? "Switch: On" : "Switch: Off", state.SwitchEnabled, "reactive-switch", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				SwitchEnabled = !current.SwitchEnabled
			});
		}), Ui.Radio("Radio: Primary", "reactive-mode", state.SelectedMode == 1, delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				SelectedMode = 1
			});
		}).Id("reactive-radio-primary").Class("control-chip"), Ui.Radio("Radio: Secondary", "reactive-mode", state.SelectedMode == 2, delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				SelectedMode = 2
			});
		}).Id("reactive-radio-secondary").Class("control-chip")).Class("control-row"), Ui.Row(new UiElementBuilder(UiNodeKind.Select, "select").Text("Select / Dropdown").Class("control-chip").FlexGrow(1f), new UiElementBuilder(UiNodeKind.Slider, "slider").Text("Slider 72%").Class("control-chip").FlexGrow(1f)).Class("control-row"), Ui.Text("ProgressBar").Class("page-copy"), UiShowcaseScaffolding.BuildProgressBar(72f)).Class("skin-card").FlexGrow(1f);
	}

	private static UiElementBuilder BuildFormsCard(ReactiveContext<ReactiveShowcaseState> context)
	{
		ReactiveShowcaseState state = context.State;
		return Ui.Card(Ui.Text("FormsPage").Class("page-card-title"), Ui.Input().Id("reactive-email-input").Class("control-chip")
			.Type("email")
			.Placeholder("Email / required / @ludots.dev")
			.Required()
			.Pattern("^[^@\\s]+@ludots\\.dev$")
			.Value(state.FormError ? string.Empty : "state@ludots.dev"), Ui.Input().Id("reactive-password-input").Class("control-chip")
			.Type("password")
			.Placeholder("Password / required / min 8")
			.Required()
			.MinLength(8)
			.Value(state.FormError ? string.Empty : "hunter22"), new UiElementBuilder(UiNodeKind.TextArea, "textarea").Id("reactive-notes-input").Class("control-chip").Placeholder("Validation summary / notes / max 64")
			.Required()
			.MaxLength(64)
			.Value(state.FormError ? string.Empty : "Textarea / validation summary"), Ui.Text(state.FormStatus).Id("reactive-form-status").Class(state.FormError ? "error-text" : "ok-text"), Ui.Row(Ui.Button("Invalid", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				FormError = true,
				FormStatus = "Validation failed: email is invalid"
			});
		}), Ui.Button("Valid", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				FormError = false,
				FormStatus = "Form submitted successfully"
			});
		}).Class("skin-primary"), Ui.Button("Reset", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				FormError = true,
				FormStatus = "Waiting validation"
			});
		})).Class("control-row")).Class("skin-card").FlexGrow(1f);
	}

	private static UiElementBuilder BuildCollectionsCard(ReactiveContext<ReactiveShowcaseState> context)
	{
		ReactiveShowcaseState state = context.State;
		return Ui.Card(Ui.Text("CollectionsPage").Class("page-card-title"), Ui.Text($"Selected item: #{state.SelectedItem}").Id("reactive-selected").Class("page-copy"), Ui.Row(UiShowcaseScaffolding.BuildSelectableCard("Item 1", state.SelectedItem == 1, "reactive-item-1", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				SelectedItem = 1
			});
		}), UiShowcaseScaffolding.BuildSelectableCard("Item 2", state.SelectedItem == 2, "reactive-item-2", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				SelectedItem = 2
			});
		}), UiShowcaseScaffolding.BuildSelectableCard("Item 3", state.SelectedItem == 3, "reactive-item-3", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				SelectedItem = 3
			});
		})).Class("control-row"), Ui.Table(Ui.TableRow(Ui.TableHeaderCell("Prototype Identifier"), Ui.TableHeaderCell("Role")), Ui.TableRow(Ui.TableCell("Sentinel Vanguard Frame").RowSpan(2), Ui.TableCell("Guardian")), Ui.TableRow(Ui.TableCell("Escort")), Ui.TableRow(Ui.TableCell("Status: Active").ColSpan(2).Class("muted"))).Id("reactive-stats-table"), Ui.Text("ListView / GridView / Tabs share one unified state model.").Class("muted")).Class("skin-card").FlexGrow(1f);
	}

	private static UiElementBuilder BuildOverlaysCard(ReactiveContext<ReactiveShowcaseState> context)
	{
		ReactiveShowcaseState state = context.State;
		return Ui.Card(Ui.Text("OverlaysPage").Class("page-card-title"), Ui.Row(Ui.Button(state.ModalOpen ? "Close Modal" : "Open Modal", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				ModalOpen = !current.ModalOpen
			});
		}).Id("reactive-modal-toggle").Class("skin-primary"), Ui.Button(state.ToastVisible ? "Hide Toast" : "Show Toast", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				ToastVisible = !current.ToastVisible
			});
		}).Id("reactive-toast-toggle")).Class("control-row"), state.ModalOpen ? Ui.Card(Ui.Text("Modal opened - focus and action path stay deterministic.")).Id("reactive-modal").Class("overlay-card") : Ui.Text("Tooltip / Dropdown / Drawer live in the same overlay model.").Class("muted"), state.ToastVisible ? Ui.Text("Toast: reactive state committed.").Id("reactive-toast").Class("toast-badge") : Ui.Text("Toast hidden.").Class("muted")).Class("skin-card").FlexGrow(1f);
	}

	private static UiElementBuilder BuildStylesCard(ReactiveContext<ReactiveShowcaseState> context)
	{
		ReactiveShowcaseState state = context.State;
		return Ui.Card(Ui.Text("AppearancePage").Class("page-card-title"), Ui.Text("Backdrop blur / filter blur / flex wrap / structural pseudo / scroll / clip / density tokens.").Class("page-copy"), new UiElementBuilder(UiNodeKind.Container, "div").Id("reactive-appearance-lab").Class("appearance-lab").Children(new UiElementBuilder(UiNodeKind.Container, "div").Id("reactive-appearance-host").Class("appearance-host").Children(Ui.Row(new UiElementBuilder(UiNodeKind.Container, "div").Class("appearance-pane-left"), new UiElementBuilder(UiNodeKind.Container, "div").Class("appearance-pane-right")).Class("appearance-background"), Ui.Card(Ui.Text("Frosted Glass").Class("page-card-title"), Ui.Text("Simplified backdrop blur on Skia scene.").Class("muted"), Ui.Text("Blur badge").Id("reactive-blur-chip").Classes("control-chip", "appearance-blur-chip")).Id("reactive-frosted-card").Class("frosted-glass")
			.Absolute(18f, 18f)), new UiElementBuilder(UiNodeKind.Container, "div").Id("reactive-wrap-demo").Class("wrap-demo").Children(UiShowcaseScaffolding.BuildChip("First", active: true), UiShowcaseScaffolding.BuildChip("Second", active: false), UiShowcaseScaffolding.BuildChip("Third", active: true), UiShowcaseScaffolding.BuildChip("Fourth", active: false), UiShowcaseScaffolding.BuildChip("Fifth", active: true), UiShowcaseScaffolding.BuildChip("Sixth", active: false)), UiShowcaseScaffolding.BuildAdvancedAppearanceRow("reactive")), UiShowcaseScaffolding.BuildPhaseOneRow("reactive"), UiShowcaseScaffolding.BuildPhaseTwoPanel("reactive"), UiShowcaseScaffolding.BuildPhaseThreePanel("reactive"), UiShowcaseScaffolding.BuildPhaseFourPanel("reactive"), UiShowcaseScaffolding.BuildPhaseFivePanel("reactive"), UiShowcaseScaffolding.BuildScrollClipRow("reactive"), Ui.Row(Ui.Button("Compact", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				DensityClass = "density-compact"
			});
		}), Ui.Button("Cozy", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				DensityClass = "density-cozy"
			});
		}).Class("skin-primary"), Ui.Button("Comfortable", delegate
		{
			context.SetState((ReactiveShowcaseState current) => current with
			{
				DensityClass = "density-comfortable"
			});
		})).Class("control-row"), Ui.Text("Current density: " + UiShowcaseScaffolding.DensityLabel(state.DensityClass)).Id("reactive-density").Class("muted"), Ui.Row(UiShowcaseScaffolding.BuildChip("Disabled", active: false).Class("state-disabled"), UiShowcaseScaffolding.BuildChip("Loading", active: true), UiShowcaseScaffolding.BuildChip("Error", active: false).Class("error-text")).Class("control-row")).Class("skin-card").FlexGrow(1f);
	}
}
