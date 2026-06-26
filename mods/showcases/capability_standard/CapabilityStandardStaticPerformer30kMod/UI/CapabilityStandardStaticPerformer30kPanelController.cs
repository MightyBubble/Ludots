using System;
using System.Collections.Generic;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Events;
using Ludots.UI.Surface;
using CapabilityStandardStaticPerformer30kMod.Runtime;

namespace CapabilityStandardStaticPerformer30kMod.UI
{
    internal sealed class CapabilityStandardStaticPerformer30kPanelController
    {
        private readonly CapabilityStandardStaticPerformer30kRuntime _runtime;
        private ReactivePage<CapabilityStandardStaticPerformer30kPanelState>? _page;
        private GameEngine? _engine;
        private CapabilityStandardStaticPerformer30kPanelState _lastState = CapabilityStandardStaticPerformer30kPanelState.Empty;
        private bool _hasLastState;
        private UiSurfaceLeaseHandle _lease;

        public CapabilityStandardStaticPerformer30kPanelController(CapabilityStandardStaticPerformer30kRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool MountOrSync(UIRoot root, GameEngine engine, in CapabilityStandardStaticPerformer30kPanelState state)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(engine);
            if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                return false;
            }

            _engine = engine;
            ReactivePage<CapabilityStandardStaticPerformer30kPanelState>? page = EnsurePage();
            if (page == null)
            {
                return false;
            }

            if (!_hasLastState || !StateEquals(in _lastState, in state))
            {
                CapabilityStandardStaticPerformer30kPanelState snapshot = state;
                page.SetState(_ => snapshot);
                _lastState = snapshot;
                _hasLastState = true;
            }

            surfaceHost.PublishReactivePage(
                ref _lease,
                new UiSurfaceLeaseRequest("Showcase.CapabilityStandardStaticPerformer30k.Panel", UiSurfaceSegment.Overlay, priority: 40),
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
            _hasLastState = false;
        }

        private ReactivePage<CapabilityStandardStaticPerformer30kPanelState>? EnsurePage()
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

            _page = new ReactivePage<CapabilityStandardStaticPerformer30kPanelState>(
                textMeasurer,
                imageSizeProvider,
                CapabilityStandardStaticPerformer30kPanelState.Empty,
                BuildRoot);
            return _page;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<CapabilityStandardStaticPerformer30kPanelState> context)
        {
            CapabilityStandardStaticPerformer30kPanelState state = context.State;
            return Ui.Panel(BuildPanel(state))
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f)
                .ZIndex(40);
        }

        private UiElementBuilder BuildPanel(CapabilityStandardStaticPerformer30kPanelState state)
        {
            return Ui.Card(
                    BuildHeader(state),
                    Ui.ScrollView(
                            BuildSummarySection(state),
                            BuildControlSection(state),
                            BuildChecklistSection(state),
                            BuildDiagnosticsSection(state),
                            BuildPerformerSection(state))
                        .Height(state.ScrollHeight)
                        .Gap(12f))
                .Id("performer-capability_static_performer-showcase-panel")
                .Width(state.PanelWidth)
                .Height(state.PanelHeight)
                .Padding(16f)
                .Gap(12f)
                .Radius(22f)
                .Background("#09131C")
                .Border(1f, ParseColor("#2E526D"))
                .Absolute(state.PanelLeft, state.PanelTop)
                .ZIndex(40);
        }

        private UiElementBuilder BuildHeader(CapabilityStandardStaticPerformer30kPanelState state)
        {
            return Ui.Row(
                    Ui.Column(
                            Ui.Text(state.Title)
                                .FontSize(22f)
                                .Bold()
                                .Color("#F6DA84"),
                            Ui.Text(state.Subtitle)
                                .FontSize(12f)
                                .Color("#B9CAD9")
                                .WhiteSpace(UiWhiteSpace.Normal))
                        .Gap(4f)
                        .FlexGrow(1f)
                        .FlexBasis(0f),
                    Ui.Column(
                            BuildPill("MOUSE", "#17324A", "#9FDBFF"),
                            BuildPill(state.ViewportLabel, "#132232", "#DCE9F4"))
                        .Gap(8f)
                        .Align(UiAlignItems.End))
                .Gap(12f)
                .Align(UiAlignItems.Start);
        }

        private UiElementBuilder BuildSummarySection(CapabilityStandardStaticPerformer30kPanelState state)
        {
            var children = new List<UiElementBuilder>
            {
                SectionTitle("Live State"),
                BuildMetricCard(state.SceneSummary, "#102033"),
                BuildMetricCard(state.ScatterSummary, "#132436"),
                BuildMetricCard(state.BenchmarkSummary, "#171F31"),
                BuildMetricCard(state.CapacitySummary, "#15261C", "#B8F2C5"),
            };

            if (!string.IsNullOrWhiteSpace(state.LastChange))
            {
                children.Add(BuildMetricCard(state.LastChange, "#2E2A12", "#F6DA84"));
            }

            return SectionCard(children.ToArray());
        }

        private UiElementBuilder BuildControlSection(CapabilityStandardStaticPerformer30kPanelState state)
        {
            return SectionCard(
                SectionTitle("Controls"),
                Ui.Text("Debug hotkeys are removed. Use the buttons below to drive the UAT states.")
                    .FontSize(11f)
                    .Color("#9FB3C7")
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                        ActionButton(state.WorkingActive ? "Working ON" : "Working OFF", state.WorkingActive, "#2A6048", "#162432", _ => Execute(runtime => runtime.ToggleWorking())),
                        ActionButton(state.NightActive ? "Night" : "Day", state.NightActive, "#6A4A1D", "#162432", _ => Execute(runtime => runtime.ToggleDayNight())),
                        ActionButton(state.RegionIndex == 0 ? "Region NORTH" : "Region SOUTH", state.RegionIndex == 1, "#244F63", "#162432", _ => Execute(runtime => runtime.CycleRegion())))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        ActionButton("Durability 100%", state.DurabilityPreset == 0, "#235D63", "#162432", _ => Execute(runtime => runtime.SetDurabilityPreset(0))),
                        ActionButton("Durability 50%", state.DurabilityPreset == 1, "#6A5824", "#162432", _ => Execute(runtime => runtime.SetDurabilityPreset(1))),
                        ActionButton("Durability 0%", state.DurabilityPreset == 2, "#6A2C28", "#162432", _ => Execute(runtime => runtime.SetDurabilityPreset(2))))
                    .Wrap()
                    .Gap(8f),
                Ui.Row(
                        ActionButton("Destroy Root", state.RootDestroyed, "#6A2C28", "#162432", _ => Execute(runtime => runtime.DestroyBuilding())),
                        ActionButton("Respawn Root", !state.RootDestroyed, "#295C45", "#162432", _ => Execute(runtime => runtime.RespawnBuilding())))
                    .Wrap()
                    .Gap(8f),
                BuildScatterControl(state));
        }

        private UiElementBuilder BuildScatterControl(CapabilityStandardStaticPerformer30kPanelState state)
        {
            float range = Math.Max(1, state.ScatterMax - state.ScatterMin);
            float percent = Math.Clamp((state.ScatterTarget - state.ScatterMin) / range * 100f, 0f, 100f);
            string targetLabel = $"Target {state.ScatterTarget} | applied {state.ScatterAppliedTotal}";

            return Ui.Column(
                    Ui.Row(
                            Ui.Text("Scatter Benchmark")
                                .FontSize(12f)
                                .Bold()
                                .Color("#F6DA84")
                                .FlexGrow(1f),
                            BuildPill(targetLabel, "#17324A", "#DCE9F4"))
                        .Align(UiAlignItems.Center)
                        .Gap(8f),
                    Ui.Panel(
                            Ui.Panel()
                                .WidthPercent(percent)
                                .Height(12f)
                                .Radius(999f)
                                .Background("#58C788"),
                            Ui.Text($"{state.ScatterMin}")
                                .FontSize(9f)
                                .Color("#9FB3C7")
                                .Absolute(10f, 18f),
                            Ui.Text($"{state.ScatterMax}")
                                .FontSize(9f)
                                .Color("#9FB3C7")
                                .Absolute(Math.Max(124f, state.PanelWidth - 170f), 18f))
                        .Height(36f)
                        .Padding(4f, 6f)
                        .Radius(999f)
                        .Background("#0A121C")
                        .Border(1f, ParseColor("#2E526D"))
                        .OnClick(ctx => Execute(runtime => runtime.SetScatterTargetFromRatio(ResolveTrackRatio(ctx)))),
                    Ui.Row(
                            ActionButton("-100", false, "#244E66", "#162432", _ => Execute(runtime => runtime.AdjustScatterTarget(-100))),
                            ActionButton("-10", false, "#244E66", "#162432", _ => Execute(runtime => runtime.AdjustScatterTarget(-10))),
                            ActionButton("+10", false, "#244E66", "#162432", _ => Execute(runtime => runtime.AdjustScatterTarget(10))),
                            ActionButton("+100", false, "#244E66", "#162432", _ => Execute(runtime => runtime.AdjustScatterTarget(100))),
                            ActionButton("Apply Scatter", state.ScatterTarget == state.ScatterAppliedTotal, "#295C45", "#6A4A1D", _ => Execute(runtime => runtime.ApplyScatterTarget())))
                        .Wrap()
                        .Gap(8f),
                    Ui.Row(
                            ActionButton("Root Only", state.ScatterTarget == 1, "#244E66", "#162432", _ => Execute(runtime => runtime.ApplyScatterLayout(1))),
                            ActionButton("3K", state.ScatterTarget == 3000, "#244E66", "#162432", _ => Execute(runtime => runtime.ApplyScatterLayout(3000))),
                            ActionButton("30K", state.ScatterTarget == 30000, "#244E66", "#162432", _ => Execute(runtime => runtime.ApplyScatterLayout(30000))),
                            ActionButton("100K", state.ScatterTarget == 100000, "#244E66", "#162432", _ => Execute(runtime => runtime.ApplyScatterLayout(100000))),
                            ActionButton("300K", state.ScatterTarget == 300000, "#244E66", "#162432", _ => Execute(runtime => runtime.ApplyScatterLayout(300000))))
                        .Wrap()
                        .Gap(8f))
                .Gap(9f)
                .Padding(12f)
                .Radius(14f)
                .Background("#0C1722");
        }

        private UiElementBuilder BuildChecklistSection(CapabilityStandardStaticPerformer30kPanelState state)
        {
            return SectionCard(
                SectionTitle("Acceptance Checks"),
                BuildLineList(state.ChecklistLines, "#D9E6F2", emptyText: "No checks available."));
        }

        private UiElementBuilder BuildDiagnosticsSection(CapabilityStandardStaticPerformer30kPanelState state)
        {
            return SectionCard(
                SectionTitle("Diagnostics"),
                BuildLineList(state.DiagnosticLines, "#C3D1DE", emptyText: "No diagnostics."));
        }

        private UiElementBuilder BuildPerformerSection(CapabilityStandardStaticPerformer30kPanelState state)
        {
            return SectionCard(
                SectionTitle("Performer Tree"),
                BuildLineList(state.PerformerLines, "#D9E6F2", emptyText: "(no performer instances)", fontFamily: "Consolas"));
        }

        private UiElementBuilder BuildLineList(string[] lines, string color, string emptyText, string? fontFamily = null)
        {
            if (lines == null || lines.Length == 0)
            {
                return Ui.Text(emptyText)
                    .FontSize(11f)
                    .Color("#8EA3B8");
            }

            var builders = new UiElementBuilder[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                UiElementBuilder line = Ui.Text(lines[i])
                    .FontSize(11f)
                    .Color(color)
                    .WhiteSpace(UiWhiteSpace.Normal);
                if (!string.IsNullOrWhiteSpace(fontFamily))
                {
                    line.FontFamily(fontFamily);
                }

                builders[i] = line;
            }

            return Ui.Column(builders).Gap(6f);
        }

        private UiElementBuilder BuildMetricCard(string text, string background, string color = "#F5F7FA")
        {
            return Ui.Text(text)
                .FontSize(12f)
                .Color(color)
                .WhiteSpace(UiWhiteSpace.Normal)
                .Padding(10f)
                .Radius(12f)
                .Background(background);
        }

        private UiElementBuilder SectionCard(params UiElementBuilder[] children)
        {
            return Ui.Card(children)
                .Gap(10f)
                .Padding(14f)
                .Radius(18f)
                .Background("#101A24");
        }

        private static UiElementBuilder SectionTitle(string text)
        {
            return Ui.Text(text)
                .FontSize(13f)
                .Bold()
                .Color("#F0C36B");
        }

        private static UiElementBuilder BuildPill(string text, string background, string color)
        {
            return Ui.Text(text)
                .FontSize(10f)
                .Bold()
                .Color(color)
                .Padding(8f, 4f)
                .Radius(999f)
                .Background(background);
        }

        private static UiElementBuilder ActionButton(
            string label,
            bool active,
            string activeBackground,
            string idleBackground,
            Action<UiActionContext> onClick)
        {
            return Ui.Button(label, onClick)
                .Padding(10f, 8f)
                .Radius(10f)
                .Background(active ? activeBackground : idleBackground)
                .Color(active ? "#F8FBFF" : "#D8E3ED")
                .FontSize(11f);
        }

        private void Execute(Action<CapabilityStandardStaticPerformer30kRuntime> action)
        {
            GameEngine engine = RequireEngine();
            action(_runtime);
            if (_lease.IsValid &&
                engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
            {
                surfaceHost.InvalidateLease(_lease);
            }
        }

        private GameEngine RequireEngine()
        {
            return _engine ?? throw new InvalidOperationException("CapabilityStandardStaticPerformer30k panel requires an active engine.");
        }

        private static float ResolveTrackRatio(UiActionContext context)
        {
            if (context.Event is not UiPointerEvent pointerEvent)
            {
                return 0f;
            }

            UiRect rect = context.TargetNode.LayoutRect;
            if (rect.Width <= 0.001f)
            {
                return 0f;
            }

            return Math.Clamp((pointerEvent.X - rect.X) / rect.Width, 0f, 1f);
        }

        private static UiColor ParseColor(string hex)
        {
            if (!UiColor.TryParse(hex, out UiColor color))
            {
                throw new InvalidOperationException($"Unsupported color literal '{hex}'.");
            }

            return color;
        }

        private static bool StateEquals(
            in CapabilityStandardStaticPerformer30kPanelState left,
            in CapabilityStandardStaticPerformer30kPanelState right)
        {
            return left.PanelLeft == right.PanelLeft &&
                   left.PanelTop == right.PanelTop &&
                   left.PanelWidth == right.PanelWidth &&
                   left.PanelHeight == right.PanelHeight &&
                   left.ScrollHeight == right.ScrollHeight &&
                   string.Equals(left.Title, right.Title, StringComparison.Ordinal) &&
                   string.Equals(left.Subtitle, right.Subtitle, StringComparison.Ordinal) &&
                   string.Equals(left.ViewportLabel, right.ViewportLabel, StringComparison.Ordinal) &&
                   string.Equals(left.SceneSummary, right.SceneSummary, StringComparison.Ordinal) &&
                   string.Equals(left.ScatterSummary, right.ScatterSummary, StringComparison.Ordinal) &&
                   string.Equals(left.LastChange, right.LastChange, StringComparison.Ordinal) &&
                   left.WorkingActive == right.WorkingActive &&
                   left.NightActive == right.NightActive &&
                   left.RootDestroyed == right.RootDestroyed &&
                   left.RegionIndex == right.RegionIndex &&
                   left.DurabilityPreset == right.DurabilityPreset &&
                   left.ScatterTarget == right.ScatterTarget &&
                   left.ScatterAppliedTotal == right.ScatterAppliedTotal &&
                   left.ScatterMin == right.ScatterMin &&
                   left.ScatterMax == right.ScatterMax &&
                   string.Equals(left.BenchmarkSummary, right.BenchmarkSummary, StringComparison.Ordinal) &&
                   string.Equals(left.CapacitySummary, right.CapacitySummary, StringComparison.Ordinal) &&
                   SequenceEqual(left.ChecklistLines, right.ChecklistLines) &&
                   SequenceEqual(left.DiagnosticLines, right.DiagnosticLines) &&
                   SequenceEqual(left.PerformerLines, right.PerformerLines);
        }

        private static bool SequenceEqual(string[]? left, string[]? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
