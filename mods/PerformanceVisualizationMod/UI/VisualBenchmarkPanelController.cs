using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using PerformanceVisualizationMod.Runtime;

namespace PerformanceVisualizationMod.UI
{
    internal sealed class VisualBenchmarkPanelController
    {
        private ReactivePage<VisualBenchmarkPanelState>? _page;
        private GameEngine? _engine;
        private readonly VisualBenchmarkRuntime _runtime;

        public VisualBenchmarkPanelController(VisualBenchmarkRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool MountOrSync(UIRoot root, GameEngine engine, in VisualBenchmarkPanelState state)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(engine);

            _engine = engine;
            ReactivePage<VisualBenchmarkPanelState>? page = EnsurePage();
            if (page == null)
            {
                return false;
            }

            VisualBenchmarkPanelState snapshot = state;
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

        private ReactivePage<VisualBenchmarkPanelState>? EnsurePage()
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

            _page = new ReactivePage<VisualBenchmarkPanelState>(
                textMeasurer,
                imageSizeProvider,
                VisualBenchmarkPanelState.Empty,
                BuildRoot);
            return _page;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<VisualBenchmarkPanelState> context)
        {
            VisualBenchmarkPanelState state = context.State;
            return Ui.Card(
                    Ui.Text(state.Title).FontSize(18f).Bold().Color("#F7FAFF"),
                    Ui.Text(state.Status).FontSize(12f).Color("#E7EEF5").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(state.Scenario).FontSize(12f).Color("#8FD0FF"),
                    Ui.Text(state.Metrics).FontSize(12f).Color("#F7D889"),
                    Ui.Text(state.Camera).FontSize(12f).Color("#CFE7FF"),
                    Ui.Row(
                        CommandButton("Run 2K", RunSmall, "#244E66"),
                        CommandButton("Run 8K", RunMedium, "#385437"),
                        CommandButton("Run 32K", RunLarge, "#5E4518"))
                        .Gap(8f),
                    Ui.Row(
                        CommandButton("HUD 100K", RunHud100k, "#5A2E66"),
                        CommandButton("Skia 10K", RunSkiaHotpath, "#1F5C58"))
                        .Gap(8f),
                    Ui.Row(
                        CommandButton("Reset Camera", ResetCamera, "#3B3665"),
                        CommandButton("Clear", ClearScenario, "#6B1E2D"))
                        .Gap(8f),
                    Ui.Text(state.Hint).FontSize(11f).Color("#B9C8D8").WhiteSpace(UiWhiteSpace.Normal))
                .Width(420f)
                .Padding(14f)
                .Gap(8f)
                .Radius(14f)
                .Background("#D40B0E13")
                .Border(1f, Color("#33879AB3"))
                .BackdropBlur(8f)
                .Absolute(16f, 16f)
                .ZIndex(30);
        }

        private void RunSmall()
        {
            Invoke(engine => _runtime.TryRunScenario(engine, VisualBenchmarkScenarioConfig.Small.Key));
        }

        private void RunMedium()
        {
            Invoke(engine => _runtime.TryRunScenario(engine, VisualBenchmarkScenarioConfig.Medium.Key));
        }

        private void RunLarge()
        {
            Invoke(engine => _runtime.TryRunScenario(engine, VisualBenchmarkScenarioConfig.Large.Key));
        }

        private void RunHud100k()
        {
            Invoke(engine => _runtime.TryRunScenario(engine, VisualBenchmarkScenarioConfig.Hud100k.Key));
        }

        private void RunSkiaHotpath()
        {
            Invoke(engine => _runtime.TryRunScenario(engine, VisualBenchmarkScenarioConfig.SkiaHotpath.Key));
        }

        private void ResetCamera()
        {
            Invoke(_runtime.TryResetCamera);
        }

        private void ClearScenario()
        {
            Invoke(_runtime.TryClearScenario);
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
            return _engine ?? throw new InvalidOperationException("VisualBenchmark panel requires an active engine.");
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
