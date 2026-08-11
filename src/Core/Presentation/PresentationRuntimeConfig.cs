using System;

namespace Ludots.Core.Presentation
{
    /// <summary>
    /// Engine-level presentation runtime capacity knobs merged from game.json.
    /// LudotsCoreMod owns the shared baseline values; feature mods override only the values they intentionally change.
    /// </summary>
    public sealed class PresentationRuntimeConfig
    {
        private int? _presenterInstanceCapacity;
        private int? _gasPresentationEventCapacity;
        private int? _presentationEventStreamCapacity;
        private int? _presentationOwnerChangeCapacity;
        private int? _presenterCommandCapacity;
        private int? _primitiveDrawBufferCapacity;
        private int? _visualSnapshotBufferCapacity;
        private int? _visualProxyBufferCapacity;
        private int? _skinnedVisualBatchCapacity;
        private int? _presentationRequestCapacity;
        private int? _globalFieldVisualRecordCapacity;
        private int? _globalFieldVisualCellCapacity;
        private int? _globalFieldVisualDirtyRectCapacity;
        private int? _groundOverlayCapacity;
        private int? _roadSplineCapacity;
        private int? _worldHudCapacity;
        private int? _screenHudCapacity;
        private int? _minimapMarkerCapacity;
        private int? _runtimeEntitySpawnQueueCapacity;
        private int? _runtimeEntitySpawnReceiptQueueCapacity;
        private int? _runtimeEntityLifecycleQueueCapacity;
        private int? _runtimeEntityLifecycleReceiptQueueCapacity;
        private CameraCullingRuntimeConfig? _cameraCulling;
        private MinimapRuntimeConfig? _minimap;

        public int PresenterInstanceCapacity { get => _presenterInstanceCapacity ?? 0; set => _presenterInstanceCapacity = value; }
        public int GasPresentationEventCapacity { get => _gasPresentationEventCapacity ?? 0; set => _gasPresentationEventCapacity = value; }
        public int PresentationEventStreamCapacity { get => _presentationEventStreamCapacity ?? 0; set => _presentationEventStreamCapacity = value; }
        public int PresentationOwnerChangeCapacity { get => _presentationOwnerChangeCapacity ?? 0; set => _presentationOwnerChangeCapacity = value; }
        public int PresenterCommandCapacity { get => _presenterCommandCapacity ?? 0; set => _presenterCommandCapacity = value; }
        public int PrimitiveDrawBufferCapacity { get => _primitiveDrawBufferCapacity ?? 0; set => _primitiveDrawBufferCapacity = value; }
        public int VisualSnapshotBufferCapacity { get => _visualSnapshotBufferCapacity ?? 0; set => _visualSnapshotBufferCapacity = value; }
        public int VisualProxyBufferCapacity { get => _visualProxyBufferCapacity ?? 0; set => _visualProxyBufferCapacity = value; }
        public int SkinnedVisualBatchCapacity { get => _skinnedVisualBatchCapacity ?? 0; set => _skinnedVisualBatchCapacity = value; }
        public int PresentationRequestCapacity { get => _presentationRequestCapacity ?? 0; set => _presentationRequestCapacity = value; }
        public int GlobalFieldVisualRecordCapacity { get => _globalFieldVisualRecordCapacity ?? 0; set => _globalFieldVisualRecordCapacity = value; }
        public int GlobalFieldVisualCellCapacity { get => _globalFieldVisualCellCapacity ?? 0; set => _globalFieldVisualCellCapacity = value; }
        public int GlobalFieldVisualDirtyRectCapacity { get => _globalFieldVisualDirtyRectCapacity ?? 0; set => _globalFieldVisualDirtyRectCapacity = value; }
        public int GroundOverlayCapacity { get => _groundOverlayCapacity ?? 0; set => _groundOverlayCapacity = value; }
        public int RoadSplineCapacity { get => _roadSplineCapacity ?? 0; set => _roadSplineCapacity = value; }
        public int WorldHudCapacity { get => _worldHudCapacity ?? 0; set => _worldHudCapacity = value; }
        public int ScreenHudCapacity { get => _screenHudCapacity ?? 0; set => _screenHudCapacity = value; }
        public int MinimapMarkerCapacity { get => _minimapMarkerCapacity ?? 0; set => _minimapMarkerCapacity = value; }
        public int RuntimeEntitySpawnQueueCapacity { get => _runtimeEntitySpawnQueueCapacity ?? 0; set => _runtimeEntitySpawnQueueCapacity = value; }
        public int RuntimeEntitySpawnReceiptQueueCapacity { get => _runtimeEntitySpawnReceiptQueueCapacity ?? 0; set => _runtimeEntitySpawnReceiptQueueCapacity = value; }
        public int RuntimeEntityLifecycleQueueCapacity { get => _runtimeEntityLifecycleQueueCapacity ?? 0; set => _runtimeEntityLifecycleQueueCapacity = value; }
        public int RuntimeEntityLifecycleReceiptQueueCapacity { get => _runtimeEntityLifecycleReceiptQueueCapacity ?? 0; set => _runtimeEntityLifecycleReceiptQueueCapacity = value; }

        public CameraCullingRuntimeConfig CameraCulling
        {
            get => _cameraCulling ?? throw new InvalidOperationException("presentation.cameraCulling must be explicitly configured.");
            set => _cameraCulling = value;
        }

        public MinimapRuntimeConfig Minimap
        {
            get => _minimap ?? throw new InvalidOperationException("presentation.minimap must be explicitly configured.");
            set => _minimap = value;
        }

        public void Validate()
        {
            RequirePositive(_presenterInstanceCapacity, "presentation.presenterInstanceCapacity");
            RequirePositive(_gasPresentationEventCapacity, "presentation.gasPresentationEventCapacity");
            RequirePositive(_presentationEventStreamCapacity, "presentation.presentationEventStreamCapacity");
            RequirePositive(_presentationOwnerChangeCapacity, "presentation.presentationOwnerChangeCapacity");
            RequirePositive(_presenterCommandCapacity, "presentation.presenterCommandCapacity");
            RequirePositive(_primitiveDrawBufferCapacity, "presentation.primitiveDrawBufferCapacity");
            RequirePositive(_visualSnapshotBufferCapacity, "presentation.visualSnapshotBufferCapacity");
            RequirePositive(_visualProxyBufferCapacity, "presentation.visualProxyBufferCapacity");
            RequirePositive(_skinnedVisualBatchCapacity, "presentation.skinnedVisualBatchCapacity");
            RequirePositive(_presentationRequestCapacity, "presentation.presentationRequestCapacity");
            RequirePositive(_globalFieldVisualRecordCapacity, "presentation.globalFieldVisualRecordCapacity");
            RequirePositive(_globalFieldVisualCellCapacity, "presentation.globalFieldVisualCellCapacity");
            RequirePositive(_globalFieldVisualDirtyRectCapacity, "presentation.globalFieldVisualDirtyRectCapacity");
            RequirePositive(_groundOverlayCapacity, "presentation.groundOverlayCapacity");
            RequirePositive(_roadSplineCapacity, "presentation.roadSplineCapacity");
            RequirePositive(_worldHudCapacity, "presentation.worldHudCapacity");
            RequirePositive(_screenHudCapacity, "presentation.screenHudCapacity");
            RequirePositive(_minimapMarkerCapacity, "presentation.minimapMarkerCapacity");
            RequirePositive(_runtimeEntitySpawnQueueCapacity, "presentation.runtimeEntitySpawnQueueCapacity");
            RequirePositive(_runtimeEntitySpawnReceiptQueueCapacity, "presentation.runtimeEntitySpawnReceiptQueueCapacity");
            RequirePositive(_runtimeEntityLifecycleQueueCapacity, "presentation.runtimeEntityLifecycleQueueCapacity");
            RequirePositive(_runtimeEntityLifecycleReceiptQueueCapacity, "presentation.runtimeEntityLifecycleReceiptQueueCapacity");

            if (_cameraCulling == null)
            {
                throw new InvalidOperationException("presentation.cameraCulling must be explicitly configured.");
            }

            if (_minimap == null)
            {
                throw new InvalidOperationException("presentation.minimap must be explicitly configured.");
            }

            _cameraCulling.Validate();
            _minimap.Validate();
        }

        internal static int RequirePositive(int? value, string path)
        {
            if (!value.HasValue || value.Value <= 0)
            {
                throw new InvalidOperationException($"{path} must be explicitly configured as > 0.");
            }

            return value.Value;
        }

        internal static float RequireFinite(float? value, string path)
        {
            if (!value.HasValue || !float.IsFinite(value.Value))
            {
                throw new InvalidOperationException($"{path} must be explicitly configured as a finite value.");
            }

            return value.Value;
        }

        internal static bool RequireBool(bool? value, string path)
        {
            if (!value.HasValue)
            {
                throw new InvalidOperationException($"{path} must be explicitly configured.");
            }

            return value.Value;
        }
    }

    public enum MinimapZoomExtentMode
    {
        OneChunk = 0,
        FullMap = 1,
        ExplicitCm = 2,
    }

    public sealed class MinimapRuntimeConfig
    {
        private float? _initialZoomNormalized;
        private float? _wheelZoomNormalizedStep;
        private float? _buttonZoomNormalizedStep;
        private bool? _zoomSliderEnabled;
        private bool? _modeToggleEnabled;
        private bool? _rotateToggleEnabled;
        private int? _debugMarkerSampleCapacity;
        private MinimapZoomExtentMode? _minZoomExtentMode;
        private MinimapZoomExtentMode? _maxZoomExtentMode;
        private float? _minZoomExplicitHalfExtentCm;
        private float? _maxZoomExplicitHalfExtentCm;

        public float InitialZoomNormalized { get => _initialZoomNormalized ?? 0f; set => _initialZoomNormalized = value; }
        public float WheelZoomNormalizedStep { get => _wheelZoomNormalizedStep ?? 0f; set => _wheelZoomNormalizedStep = value; }
        public float ButtonZoomNormalizedStep { get => _buttonZoomNormalizedStep ?? 0f; set => _buttonZoomNormalizedStep = value; }
        public bool ZoomSliderEnabled { get => _zoomSliderEnabled ?? false; set => _zoomSliderEnabled = value; }
        public bool ModeToggleEnabled { get => _modeToggleEnabled ?? false; set => _modeToggleEnabled = value; }
        public bool RotateToggleEnabled { get => _rotateToggleEnabled ?? false; set => _rotateToggleEnabled = value; }
        public int DebugMarkerSampleCapacity { get => _debugMarkerSampleCapacity ?? 0; set => _debugMarkerSampleCapacity = value; }
        public MinimapZoomExtentMode MinZoomExtentMode { get => _minZoomExtentMode ?? MinimapZoomExtentMode.OneChunk; set => _minZoomExtentMode = value; }
        public MinimapZoomExtentMode MaxZoomExtentMode { get => _maxZoomExtentMode ?? MinimapZoomExtentMode.FullMap; set => _maxZoomExtentMode = value; }
        public float MinZoomExplicitHalfExtentCm { get => _minZoomExplicitHalfExtentCm ?? 0f; set => _minZoomExplicitHalfExtentCm = value; }
        public float MaxZoomExplicitHalfExtentCm { get => _maxZoomExplicitHalfExtentCm ?? 0f; set => _maxZoomExplicitHalfExtentCm = value; }

        public void Validate()
        {
            float initialZoom = PresentationRuntimeConfig.RequireFinite(_initialZoomNormalized, "presentation.minimap.initialZoomNormalized");
            if (initialZoom < 0f || initialZoom > 1f)
            {
                throw new InvalidOperationException(
                    "presentation.minimap.initialZoomNormalized must be in [0, 1].");
            }

            float wheelStep = PresentationRuntimeConfig.RequireFinite(_wheelZoomNormalizedStep, "presentation.minimap.wheelZoomNormalizedStep");
            if (wheelStep <= 0f)
            {
                throw new InvalidOperationException(
                    "presentation.minimap.wheelZoomNormalizedStep must be > 0.");
            }

            float buttonStep = PresentationRuntimeConfig.RequireFinite(_buttonZoomNormalizedStep, "presentation.minimap.buttonZoomNormalizedStep");
            if (buttonStep <= 0f)
            {
                throw new InvalidOperationException(
                    "presentation.minimap.buttonZoomNormalizedStep must be > 0.");
            }

            PresentationRuntimeConfig.RequireBool(_zoomSliderEnabled, "presentation.minimap.zoomSliderEnabled");
            PresentationRuntimeConfig.RequireBool(_modeToggleEnabled, "presentation.minimap.modeToggleEnabled");
            PresentationRuntimeConfig.RequireBool(_rotateToggleEnabled, "presentation.minimap.rotateToggleEnabled");

            if (!_debugMarkerSampleCapacity.HasValue || _debugMarkerSampleCapacity.Value < 0)
            {
                throw new InvalidOperationException(
                    "presentation.minimap.debugMarkerSampleCapacity must be explicitly configured as >= 0.");
            }

            if (!_minZoomExtentMode.HasValue)
            {
                throw new InvalidOperationException("presentation.minimap.minZoomExtentMode must be explicitly configured.");
            }

            if (!_maxZoomExtentMode.HasValue)
            {
                throw new InvalidOperationException("presentation.minimap.maxZoomExtentMode must be explicitly configured.");
            }

            float minExplicit = PresentationRuntimeConfig.RequireFinite(
                _minZoomExplicitHalfExtentCm,
                "presentation.minimap.minZoomExplicitHalfExtentCm");
            float maxExplicit = PresentationRuntimeConfig.RequireFinite(
                _maxZoomExplicitHalfExtentCm,
                "presentation.minimap.maxZoomExplicitHalfExtentCm");

            if (minExplicit < 0f)
            {
                throw new InvalidOperationException(
                    "presentation.minimap.minZoomExplicitHalfExtentCm must be >= 0.");
            }

            if (maxExplicit < 0f)
            {
                throw new InvalidOperationException(
                    "presentation.minimap.maxZoomExplicitHalfExtentCm must be >= 0.");
            }

            if (_minZoomExtentMode.Value == MinimapZoomExtentMode.ExplicitCm && minExplicit <= 0f)
            {
                throw new InvalidOperationException(
                    "presentation.minimap.minZoomExplicitHalfExtentCm must be > 0 when minZoomExtentMode is ExplicitCm.");
            }

            if (_maxZoomExtentMode.Value == MinimapZoomExtentMode.ExplicitCm && maxExplicit <= 0f)
            {
                throw new InvalidOperationException(
                    "presentation.minimap.maxZoomExplicitHalfExtentCm must be > 0 when maxZoomExtentMode is ExplicitCm.");
            }
        }
    }

    public sealed class CameraCullingRuntimeConfig
    {
        private float? _highLodDistanceCm;
        private float? _mediumLodDistanceCm;
        private float? _lowLodDistanceCm;

        public float HighLodDistanceCm { get => _highLodDistanceCm ?? 0f; set => _highLodDistanceCm = value; }
        public float MediumLodDistanceCm { get => _mediumLodDistanceCm ?? 0f; set => _mediumLodDistanceCm = value; }
        public float LowLodDistanceCm { get => _lowLodDistanceCm ?? 0f; set => _lowLodDistanceCm = value; }

        public void Validate()
        {
            float high = PresentationRuntimeConfig.RequireFinite(
                _highLodDistanceCm,
                "presentation.cameraCulling.highLodDistanceCm");
            float medium = PresentationRuntimeConfig.RequireFinite(
                _mediumLodDistanceCm,
                "presentation.cameraCulling.mediumLodDistanceCm");
            float low = PresentationRuntimeConfig.RequireFinite(
                _lowLodDistanceCm,
                "presentation.cameraCulling.lowLodDistanceCm");

            if (high <= 0f || medium <= high || low <= medium)
            {
                throw new InvalidOperationException(
                    "presentation.cameraCulling requires positive lod distances with high < medium < low.");
            }
        }
    }
}
