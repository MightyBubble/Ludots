using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Loads virtual camera definitions from ConfigPipeline (Camera/virtual_cameras.json)
    /// into VirtualCameraRegistry.
    /// </summary>
    public sealed class VirtualCameraDefinitionLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly VirtualCameraRegistry _registry;
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public VirtualCameraDefinitionLoader(ConfigPipeline pipeline, VirtualCameraRegistry registry)
        {
            _pipeline = pipeline ?? throw new System.ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new System.ArgumentNullException(nameof(registry));
        }

        public void Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            var entry = ConfigPipeline.GetEntryOrDefault(catalog, "Camera/virtual_cameras.json", ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            if (merged == null || merged.Count == 0)
            {
                return;
            }

            for (int i = 0; i < merged.Count; i++)
            {
                var node = merged[i].Node;
                if (node == null)
                {
                    continue;
                }

                try
                {
                    var config = JsonSerializer.Deserialize<VirtualCameraDefinitionConfig>(node.ToJsonString(), _options);
                    if (config == null || string.IsNullOrWhiteSpace(config.Id))
                    {
                        continue;
                    }

                    _registry.Register(new VirtualCameraDefinition
                    {
                        Id = config.Id,
                        RigKind = config.RigKind,
                        TargetSource = config.TargetSource,
                        FixedTargetCm = config.FixedTargetCm == null
                            ? System.Numerics.Vector2.Zero
                            : new System.Numerics.Vector2(config.FixedTargetCm.X, config.FixedTargetCm.Y),
                        Yaw = config.Yaw,
                        Pitch = config.Pitch,
                        DistanceCm = config.DistanceCm,
                        FovYDeg = config.FovYDeg,
                        DefaultBlendDuration = config.DefaultBlendDuration,
                        BlendCurve = config.BlendCurve,
                        AllowUserInput = config.AllowUserInput
                    });
                }
                catch (System.Exception)
                {
                    // Skip invalid entries
                }
            }
        }

        private sealed class VirtualCameraDefinitionConfig
        {
            public string Id { get; set; } = string.Empty;
            public CameraRigKind RigKind { get; set; } = CameraRigKind.Orbit;
            public VirtualCameraTargetSource TargetSource { get; set; } = VirtualCameraTargetSource.Fixed;
            public Vector2Config? FixedTargetCm { get; set; }
            public float Yaw { get; set; } = 180f;
            public float Pitch { get; set; } = 45f;
            public float DistanceCm { get; set; } = 3000f;
            public float FovYDeg { get; set; } = 60f;
            public float DefaultBlendDuration { get; set; } = 0.25f;
            public CameraBlendCurve BlendCurve { get; set; } = CameraBlendCurve.SmoothStep;
            public bool AllowUserInput { get; set; }
        }

        private sealed class Vector2Config
        {
            public float X { get; set; }
            public float Y { get; set; }
        }
    }
}
