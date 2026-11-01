using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Minimap
{
    public delegate bool MinimapKnowledgeViewerProvider(GameEngine engine, out Entity viewer);

    public sealed class MinimapPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly MinimapRuntime _runtime;
        private readonly MinimapMarkerBuffer? _markers;
        private readonly MinimapScreenMarkerBuffer? _screenMarkers;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;

        public MinimapPresentationSystem(GameEngine engine, MinimapRuntime runtime)
            : this(engine, runtime, null, null)
        {
        }

        public MinimapPresentationSystem(
            GameEngine engine,
            MinimapRuntime runtime,
            MinimapMarkerBuffer? markers,
            MinimapScreenMarkerBuffer? screenMarkers,
            PresentationTimingDiagnostics? timingDiagnostics = null)
        {
            _engine = engine ?? throw new System.ArgumentNullException(nameof(engine));
            _runtime = runtime ?? throw new System.ArgumentNullException(nameof(runtime));
            _markers = markers;
            _screenMarkers = screenMarkers;
            _timingDiagnostics = timingDiagnostics;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float t)
        {
            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            MinimapMarkerBuffer? markers = _markers ?? _engine.GetService(CoreServiceKeys.MinimapMarkerBuffer);
            MinimapScreenMarkerBuffer? screenMarkers = _screenMarkers ?? _engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer);
            if (markers == null || screenMarkers == null)
            {
                return;
            }

            long refreshStart = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            _runtime.Refresh(_engine, markers, screenMarkers);
            if (_timingDiagnostics != null)
            {
                _timingDiagnostics.ObserveMinimapProjection(
                    (Stopwatch.GetTimestamp() - refreshStart) * 1000d / Stopwatch.Frequency,
                    screenMarkers.Count,
                    screenMarkers.DroppedSinceClear);
            }

            _runtime.Render(overlay);
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }
    }
}
