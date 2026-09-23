using System;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.Web.Streaming
{
    public sealed class PresentationExtractor
    {
        private readonly GameEngine _engine;
        private readonly BinaryFrameEncoder _fullEncoder;
        private readonly WebCameraAdapter _cameraAdapter;
        private readonly WebUiRuntimeBridge _uiBridge;
        private uint _frameNumber;
        private string? _lastUiSceneJson;
        private bool _hasUiSceneSnapshot;

        public PresentationExtractor(GameEngine engine, WebCameraAdapter cameraAdapter, WebUiRuntimeBridge uiBridge)
        {
            _engine = engine;
            _cameraAdapter = cameraAdapter;
            _uiBridge = uiBridge;
            _fullEncoder = new BinaryFrameEncoder();
        }

        public ReadOnlyMemory<byte> CaptureFrame()
        {
            _frameNumber++;

            PrimitiveDrawBuffer? primitives = _engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            SkinnedVisualBatchBuffer? skinnedVisuals = _engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
            GroundOverlayBuffer? groundOverlays = _engine.GetService(CoreServiceKeys.GroundOverlayBuffer);
            WorldHudBatchBuffer? worldHud = _engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            ScreenHudBatchBuffer? screenHud = _engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            var worldHudStrings = _engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
            DebugDrawCommandBuffer? debugDraw = _engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer);
            ScreenOverlayBuffer? screenOverlay = _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            MinimapScreenMarkerBuffer? minimapMarkers = _engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer);
            string? uiSceneJson = null;
            if (_uiBridge.TryConsumeScene(out string? changedSceneJson))
            {
                uiSceneJson = changedSceneJson;
                _lastUiSceneJson = changedSceneJson;
                _hasUiSceneSnapshot = true;
            }

            var camera = _cameraAdapter.CurrentState;
            long timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int simTick = _engine.GameSession?.CurrentTick ?? 0;

            string? fullFrameUiScene = uiSceneJson;
            if (fullFrameUiScene == null && _hasUiSceneSnapshot)
            {
                fullFrameUiScene = _lastUiSceneJson;
            }

            _fullEncoder.Encode(
                _frameNumber,
                simTick,
                timestampMs,
                in camera,
                primitives,
                groundOverlays,
                worldHud,
                screenHud,
                worldHudStrings,
                debugDraw,
                screenOverlay,
                fullFrameUiScene,
                skinnedVisuals,
                minimapMarkers);
            ClearConsumedBuffers(screenOverlay);
            return _fullEncoder.GetResultMemory();
        }

        private static void ClearConsumedBuffers(ScreenOverlayBuffer? screenOverlay)
        {
            screenOverlay?.Clear();
        }

    }
}
