using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
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

    public void MountOrRefresh(UIRoot root, GameEngine engine, NarrativeFrontendRenderState state)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            return;
        }
        _surfaceHost = surfaceHost;

        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<NarrativeFrontendRenderState>(textMeasurer, imageSizeProvider, state, NarrativeFrontendUiComposer.BuildRoot);
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
    }
}
