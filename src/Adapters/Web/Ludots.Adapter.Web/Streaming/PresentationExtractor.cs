using System;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.Web.Streaming
{
    public sealed class PresentationExtractor
    {
        private readonly GameEngine _engine;
        private readonly BinaryFrameEncoder _fullEncoder;
        private readonly DeltaCompressor _deltaCompressor;
        private readonly WebCameraAdapter _cameraAdapter;
        private readonly WebUiRuntimeBridge _uiBridge;
        private uint _frameNumber;
        private int _fullFrameInterval = 30;
        private int _framesSinceFullFrame;
        private byte[] _snapshot = new byte[256 * 1024];
        private string? _lastUiSceneJson;
        private bool _hasUiSceneSnapshot;

        public PresentationExtractor(GameEngine engine, WebCameraAdapter cameraAdapter, WebUiRuntimeBridge uiBridge)
        {
            _engine = engine;
            _cameraAdapter = cameraAdapter;
            _uiBridge = uiBridge;
            _fullEncoder = new BinaryFrameEncoder();
            _deltaCompressor = new DeltaCompressor();
        }

        public (byte[] Data, int Length) CaptureFrame()
        {
            _frameNumber++;
            _framesSinceFullFrame++;

            PrimitiveDrawBuffer? primitives = _engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            GroundOverlayBuffer? groundOverlays = _engine.GetService(CoreServiceKeys.GroundOverlayBuffer);
            WorldHudBatchBuffer? worldHud = _engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            ScreenHudBatchBuffer? screenHud = _engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            DebugDrawCommandBuffer? debugDraw = _engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer);
            ScreenOverlayBuffer? screenOverlay = _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);

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

            bool useDelta = _framesSinceFullFrame < _fullFrameInterval;
            bool deltaOk = false;

            if (useDelta)
            {
                deltaOk = _deltaCompressor.TryEncodeDelta(
                    _frameNumber,
                    simTick,
                    timestampMs,
                    in camera,
                    primitives,
                    groundOverlays,
                    worldHud,
                    debugDraw,
                    screenOverlay,
                    uiSceneJson);
            }

            if (deltaOk)
            {
                int len = _deltaCompressor.EncodedLength;
                EnsureSnapshot(len);
                _deltaCompressor.CopyTo(_snapshot);
                ClearConsumedBuffers(screenOverlay);
                return (_snapshot, len);
            }

            _framesSinceFullFrame = 0;
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
                debugDraw,
                screenOverlay,
                fullFrameUiScene);
            int fullLength = _fullEncoder.EncodedLength;
            EnsureSnapshot(fullLength);
            _fullEncoder.CopyTo(_snapshot);
            ClearConsumedBuffers(screenOverlay);
            return (_snapshot, fullLength);
        }

        private static void ClearConsumedBuffers(ScreenOverlayBuffer? screenOverlay)
        {
            screenOverlay?.Clear();
        }

        private void EnsureSnapshot(int required)
        {
            if (_snapshot.Length < required)
            {
                _snapshot = new byte[required * 2];
            }
        }
    }
}
