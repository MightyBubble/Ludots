using System;

namespace Ludots.Core.Presentation.Hud
{
    public sealed class PresentationOverlayLanePacer
    {
        private static readonly PresentationOverlayItemKind[] LargeLaneOrder =
        {
            PresentationOverlayItemKind.Bar,
            PresentationOverlayItemKind.Text,
        };

        private readonly PresentationOverlayLayer _layer;
        private readonly int _largeUnderUiThreshold;
        private readonly int _maxLargeLanesPerFrame;
        private readonly int[] _presentedVersions = new int[3];
        private readonly bool[] _presentedHasContent = new bool[3];

        private int _nextLargeLaneCursor;

        public PresentationOverlayLanePacer(
            PresentationOverlayLayer layer,
            int largeUnderUiThreshold = 48,
            int maxLargeLanesPerFrame = 1)
        {
            if (maxLargeLanesPerFrame <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLargeLanesPerFrame));
            }

            _layer = layer;
            _largeUnderUiThreshold = largeUnderUiThreshold;
            _maxLargeLanesPerFrame = maxLargeLanesPerFrame;
            Reset();
        }

        public LaneRefreshPlan BuildPlan(PresentationOverlayScene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);

            byte refreshFlags = 0;
            int deferredLargeCount = 0;
            Span<PresentationOverlayItemKind> deferredLargeKinds = stackalloc PresentationOverlayItemKind[LargeLaneOrder.Length];

            for (int kindValue = (int)PresentationOverlayItemKind.Text; kindValue <= (int)PresentationOverlayItemKind.Bar; kindValue++)
            {
                PresentationOverlayItemKind kind = (PresentationOverlayItemKind)kindValue;
                int index = GetKindIndex(kind);
                bool hasContent = scene.GetLaneSpan(_layer, kind).Length > 0;
                int version = scene.GetLaneVersion(_layer, kind);
                bool needsRefresh = version != _presentedVersions[index] || hasContent != _presentedHasContent[index];
                if (!needsRefresh)
                {
                    continue;
                }

                bool isColdStart = _presentedVersions[index] < 0;
                bool isClearRefresh = !hasContent && _presentedHasContent[index];
                bool isDeferredLargeLane = !isColdStart &&
                    !isClearRefresh &&
                    IsDeferredLargeLane(scene, kind);

                if (isDeferredLargeLane)
                {
                    deferredLargeKinds[deferredLargeCount++] = kind;
                    continue;
                }

                refreshFlags |= ToFlag(kind);
            }

            if (deferredLargeCount > 0)
            {
                int selectedLargeCount = 0;
                while (selectedLargeCount < _maxLargeLanesPerFrame && selectedLargeCount < deferredLargeCount)
                {
                    PresentationOverlayItemKind selectedKind = SelectNextLargeLane(deferredLargeKinds[..deferredLargeCount]);
                    byte selectedFlag = ToFlag(selectedKind);
                    if ((refreshFlags & selectedFlag) == 0)
                    {
                        refreshFlags |= selectedFlag;
                        selectedLargeCount++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return new LaneRefreshPlan(refreshFlags);
        }

        public void MarkPresented(PresentationOverlayScene scene, in LaneRefreshPlan plan)
        {
            ArgumentNullException.ThrowIfNull(scene);

            for (int kindValue = (int)PresentationOverlayItemKind.Text; kindValue <= (int)PresentationOverlayItemKind.Bar; kindValue++)
            {
                PresentationOverlayItemKind kind = (PresentationOverlayItemKind)kindValue;
                if (!plan.ShouldRefresh(kind))
                {
                    continue;
                }

                int index = GetKindIndex(kind);
                _presentedVersions[index] = scene.GetLaneVersion(_layer, kind);
                _presentedHasContent[index] = scene.GetLaneSpan(_layer, kind).Length > 0;
            }
        }

        public void Reset()
        {
            Array.Fill(_presentedVersions, -1);
            Array.Clear(_presentedHasContent, 0, _presentedHasContent.Length);
            _nextLargeLaneCursor = 0;
        }

        private bool IsDeferredLargeLane(PresentationOverlayScene scene, PresentationOverlayItemKind kind)
        {
            if (_layer != PresentationOverlayLayer.UnderUi)
            {
                return false;
            }

            return kind switch
            {
                PresentationOverlayItemKind.Bar or PresentationOverlayItemKind.Text =>
                    scene.GetLaneSpan(_layer, kind).Length >= _largeUnderUiThreshold,
                _ => false,
            };
        }

        private PresentationOverlayItemKind SelectNextLargeLane(ReadOnlySpan<PresentationOverlayItemKind> deferredLargeKinds)
        {
            for (int offset = 0; offset < LargeLaneOrder.Length; offset++)
            {
                int candidateIndex = (_nextLargeLaneCursor + offset) % LargeLaneOrder.Length;
                PresentationOverlayItemKind candidate = LargeLaneOrder[candidateIndex];
                for (int i = 0; i < deferredLargeKinds.Length; i++)
                {
                    if (deferredLargeKinds[i] == candidate)
                    {
                        _nextLargeLaneCursor = (candidateIndex + 1) % LargeLaneOrder.Length;
                        return candidate;
                    }
                }
            }

            PresentationOverlayItemKind fallback = deferredLargeKinds[0];
            _nextLargeLaneCursor = GetLargeLaneOrderIndex(fallback) + 1;
            if (_nextLargeLaneCursor >= LargeLaneOrder.Length)
            {
                _nextLargeLaneCursor = 0;
            }
            return fallback;
        }

        private static int GetLargeLaneOrderIndex(PresentationOverlayItemKind kind)
        {
            for (int i = 0; i < LargeLaneOrder.Length; i++)
            {
                if (LargeLaneOrder[i] == kind)
                {
                    return i;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        private static int GetKindIndex(PresentationOverlayItemKind kind)
        {
            return kind switch
            {
                PresentationOverlayItemKind.Text => 0,
                PresentationOverlayItemKind.Rect => 1,
                PresentationOverlayItemKind.Bar => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        private static byte ToFlag(PresentationOverlayItemKind kind)
        {
            return kind switch
            {
                PresentationOverlayItemKind.Text => 1 << 0,
                PresentationOverlayItemKind.Rect => 1 << 1,
                PresentationOverlayItemKind.Bar => 1 << 2,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        public readonly record struct LaneRefreshPlan(byte Flags)
        {
            public bool HasAnyRefresh => Flags != 0;

            public bool ShouldRefresh(PresentationOverlayItemKind kind)
            {
                return (Flags & ToFlag(kind)) != 0;
            }
        }
    }
}
