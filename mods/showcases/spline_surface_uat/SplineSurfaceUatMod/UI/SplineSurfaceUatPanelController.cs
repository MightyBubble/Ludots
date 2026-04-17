using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using SplineSurfaceUatMod.Runtime;

namespace SplineSurfaceUatMod.UI
{
    internal sealed class SplineSurfaceUatPanelController
    {
        private ReactivePage<SplineSurfaceUatPanelState>? _page;
        private GameEngine? _engine;
        private readonly SplineSurfaceUatRuntime _runtime;

        public SplineSurfaceUatPanelController(SplineSurfaceUatRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool MountOrSync(UIRoot root, GameEngine engine, in SplineSurfaceUatPanelState state)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(engine);

            _engine = engine;
            ReactivePage<SplineSurfaceUatPanelState>? page = EnsurePage();
            if (page == null)
            {
                return false;
            }

            SplineSurfaceUatPanelState snapshot = state;
            page.SetState(_ => snapshot);
            root.IsDirty = true;
            if (ReferenceEquals(root.Scene, page.Scene))
            {
                return true;
            }

            root.MountScene(page.Scene);
            return true;
        }

        public void ClearIfOwned(UIRoot root)
        {
            ArgumentNullException.ThrowIfNull(root);

            if (_page != null && ReferenceEquals(root.Scene, _page.Scene))
            {
                root.ClearScene();
            }

            _engine = null;
        }

        private ReactivePage<SplineSurfaceUatPanelState>? EnsurePage()
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

            _page = new ReactivePage<SplineSurfaceUatPanelState>(
                textMeasurer,
                imageSizeProvider,
                SplineSurfaceUatPanelState.Empty,
                BuildRoot);
            return _page;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<SplineSurfaceUatPanelState> context)
        {
            SplineSurfaceUatPanelState state = context.State;
            return Ui.Card(
                    Ui.Text(state.Title).FontSize(17f).Bold().Color("#F7FAFF"),
                    Ui.Text(state.Status).FontSize(12f).Color("#E7EEF5").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(state.Camera).FontSize(12f).Color("#8FD0FF"),
                    Ui.Text(state.Surfaces).FontSize(12f).Color("#C7E39D").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Row(
                        CommandButton("Reset Camera", ResetCamera, "#244E66"),
                        CommandButton("Focus Road", FocusRoad, "#5B3B77"))
                        .Gap(8f),
                    Ui.Row(
                        CommandButton("Focus River", FocusRiver, "#18596D"),
                        CommandButton("Focus Lake", FocusLake, "#2A6B4A"),
                        CommandButton("Focus Raw", FocusRaw, "#6B4A24"))
                        .Gap(8f),
                    Ui.Text(state.Hint).FontSize(11f).Color("#B9C8D8").WhiteSpace(UiWhiteSpace.Normal))
                .Width(364f)
                .Padding(14f)
                .Gap(8f)
                .Radius(14f)
                .Background("#D40B0E13")
                .Border(1f, Color("#33879AB3"))
                .BackdropBlur(8f)
                .Absolute(16f, 16f)
                .ZIndex(30);
        }

        private void ResetCamera()
        {
            Invoke(engine => _runtime.TryResetCamera(engine));
        }

        private void FocusRoad()
        {
            Invoke(engine => _runtime.TryFocusSurface(engine, SplineSurfaceFocusTarget.Road));
        }

        private void FocusRiver()
        {
            Invoke(engine => _runtime.TryFocusSurface(engine, SplineSurfaceFocusTarget.River));
        }

        private void FocusLake()
        {
            Invoke(engine => _runtime.TryFocusSurface(engine, SplineSurfaceFocusTarget.Lake));
        }

        private void FocusRaw()
        {
            Invoke(engine => _runtime.TryFocusSurface(engine, SplineSurfaceFocusTarget.RawMesh));
        }

        private void Invoke(Func<GameEngine, bool> action)
        {
            GameEngine? engine = _engine;
            if (engine == null)
            {
                return;
            }

            action(engine);
            if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                root.IsDirty = true;
            }
        }

        private GameEngine RequireEngine()
        {
            return _engine ?? throw new InvalidOperationException("Spline surface UAT panel requires an active engine.");
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
