using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace CameraAcceptanceMod.Runtime
{
    internal sealed class CameraAcceptanceDiagnosticsState
    {
        private const float SampleWeight = 0.18f;

        public bool HudEnabled { get; set; } = true;
        public bool TextEnabled { get; set; } = true;

        public float PanelSyncMs { get; private set; }
        public float HudBuildMs { get; private set; }
        public float TextBuildMs { get; private set; }
        public ReactiveApplyMode PanelLastApplyMode { get; private set; }
        public int PanelLastPatchedNodes { get; private set; }
        public int PanelLastSelectionRowsTouched { get; private set; }
        public int PanelRowPoolSize { get; private set; }
        public long PanelFullRecomposeCount { get; private set; }
        public long PanelIncrementalPatchCount { get; private set; }
        public int PanelVirtualizedWindowCount { get; private set; }
        public int PanelVirtualizedTotalItems { get; private set; }
        public int PanelVirtualizedComposedItems { get; private set; }

        public void ObservePanelSync(double sampleMs) => PanelSyncMs = Smooth(PanelSyncMs, (float)sampleMs);
        public void ObserveHudBuild(double sampleMs) => HudBuildMs = Smooth(HudBuildMs, (float)sampleMs);
        public void ObserveTextBuild(double sampleMs) => TextBuildMs = Smooth(TextBuildMs, (float)sampleMs);

        public void ObservePanelUpdate(ReactiveUpdateStats stats, UiReactiveUpdateMetrics metrics, int selectionRowsTouched, int rowPoolSize, long fullRecomposeCount, long incrementalPatchCount)
        {
            PanelLastApplyMode = stats.Mode;
            PanelLastPatchedNodes = stats.PatchedNodes;
            PanelLastSelectionRowsTouched = selectionRowsTouched;
            PanelRowPoolSize = rowPoolSize;
            PanelFullRecomposeCount = fullRecomposeCount;
            PanelIncrementalPatchCount = incrementalPatchCount;
            PanelVirtualizedWindowCount = metrics.VirtualizedWindowCount;
            PanelVirtualizedTotalItems = metrics.VirtualizedTotalItems;
            PanelVirtualizedComposedItems = metrics.VirtualizedComposedItems;
        }

        private static float Smooth(float current, float sampleMs)
        {
            if (sampleMs < 0f)
            {
                sampleMs = 0f;
            }

            return current <= 0.001f
                ? sampleMs
                : (current * (1f - SampleWeight)) + (sampleMs * SampleWeight);
        }
    }
}
