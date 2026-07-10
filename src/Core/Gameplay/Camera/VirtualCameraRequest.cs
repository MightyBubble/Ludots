using Arch.Core;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class VirtualCameraRequest
    {
        public string Id { get; set; } = string.Empty;
        public float? BlendDurationSeconds { get; set; }
        public bool Clear { get; set; }
        public bool ReplaceActiveStack { get; set; }
        public int? PriorityOverride { get; set; }
        public CameraFollowTargetKind? FollowTargetKindOverride { get; set; }
        public Entity FollowCollectionOwnerOverride { get; set; } = Entity.Null;
        public string FollowCollectionKeyOverride { get; set; } = string.Empty;
        public bool SnapToFollowTargetWhenAvailable { get; set; } = true;
        public bool ResetRuntimeState { get; set; } = true;
    }
}
