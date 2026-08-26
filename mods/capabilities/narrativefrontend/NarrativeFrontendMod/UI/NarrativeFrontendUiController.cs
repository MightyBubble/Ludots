using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Panels;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using NarrativeFrontendMod.Runtime;

namespace NarrativeFrontendMod.UI;

internal sealed class NarrativeFrontendUiController
{
    private ReactivePage<NarrativeFrontendRenderState>? _page;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;
    private string? _mountedThemeId;

    public void MountOrRefresh(UIRoot root, GameEngine engine, NarrativeFrontendRenderState state)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            return;
        }
        _surfaceHost = surfaceHost;

        PanelTheme? theme = PanelThemeCatalog.TryLoad(engine);
        string? themeId = theme?.Id;
        bool themeChanged = !string.Equals(_mountedThemeId, themeId, StringComparison.Ordinal);

        if (_page == null || themeChanged)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            UiStyleSheet[] sheets = theme == null
                ? Array.Empty<UiStyleSheet>()
                : new[] { theme.StyleSheet };
            _page = new ReactivePage<NarrativeFrontendRenderState>(
                textMeasurer,
                imageSizeProvider,
                state,
                NarrativeFrontendUiComposer.BuildRoot,
                theme: null,
                sheets);
            _mountedThemeId = themeId;
        }
        else
        {
            _page.SetState(_ => state);
        }

        surfaceHost.Publish(
            surfaceHost.EnsureLease(
                ref _lease,
                new UiSurfaceLeaseRequest("NarrativeFrontend.Ui", UiSurfaceSegment.Overlay, priority: 60)),
            UiSurfaceContribution.FromReactivePage(_page));
    }

    public void ClearIfOwned(UIRoot root)
    {
        if (_lease.IsValid && _surfaceHost != null)
        {
            _surfaceHost.Release(_lease);
            _lease = default;
            _surfaceHost = null;
        }

        _page = null;
        _mountedThemeId = null;
    }
}
