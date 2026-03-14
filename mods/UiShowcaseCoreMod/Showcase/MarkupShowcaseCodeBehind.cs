using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Skia;

namespace UiShowcaseCoreMod.Showcase;

internal sealed class MarkupShowcaseCodeBehind
{
	private readonly UiMarkupLoader _loader = new UiMarkupLoader();

	private int _count = 5;

	private string _themeClass = "theme-light";

	private string _densityClass = "density-cozy";

	private bool _checkboxChecked = true;

	private bool _switchEnabled = true;

	private bool _formError = true;

	private string _formStatus = "Waiting validation";

	private int _selectedItem = 2;

	private int _selectedMode = 1;

	private bool _modalOpen;

	private bool _toastVisible;

	internal UiScene BuildScene()
	{
		UiDocument document = _loader.LoadDocument(BuildHtml(), BuildCss());
		ValidatePrototype(document);
		UiScene uiScene = new UiScene(new SkiaTextMeasurer(), new SkiaImageSizeProvider());
		uiScene.MountDocument(document);
		MarkupBinder.Bind(uiScene, this);
		return uiScene;
	}

	private string BuildHtml()
	{
		string markupShowcaseHtmlTemplate = UiShowcaseAssets.GetMarkupShowcaseHtmlTemplate();
		Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["theme_class"] = _themeClass,
			["density_class"] = _densityClass,
			["count"] = _count.ToString(),
			["theme_label"] = UiShowcaseScaffolding.ThemeLabel(_themeClass),
			["density_label"] = UiShowcaseScaffolding.DensityLabel(_densityClass),
			["density_label_prefixed"] = UiShowcaseScaffolding.DensityLabel(_densityClass),
			["checkbox_class"] = (_checkboxChecked ? "active" : string.Empty),
			["checkbox_label"] = (_checkboxChecked ? "Checked" : "Off"),
			["switch_class"] = (_switchEnabled ? "active" : string.Empty),
			["switch_label"] = (_switchEnabled ? "On" : "Off"),
			["radio_primary_checked"] = ((_selectedMode == 1) ? "checked=\"true\"" : string.Empty),
			["radio_secondary_checked"] = ((_selectedMode == 2) ? "checked=\"true\"" : string.Empty),
			["email_value"] = (_formError ? string.Empty : "markup@ludots.dev"),
			["password_value"] = (_formError ? string.Empty : "hunter22"),
			["notes_value"] = (_formError ? string.Empty : "Textarea / validation summary"),
			["form_status_class"] = (_formError ? "error-text" : "ok-text"),
			["form_status_text"] = _formStatus,
			["selected_item"] = _selectedItem.ToString(),
			["item_1_class"] = ((_selectedItem == 1) ? "selected-item" : string.Empty),
			["item_2_class"] = ((_selectedItem == 2) ? "selected-item" : string.Empty),
			["item_3_class"] = ((_selectedItem == 3) ? "selected-item" : string.Empty),
			["modal_toggle_text"] = (_modalOpen ? "Close Modal" : "Open Modal"),
			["toast_toggle_text"] = (_toastVisible ? "Hide Toast" : "Show Toast"),
			["modal_fragment"] = (_modalOpen ? "<div id=\"markup-modal\" class=\"overlay-card\"><div>Modal opened - code-behind stays in C#.</div><button ui-click=\"ToggleModal\">Close Modal</button></div>" : "<div class=\"muted\">Tooltip / Drawer / ContextMenu share the same overlay contract.</div>"),
			["toast_fragment"] = (_toastVisible ? "<div id=\"markup-toast\" class=\"toast-badge\">Toast: markup action committed.</div>" : "<div class=\"muted\">Toast hidden.</div>"),
			["cover_art_data_uri"] = UiShowcaseImageAssets.CoverArtDataUri,
			["frame_art_data_uri"] = UiShowcaseImageAssets.FrameArtDataUri,
			["diagnostics_fragment"] = string.Join(string.Empty, from item in ScanPrototypeDiagnostics()
				select "<div class=\"prototype-box\">" + item + "</div>")
		};
		return UiShowcaseAssets.RenderTemplate(markupShowcaseHtmlTemplate, values);
	}

	private static string BuildCss()
	{
		return UiShowcaseStyles.BuildAuthoringCss() + Environment.NewLine + UiShowcaseAssets.GetMarkupShowcaseCss();
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
		if ("grid-template-columns:repeat(3,1fr); animation:fade-in 180ms ease; calc(100% - 24px); position:fixed;".Contains("grid-template-columns", StringComparison.OrdinalIgnoreCase))
		{
			yield return "Unsupported: CSS Grid layout";
		}
		if ("grid-template-columns:repeat(3,1fr); animation:fade-in 180ms ease; calc(100% - 24px); position:fixed;".Contains("calc(", StringComparison.OrdinalIgnoreCase))
		{
			yield return "Unsupported: calc() expressions";
		}
		if ("grid-template-columns:repeat(3,1fr); animation:fade-in 180ms ease; calc(100% - 24px); position:fixed;".Contains("position:fixed", StringComparison.OrdinalIgnoreCase))
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

	private void ThemeLight(UiActionContext context)
	{
		_themeClass = "theme-light";
		Rebuild(context);
	}

	private void ThemeDark(UiActionContext context)
	{
		_themeClass = "theme-dark";
		Rebuild(context);
	}

	private void ThemeHud(UiActionContext context)
	{
		_themeClass = "theme-hud";
		Rebuild(context);
	}

	private void DensityCompact(UiActionContext context)
	{
		_densityClass = "density-compact";
		Rebuild(context);
	}

	private void DensityCozy(UiActionContext context)
	{
		_densityClass = "density-cozy";
		Rebuild(context);
	}

	private void DensityComfortable(UiActionContext context)
	{
		_densityClass = "density-comfortable";
		Rebuild(context);
	}

	private void Increment(UiActionContext context)
	{
		_count++;
		Rebuild(context);
	}

	private void ResetCounter(UiActionContext context)
	{
		_count = 0;
		Rebuild(context);
	}

	private void ToggleCheckbox(UiActionContext context)
	{
		_checkboxChecked = !_checkboxChecked;
		Rebuild(context);
	}

	private void ToggleSwitch(UiActionContext context)
	{
		_switchEnabled = !_switchEnabled;
		Rebuild(context);
	}

	private void SubmitInvalid(UiActionContext context)
	{
		_formError = true;
		_formStatus = "Validation failed: email is invalid";
		Rebuild(context);
	}

	private void SubmitValid(UiActionContext context)
	{
		_formError = false;
		_formStatus = "Form submitted successfully";
		Rebuild(context);
	}

	private void ResetForm(UiActionContext context)
	{
		_formError = true;
		_formStatus = "Waiting validation";
		Rebuild(context);
	}

	private void SelectItemOne(UiActionContext context)
	{
		_selectedItem = 1;
		Rebuild(context);
	}

	private void SelectItemTwo(UiActionContext context)
	{
		_selectedItem = 2;
		Rebuild(context);
	}

	private void SelectItemThree(UiActionContext context)
	{
		_selectedItem = 3;
		Rebuild(context);
	}

	private void SelectModePrimary(UiActionContext context)
	{
		_selectedMode = 1;
		Rebuild(context);
	}

	private void SelectModeSecondary(UiActionContext context)
	{
		_selectedMode = 2;
		Rebuild(context);
	}

	private void ToggleModal(UiActionContext context)
	{
		_modalOpen = !_modalOpen;
		Rebuild(context);
	}

	private void ToggleToast(UiActionContext context)
	{
		_toastVisible = !_toastVisible;
		Rebuild(context);
	}

	private UiCanvasContent BuildPhaseFourCanvas()
	{
		return UiShowcaseScaffolding.CreatePhaseFourCanvasContent();
	}
}
