using System;
using System.Collections.Generic;
using Arch.System;
using DualSeatPanelsShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace DualSeatPanelsShowcaseMod.Runtime
{
    /// <summary>
    /// Full-window feedback strip for the dual-seat panel showcase: first-screen key
    /// guide, the shared panel's effective audience, and the most recent seat-attributed
    /// outcomes (admitted green / refused red with the engine's reason verbatim).
    /// Panels themselves are template panels rendered by the engine; this overlay only
    /// carries cross-seat guidance and admission feedback (declared full-window
    /// overlay tradeoff).
    /// </summary>
    internal sealed class DualSeatPanelsHudSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly DualSeatPanelsFeedback _feedback;
        private readonly UiColor _accent = new(0x8A, 0xD7, 0xFF);
        private readonly UiColor _dim = new(0x8F, 0x9D, 0xAD);
        private readonly UiColor _admit = new(0x8D, 0xE3, 0xAE);
        private readonly UiColor _refuse = new(0xFF, 0x8A, 0x8A);

        private ReactivePage<IReadOnlyList<string>>? _page;
        private UiSurfaceLeaseHandle _lease;
        private IReadOnlyList<string>? _lastLines;

        public DualSeatPanelsHudSystem(GameEngine engine, DualSeatPanelsFeedback feedback)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() => Release();

        public void Update(in float dt)
        {
            if (!DualSeatPanelsShowcaseIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
            {
                Release();
                return;
            }

            if (_engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host ||
                _engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            IReadOnlyList<string> lines = BuildLines();
            if (_page == null)
            {
                var text = (_engine.GetService(CoreServiceKeys.UiTextMeasurer) as IUiTextMeasurer)
                    ?? new NullTextMeasurer();
                var images = (_engine.GetService(CoreServiceKeys.UiImageSizeProvider) as IUiImageSizeProvider)
                    ?? new NullImageSizeProvider();
                _page = new ReactivePage<IReadOnlyList<string>>(text, images, lines, BuildRoot);
            }
            else if (!SameLines(_lastLines, lines))
            {
                _page.SetState(_ => lines);
            }

            _lastLines = lines;
            host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest(
                "DualSeatPanelsShowcaseMod.Hud", UiSurfaceSegment.Overlay, priority: 55), _page);
        }

        private IReadOnlyList<string> BuildLines()
        {
            var lines = new List<string>
            {
                "双 Seat 面板：左半屏 seat.0 / 右半屏 seat.1，各一块自己的模板面板 + 一块共享面板",
                "seat.0 (左)： Q 强化自己 +10 · W 打击自己 -10 · E 戳对面面板 · R 共享蓄能 · T 轮换共享受众",
                "seat.1 (右)： U 强化自己 +10 · I 打击自己 -10 · O 戳对面面板 · P 共享蓄能 · Y 轮换共享受众",
                $"共享面板受众：{DescribeSharedAudience()}（面板数值全部来自图求值；拒绝的操作不进游戏状态）",
            };

            foreach (DualSeatPanelOutcome outcome in _feedback.Snapshot())
            {
                lines.Add(outcome.Admitted
                    ? $"{outcome.SeatId} → {outcome.PanelId} {outcome.EventId}：准入"
                    : $"{outcome.SeatId} → {outcome.PanelId} {outcome.EventId}：拒绝 — {outcome.Reason}");
            }

            return lines;
        }

        private string DescribeSharedAudience()
        {
            if (_engine.GetService(CoreServiceKeys.PanelActivationStore) is UiPanelActivationStore activation &&
                activation.TryGetAudienceOverride(DualSeatPanelsShowcaseIds.SharedPanelId, out PanelAudience overrideAudience))
            {
                return $"覆盖为 {overrideAudience}";
            }

            return "声明受众 [seat.0, seat.1]";
        }

        private UiElementBuilder BuildRoot(ReactiveContext<IReadOnlyList<string>> context)
        {
            var rows = new List<UiElementBuilder>();
            IReadOnlyList<string> lines = context.State;
            for (int i = 0; i < lines.Count; i++)
            {
                bool isOutcome = i >= 4;
                UiColor color = !isOutcome
                    ? i == 0 ? _accent : _dim
                    : lines[i].Contains("拒绝", StringComparison.Ordinal) ? _refuse : _admit;
                UiElementBuilder text = Ui.Text(lines[i])
                    .FontSize(isOutcome ? 12f : 11f)
                    .Color(color)
                    .WhiteSpace(UiWhiteSpace.Normal)
                    .WidthPercent(100f);
                if (i == 0 || isOutcome)
                {
                    text = text.Bold();
                }

                rows.Add(text);
            }

            return Ui.Column(
                    Ui.Column(rows.ToArray()).Gap(3f).Width(1180f).Padding(10f)
                        .Background("#0B1520E6")
                        .Border(1f, new UiColor(0x2F, 0x47, 0x5E))
                        .Radius(8f))
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Padding(8f)
                .Align(UiAlignItems.End)
                .ZIndex(55);
        }

        private static bool SameLines(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void Release()
        {
            if (_lease.IsValid &&
                _engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host)
            {
                host.ReleaseLease(ref _lease);
            }

            _lastLines = null;
        }

        private sealed class NullTextMeasurer : IUiTextMeasurer
        {
            public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
            {
                float width = MeasureWidth(text, style);
                float height = Math.Max(1f, style.FontSize);
                return new UiTextLayoutResult(new[] { text ?? string.Empty }, width, height, height);
            }

            public float MeasureWidth(string? text, UiStyle style)
            {
                int length = string.IsNullOrEmpty(text) ? 0 : text.Length;
                return length * Math.Max(1f, style.FontSize) * 0.55f;
            }
        }

        private sealed class NullImageSizeProvider : IUiImageSizeProvider
        {
            public bool TryGetSize(string? source, out float width, out float height)
            {
                width = 0f;
                height = 0f;
                return false;
            }
        }
    }
}
