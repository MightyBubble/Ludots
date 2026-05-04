using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Minimap
{
    public enum MinimapZoomBand : byte
    {
        Strategic = 0,
        Regional = 1,
        Tactical = 2,
    }

    public enum MinimapPreset : byte
    {
        FollowEntity = 0,
        RtsFullMap = 1,
        FollowCamera = 2,
    }

    public readonly record struct MinimapDebugMarker(
        int StableId,
        float WorldXcm,
        float WorldYcm,
        float NormalizedX,
        float NormalizedY,
        Vector4 Color,
        float SizePx,
        float OrientationRad,
        float OrientationLengthPx,
        uint Flags);

    public sealed record MinimapDebugSnapshot(
        string MapId,
        MinimapZoomBand ZoomBand,
        MinimapPreset Preset,
        float CenterXcm,
        float CenterYcm,
        float HalfExtentCm,
        float MinWorldXcm,
        float MinWorldYcm,
        float MaxWorldXcm,
        float MaxWorldYcm,
        float CameraTargetXcm,
        float CameraTargetYcm,
        int MarkerCount,
        int VisibleMarkerCount,
        IReadOnlyList<MinimapDebugMarker> VisibleMarkers);

    public sealed class MinimapRuntime
    {
        private const float MinHalfExtentCm = 750f;
        private const float MaxHalfExtentCm = 100_000_000f;
        private const int PanelInset = 18;
        private const int PanelHeaderHeight = 44;
        private const int PanelFooterTextHeight = 36;
        private const int ZoomSliderHeight = 28;
        private const int MinFieldSize = 264;
        private const int MaxFieldSize = 660;
        private const int PanelMargin = 24;
        private const int ZoomSliderTrackHeight = 6;
        private const int ZoomSliderThumbWidth = 10;
        private const int ZoomSliderThumbHeight = 18;
        private const int ZoomSliderHitPadding = 6;
        private const int MarkerStableIdSalt = 0x4d4d;
        private const int CameraFrustumPointCapacity = 16;
        private const int CameraFrustumLineThickness = 3;
        private const int CameraFrustumShadowThickness = 5;
        private const float CameraFrustumMinScreenSize = 60f;
        private const float CameraFrustumPlaneEpsilon = -0.0001f;
        private const int CameraCenterCrossHalfExtent = 10;
        private const int CameraCenterCrossThickness = 3;
        private const float GridTargetSpacingPx = 82f;
        private const int MaxGridLinesPerAxis = 96;
        private const byte LineClipOutLeft = 1;
        private const byte LineClipOutRight = 2;
        private const byte LineClipOutBottom = 4;
        private const byte LineClipOutTop = 8;
        private const int DebugVisibleMarkerCapacity = 2048;

        private readonly MinimapRuntimeConfig _config;
        private static readonly string[] BandLabels =
        {
            "Strategic",
            "Regional",
            "Tactical",
        };

        private static readonly float[] MetricGridStepsCm =
        {
            100f,
            200f,
            500f,
            1000f,
            2000f,
            5000f,
            10000f,
            20000f,
            50000f,
            100000f,
            200000f,
            500000f,
            1000000f,
            2000000f,
            5000000f,
            10000000f,
        };

        private readonly List<MinimapDebugMarker> _debugVisibleMarkers = new(DebugVisibleMarkerCapacity);
        private readonly Vector2[] _cameraFrustumScreenPoints = new Vector2[CameraFrustumPointCapacity];
        private string _currentMapId = string.Empty;
        private string _diagnostic = string.Empty;
        private float _centerXcm;
        private float _centerYcm;
        private float _halfExtentCm = 22000f;
        private float _zoomNormalized = 1f;
        private float _minZoomHalfExtentCm = 750f;
        private float _maxZoomHalfExtentCm = 22000f;
        private bool _zoomRangeInitialized;
        private float _minWorldXcm;
        private float _minWorldYcm;
        private float _maxWorldXcm;
        private float _maxWorldYcm;
        private Vector2 _mapRight = Vector2.UnitX;
        private Vector2 _mapUp = Vector2.UnitY;
        private float _metricGridStepCm = 1000f;
        private bool _viewportInitialized;
        private int _panelX = 840;
        private int _panelY = 24;
        private int _panelWidth = 416;
        private int _panelHeight = 414;
        private int _fieldX = 858;
        private int _fieldY = 80;
        private int _fieldSize = 272;
        private int _zoomSliderX = 858;
        private int _zoomSliderY = 356;
        private int _zoomSliderWidth = 272;
        private int _markerCount;
        private int _visibleMarkerCount;
        private bool _cameraFrustumVisible;
        private int _cameraFrustumPointCount;
        private float _cameraTargetXcm;
        private float _cameraTargetYcm;
        private int _cachedFooterMarkerCount = -1;
        private int _cachedFooterVisibleMarkerCount = -1;
        private string _cachedMarkerFooter = "Markers 0/0";

        public MinimapRuntime()
            : this(null)
        {
        }

        public MinimapRuntime(MinimapRuntimeConfig? config)
        {
            _config = config ?? new MinimapRuntimeConfig();
            _config.Validate();
            _zoomNormalized = Math.Clamp(_config.InitialZoomNormalized, 0f, 1f);
        }

        public bool Visible { get; set; }

        public MinimapPreset Preset { get; private set; } = MinimapPreset.RtsFullMap;

        public Entity FollowEntity { get; private set; } = Entity.Null;

        public MinimapZoomBand ZoomBand { get; private set; } = MinimapZoomBand.Strategic;

        public bool RotateWithCamera { get; private set; }

        public string CurrentMapId => _currentMapId;

        public int MarkerCount => _markerCount;

        public int VisibleMarkerCount => _visibleMarkerCount;

        public float CenterXcm => _centerXcm;

        public float CenterYcm => _centerYcm;

        public float HalfExtentCm => _halfExtentCm;

        public float ZoomNormalized => _zoomNormalized;

        public bool ZoomSliderEnabled => _config.ZoomSliderEnabled;

        public string Diagnostic => _diagnostic;

        public int FieldX => _fieldX;

        public int FieldY => _fieldY;

        public int FieldSize => _fieldSize;

        public int ZoomSliderX => _zoomSliderX;

        public int ZoomSliderY => _zoomSliderY;

        public int ZoomSliderWidth => _zoomSliderWidth;

        public int ZoomSliderHeightPx => _config.ZoomSliderEnabled ? ZoomSliderHeight : 0;

        public float MetricGridStepCm => _metricGridStepCm;

        public void SetRotateWithCamera(bool enabled)
        {
            RotateWithCamera = enabled;
        }

        public void ToggleRotateWithCamera()
        {
            RotateWithCamera = !RotateWithCamera;
        }

        public void UseFollowEntityPreset(Entity entity, float halfExtentCm = 7000f)
        {
            Preset = MinimapPreset.FollowEntity;
            FollowEntity = entity;
            SetHalfExtentAndSyncZoom(halfExtentCm);
            _viewportInitialized = entity != Entity.Null;
        }

        public void UseRtsFullMapPreset()
        {
            Preset = MinimapPreset.RtsFullMap;
            FollowEntity = Entity.Null;
            _viewportInitialized = false;
        }

        public void UseFollowCameraPreset(float halfExtentCm = 7000f, bool rotateWithCamera = true)
        {
            Preset = MinimapPreset.FollowCamera;
            FollowEntity = Entity.Null;
            SetHalfExtentAndSyncZoom(halfExtentCm);
            RotateWithCamera = rotateWithCamera;
            _viewportInitialized = true;
        }

        public void Refresh(GameEngine engine)
        {
            MinimapMarkerBuffer markers = engine.GetService(Core.Scripting.CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MINIMAP.ERR.MarkerBufferMissing");
            MinimapScreenMarkerBuffer screenMarkers = engine.GetService(Core.Scripting.CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MINIMAP.ERR.ScreenMarkerBufferMissing");
            Refresh(engine, markers, screenMarkers);
        }

        public void Refresh(GameEngine engine, MinimapMarkerBuffer markers, MinimapScreenMarkerBuffer screenMarkers)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(markers);
            ArgumentNullException.ThrowIfNull(screenMarkers);

            RefreshPanelLayout(engine);
            screenMarkers.BeginFrame();
            _debugVisibleMarkers.Clear();
            _markerCount = markers.Count;
            _visibleMarkerCount = 0;
            _diagnostic = string.Empty;
            _currentMapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;

            if (!Visible)
            {
                ResetBounds();
                return;
            }

            WorldAabbCm bounds = ResolveRequiredWorldBounds(engine);
            _minWorldXcm = bounds.Left;
            _minWorldYcm = bounds.Top;
            _maxWorldXcm = bounds.Right;
            _maxWorldYcm = bounds.Bottom;
            ResolveZoomRange(engine, in bounds);

            ApplyPresetViewport(engine, in bounds);
            ClampViewportToBounds(in bounds);
            UpdateMapBasis(engine);
            ZoomBand = ResolveZoomBand(_halfExtentCm, in bounds);
            UpdateCameraFrustum(engine);
            ProjectMarkers(markers, screenMarkers);
        }

        public void Render(ScreenOverlayBuffer overlay)
        {
            ArgumentNullException.ThrowIfNull(overlay);
            if (!Visible)
            {
                return;
            }

            overlay.AddRect(
                _panelX,
                _panelY,
                _panelWidth,
                _panelHeight,
                new Vector4(0.02f, 0.05f, 0.07f, 0.96f),
                new Vector4(0.48f, 0.70f, 0.86f, 1f));
            overlay.AddRect(
                _fieldX,
                _fieldY,
                _fieldSize,
                _fieldSize,
                new Vector4(0.01f, 0.04f, 0.06f, 0.99f),
                new Vector4(0.42f, 0.65f, 0.80f, 1f));
            overlay.AddText(_panelX + PanelInset + 6, _panelY + 13, "Minimap", 18, new Vector4(0.98f, 0.99f, 1f, 1f));
            overlay.AddText(_panelX + _panelWidth - 118, _panelY + 14, BandLabels[(int)ZoomBand], 14, new Vector4(1f, 0.84f, 0.42f, 1f));
            RenderGrid(overlay);
            RenderCameraFrustum(overlay);
            RenderZoomSlider(overlay);

            if (!string.IsNullOrWhiteSpace(_diagnostic))
            {
                overlay.AddText(_fieldX + 16, _fieldY + 28, _diagnostic, 14, new Vector4(1f, 0.72f, 0.48f, 1f));
            }

            int footerTextY = _fieldY + _fieldSize + (_config.ZoomSliderEnabled ? ZoomSliderHeight : 0) + 21;
            overlay.AddText(_panelX + PanelInset, footerTextY, ResolveMarkerFooterText(), 13, new Vector4(0.78f, 0.86f, 0.93f, 1f));
            overlay.AddText(_panelX + _panelWidth - 146, footerTextY, ResolvePresetLabel(), 13, new Vector4(0.66f, 0.76f, 0.84f, 1f));
        }

        public bool ContainsField(Vector2 screenPosition)
        {
            return Visible &&
                screenPosition.X >= _fieldX &&
                screenPosition.Y >= _fieldY &&
                screenPosition.X <= _fieldX + _fieldSize &&
                screenPosition.Y <= _fieldY + _fieldSize;
        }

        public bool ContainsInteractiveRegion(Vector2 screenPosition)
        {
            return ContainsField(screenPosition) || ContainsZoomSlider(screenPosition);
        }

        public bool ContainsZoomSlider(Vector2 screenPosition)
        {
            return Visible &&
                _config.ZoomSliderEnabled &&
                screenPosition.X >= _zoomSliderX - ZoomSliderHitPadding &&
                screenPosition.Y >= _zoomSliderY - ZoomSliderHitPadding &&
                screenPosition.X <= _zoomSliderX + _zoomSliderWidth + ZoomSliderHitPadding &&
                screenPosition.Y <= _zoomSliderY + ZoomSliderHeight + ZoomSliderHitPadding;
        }

        public void SetZoomFromSliderPointer(Vector2 screenPosition)
        {
            if (!_config.ZoomSliderEnabled)
            {
                return;
            }

            float normalized = (screenPosition.X - _zoomSliderX) / MathF.Max(1f, _zoomSliderWidth);
            SetZoomNormalized(normalized);
        }

        public bool TryScreenToWorld(Vector2 screenPosition, out Vector2 worldCm)
        {
            worldCm = default;
            if (!ContainsField(screenPosition))
            {
                return false;
            }

            ScreenToMapLocal(screenPosition, clampToField: false, out float localXcm, out float localYcm);
            worldCm = MapLocalToWorld(localXcm, localYcm);
            return true;
        }

        public bool TryScreenToWorldClamped(Vector2 screenPosition, out Vector2 worldCm)
        {
            worldCm = default;
            if (!Visible)
            {
                return false;
            }

            ScreenToMapLocal(screenPosition, clampToField: true, out float localXcm, out float localYcm);
            worldCm = MapLocalToWorld(localXcm, localYcm);
            return true;
        }

        public void JumpCameraTo(GameEngine engine, Vector2 worldCm)
        {
            ArgumentNullException.ThrowIfNull(engine);
            WorldAabbCm bounds = ResolveRequiredWorldBounds(engine);
            var clamped = new Vector2(
                Math.Clamp(worldCm.X, bounds.Left, bounds.Right),
                Math.Clamp(worldCm.Y, bounds.Top, bounds.Bottom));
            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest { TargetCm = clamped });
            _cameraTargetXcm = clamped.X;
            _cameraTargetYcm = clamped.Y;
        }

        public void SetViewport(float centerXcm, float centerYcm, float halfExtentCm)
        {
            _centerXcm = centerXcm;
            _centerYcm = centerYcm;
            SetHalfExtentAndSyncZoom(halfExtentCm);
            _viewportInitialized = true;
        }

        public void FocusOnContent()
        {
            if (_markerCount <= 0)
            {
                return;
            }

            _centerXcm = (_minWorldXcm + _maxWorldXcm) * 0.5f;
            _centerYcm = (_minWorldYcm + _maxWorldYcm) * 0.5f;
            float spanX = MathF.Max(2400f, _maxWorldXcm - _minWorldXcm);
            float spanY = MathF.Max(2400f, _maxWorldYcm - _minWorldYcm);
            SetHalfExtentAndSyncZoom(MathF.Max(spanX, spanY) * 0.6f);
            _viewportInitialized = true;
        }

        public void CenterOnSelected(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);
            Entity selected = ResolveSelectedEntity(engine);
            if (selected == Entity.Null)
            {
                return;
            }

            if (TryResolveFollowPosition(engine, selected, out float worldXcm, out float worldYcm))
            {
                _centerXcm = worldXcm;
                _centerYcm = worldYcm;
                _viewportInitialized = true;
            }
        }

        public void ApplyWheelZoom(float wheelDelta)
        {
            ApplyWheelZoom(wheelDelta, default, useAnchor: false);
        }

        public void ApplyWheelZoom(float wheelDelta, Vector2 screenAnchor)
        {
            ApplyWheelZoom(wheelDelta, screenAnchor, useAnchor: true);
        }

        private void ApplyWheelZoom(float wheelDelta, Vector2 screenAnchor, bool useAnchor)
        {
            if (wheelDelta == 0f)
            {
                return;
            }

            Vector2 anchorWorldBefore = default;
            bool hasAnchor = false;
            if (useAnchor)
            {
                hasAnchor = TryScreenToWorld(screenAnchor, out anchorWorldBefore);
            }
            float nextZoom = _zoomNormalized - (wheelDelta * _config.WheelZoomNormalizedStep);
            SetZoomNormalized(nextZoom);
            if (hasAnchor)
            {
                ScreenToMapLocal(screenAnchor, clampToField: false, out float localXcm, out float localYcm);
                Vector2 anchorOffsetAfter = MapLocalOffsetToWorld(localXcm, localYcm);
                _centerXcm = anchorWorldBefore.X - anchorOffsetAfter.X;
                _centerYcm = anchorWorldBefore.Y - anchorOffsetAfter.Y;
            }

            _viewportInitialized = true;
        }

        public void SetZoomNormalized(float normalized)
        {
            _zoomNormalized = Math.Clamp(normalized, 0f, 1f);
            ApplyZoomNormalized(_zoomNormalized);
            _viewportInitialized = true;
        }

        public void CycleZoom(int delta)
        {
            if (delta == 0)
            {
                return;
            }

            SetZoomNormalized(_zoomNormalized + (delta * _config.ButtonZoomNormalizedStep));
        }

        public void PanNormalized(float dx, float dy)
        {
            if (dx == 0f && dy == 0f)
            {
                return;
            }

            float step = _halfExtentCm * 1.1f;
            Vector2 delta = (_mapRight * (dx * step)) + (_mapUp * (dy * step));
            _centerXcm += delta.X;
            _centerYcm += delta.Y;
            _viewportInitialized = true;
        }

        public MinimapDebugSnapshot CaptureDebugSnapshot()
        {
            return new MinimapDebugSnapshot(
                _currentMapId,
                ZoomBand,
                Preset,
                _centerXcm,
                _centerYcm,
                _halfExtentCm,
                _minWorldXcm,
                _minWorldYcm,
                _maxWorldXcm,
                _maxWorldYcm,
                _cameraTargetXcm,
                _cameraTargetYcm,
                _markerCount,
                _visibleMarkerCount,
                _debugVisibleMarkers.ToArray());
        }

        private void ApplyPresetViewport(GameEngine engine, in WorldAabbCm bounds)
        {
            if (Preset == MinimapPreset.FollowEntity &&
                FollowEntity != Entity.Null &&
                TryResolveFollowPosition(engine, FollowEntity, out float followXcm, out float followYcm))
            {
                _centerXcm = followXcm;
                _centerYcm = followYcm;
                _viewportInitialized = true;
                return;
            }

            if (Preset == MinimapPreset.FollowCamera)
            {
                Vector2 cameraTarget = engine.GameSession.Camera.State.TargetCm;
                _centerXcm = cameraTarget.X;
                _centerYcm = cameraTarget.Y;
                _viewportInitialized = true;
                return;
            }

            if (Preset == MinimapPreset.RtsFullMap)
            {
                if (!_viewportInitialized)
                {
                    _centerXcm = bounds.Left + (bounds.Width * 0.5f);
                    _centerYcm = bounds.Top + (bounds.Height * 0.5f);
                    SetZoomNormalized(1f);
                    _viewportInitialized = true;
                }

                return;
            }

            if (!_viewportInitialized)
            {
                _centerXcm = bounds.Left + (bounds.Width * 0.5f);
                _centerYcm = bounds.Top + (bounds.Height * 0.5f);
                SetZoomNormalized(1f);
                _viewportInitialized = true;
            }
        }

        private void ProjectMarkers(MinimapMarkerBuffer markers, MinimapScreenMarkerBuffer screenMarkers)
        {
            int count = markers.Count;
            for (int i = 0; i < count; i++)
            {
                if (!TryWorldToMapNormalized(
                        markers.GetWorldXcm(i),
                        markers.GetWorldZcm(i),
                        out float normalizedX,
                        out float normalizedY))
                {
                    continue;
                }

                float screenX = _fieldX + (normalizedX * (_fieldSize - 1));
                float screenY = _fieldY + ((1f - normalizedY) * (_fieldSize - 1));
                int stableId = ComposeMarkerStableId(markers.GetStableId(i));
                Vector4 color = markers.GetColor(i);
                float size = markers.GetSizePx(i);
                uint flags = markers.GetFlags(i);
                float orientationRad = 0f;
                float orientationLengthPx = 0f;
                if ((flags & MinimapMarkerFlags.HasOrientation) != 0u)
                {
                    orientationRad = ProjectOrientationToScreen(markers.GetOrientationRad(i));
                    orientationLengthPx = markers.GetOrientationLengthPx(i);
                }

                if (screenMarkers.TryAdd(stableId, screenX, screenY, in color, size, flags, orientationRad, orientationLengthPx))
                {
                    _visibleMarkerCount++;
                    if (_debugVisibleMarkers.Count < DebugVisibleMarkerCapacity)
                    {
                        _debugVisibleMarkers.Add(new MinimapDebugMarker(
                            stableId,
                            markers.GetWorldXcm(i),
                            markers.GetWorldZcm(i),
                            normalizedX,
                            normalizedY,
                            color,
                            size,
                            orientationRad,
                            orientationLengthPx,
                            flags));
                    }
                }
            }
        }

        private float ProjectOrientationToScreen(float worldOrientationRad)
        {
            Vector2 direction = new(MathF.Cos(worldOrientationRad), MathF.Sin(worldOrientationRad));
            float localRight = Vector2.Dot(direction, _mapRight);
            float localUp = Vector2.Dot(direction, _mapUp);
            return MathF.Atan2(-localUp, localRight);
        }

        private static int ComposeMarkerStableId(int stableId)
        {
            unchecked
            {
                int hash = (stableId * 397) ^ MarkerStableIdSalt;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }

        private void ClampViewportToBounds(in WorldAabbCm bounds)
        {
            float maxHalf = MathF.Max(_minZoomHalfExtentCm, _maxZoomHalfExtentCm);
            SetHalfExtentAndSyncZoom(MathF.Min(ClampHalfExtent(_halfExtentCm), maxHalf));
            float padding = _halfExtentCm * 0.35f;
            _centerXcm = Math.Clamp(_centerXcm, bounds.Left - padding, bounds.Right + padding);
            _centerYcm = Math.Clamp(_centerYcm, bounds.Top - padding, bounds.Bottom + padding);
        }

        private void UpdateMapBasis(GameEngine engine)
        {
            if (!RotateWithCamera)
            {
                _mapRight = Vector2.UnitX;
                _mapUp = Vector2.UnitY;
                return;
            }

            CameraState state = engine.GameSession.Camera.State;
            Vector2 forward = OrbitCameraDirectionUtil.ForwardFromYawDegrees(state.Yaw);
            Vector2 right = OrbitCameraDirectionUtil.RightFromYawDegrees(state.Yaw);
            _mapUp = NormalizeOrDefault(forward, Vector2.UnitY);
            _mapRight = NormalizeOrDefault(right, Vector2.UnitX);
        }

        private void UpdateCameraFrustum(GameEngine engine)
        {
            CameraState state = engine.GameSession.Camera.State;
            _cameraTargetXcm = state.TargetCm.X;
            _cameraTargetYcm = state.TargetCm.Y;
            _cameraFrustumVisible = false;
            _cameraFrustumPointCount = 0;

            int screenWidth = Math.Max(1, engine.MergedConfig?.WindowWidth ?? 1280);
            int screenHeight = Math.Max(1, engine.MergedConfig?.WindowHeight ?? 720);
            float aspect = screenWidth / (float)screenHeight;
            Vector2 resolution = new(screenWidth, screenHeight);
            var camera = CameraViewportUtil.StateToRenderState(state);

            if (!TryProjectCameraCorner(new Vector2(0f, 0f), in camera, resolution, aspect, 0) ||
                !TryProjectCameraCorner(new Vector2(screenWidth - 1f, 0f), in camera, resolution, aspect, 1) ||
                !TryProjectCameraCorner(new Vector2(screenWidth - 1f, screenHeight - 1f), in camera, resolution, aspect, 2) ||
                !TryProjectCameraCorner(new Vector2(0f, screenHeight - 1f), in camera, resolution, aspect, 3))
            {
                return;
            }

            _cameraFrustumPointCount = 4;
            EnsureCameraFrustumMinimumDisplaySize();
            _cameraFrustumVisible = true;
        }

        private static bool TryResolveFollowPosition(GameEngine engine, Entity entity, out float worldXcm, out float worldYcm)
        {
            worldXcm = 0f;
            worldYcm = 0f;
            if (!engine.World.IsAlive(entity))
            {
                return false;
            }

            if (engine.World.TryGet(entity, out PerformerWorldPosition performerPosition))
            {
                worldXcm = performerPosition.Value.X * 100f;
                worldYcm = performerPosition.Value.Z * 100f;
                return true;
            }

            return false;
        }

        private static Entity ResolveSelectedEntity(GameEngine engine)
        {
            return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected)
                ? selected
                : Entity.Null;
        }

        private static WorldAabbCm ResolveRequiredWorldBounds(GameEngine engine)
        {
            WorldAabbCm bounds = engine.WorldSizeSpec.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("MINIMAP.ERR.BoundsMissing WorldSizeSpec.Bounds must provide positive board/world bounds.");
            }

            return bounds;
        }

        private void RenderGrid(ScreenOverlayBuffer overlay)
        {
            _metricGridStepCm = ResolveMetricGridStepCm();
            ResolveViewportWorldAabb(out float minX, out float minY, out float maxX, out float maxY);
            float step = _metricGridStepCm;
            Vector4 minor = new(0.24f, 0.39f, 0.49f, 0.70f);
            Vector4 major = new(0.48f, 0.66f, 0.78f, 0.88f);

            float startX = MathF.Ceiling(minX / step) * step;
            int lineCount = 0;
            for (float x = startX; x <= maxX && lineCount < MaxGridLinesPerAxis; x += step, lineCount++)
            {
                Vector4 color = IsMajorGridLine(x, step) ? major : minor;
                int thickness = IsMajorGridLine(x, step) ? 2 : 1;
                AddWorldLineClipped(overlay, x, minY, x, maxY, thickness, color);
            }

            float startY = MathF.Ceiling(minY / step) * step;
            lineCount = 0;
            for (float y = startY; y <= maxY && lineCount < MaxGridLinesPerAxis; y += step, lineCount++)
            {
                Vector4 color = IsMajorGridLine(y, step) ? major : minor;
                int thickness = IsMajorGridLine(y, step) ? 2 : 1;
                AddWorldLineClipped(overlay, minX, y, maxX, y, thickness, color);
            }
        }

        private void RenderZoomSlider(ScreenOverlayBuffer overlay)
        {
            if (!_config.ZoomSliderEnabled)
            {
                return;
            }

            int trackX = _zoomSliderX;
            int trackY = _zoomSliderY + ((ZoomSliderHeight - ZoomSliderTrackHeight) / 2);
            int thumbCenterX = trackX + (int)MathF.Round(_zoomNormalized * MathF.Max(1, _zoomSliderWidth));
            int thumbX = thumbCenterX - (ZoomSliderThumbWidth / 2);
            int thumbY = _zoomSliderY + ((ZoomSliderHeight - ZoomSliderThumbHeight) / 2);
            Vector4 track = new(0.15f, 0.25f, 0.31f, 0.95f);
            Vector4 fill = new(0.93f, 0.72f, 0.28f, 0.98f);
            Vector4 thumb = new(1f, 0.93f, 0.62f, 1f);
            Vector4 border = new(0.48f, 0.70f, 0.86f, 1f);
            overlay.AddRect(trackX, trackY, _zoomSliderWidth, ZoomSliderTrackHeight, track, border);
            int fillWidth = Math.Max(0, thumbCenterX - trackX);
            if (fillWidth > 0)
            {
                overlay.AddRect(trackX, trackY, fillWidth, ZoomSliderTrackHeight, fill, fill);
            }

            overlay.AddRect(thumbX, thumbY, ZoomSliderThumbWidth, ZoomSliderThumbHeight, thumb, border);
        }

        private float ResolveMetricGridStepCm()
        {
            float targetCm = MathF.Max(1f, (GridTargetSpacingPx / MathF.Max(1f, _fieldSize - 1)) * (_halfExtentCm * 2f));
            for (int i = 0; i < MetricGridStepsCm.Length; i++)
            {
                if (MetricGridStepsCm[i] >= targetCm)
                {
                    return MetricGridStepsCm[i];
                }
            }

            return MetricGridStepsCm[^1];
        }

        private static bool IsMajorGridLine(float value, float step)
        {
            float majorStep = step * 5f;
            if (majorStep <= 0f)
            {
                return false;
            }

            float nearest = MathF.Round(value / majorStep) * majorStep;
            return MathF.Abs(value - nearest) <= MathF.Max(1f, step * 0.001f);
        }

        private void ResolveViewportWorldAabb(out float minX, out float minY, out float maxX, out float maxY)
        {
            Vector2 c0 = MapLocalToWorld(-_halfExtentCm, -_halfExtentCm);
            Vector2 c1 = MapLocalToWorld(_halfExtentCm, -_halfExtentCm);
            Vector2 c2 = MapLocalToWorld(_halfExtentCm, _halfExtentCm);
            Vector2 c3 = MapLocalToWorld(-_halfExtentCm, _halfExtentCm);

            minX = MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(c2.X, c3.X));
            minY = MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(c2.Y, c3.Y));
            maxX = MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(c2.X, c3.X));
            maxY = MathF.Max(MathF.Max(c0.Y, c1.Y), MathF.Max(c2.Y, c3.Y));
        }

        private void AddWorldLineClipped(
            ScreenOverlayBuffer overlay,
            float worldX0,
            float worldY0,
            float worldX1,
            float worldY1,
            int thickness,
            Vector4 color)
        {
            ProjectWorldToScreenUnclipped(worldX0, worldY0, out float x0, out float y0);
            ProjectWorldToScreenUnclipped(worldX1, worldY1, out float x1, out float y1);
            if (!TryClipScreenLineToField(ref x0, ref y0, ref x1, ref y1))
            {
                return;
            }

            overlay.AddLine(
                (int)MathF.Round(x0),
                (int)MathF.Round(y0),
                (int)MathF.Round(x1),
                (int)MathF.Round(y1),
                thickness,
                color);
        }

        private bool TryClipScreenLineToField(ref float x0, ref float y0, ref float x1, ref float y1)
        {
            float minX = _fieldX;
            float minY = _fieldY;
            float maxX = _fieldX + _fieldSize - 1;
            float maxY = _fieldY + _fieldSize - 1;

            byte code0 = ComputeLineOutCode(x0, y0, minX, minY, maxX, maxY);
            byte code1 = ComputeLineOutCode(x1, y1, minX, minY, maxX, maxY);

            while (true)
            {
                if ((code0 | code1) == 0)
                {
                    return true;
                }

                if ((code0 & code1) != 0)
                {
                    return false;
                }

                byte outCode = code0 != 0 ? code0 : code1;
                float x;
                float y;

                if ((outCode & LineClipOutTop) != 0)
                {
                    if (MathF.Abs(y1 - y0) <= 0.0001f)
                    {
                        return false;
                    }

                    x = x0 + ((x1 - x0) * (minY - y0) / (y1 - y0));
                    y = minY;
                }
                else if ((outCode & LineClipOutBottom) != 0)
                {
                    if (MathF.Abs(y1 - y0) <= 0.0001f)
                    {
                        return false;
                    }

                    x = x0 + ((x1 - x0) * (maxY - y0) / (y1 - y0));
                    y = maxY;
                }
                else if ((outCode & LineClipOutRight) != 0)
                {
                    if (MathF.Abs(x1 - x0) <= 0.0001f)
                    {
                        return false;
                    }

                    y = y0 + ((y1 - y0) * (maxX - x0) / (x1 - x0));
                    x = maxX;
                }
                else
                {
                    if (MathF.Abs(x1 - x0) <= 0.0001f)
                    {
                        return false;
                    }

                    y = y0 + ((y1 - y0) * (minX - x0) / (x1 - x0));
                    x = minX;
                }

                if (outCode == code0)
                {
                    x0 = x;
                    y0 = y;
                    code0 = ComputeLineOutCode(x0, y0, minX, minY, maxX, maxY);
                }
                else
                {
                    x1 = x;
                    y1 = y;
                    code1 = ComputeLineOutCode(x1, y1, minX, minY, maxX, maxY);
                }
            }
        }

        private static byte ComputeLineOutCode(float x, float y, float minX, float minY, float maxX, float maxY)
        {
            byte code = 0;
            if (x < minX)
            {
                code |= LineClipOutLeft;
            }
            else if (x > maxX)
            {
                code |= LineClipOutRight;
            }

            if (y < minY)
            {
                code |= LineClipOutTop;
            }
            else if (y > maxY)
            {
                code |= LineClipOutBottom;
            }

            return code;
        }

        private void RenderCameraFrustum(ScreenOverlayBuffer overlay)
        {
            if (!_cameraFrustumVisible || _cameraFrustumPointCount < 4)
            {
                return;
            }

            Vector4 shadow = new(0f, 0f, 0f, 0.92f);
            Vector4 frustumColor = new(1f, 0.86f, 0.30f, 0.98f);
            for (int i = 0; i < _cameraFrustumPointCount; i++)
            {
                Vector2 a = _cameraFrustumScreenPoints[i];
                Vector2 b = _cameraFrustumScreenPoints[(i + 1) % _cameraFrustumPointCount];
                AddScreenLine(overlay, a, b, CameraFrustumShadowThickness, shadow);
                AddScreenLine(overlay, a, b, CameraFrustumLineThickness, frustumColor);
            }

            if (TryWorldToScreen(_cameraTargetXcm, _cameraTargetYcm, out float targetX, out float targetY))
            {
                Vector4 targetColor = new(1f, 0.95f, 0.55f, 1f);
                int cx = (int)MathF.Round(targetX);
                int cy = (int)MathF.Round(targetY);
                overlay.AddRect(cx - CameraCenterCrossHalfExtent - 1, cy - 2, (CameraCenterCrossHalfExtent * 2) + 2, CameraCenterCrossThickness + 2, Vector4.Zero, shadow);
                overlay.AddRect(cx - 2, cy - CameraCenterCrossHalfExtent - 1, CameraCenterCrossThickness + 2, (CameraCenterCrossHalfExtent * 2) + 2, Vector4.Zero, shadow);
                overlay.AddRect(cx - CameraCenterCrossHalfExtent, cy - 1, CameraCenterCrossHalfExtent * 2, CameraCenterCrossThickness, Vector4.Zero, targetColor);
                overlay.AddRect(cx - 1, cy - CameraCenterCrossHalfExtent, CameraCenterCrossThickness, CameraCenterCrossHalfExtent * 2, Vector4.Zero, targetColor);
            }
        }

        private static void AddScreenLine(ScreenOverlayBuffer overlay, Vector2 a, Vector2 b, int thickness, Vector4 color)
        {
            overlay.AddLine(
                (int)MathF.Round(a.X),
                (int)MathF.Round(a.Y),
                (int)MathF.Round(b.X),
                (int)MathF.Round(b.Y),
                thickness,
                color);
        }

        private bool TryProjectCameraCorner(
            Vector2 screenCorner,
            in Ludots.Core.Presentation.Camera.CameraRenderState3D camera,
            Vector2 resolution,
            float aspect,
            int index)
        {
            ScreenRay ray = CameraViewportUtil.ScreenToRay(screenCorner, in camera, resolution, aspect);
            if (!TryIntersectGroundPlane(in ray, out Vector2 worldCm))
            {
                return false;
            }

            ProjectWorldToScreenClamped(worldCm.X, worldCm.Y, out float screenX, out float screenY);
            _cameraFrustumScreenPoints[index] = new Vector2(screenX, screenY);
            return true;
        }

        private void EnsureCameraFrustumMinimumDisplaySize()
        {
            if (_cameraFrustumPointCount < 4)
            {
                return;
            }

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < _cameraFrustumPointCount; i++)
            {
                Vector2 point = _cameraFrustumScreenPoints[i];
                minX = MathF.Min(minX, point.X);
                minY = MathF.Min(minY, point.Y);
                maxX = MathF.Max(maxX, point.X);
                maxY = MathF.Max(maxY, point.Y);
            }

            float extent = MathF.Max(maxX - minX, maxY - minY);
            if (!float.IsFinite(extent) || extent >= CameraFrustumMinScreenSize || extent <= 0.001f)
            {
                return;
            }

            float anchorX = (minX + maxX) * 0.5f;
            float anchorY = (minY + maxY) * 0.5f;
            if (TryWorldToScreen(_cameraTargetXcm, _cameraTargetYcm, out float targetX, out float targetY))
            {
                anchorX = targetX;
                anchorY = targetY;
            }

            float scale = CameraFrustumMinScreenSize / extent;
            for (int i = 0; i < _cameraFrustumPointCount; i++)
            {
                Vector2 point = _cameraFrustumScreenPoints[i];
                float x = anchorX + ((point.X - anchorX) * scale);
                float y = anchorY + ((point.Y - anchorY) * scale);
                _cameraFrustumScreenPoints[i] = new Vector2(
                    Math.Clamp(x, _fieldX, _fieldX + _fieldSize - 1),
                    Math.Clamp(y, _fieldY, _fieldY + _fieldSize - 1));
            }
        }

        private static bool TryIntersectGroundPlane(in ScreenRay ray, out Vector2 worldCm)
        {
            worldCm = default;
            if (!float.IsFinite(ray.Origin.X) ||
                !float.IsFinite(ray.Origin.Y) ||
                !float.IsFinite(ray.Origin.Z) ||
                !float.IsFinite(ray.Direction.X) ||
                !float.IsFinite(ray.Direction.Y) ||
                !float.IsFinite(ray.Direction.Z) ||
                ray.Direction.Y >= CameraFrustumPlaneEpsilon)
            {
                return false;
            }

            float t = -ray.Origin.Y / ray.Direction.Y;
            if (!float.IsFinite(t) || t <= 0f)
            {
                return false;
            }

            Vector3 hit = ray.Origin + (ray.Direction * t);
            worldCm = new Vector2(hit.X * 100f, hit.Z * 100f);
            return float.IsFinite(worldCm.X) && float.IsFinite(worldCm.Y);
        }

        private bool TryWorldToScreen(float worldXcm, float worldYcm, out float screenX, out float screenY)
        {
            if (!TryWorldToMapNormalized(worldXcm, worldYcm, out float normalizedX, out float normalizedY))
            {
                screenX = 0f;
                screenY = 0f;
                return false;
            }

            screenX = _fieldX + (normalizedX * (_fieldSize - 1));
            screenY = _fieldY + ((1f - normalizedY) * (_fieldSize - 1));
            return true;
        }

        private bool TryWorldToMapNormalized(float worldXcm, float worldYcm, out float normalizedX, out float normalizedY)
        {
            WorldToMapNormalizedUnclipped(worldXcm, worldYcm, out normalizedX, out normalizedY);
            return normalizedX >= 0f && normalizedX <= 1f && normalizedY >= 0f && normalizedY <= 1f;
        }

        private void WorldToMapNormalizedUnclipped(float worldXcm, float worldYcm, out float normalizedX, out float normalizedY)
        {
            Vector2 delta = new(worldXcm - _centerXcm, worldYcm - _centerYcm);
            float localXcm = Vector2.Dot(delta, _mapRight);
            float localYcm = Vector2.Dot(delta, _mapUp);
            float invExtent = 1f / MathF.Max(1f, _halfExtentCm * 2f);
            normalizedX = (localXcm + _halfExtentCm) * invExtent;
            normalizedY = (localYcm + _halfExtentCm) * invExtent;
        }

        private void ScreenToMapLocal(Vector2 screenPosition, bool clampToField, out float localXcm, out float localYcm)
        {
            float normalizedX = (screenPosition.X - _fieldX) / MathF.Max(1f, _fieldSize - 1);
            float normalizedY = 1f - ((screenPosition.Y - _fieldY) / MathF.Max(1f, _fieldSize - 1));
            if (clampToField)
            {
                normalizedX = Math.Clamp(normalizedX, 0f, 1f);
                normalizedY = Math.Clamp(normalizedY, 0f, 1f);
            }

            localXcm = (normalizedX * _halfExtentCm * 2f) - _halfExtentCm;
            localYcm = (normalizedY * _halfExtentCm * 2f) - _halfExtentCm;
        }

        private Vector2 MapLocalToWorld(float localXcm, float localYcm)
        {
            Vector2 offset = MapLocalOffsetToWorld(localXcm, localYcm);
            return new Vector2(_centerXcm + offset.X, _centerYcm + offset.Y);
        }

        private Vector2 MapLocalOffsetToWorld(float localXcm, float localYcm)
        {
            return (_mapRight * localXcm) + (_mapUp * localYcm);
        }

        private static Vector2 NormalizeOrDefault(Vector2 value, Vector2 defaultValue)
        {
            float lengthSquared = value.LengthSquared();
            if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
            {
                return defaultValue;
            }

            return value / MathF.Sqrt(lengthSquared);
        }

        private void ProjectWorldToScreenClamped(float worldXcm, float worldYcm, out float screenX, out float screenY)
        {
            WorldToMapNormalizedUnclipped(worldXcm, worldYcm, out float normalizedX, out float normalizedY);
            normalizedX = Math.Clamp(normalizedX, 0f, 1f);
            normalizedY = Math.Clamp(normalizedY, 0f, 1f);
            screenX = _fieldX + (normalizedX * (_fieldSize - 1));
            screenY = _fieldY + ((1f - normalizedY) * (_fieldSize - 1));
        }

        private void ProjectWorldToScreenUnclipped(float worldXcm, float worldYcm, out float screenX, out float screenY)
        {
            WorldToMapNormalizedUnclipped(worldXcm, worldYcm, out float normalizedX, out float normalizedY);
            screenX = _fieldX + (normalizedX * (_fieldSize - 1));
            screenY = _fieldY + ((1f - normalizedY) * (_fieldSize - 1));
        }

        private void RefreshPanelLayout(GameEngine engine)
        {
            int screenWidth = engine.MergedConfig?.WindowWidth > 0 ? engine.MergedConfig.WindowWidth : 1280;
            int screenHeight = engine.MergedConfig?.WindowHeight > 0 ? engine.MergedConfig.WindowHeight : 720;
            _fieldSize = ResolveFieldSize(screenWidth, screenHeight, _config.ZoomSliderEnabled);
            _panelWidth = _fieldSize + (PanelInset * 2);
            _panelHeight = PanelHeaderHeight + _fieldSize + PanelFooterTextHeight + (_config.ZoomSliderEnabled ? ZoomSliderHeight : 0);
            _panelX = Math.Max(PanelMargin, screenWidth - _panelWidth - PanelMargin);
            _panelY = Math.Max(PanelMargin, Math.Min(PanelMargin, screenHeight - _panelHeight - PanelMargin));
            _fieldX = _panelX + PanelInset;
            _fieldY = _panelY + PanelHeaderHeight;
            _zoomSliderX = _fieldX;
            _zoomSliderY = _fieldY + _fieldSize + 4;
            _zoomSliderWidth = _fieldSize;
        }

        private static int ResolveFieldSize(int screenWidth, int screenHeight, bool zoomSliderEnabled)
        {
            int widthLimit = Math.Max(120, screenWidth - (PanelMargin * 2) - (PanelInset * 2));
            int heightLimit = Math.Max(
                120,
                screenHeight - (PanelMargin * 2) - PanelHeaderHeight - PanelFooterTextHeight - (zoomSliderEnabled ? ZoomSliderHeight : 0));
            int available = Math.Min(widthLimit, heightLimit);
            int desired = Math.Min(MaxFieldSize, available);
            if (desired >= MinFieldSize)
            {
                desired = Math.Max(MinFieldSize, desired);
            }

            return Math.Max(120, desired);
        }

        private static MinimapZoomBand ResolveZoomBand(float halfExtentCm, in WorldAabbCm bounds)
        {
            float worldHalf = MathF.Max(bounds.Width, bounds.Height) * 0.5f;
            if (halfExtentCm >= worldHalf * 0.75f)
            {
                return MinimapZoomBand.Strategic;
            }

            return halfExtentCm >= worldHalf * 0.25f
                ? MinimapZoomBand.Regional
                : MinimapZoomBand.Tactical;
        }

        private string ResolvePresetLabel()
        {
            return Preset switch
            {
                MinimapPreset.RtsFullMap => RotateWithCamera ? "RTS Rot" : "RTS North",
                MinimapPreset.FollowCamera => RotateWithCamera ? "Camera Rot" : "Camera North",
                _ => RotateWithCamera ? "Follow Rot" : "Follow North",
            };
        }

        private string ResolveMarkerFooterText()
        {
            if (_cachedFooterMarkerCount != _markerCount ||
                _cachedFooterVisibleMarkerCount != _visibleMarkerCount)
            {
                _cachedFooterMarkerCount = _markerCount;
                _cachedFooterVisibleMarkerCount = _visibleMarkerCount;
                _cachedMarkerFooter = string.Concat("Markers ", _visibleMarkerCount.ToString(), "/", _markerCount.ToString());
            }

            return _cachedMarkerFooter;
        }

        private static float ClampHalfExtent(float halfExtentCm)
        {
            return Math.Clamp(halfExtentCm, MinHalfExtentCm, MaxHalfExtentCm);
        }

        private void ResolveZoomRange(GameEngine engine, in WorldAabbCm bounds)
        {
            float minHalfExtent = ResolveConfiguredHalfExtent(engine, in bounds, _config.MinZoomExtentMode, _config.MinZoomExplicitHalfExtentCm);
            float maxHalfExtent = ResolveConfiguredHalfExtent(engine, in bounds, _config.MaxZoomExtentMode, _config.MaxZoomExplicitHalfExtentCm);
            minHalfExtent = ClampHalfExtent(minHalfExtent);
            maxHalfExtent = ClampHalfExtent(maxHalfExtent);
            if (minHalfExtent > maxHalfExtent)
            {
                throw new InvalidOperationException(
                    $"MINIMAP.ERR.InvalidZoomRange minHalfExtentCm={minHalfExtent} must be <= maxHalfExtentCm={maxHalfExtent}.");
            }

            bool changed =
                !_zoomRangeInitialized ||
                MathF.Abs(_minZoomHalfExtentCm - minHalfExtent) > 0.001f ||
                MathF.Abs(_maxZoomHalfExtentCm - maxHalfExtent) > 0.001f;
            _minZoomHalfExtentCm = minHalfExtent;
            _maxZoomHalfExtentCm = maxHalfExtent;
            _zoomRangeInitialized = true;
            if (_viewportInitialized)
            {
                SetHalfExtentAndSyncZoom(_halfExtentCm);
                return;
            }

            if (!changed)
            {
                return;
            }

            _zoomNormalized = Math.Clamp(_config.InitialZoomNormalized, 0f, 1f);
            ApplyZoomNormalized(_zoomNormalized);
        }

        private static float ResolveConfiguredHalfExtent(
            GameEngine engine,
            in WorldAabbCm bounds,
            MinimapZoomExtentMode mode,
            float explicitHalfExtentCm)
        {
            return mode switch
            {
                MinimapZoomExtentMode.OneChunk => ResolvePrimaryBoardChunkExtentCm(engine),
                MinimapZoomExtentMode.FullMap => MathF.Max(bounds.Width, bounds.Height) * 0.52f,
                MinimapZoomExtentMode.ExplicitCm => explicitHalfExtentCm,
                _ => throw new InvalidOperationException($"MINIMAP.ERR.InvalidZoomExtentMode {mode}."),
            };
        }

        private static float ResolvePrimaryBoardChunkExtentCm(GameEngine engine)
        {
            if (engine.CurrentMapSession?.MapConfig?.Boards == null ||
                engine.CurrentMapSession.MapConfig.Boards.Count == 0)
            {
                throw new InvalidOperationException("MINIMAP.ERR.ChunkZoomRequiresBoardConfig");
            }

            var boards = engine.CurrentMapSession.MapConfig.Boards;
            var board = boards.Count == 1 ? boards[0] : null;
            for (int i = 0; i < boards.Count; i++)
            {
                if (string.Equals(boards[i]?.Name, "default", StringComparison.OrdinalIgnoreCase))
                {
                    board = boards[i];
                    break;
                }
            }

            if (board == null)
            {
                throw new InvalidOperationException("MINIMAP.ERR.ChunkZoomRequiresDefaultBoardConfig");
            }

            if (board.ChunkSizeCells <= 0 || board.GridCellSizeCm <= 0)
            {
                throw new InvalidOperationException("MINIMAP.ERR.ChunkZoomInvalidBoardConfig");
            }

            return board.ChunkSizeCells * board.GridCellSizeCm * 0.5f;
        }

        private void ApplyZoomNormalized(float normalized)
        {
            float t = Math.Clamp(normalized, 0f, 1f);
            _halfExtentCm = InterpolateZoomHalfExtent(t);
        }

        private void SetHalfExtentAndSyncZoom(float halfExtentCm)
        {
            float clamped = ClampHalfExtent(halfExtentCm);
            if (!_zoomRangeInitialized)
            {
                _halfExtentCm = clamped;
                return;
            }

            clamped = Math.Clamp(clamped, _minZoomHalfExtentCm, _maxZoomHalfExtentCm);
            _halfExtentCm = clamped;
            _zoomNormalized = SolveZoomNormalized(clamped);
        }

        private float InterpolateZoomHalfExtent(float normalized)
        {
            float min = MathF.Max(1f, _minZoomHalfExtentCm);
            float max = MathF.Max(min, _maxZoomHalfExtentCm);
            if (max - min <= 0.001f)
            {
                return ClampHalfExtent(min);
            }

            float ratio = max / min;
            return ClampHalfExtent(min * MathF.Pow(ratio, normalized));
        }

        private float SolveZoomNormalized(float halfExtentCm)
        {
            float min = MathF.Max(1f, _minZoomHalfExtentCm);
            float max = MathF.Max(min, _maxZoomHalfExtentCm);
            if (max - min <= 0.001f)
            {
                return 0f;
            }

            float clamped = Math.Clamp(halfExtentCm, min, max);
            return Math.Clamp(MathF.Log(clamped / min) / MathF.Log(max / min), 0f, 1f);
        }

        private void ResetBounds()
        {
            _minWorldXcm = 0f;
            _minWorldYcm = 0f;
            _maxWorldXcm = 0f;
            _maxWorldYcm = 0f;
        }
    }
}
