using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.GAS.Config
{
    public sealed class GasClockConfig
    {
        public int StepEveryFixedTicks { get; set; } = 1;
        public GasStepMode Mode { get; set; } = GasStepMode.Auto;
    }

    public sealed class GasClockConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public GasClockConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public GasClockConfig Load(string relativePath = "GAS/clock.json")
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
                return new GasClockConfig();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var config = mergedObject.Deserialize<GasClockConfig>(options);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize GasClockConfig.");
            }

            if (config.StepEveryFixedTicks < 1)
            {
                throw new InvalidOperationException("GasClockConfig.StepEveryFixedTicks must be >= 1.");
            }

            if (!Enum.IsDefined(typeof(GasStepMode), config.Mode))
            {
                throw new InvalidOperationException("GasClockConfig.Mode is invalid.");
            }

            return config;
        }
    }
}
