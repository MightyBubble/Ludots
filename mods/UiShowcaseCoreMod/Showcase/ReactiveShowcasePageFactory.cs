using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace UiShowcaseCoreMod.Showcase;

internal static class ReactiveShowcasePageFactory
{
    internal static ReactivePage<ReactiveShowcaseState> CreatePage()
    {
        return new ReactivePage<ReactiveShowcaseState>(
            new ReactiveShowcaseState(
                3,
                "theme-hud",
                "density-cozy",
                true,
                true,
                true,
                "Waiting validation",
                2,
                1,
                false,
                false),
            BuildRoot,
            null,
            UiShowcaseScaffolding.AuthoringStyleSheet);
    }

    private static UiElementBuilder BuildRoot(ReactiveContext<ReactiveShowcaseState> context)
    {
        ReactiveShowcaseState state = context.State;
        return Ui.Column(
                Ui.Text("Reactive Fluent ¡ª State Drives UI").Class("skin-header"),
                UiShowcaseScaffolding.BuildThemeToolbar(
                    "reactive",
                    _ => context.SetState(current => current with { ThemeClass = "theme-light" }),
                    _ => context.SetState(current => current with { ThemeClass = "theme-dark" }),
                    _ => context.SetState(current => current with { ThemeClass = "theme-hud" })),
                Ui.Row(
                    BuildOverviewCard(context),
                    BuildControlsCard(context),
                    BuildFormsCard(context))
                    .Class("page-grid-row")
                    .Gap(12)
                    .FlexGrow(1),
                Ui.Row(
                    BuildCollectionsCard(context),
                    BuildOverlaysCard(context),
                    BuildStylesCard(context))
                    .Class("page-grid-row")
                    .Gap(12)
                    .FlexGrow(1))
            .Classes("skin-root", state.ThemeClass, state.DensityClass)
            .Width(1280)
            .Height(720)
            .Gap(12);
    }

    private static UiElementBuilder BuildOverviewCard(ReactiveContext<ReactiveShowcaseState> context)
    {
        ReactiveShowcaseState state = context.State;
        return Ui.Card(
                Ui.Text("OverviewPage").Class("page-card-title"),
                Ui.Text("React-like state rendering in pure C#.").Class("page-copy"),
                Ui.Text($"Counter: {state.Counter}").Id("reactive-count").FontSize(36).Bold(),
                Ui.Row(
                    Ui.Button("-1", _ => context.SetState(current => current with { Counter = current.Counter - 1 })).Id("reactive-dec"),
                    Ui.Button("+1", _ => context.SetState(current => current with { Counter = current.Counter + 1 })).Id("reactive-inc").Class("skin-primary"),
                    Ui.Button("Reset", _ => context.SetState(current => current with { Counter = 0 })))
                    .Class("control-row"),
                Ui.Text($"Theme: {UiShowcaseScaffolding.ThemeLabel(state.ThemeClass)} / Density: {UiShowcaseScaffolding.DensityLabel(state.DensityClass)}")
                    .Class("muted"))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private static UiElementBuilder BuildControlsCard(ReactiveContext<ReactiveShowcaseState> context)
    {
        ReactiveShowcaseState state = context.State;
        return Ui.Card(
                Ui.Text("ControlsPage").Class("page-card-title"),
                Ui.Row(
                    UiShowcaseScaffolding.BuildChip(state.CheckboxChecked ? "Checkbox: Checked" : "Checkbox: Off", state.CheckboxChecked, "reactive-checkbox", _ => context.SetState(current => current with { CheckboxChecked = !current.CheckboxChecked })),
                    UiShowcaseScaffolding.BuildChip(state.SwitchEnabled ? "Switch: On" : "Switch: Off", state.SwitchEnabled, "reactive-switch", _ => context.SetState(current => current with { SwitchEnabled = !current.SwitchEnabled })),
                    Ui.Radio("Radio: Primary", "reactive-mode", state.SelectedMode == 1, _ => context.SetState(current => current with { SelectedMode = 1 })).Id("reactive-radio-primary").Class("control-chip"),
                    Ui.Radio("Radio: Secondary", "reactive-mode", state.SelectedMode == 2, _ => context.SetState(current => current with { SelectedMode = 2 })).Id("reactive-radio-secondary").Class("control-chip"))
                    .Class("control-row"),
                Ui.Row(
                    new UiElementBuilder(UiNodeKind.Select, "select").Text("Select / Dropdown").Class("control-chip").FlexGrow(1),
                    new UiElementBuilder(UiNodeKind.Slider, "slider").Text("Slider 72%").Class("control-chip").FlexGrow(1))
                    .Class("control-row"),
                Ui.Text("ProgressBar").Class("page-copy"),
                UiShowcaseScaffolding.BuildProgressBar(72f))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private static UiElementBuilder BuildFormsCard(ReactiveContext<ReactiveShowcaseState> context)
    {
        ReactiveShowcaseState state = context.State;
        return Ui.Card(
                Ui.Text("FormsPage").Class("page-card-title"),
                new UiElementBuilder(UiNodeKind.Input, "input").Text("Email: state@ludots.dev").Class("control-chip"),
                new UiElementBuilder(UiNodeKind.Custom, "input").Text("Password: ???????").Class("control-chip"),
                new UiElementBuilder(UiNodeKind.TextArea, "textarea").Text("Textarea / validation summary").Class("control-chip"),
                Ui.Text(state.FormStatus).Id("reactive-form-status").Class(state.FormError ? "error-text" : "ok-text"),
                Ui.Row(
                    Ui.Button("Invalid", _ => context.SetState(current => current with { FormError = true, FormStatus = "Validation failed: email is invalid" })),
                    Ui.Button("Valid", _ => context.SetState(current => current with { FormError = false, FormStatus = "Form submitted successfully" })).Class("skin-primary"),
                    Ui.Button("Reset", _ => context.SetState(current => current with { FormError = true, FormStatus = "Waiting validation" })))
                    .Class("control-row"))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private static UiElementBuilder BuildCollectionsCard(ReactiveContext<ReactiveShowcaseState> context)
    {
        ReactiveShowcaseState state = context.State;
        return Ui.Card(
                Ui.Text("CollectionsPage").Class("page-card-title"),
                Ui.Text($"Selected item: #{state.SelectedItem}").Id("reactive-selected").Class("page-copy"),
                Ui.Row(
                    UiShowcaseScaffolding.BuildSelectableCard("Item 1", state.SelectedItem == 1, "reactive-item-1", _ => context.SetState(current => current with { SelectedItem = 1 })),
                    UiShowcaseScaffolding.BuildSelectableCard("Item 2", state.SelectedItem == 2, "reactive-item-2", _ => context.SetState(current => current with { SelectedItem = 2 })),
                    UiShowcaseScaffolding.BuildSelectableCard("Item 3", state.SelectedItem == 3, "reactive-item-3", _ => context.SetState(current => current with { SelectedItem = 3 })))
                    .Class("control-row"),
                Ui.Table(
                    Ui.TableRow(
                        Ui.TableHeaderCell("Name"),
                        Ui.TableHeaderCell("Role")),
                    Ui.TableRow(
                        Ui.TableCell("Sentinel"),
                        Ui.TableCell("Guardian")),
                    Ui.TableRow(
                        Ui.TableCell("Courier"),
                        Ui.TableCell("Support")))
                    .Id("reactive-stats-table"),
                Ui.Text("ListView / GridView / Tabs share one unified state model.")
                    .Class("muted"))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private static UiElementBuilder BuildOverlaysCard(ReactiveContext<ReactiveShowcaseState> context)
    {
        ReactiveShowcaseState state = context.State;
        return Ui.Card(
                Ui.Text("OverlaysPage").Class("page-card-title"),
                Ui.Row(
                    Ui.Button(state.ModalOpen ? "Close Modal" : "Open Modal", _ => context.SetState(current => current with { ModalOpen = !current.ModalOpen })).Id("reactive-modal-toggle").Class("skin-primary"),
                    Ui.Button(state.ToastVisible ? "Hide Toast" : "Show Toast", _ => context.SetState(current => current with { ToastVisible = !current.ToastVisible })).Id("reactive-toast-toggle"))
                    .Class("control-row"),
                state.ModalOpen
                    ? Ui.Card(Ui.Text("Modal opened ¡ª focus and action path stay deterministic.")).Id("reactive-modal").Class("overlay-card")
                    : Ui.Text("Tooltip / Dropdown / Drawer live in the same overlay model.").Class("muted"),
                state.ToastVisible
                    ? Ui.Text("Toast: reactive state committed.").Id("reactive-toast").Class("toast-badge")
                    : Ui.Text("Toast hidden.").Class("muted"))
            .Class("skin-card")
            .FlexGrow(1);
    }

    private static UiElementBuilder BuildStylesCard(ReactiveContext<ReactiveShowcaseState> context)
    {
        ReactiveShowcaseState state = context.State;
        return Ui.Card(
                Ui.Text("StylesPage").Class("page-card-title"),
                Ui.Text("Typography / spacing / density / token parity.").Class("page-copy"),
                Ui.Row(
                    Ui.Button("Compact", _ => context.SetState(current => current with { DensityClass = "density-compact" })),
                    Ui.Button("Cozy", _ => context.SetState(current => current with { DensityClass = "density-cozy" })).Class("skin-primary"),
                    Ui.Button("Comfortable", _ => context.SetState(current => current with { DensityClass = "density-comfortable" })))
                    .Class("control-row"),
                Ui.Text($"Current density: {UiShowcaseScaffolding.DensityLabel(state.DensityClass)}").Id("reactive-density").Class("muted"),
                Ui.Row(
                    UiShowcaseScaffolding.BuildChip("Disabled", false).Class("state-disabled"),
                    UiShowcaseScaffolding.BuildChip("Loading", true),
                    UiShowcaseScaffolding.BuildChip("Error", false).Class("error-text"))
                    .Class("control-row"))
            .Class("skin-card")
            .FlexGrow(1);
    }
}

public sealed record ReactiveShowcaseState(
    int Counter,
    string ThemeClass,
    string DensityClass,
    bool CheckboxChecked,
    bool SwitchEnabled,
    bool FormError,
    string FormStatus,
    int SelectedItem,
    int SelectedMode,
    bool ModalOpen,
    bool ToastVisible);
