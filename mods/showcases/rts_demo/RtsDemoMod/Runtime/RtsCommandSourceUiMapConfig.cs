using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.UI.EntityCommandPanels;

namespace RtsDemoMod.Runtime
{
    internal sealed class RtsCommandSourceUiMapConfig
    {
        public const string MetadataKey = "rts.commandSourceUi";

        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        public bool ToolbarVisible { get; set; } = true;

        public bool SkillBarVisible { get; set; }

        public float? CameraFocusDistanceCm { get; set; }

        public float CameraFocusTowardDefaultTargetCm { get; set; }

        public RtsCommandSourcePanelMapConfig CommandDeck { get; set; } =
            RtsCommandSourcePanelMapConfig.CreateDefault(
                visible: true,
                EntityCommandPanelAnchorPreset.BottomCenter,
                offsetX: 0f,
                offsetY: 18f,
                widthPx: 702f,
                heightPx: 226f);

        public RtsCommandSourcePanelMapConfig OrderMonitor { get; set; } =
            RtsCommandSourcePanelMapConfig.CreateDefault(
                visible: true,
                EntityCommandPanelAnchorPreset.TopRight,
                offsetX: 28f,
                offsetY: 126f,
                widthPx: 390f,
                heightPx: 428f);

        public static RtsCommandSourceUiMapConfig Resolve(MapConfig? mapConfig)
        {
            if (mapConfig?.Metadata == null ||
                !mapConfig.Metadata.TryGetValue(MetadataKey, out var configNode))
            {
                return new RtsCommandSourceUiMapConfig();
            }

            RtsCommandSourceUiMapConfig config;
            try
            {
                config = configNode?.Deserialize<RtsCommandSourceUiMapConfig>(JsonOptions)
                    ?? throw new InvalidOperationException("configuration is null");
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"RTS map '{mapConfig.Id}' metadata '{MetadataKey}' is invalid: {exception.Message}",
                    exception);
            }

            config.Validate(mapConfig.Id);
            return config;
        }

        private void Validate(string mapId)
        {
            if (CommandDeck == null)
            {
                throw new InvalidOperationException(
                    $"RTS map '{mapId}' metadata '{MetadataKey}' requires commandDeck.");
            }
            if (OrderMonitor == null)
            {
                throw new InvalidOperationException(
                    $"RTS map '{mapId}' metadata '{MetadataKey}' requires orderMonitor.");
            }
            if (CameraFocusDistanceCm.HasValue &&
                (!float.IsFinite(CameraFocusDistanceCm.Value) || CameraFocusDistanceCm.Value <= 0f))
            {
                throw new InvalidOperationException(
                    $"RTS map '{mapId}' metadata '{MetadataKey}.cameraFocusDistanceCm' must be a positive finite distance.");
            }
            if (!float.IsFinite(CameraFocusTowardDefaultTargetCm) || CameraFocusTowardDefaultTargetCm < 0f)
            {
                throw new InvalidOperationException(
                    $"RTS map '{mapId}' metadata '{MetadataKey}.cameraFocusTowardDefaultTargetCm' must be a non-negative finite distance.");
            }
            CommandDeck.Validate(mapId, MetadataKey, "commandDeck", 360f, 180f);
            OrderMonitor.Validate(mapId, MetadataKey, "orderMonitor", 300f, 220f);
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase();
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }

    internal sealed class RtsCommandSourcePanelMapConfig
    {
        public bool Visible { get; set; }

        public EntityCommandPanelAnchorPreset Anchor { get; set; }

        public float OffsetX { get; set; }

        public float OffsetY { get; set; }

        public float WidthPx { get; set; }

        public float HeightPx { get; set; }

        public EntityCommandPanelAnchor ToAnchor() => new(Anchor, OffsetX, OffsetY);

        public EntityCommandPanelSize ToSize() => new(WidthPx, HeightPx);

        public static RtsCommandSourcePanelMapConfig CreateDefault(
            bool visible,
            EntityCommandPanelAnchorPreset anchor,
            float offsetX,
            float offsetY,
            float widthPx,
            float heightPx)
        {
            return new RtsCommandSourcePanelMapConfig
            {
                Visible = visible,
                Anchor = anchor,
                OffsetX = offsetX,
                OffsetY = offsetY,
                WidthPx = widthPx,
                HeightPx = heightPx,
            };
        }

        public void Validate(
            string mapId,
            string metadataKey,
            string propertyName,
            float minimumWidth,
            float minimumHeight)
        {
            if (!float.IsFinite(OffsetX) ||
                !float.IsFinite(OffsetY) ||
                !float.IsFinite(WidthPx) ||
                !float.IsFinite(HeightPx) ||
                WidthPx < minimumWidth ||
                HeightPx < minimumHeight)
            {
                throw new InvalidOperationException(
                    $"RTS map '{mapId}' metadata '{metadataKey}.{propertyName}' requires finite offsets and a size of at least {minimumWidth}x{minimumHeight}.");
            }
        }
    }
}
