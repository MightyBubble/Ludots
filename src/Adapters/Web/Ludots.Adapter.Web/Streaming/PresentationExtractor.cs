using System;
using Ludots.Adapter.Web.Services;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

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
        private byte[] _snapshot = new byte[256 * 1024];
        private byte[] _deltaSnapshot = new byte[128 * 1024];
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

        public readonly record struct CapturedFrame(
            uint FrameNumber,
            byte[] FullData,
            int FullLength,
            byte[]? DeltaData,
            int DeltaLength);

        public CapturedFrame CaptureFrame()
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
                skinnedVisuals);
            int fullLength = _fullEncoder.EncodedLength;
            EnsureSnapshot(fullLength);
            _fullEncoder.CopyTo(_snapshot);

            // DeltaCompressor 必须每帧调用以维持 prev 快照与帧号连续；皮肤 lane 可见或
            // delta 大于全帧时丢弃 delta 载荷，只保留快照推进。
            _deltaCompressor.TryEncodeDelta(
                _frameNumber,
                simTick,
                timestampMs,
                in camera,
                primitives,
                groundOverlays,
                screenHud,
                worldHudStrings,
                debugDraw,
                screenOverlay,
                uiSceneJson);
            byte[]? deltaData = null;
            int deltaLength = 0;
            if (!HasVisibleSkinnedVisuals(skinnedVisuals) && _deltaCompressor.EncodedLength < fullLength)
            {
                deltaLength = _deltaCompressor.EncodedLength;
                EnsureDeltaSnapshot(deltaLength);
                _deltaCompressor.CopyTo(_deltaSnapshot);
                deltaData = _deltaSnapshot;
            }

            ClearConsumedBuffers(screenOverlay);
            return new CapturedFrame(_frameNumber, _snapshot, fullLength, deltaData, deltaLength);
        }

        private static bool HasVisibleSkinnedVisuals(SkinnedVisualBatchBuffer? skinnedVisuals)
        {
            if (skinnedVisuals == null)
            {
                return false;
            }

            ReadOnlySpan<SkinnedVisualBatchItem> span = skinnedVisuals.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Visibility == VisualVisibility.Visible)
                {
                    return true;
                }
            }

            return false;
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

        private void EnsureDeltaSnapshot(int required)
        {
            if (_deltaSnapshot.Length < required)
            {
                _deltaSnapshot = new byte[required * 2];
            }
        }
    }
}
