using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.UI
{
    internal sealed class RoadNetworkShowcasePanelController
    {
        private ReactivePage<RoadNetworkShowcasePanelState>? _page;
        private GameEngine? _engine;
        private readonly RoadNetworkShowcaseRuntime _runtime;

        public RoadNetworkShowcasePanelController(RoadNetworkShowcaseRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool MountOrSync(UIRoot root, GameEngine engine, in RoadNetworkShowcasePanelState state)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(engine);

            _engine = engine;
            ReactivePage<RoadNetworkShowcasePanelState>? page = EnsurePage();
            if (page == null)
            {
                return false;
            }

            RoadNetworkShowcasePanelState snapshot = state;
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

        private ReactivePage<RoadNetworkShowcasePanelState>? EnsurePage()
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

            _page = new ReactivePage<RoadNetworkShowcasePanelState>(
                textMeasurer,
                imageSizeProvider,
                RoadNetworkShowcasePanelState.Empty,
                BuildRoot);
            return _page;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<RoadNetworkShowcasePanelState> context)
        {
            RoadNetworkShowcasePanelState state = context.State;
            return Ui.Card(
                    Ui.Text(state.Title).FontSize(17f).Bold().Color("#F7FAFF"),
                    Ui.Text(state.Status).FontSize(12f).Color("#E7EEF5").WhiteSpace(UiWhiteSpace.Normal),
                    Ui.Text(state.Actor).FontSize(12f).Color("#8FD0FF"),
                    Ui.Text(state.Profile).FontSize(12f).Color("#F7D889"),
                    Ui.Text(state.Chunks).FontSize(12f).Color("#C2D3E3"),
                    Ui.Row(
                        CommandButton("Reset Camera", ResetCamera, "#244E66"),
                        CommandButton("Reset Scenario", ReloadScenario, "#385437"))
                        .Gap(8f),
                    Ui.Row(
                        CommandButton("Focus West", FocusWest, "#4A3D66"),
                        CommandButton("Focus Center", FocusCenter, "#4A3D66"),
                        CommandButton("Focus East", FocusEast, "#4A3D66"))
                        .Gap(8f),
                    Ui.Row(
                        CommandButton("Long Haul", RunLongHaul, "#6A4524"),
                        CommandButton("North Demo", RunNorthDemo, "#16546B"),
                        CommandButton("South Demo", RunSouthDemo, "#6B1E2D"))
                        .Gap(8f),
                    Ui.Text(state.Hint).FontSize(11f).Color("#B9C8D8").WhiteSpace(UiWhiteSpace.Normal))
                .Width(356f)
                .Padding(14f)
                .Gap(8f)
                .Radius(14f)
                .Background("#D40B0E13")
                .Border(1f, Color("#33879AB3"))
                .BackdropBlur(8f)
                .Absolute(16f, 252f)
                .ZIndex(30);
        }

        private void ResetCamera()
        {
            Invoke(action: engine => _runtime.TryResetCamera(engine));
        }

        private void ReloadScenario()
        {
            Invoke(action: engine => _runtime.TryReloadScenario(engine));
        }

        private void FocusWest()
        {
            Invoke(action: engine => _runtime.TryFocusLandmark(engine, RoadNetworkScenarioDefinition.RoadLandmarkId.WestGate, "Camera focused on West Gate."));
        }

        private void FocusCenter()
        {
            Invoke(action: engine => _runtime.TryFocusLandmark(engine, RoadNetworkScenarioDefinition.RoadLandmarkId.CentralCrossing, "Camera focused on Central Crossing."));
        }

        private void FocusEast()
        {
            Invoke(action: engine => _runtime.TryFocusLandmark(engine, RoadNetworkScenarioDefinition.RoadLandmarkId.EastGate, "Camera focused on East Gate."));
        }

        private void RunLongHaul()
        {
            Invoke(action: engine => _runtime.TryRunPreset(engine, RoadNetworkShowcaseRuntime.ShowcaseCommandPreset.LongHaulToRedCapital));
        }

        private void RunNorthDemo()
        {
            Invoke(action: engine => _runtime.TryRunPreset(engine, RoadNetworkShowcaseRuntime.ShowcaseCommandPreset.NorthFlankToNorthWatch));
        }

        private void RunSouthDemo()
        {
            Invoke(action: engine => _runtime.TryRunPreset(engine, RoadNetworkShowcaseRuntime.ShowcaseCommandPreset.SouthGuardToSouthWatch));
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
            return _engine ?? throw new InvalidOperationException("RoadNetworkShowcase panel requires an active engine.");
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
