namespace Ludots.Core.Gameplay.Camera
{
    public sealed class VirtualCameraRequest
    {
        public string Id { get; set; } = string.Empty;
        public float? BlendDurationSeconds { get; set; }
        public bool Clear { get; set; }
    }
}
