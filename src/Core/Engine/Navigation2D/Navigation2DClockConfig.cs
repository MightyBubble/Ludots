using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace Ludots.Core.Engine.Navigation2D
{
    public sealed class Navigation2DClockConfig
    {
        public int NavigationHz { get; set; } = 15;
        public int MaxStepsPerFixedTick { get; set; } = 2;
    }

    public sealed class Navigation2DClockConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public Navigation2DClockConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public Navigation2DClockConfig Load(string relativePath = "Navigation2D/clock.json")
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
                return new Navigation2DClockConfig();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = mergedObject.Deserialize<Navigation2DClockConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize Navigation2DClockConfig.");
            }

            if (config.NavigationHz < 0)
            {
                throw new InvalidOperationException("Navigation2DClockConfig.NavigationHz must be >= 0.");
            }

            if (config.MaxStepsPerFixedTick < 1)
            {
                throw new InvalidOperationException("Navigation2DClockConfig.MaxStepsPerFixedTick must be >= 1.");
            }

            return config;
        }
    }
}

