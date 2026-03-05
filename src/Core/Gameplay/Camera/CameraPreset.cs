namespace Ludots.Core.Gameplay.Camera
{
    /// <summary>
    /// Camera preset for reuse across maps. Loaded from ConfigPipeline (Camera/presets.json).
    /// Mods can extend or override via assets/Configs/Camera/presets.json.
    /// </summary>
    public sealed class CameraPreset
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public float DistanceCm { get; set; }
        public float Pitch { get; set; }
        public float FovYDeg { get; set; } = 60f;
        public float Yaw { get; set; } = 180f;
        public float MinDistanceCm { get; set; }
        public float MaxDistanceCm { get; set; }
        public float MinPitchDeg { get; set; }
        public float MaxPitchDeg { get; set; }
    }
}
