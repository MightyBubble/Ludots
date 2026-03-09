namespace Ludots.Core.Gameplay.Camera
{
    public sealed class CameraPresetRequest
    {
        public string PresetId { get; set; } = string.Empty;
        public CameraFollowTargetKind? FollowTargetKindOverride { get; set; }
        public bool SnapToFollowTargetWhenAvailable { get; set; } = true;
        public bool ClearActiveVirtualCamera { get; set; } = true;
    }
}
