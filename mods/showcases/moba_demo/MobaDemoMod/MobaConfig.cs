using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Modding;

namespace MobaDemoMod
{
    /// <summary>
    /// Centralized configuration for MobaDemoMod.
    /// Loaded from assets/moba_config.json.
    /// Keeps tunable values in data instead of runtime code.
    /// </summary>
    public sealed class MobaConfig
    {
        [JsonPropertyName("abilities")]
        public AbilityConfig Abilities { get; set; } = new();

        [JsonPropertyName("commands")]
        public CommandConfig Commands { get; set; } = new();

        [JsonPropertyName("presentation")]
        public PresentationConfig Presentation { get; set; } = new();

        // ── Nested config classes ──

        public sealed class AbilityConfig
        {
            [JsonPropertyName("skillQ")] public SkillConfig SkillQ { get; set; } = new() { RangeCm = 600 };
            [JsonPropertyName("skillW")] public SkillConfig SkillW { get; set; } = new() { RangeCm = 0 };
            [JsonPropertyName("skillE")] public SkillConfig SkillE { get; set; } = new() { RangeCm = 800 };
            [JsonPropertyName("skillR")] public SkillConfig SkillR { get; set; } = new() { RangeCm = 1000 };
            [JsonPropertyName("indicator")] public IndicatorConfig Indicator { get; set; } = new();
        }

        public sealed class SkillConfig
        {
            [JsonPropertyName("rangeCm")] public float RangeCm { get; set; }
        }

        public sealed class IndicatorConfig
        {
            [JsonPropertyName("validColor")] public float[] ValidColor { get; set; } = { 0.3f, 0.8f, 1f, 0.4f };
            [JsonPropertyName("invalidColor")] public float[] InvalidColor { get; set; } = { 1f, 0.3f, 0.2f, 0.3f };
            [JsonPropertyName("rangeCircleColor")] public float[] RangeCircleColor { get; set; } = { 0.3f, 0.7f, 1f, 0.2f };
        }

        public sealed class CommandConfig
        {
            [JsonPropertyName("maxPerFrame")] public int MaxPerFrame { get; set; } = 128;
        }

        public sealed class PresentationConfig
        {
            [JsonPropertyName("commandSourceIndicatorDefKey")] public string CommandSourceIndicatorDefKey { get; set; } = "moba_command_source_indicator";
            [JsonPropertyName("commandSourceScopeId")] public int CommandSourceScopeId { get; set; } = 99001;
            [JsonPropertyName("rangeCircleIndicatorDefKey")] public string RangeCircleIndicatorDefKey { get; set; } = "moba_ability_range";
            [JsonPropertyName("circleEnemyMarker")] public CircleEnemyMarkerConfig CircleEnemyMarker { get; set; } = new();
        }

        public sealed class CircleEnemyMarkerConfig
        {
            [JsonPropertyName("scale")] public float[] Scale { get; set; } = { 1.2f, 0.08f, 1.2f };
            [JsonPropertyName("color")] public float[] Color { get; set; } = { 0.8f, 0.2f, 1f, 0.75f };
            [JsonPropertyName("lifetimeSeconds")] public float LifetimeSeconds { get; set; } = 0.35f;
            [JsonPropertyName("yOffsetMeters")] public float YOffsetMeters { get; set; } = 0.1f;
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
            const string uri = "assets/moba_config.json";
            string fullUri = $"{ctx.ModId}:{uri}";
            using var stream = ctx.VFS.GetStream(fullUri);
            return JsonSerializer.Deserialize<MobaConfig>(stream, JsonOptions)
                   ?? throw new InvalidOperationException($"[MobaConfig] Deserialized null from '{fullUri}'.");
        }
    }
}
