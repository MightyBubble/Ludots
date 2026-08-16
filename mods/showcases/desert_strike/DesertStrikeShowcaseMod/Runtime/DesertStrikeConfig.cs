using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Modding;

namespace DesertStrikeShowcaseMod.Runtime
{
    public sealed class DesertStrikeConfig
    {
        [JsonPropertyName("waveIntervalTicks")] public int WaveIntervalTicks { get; set; } = 1800;
        [JsonPropertyName("incomeIntervalTicks")] public int IncomeIntervalTicks { get; set; } = 600;
        [JsonPropertyName("incomePerInterval")] public int IncomePerInterval { get; set; } = 120;
        [JsonPropertyName("startingMinerals")] public int StartingMinerals { get; set; } = 600;
        [JsonPropertyName("lanes")] public LanesConfig Lanes { get; set; } = new();
        [JsonPropertyName("starterWave")] public StarterWaveConfig StarterWave { get; set; } = new();
        [JsonPropertyName("units")] public Dictionary<string, UnitConfig> Units { get; set; } = new();
        [JsonPropertyName("ai")] public AiConfig Ai { get; set; } = new();

        public sealed class LanesConfig
        {
            [JsonPropertyName("playerSpawns")] public List<SpawnPointConfig> PlayerSpawns { get; set; } = new();
            [JsonPropertyName("aiSpawns")] public List<SpawnPointConfig> AiSpawns { get; set; } = new();
        }

        public sealed class SpawnPointConfig
        {
            [JsonPropertyName("x")] public int X { get; set; }
            [JsonPropertyName("y")] public int Y { get; set; }
        }

        public sealed class StarterWaveConfig
        {
            [JsonPropertyName("player")] public List<StarterUnitEntry> Player { get; set; } = new();
            [JsonPropertyName("ai")] public List<StarterUnitEntry> Ai { get; set; } = new();
        }

        public sealed class StarterUnitEntry
        {
            [JsonPropertyName("unit")] public string Unit { get; set; } = string.Empty;
            [JsonPropertyName("lane")] public int Lane { get; set; }
        }

        public sealed class UnitConfig
        {
            [JsonPropertyName("template")] public string Template { get; set; } = string.Empty;
            [JsonPropertyName("cost")] public int Cost { get; set; }
            [JsonPropertyName("purchaseTag")] public string PurchaseTag { get; set; } = string.Empty;
            [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
        }

        public sealed class AiConfig
        {
            [JsonPropertyName("thinkIntervalTicks")] public int ThinkIntervalTicks { get; set; } = 120;
            [JsonPropertyName("weights")] public Dictionary<string, int> Weights { get; set; } = new();
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static DesertStrikeConfig Load(IModContext ctx)
        {
            const string uri = "assets/Configs/desert_strike_config.json";
            string fullUri = $"{ctx.ModId}:{uri}";
            using var stream = ctx.VFS.GetStream(fullUri);
            return JsonSerializer.Deserialize<DesertStrikeConfig>(stream, JsonOptions)
                   ?? throw new InvalidOperationException($"[DesertStrikeShowcaseMod] Deserialized null from '{fullUri}'.");
        }
    }
}
