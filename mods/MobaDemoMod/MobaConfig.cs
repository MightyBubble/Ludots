using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Modding;

namespace MobaDemoMod
{
    /// <summary>
    /// Centralized configuration for MobaDemoMod.
    /// Loaded from assets/Configs/moba_config.json.
    /// Replaces 25+ hardcoded values across the mod.
    /// </summary>
    public sealed class MobaConfig
    {
        [JsonPropertyName("abilities")]
        public AbilityConfig Abilities { get; set; } = new();

        [JsonPropertyName("movement")]
        public MovementConfig Movement { get; set; } = new();

        [JsonPropertyName("commands")]
        public CommandConfig Commands { get; set; } = new();

        [JsonPropertyName("presentation")]
        public PresentationConfig Presentation { get; set; } = new();

        [JsonPropertyName("camera")]
        public CameraConfig Camera { get; set; } = new();

        // ── Nested config classes ──

        public sealed class AbilityConfig
        {
            [JsonPropertyName("skillQ")] public SkillConfig SkillQ { get; set; } = new() { RangeCm = 600, CooldownTicks = 180 };
            [JsonPropertyName("skillW")] public SkillConfig SkillW { get; set; } = new() { RangeCm = 0, CooldownTicks = 300 };
            [JsonPropertyName("skillE")] public SkillConfig SkillE { get; set; } = new() { RangeCm = 800, CooldownTicks = 240 };
            [JsonPropertyName("skillR")] public SkillConfig SkillR { get; set; } = new() { RangeCm = 1000, CooldownTicks = 600 };
            [JsonPropertyName("globalCooldownTicks")] public int GlobalCooldownTicks { get; set; } = 15;
            [JsonPropertyName("indicator")] public IndicatorConfig Indicator { get; set; } = new();
        }

        public sealed class SkillConfig
        {
            [JsonPropertyName("rangeCm")] public float RangeCm { get; set; }
            [JsonPropertyName("cooldownTicks")] public int CooldownTicks { get; set; }
        }

        public sealed class IndicatorConfig
        {
            [JsonPropertyName("validColor")] public float[] ValidColor { get; set; } = { 0.3f, 0.8f, 1f, 0.4f };
            [JsonPropertyName("invalidColor")] public float[] InvalidColor { get; set; } = { 1f, 0.3f, 0.2f, 0.3f };
            [JsonPropertyName("rangeCircleColor")] public float[] RangeCircleColor { get; set; } = { 0.3f, 0.7f, 1f, 0.2f };
        }

        public sealed class MovementConfig
        {
            [JsonPropertyName("speedCmPerSec")] public int SpeedCmPerSec { get; set; } = 600;
            [JsonPropertyName("stopRadiusCm")] public int StopRadiusCm { get; set; } = 20;
        }

        public sealed class CommandConfig
        {
            [JsonPropertyName("maxPerFrame")] public int MaxPerFrame { get; set; } = 128;
        }

        public sealed class PresentationConfig
        {
            [JsonPropertyName("selectionIndicatorDefId")] public int SelectionIndicatorDefId { get; set; } = 5002;
            [JsonPropertyName("selectionScopeId")] public int SelectionScopeId { get; set; } = 99001;
            [JsonPropertyName("rangeCircleIndicatorDefId")] public int RangeCircleIndicatorDefId { get; set; } = 5004;
            [JsonPropertyName("circleEnemyMarker")] public CircleEnemyMarkerConfig CircleEnemyMarker { get; set; } = new();
        }

        public sealed class CircleEnemyMarkerConfig
        {
            [JsonPropertyName("scale")] public float[] Scale { get; set; } = { 1.2f, 0.08f, 1.2f };
            [JsonPropertyName("color")] public float[] Color { get; set; } = { 0.8f, 0.2f, 1f, 0.75f };
            [JsonPropertyName("lifetimeSeconds")] public float LifetimeSeconds { get; set; } = 0.35f;
            [JsonPropertyName("yOffsetMeters")] public float YOffsetMeters { get; set; } = 0.1f;
        }

        public sealed class CameraConfig
        {
            [JsonPropertyName("zoomCmPerWheel")] public float ZoomCmPerWheel { get; set; } = 2000f;
            [JsonPropertyName("rotateDegPerSecond")] public float RotateDegPerSecond { get; set; } = 90f;
            [JsonPropertyName("initialYawDegrees")] public float InitialYawDegrees { get; set; } = 35f;
            [JsonPropertyName("initialPitchDegrees")] public float InitialPitchDegrees { get; set; } = 60f;
            [JsonPropertyName("initialDistanceCm")] public float InitialDistanceCm { get; set; } = 25000f;
        }

        // ── Loading ──

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Load MobaConfig via VFS. Throws on error — caller should not silently swallow.
        /// </summary>
        public static MobaConfig Load(IModContext ctx)
        {
            const string uri = "assets/Configs/moba_config.json";
            string fullUri = $"{ctx.ModId}:{uri}";
            using var stream = ctx.VFS.GetStream(fullUri);
            return JsonSerializer.Deserialize<MobaConfig>(stream, JsonOptions)
                   ?? throw new InvalidOperationException($"[MobaConfig] Deserialized null from '{fullUri}'.");
        }
    }
}
