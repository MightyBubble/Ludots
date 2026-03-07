using Ludots.UI.Runtime;

namespace UiShowcaseCoreMod.Showcase;

public static class UiShowcaseStyles
{
    public static UiStyleSheet BuildAuthoringStyleSheet()
    {
        return new UiStyleSheet()
            .AddRule(".theme-light", style =>
            {
                style.Set("--page-bg", "#f3f6fb");
                style.Set("--surface", "#ffffff");
                style.Set("--surface-alt", "#e8edf5");
                style.Set("--accent", "#2563eb");
                style.Set("--accent-contrast", "#ffffff");
                style.Set("--text", "#1f2937");
                style.Set("--muted", "#6b7280");
                style.Set("--danger", "#dc2626");
                style.Set("--success", "#16a34a");
                style.Set("--border", "#c8d2e3");
            })
            .AddRule(".theme-dark", style =>
            {
                style.Set("--page-bg", "#1d2433");
                style.Set("--surface", "#283349");
                style.Set("--surface-alt", "#3a4661");
                style.Set("--accent", "#3c82ff");
                style.Set("--accent-contrast", "#ffffff");
                style.Set("--text", "#f8fbff");
                style.Set("--muted", "#a7b1c4");
                style.Set("--danger", "#ff6b6b");
                style.Set("--success", "#4ade80");
                style.Set("--border", "#4c5976");
            })
            .AddRule(".theme-hud", style =>
            {
                style.Set("--page-bg", "#08131f");
                style.Set("--surface", "rgba(8, 34, 52, 0.92)");
                style.Set("--surface-alt", "#0f3047");
                style.Set("--accent", "#00b4ff");
                style.Set("--accent-contrast", "#03131d");
                style.Set("--text", "#d9fbff");
                style.Set("--muted", "#7dc8d6");
                style.Set("--danger", "#ff7a90");
                style.Set("--success", "#67f1b5");
                style.Set("--border", "#38f0ff");
            })
            .AddRule(".skin-root", style =>
            {
                style.Set("display", "flex");
                style.Set("flex-direction", "column");
                style.Set("padding", "18px");
                style.Set("gap", "12px");
                style.Set("background-color", "var(--page-bg, #1d2433)");
                style.Set("color", "var(--text, #ffffff)");
            })
            .AddRule(".skin-header", style =>
            {
                style.Set("font-size", "30px");
                style.Set("font-weight", "700");
                style.Set("color", "var(--accent, #3c82ff)");
            })
            .AddRule(".page-grid-row", style =>
            {
                style.Set("display", "flex");
                style.Set("flex-direction", "row");
                style.Set("gap", "12px");
            })
            .AddRule(".skin-card", style =>
            {
                style.Set("background-color", "var(--surface, #283349)");
                style.Set("color", "var(--text, #ffffff)");
                style.Set("border-color", "var(--border, #4c5976)");
                style.Set("border-width", "1px");
                style.Set("border-radius", "12px");
                style.Set("padding", "14px");
                style.Set("gap", "8px");
            })
            .AddRule(".page-card-title", style =>
            {
                style.Set("font-size", "18px");
                style.Set("font-weight", "700");
                style.Set("color", "var(--accent, #3c82ff)");
            })
            .AddRule(".page-copy", style =>
            {
                style.Set("font-size", "14px");
            })
            .AddRule(".muted", style =>
            {
                style.Set("color", "var(--muted, #a7b1c4)");
                style.Set("font-size", "13px");
            })
            .AddRule(".control-row", style =>
            {
                style.Set("display", "flex");
                style.Set("flex-direction", "row");
                style.Set("gap", "8px");
            })
            .AddRule(".control-chip", style =>
            {
                style.Set("background-color", "var(--surface-alt, #3a4661)");
                style.Set("border-radius", "8px");
                style.Set("padding", "8px 10px");
                style.Set("font-size", "13px");
            })
            .AddRule(".control-chip.active", style =>
            {
                style.Set("background-color", "var(--accent, #3c82ff)");
                style.Set("color", "var(--accent-contrast, #ffffff)");
            })
            .AddRule(".state-disabled", style =>
            {
                style.Set("opacity", "0.55");
            })
            .AddRule(".ok-text", style =>
            {
                style.Set("color", "var(--success, #4ade80)");
                style.Set("font-size", "13px");
            })
            .AddRule(".error-text", style =>
            {
                style.Set("color", "var(--danger, #ff6b6b)");
                style.Set("font-size", "13px");
            })
            .AddRule(".progress-track", style =>
            {
                style.Set("background-color", "var(--surface-alt, #3a4661)");
                style.Set("padding", "4px");
                style.Set("border-radius", "999px");
            })
            .AddRule(".progress-fill", style =>
            {
                style.Set("background-color", "var(--accent, #3c82ff)");
                style.Set("height", "10px");
                style.Set("border-radius", "999px");
            })
            .AddRule(".selected-item", style =>
            {
                style.Set("border-color", "var(--accent, #3c82ff)");
                style.Set("border-width", "2px");
            })
            .AddRule(".overlay-card", style =>
            {
                style.Set("background-color", "var(--surface-alt, #3a4661)");
                style.Set("border-radius", "12px");
                style.Set("padding", "12px");
                style.Set("gap", "8px");
            })
            .AddRule(".toast-badge", style =>
            {
                style.Set("background-color", "var(--accent, #3c82ff)");
                style.Set("color", "var(--accent-contrast, #ffffff)");
                style.Set("padding", "6px 10px");
                style.Set("border-radius", "999px");
                style.Set("font-size", "12px");
            })
            .AddRule(".prototype-box", style =>
            {
                style.Set("background-color", "var(--surface-alt, #3a4661)");
                style.Set("padding", "10px");
                style.Set("border-radius", "8px");
                style.Set("font-size", "13px");
            })
            .AddRule(".density-compact .skin-card", style =>
            {
                style.Set("padding", "10px");
            })
            .AddRule(".density-comfortable .skin-card", style =>
            {
                style.Set("padding", "18px");
            })
            .AddRule(".skin-primary", style =>
            {
                style.Set("background-color", "var(--accent, #3c82ff)");
                style.Set("color", "var(--accent-contrast, #ffffff)");
            });
    }

    public static string BuildAuthoringCss()
    {
        return """
.theme-light {
  --page-bg:#f3f6fb;
  --surface:#ffffff;
  --surface-alt:#e8edf5;
  --accent:#2563eb;
  --accent-contrast:#ffffff;
  --text:#1f2937;
  --muted:#6b7280;
  --danger:#dc2626;
  --success:#16a34a;
  --border:#c8d2e3;
}
.theme-dark {
  --page-bg:#1d2433;
  --surface:#283349;
  --surface-alt:#3a4661;
  --accent:#3c82ff;
  --accent-contrast:#ffffff;
  --text:#f8fbff;
  --muted:#a7b1c4;
  --danger:#ff6b6b;
  --success:#4ade80;
  --border:#4c5976;
}
.theme-hud {
  --page-bg:#08131f;
  --surface:rgba(8, 34, 52, 0.92);
  --surface-alt:#0f3047;
  --accent:#00b4ff;
  --accent-contrast:#03131d;
  --text:#d9fbff;
  --muted:#7dc8d6;
  --danger:#ff7a90;
  --success:#67f1b5;
  --border:#38f0ff;
}
.skin-root { display:flex; flex-direction:column; width:1280px; height:720px; padding:18px; gap:12px; background-color:var(--page-bg, #1d2433); color:var(--text, #ffffff); }
.skin-header { font-size:30px; font-weight:700; color:var(--accent, #3c82ff); }
.page-grid-row { display:flex; flex-direction:row; gap:12px; }
.skin-card { background-color:var(--surface, #283349); color:var(--text, #ffffff); border-color:var(--border, #4c5976); border-width:1px; border-radius:12px; padding:14px; gap:8px; }
.page-card-title { font-size:18px; font-weight:700; color:var(--accent, #3c82ff); }
.page-copy { font-size:14px; }
.muted { color:var(--muted, #a7b1c4); font-size:13px; }
.control-row { display:flex; flex-direction:row; gap:8px; }
.control-chip { background-color:var(--surface-alt, #3a4661); border-radius:8px; padding:8px 10px; font-size:13px; }
.control-chip.active { background-color:var(--accent, #3c82ff); color:var(--accent-contrast, #ffffff); }
.state-disabled { opacity:0.55; }
.ok-text { color:var(--success, #4ade80); font-size:13px; }
.error-text { color:var(--danger, #ff6b6b); font-size:13px; }
.progress-track { background-color:var(--surface-alt, #3a4661); padding:4px; border-radius:999px; }
.progress-fill { background-color:var(--accent, #3c82ff); height:10px; border-radius:999px; }
.selected-item { border-color:var(--accent, #3c82ff); border-width:2px; }
.overlay-card { background-color:var(--surface-alt, #3a4661); border-radius:12px; padding:12px; gap:8px; }
.toast-badge { background-color:var(--accent, #3c82ff); color:var(--accent-contrast, #ffffff); padding:6px 10px; border-radius:999px; font-size:12px; }
.prototype-box { background-color:var(--surface-alt, #3a4661); padding:10px; border-radius:8px; font-size:13px; }
.density-compact .skin-card { padding:10px; }
.density-comfortable .skin-card { padding:18px; }
.skin-primary { background-color:var(--accent, #3c82ff); color:var(--accent-contrast, #ffffff); }
button { padding:12px 18px; border-radius:10px; }
""";
    }
}
