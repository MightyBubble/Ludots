using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Engine
{
    public sealed class EngineClockConfig
    {
        public int FixedHz { get; set; } = 50;
    }

    public sealed class EngineClockConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public EngineClockConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public EngineClockConfig Load(string relativePath = "Engine/clock.json")
        {
            var fragments = _pipeline.CollectFragments(relativePath);
            JsonObject mergedObject = null;

            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i] is not JsonObject obj) continue;
                if (mergedObject == null)
                {
                    mergedObject = (JsonObject)obj.DeepClone();
                }
                else
                {
                    JsonMerger.Merge(mergedObject, obj);
                }
            }

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

            return config;
        }
    }
}

