using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Loads camera presets from ConfigPipeline (Camera/presets.json) into CameraPresetRegistry.
    /// </summary>
    public sealed class CameraPresetLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly CameraPresetRegistry _registry;

        public CameraPresetLoader(ConfigPipeline pipeline, CameraPresetRegistry registry)
        {
            _pipeline = pipeline ?? throw new System.ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Camera/presets.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            if (merged == null || merged.Count == 0) return;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                if (node == null) continue;

                try
                {
                    var preset = JsonSerializer.Deserialize<CameraPreset>(node.ToJsonString(), options);
                    if (preset != null && !string.IsNullOrWhiteSpace(preset.Id))
                        _registry.Register(preset);
                }
                catch (System.Exception)
                {
                    // Skip invalid entries
                }
            }
        }
    }
}
