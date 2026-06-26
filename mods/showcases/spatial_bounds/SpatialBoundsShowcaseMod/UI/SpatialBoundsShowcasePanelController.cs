using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using SpatialBoundsShowcaseMod.Runtime;

namespace SpatialBoundsShowcaseMod.UI
{
    internal sealed class SpatialBoundsShowcasePanelController
    {
        private readonly SpatialBoundsShowcaseRuntime _runtime;
        private ReactivePage<SpatialBoundsShowcasePanelState>? _page;
        private GameEngine? _engine;
        private UiSurfaceLeaseHandle _lease;

        public SpatialBoundsShowcasePanelController(SpatialBoundsShowcaseRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool MountOrSync(UIRoot root, GameEngine engine, in SpatialBoundsShowcasePanelState state)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(engine);
            if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                return false;
            }

            _engine = engine;
            ReactivePage<SpatialBoundsShowcasePanelState>? page = EnsurePage();
            if (page == null)
            {
                return false;
            }

            SpatialBoundsShowcasePanelState snapshot = state;
            page.SetState(_ => snapshot);
            surfaceHost.PublishReactivePage(
                ref _lease,
                new UiSurfaceLeaseRequest("Showcase.SpatialBounds.Panel", UiSurfaceSegment.Overlay, priority: 40),
                page);
            return true;
        }

        public void ClearIfOwned(UIRoot root)
        {
            ArgumentNullException.ThrowIfNull(root);

            if (_lease.IsValid &&
                _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
            {
                surfaceHost.ReleaseLease(ref _lease);
            }

            _engine = null;
        }

        private ReactivePage<SpatialBoundsShowcasePanelState>? EnsurePage()
        {
            if (_page != null)
            {
                return _page;
            }

            GameEngine engine = RequireEngine();
            if (engine.GetService(CoreServiceKeys.UiTextMeasurer) is not IUiTextMeasurer textMeasurer ||
                engine.GetService(CoreServiceKeys.UiImageSizeProvider) is not IUiImageSizeProvider imageSizeProvider)
            {
                return null;
            }

            _page = new ReactivePage<SpatialBoundsShowcasePanelState>(
                textMeasurer,
                imageSizeProvider,
                SpatialBoundsShowcasePanelState.Empty,
                BuildRoot);
            return _page;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<SpatialBoundsShowcasePanelState> context)
        {
            SpatialBoundsShowcasePanelState state = context.State;
            return Ui.Card(
                    Ui.Text(state.Title).FontSize(16f).Bold().Color("#F7FAFF"),
                    Ui.Text(state.Camera).FontSize(12f).Color("#8FD0FF"),
                    CommandButton("Reset Camera", ResetCamera, "#244E66"),
                    Ui.Text(state.Hint).FontSize(11f).Color("#B9C8D8").WhiteSpace(UiWhiteSpace.Normal))
                .Width(220f)
                .Padding(14f)
                .Gap(8f)
                .Radius(14f)
                .Background("#D40B0E13")
                .Border(1f, Color("#33879AB3"))
                .BackdropBlur(8f)
                .Absolute(16f, 372f)
                .ZIndex(30);
        }

        private void ResetCamera()
        {
            GameEngine? engine = _engine;
            if (engine == null)
            {
                return;
            }

            if (_runtime.TryResetCamera(engine) &&
                _lease.IsValid &&
                engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
            {
                surfaceHost.InvalidateLease(_lease);
            }
        }

        private GameEngine RequireEngine()
        {
            return _engine ?? throw new InvalidOperationException("SpatialBoundsShowcase panel requires an active engine.");
        }

        private static UiElementBuilder CommandButton(string label, Action onClick, string background)
        {
            return Ui.Button(label, _ => onClick())
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(background)
                .Color("#F7FAFF")
                .FontSize(11f);
        }

        private static UiColor Color(string hex)
        {
            if (!UiColor.TryParse(hex, out UiColor color))
            {
                throw new InvalidOperationException($"Unsupported color literal '{hex}'.");
            }

            return color;
        }
    }
}
