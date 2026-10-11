using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Engine
{
    public sealed class EngineClockConfig
    {
        public int FixedHz { get; set; } = 50;

        /// <summary>
        /// 固定步累积器的积压上限（秒）。宿主卡顿后超出部分直接丢弃（慢动作），
        /// 不再整段追赶（瞬移）。下限由 loader 按当前 FixedHz 校验为 2 个固定步。
        /// </summary>
        public double MaxAccumulatedSeconds { get; set; } = 0.1;
    }

    public sealed class EngineClockConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public EngineClockConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public EngineClockConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Engine/clock.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);

            if (mergedObject == null)
            {
                return new EngineClockConfig();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = mergedObject.Deserialize<EngineClockConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize EngineClockConfig.");
            }

            if (config.FixedHz < 1)
            {
                throw new InvalidOperationException("EngineClockConfig.FixedHz must be >= 1.");
            }

            double minAccumulatedSeconds = 2.0 / config.FixedHz;
            if (config.MaxAccumulatedSeconds < minAccumulatedSeconds)
            {
                throw new InvalidOperationException(
                    $"EngineClockConfig.MaxAccumulatedSeconds must be >= {minAccumulatedSeconds:0.###} " +
                    $"(2 fixed ticks at FixedHz={config.FixedHz}: one in-flight step plus one digestible step).");
            }

            return config;
        }
    }
}
