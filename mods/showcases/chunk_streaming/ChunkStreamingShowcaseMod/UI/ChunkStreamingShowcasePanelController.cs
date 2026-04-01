using System;
using ChunkStreamingShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using RoadNetworkShowcaseMod.Runtime;

namespace ChunkStreamingShowcaseMod.UI
{
    internal sealed class ChunkStreamingShowcasePanelController
    {
        private ReactivePage<ChunkStreamingShowcasePanelState>? _page;
        private GameEngine? _engine;
        private readonly ChunkStreamingShowcaseRuntime _runtime;

        public ChunkStreamingShowcasePanelController(ChunkStreamingShowcaseRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool MountOrSync(UIRoot root, GameEngine engine, in ChunkStreamingShowcasePanelState state)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(engine);

            _engine = engine;
            ReactivePage<ChunkStreamingShowcasePanelState>? page = EnsurePage();
            if (page == null)
            {
                return false;
            }

            ChunkStreamingShowcasePanelState snapshot = state;
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

        private ReactivePage<ChunkStreamingShowcasePanelState>? EnsurePage()
        {
            if (_page != null)
            {
                return _page;
            }

            var engine = RequireEngine();
            if (engine.GetService(CoreServiceKeys.UiTextMeasurer) is not IUiTextMeasurer textMeasurer ||
                engine.GetService(CoreServiceKeys.UiImageSizeProvider) is not IUiImageSizeProvider imageSizeProvider)
            {
                return null;
            }

            _page = new ReactivePage<ChunkStreamingShowcasePanelState>(
                textMeasurer,
                imageSizeProvider,
                ChunkStreamingShowcasePanelState.Empty,
                BuildRoot);
            return _page;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<ChunkStreamingShowcasePanelState> context)
        {
            ChunkStreamingShowcasePanelState state = context.State;
            return Ui.Card(
                    Ui.Text(state.Title).FontSize(17f).Bold().Color("#F7FAFF"),
                    Ui.Text(state.Status).FontSize(12f).Color("#E7EEF5").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(state.Camera).FontSize(12f).Color("#8FD0FF"),
                    Ui.Text(state.Chunks).FontSize(12f).Color("#F7D889"),
                    Ui.Row(
                        CommandButton("Reset", ResetCamera, "#244E66"),
                        CommandButton("Reload", ReloadScenario, "#385437"))
                        .Gap(8f),
                    Ui.Row(
                        CommandButton("West Gate", FocusWest, "#2D4F76"),
                        CommandButton("Center", FocusCenter, "#6A4524"),
                        CommandButton("East Gate", FocusEast, "#7A2E2E"))
                        .Gap(8f),
                    Ui.Row(
                        CommandButton("Red Capital", FocusRedCapital, "#742E3B"))
                        .Gap(8f),
                    Ui.Text(state.Hint).FontSize(11f).Color("#B9C8D8").WhiteSpace(UiWhiteSpace.Normal))
                .Width(356f)
                .Padding(14f)
                .Gap(8f)
                .Radius(14f)
                .Background("#D40B0E13")
                .Border(1f, Color("#33879AB3"))
                .BackdropBlur(8f)
                .Absolute(16f, 208f)
                .ZIndex(30);
        }

        private void ResetCamera()
        {
            Invoke(engine => _runtime.TryResetCamera(engine));
        }

        private void ReloadScenario()
        {
            Invoke(engine => _runtime.TryReloadScenario(engine));
        }

        private void FocusWest()
        {
            Invoke(engine => _runtime.TryFocusLandmark(engine, RoadNetworkScenarioDefinition.RoadLandmarkId.WestGate, "Camera focused on West Gate chunk window."));
        }

        private void FocusCenter()
        {
            Invoke(engine => _runtime.TryFocusLandmark(engine, RoadNetworkScenarioDefinition.RoadLandmarkId.CentralCrossing, "Camera focused on Central Crossing chunk window."));
        }

        private void FocusEast()
        {
            Invoke(engine => _runtime.TryFocusLandmark(engine, RoadNetworkScenarioDefinition.RoadLandmarkId.EastGate, "Camera focused on East Gate chunk window."));
        }

        private void FocusRedCapital()
        {
            Invoke(engine => _runtime.TryFocusLandmark(engine, RoadNetworkScenarioDefinition.RoadLandmarkId.RedCapital, "Camera focused on Red Capital chunk window."));
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
            return _engine ?? throw new InvalidOperationException("ChunkStreamingShowcase panel requires an active engine.");
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
