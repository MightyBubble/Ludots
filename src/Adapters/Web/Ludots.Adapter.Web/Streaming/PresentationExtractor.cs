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
        private readonly WebCameraAdapter _cameraAdapter;
        private readonly WebUiFrameSource _uiFrameSource;
        private readonly BinaryFrameEncoder _full = new();
        private readonly DeltaCompressor _delta = new();
        private uint _frameNumber;

        public PresentationExtractor(GameEngine engine, WebCameraAdapter cameraAdapter, WebUiFrameSource uiFrameSource)
        {
            _engine = engine;
            _cameraAdapter = cameraAdapter;
            _uiFrameSource = uiFrameSource;
        }

        public (byte[] Buffer, int Length) CaptureFrame()
        {
            PrimitiveDrawBuffer? primitives = _engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            GroundOverlayBuffer? groundOverlays = _engine.GetService(CoreServiceKeys.GroundOverlayBuffer);
            WorldHudBatchBuffer? worldHud = _engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            ScreenHudBatchBuffer? screenHud = _engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            ScreenOverlayBuffer? screenOverlay = _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            DebugDrawCommandBuffer? debugDraw = _engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer);
            _uiFrameSource.TryConsume(out string? uiSceneDiffJson);

            _frameNumber++;
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var camera = _cameraAdapter.CurrentState;
            int simTick = _engine.GameSession?.CurrentTick ?? 0;

            bool useDelta = _delta.TryEncodeDelta(
                _frameNumber,
                simTick,
                ts,
                in camera,
                primitives,
                groundOverlays,
                worldHud,
                debugDraw,
                screenOverlay,
                uiSceneDiffJson);

            if (useDelta)
            {
                int len = _delta.EncodedLength;
                byte[] outBuf = new byte[len];
                _delta.CopyTo(outBuf);
                ClearConsumedBuffers(screenOverlay);
                return (outBuf, len);
            }

            _full.Encode(
                _frameNumber,
                simTick,
                ts,
                in camera,
                primitives,
                groundOverlays,
                worldHud,
                screenHud,
                debugDraw,
                screenOverlay,
                uiSceneDiffJson);
            int fullLen = _full.EncodedLength;
            byte[] fullBuf = new byte[fullLen];
            _full.CopyTo(fullBuf);
            ClearConsumedBuffers(screenOverlay);
            return (fullBuf, fullLen);
        }

        private static void ClearConsumedBuffers(ScreenOverlayBuffer? screenOverlay)
        {
            screenOverlay?.Clear();
        }
    }
}
