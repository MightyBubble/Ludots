using System.Numerics;

namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// A pure data structure representing a visual transformation in 3D space.
    /// This decouples the logic layer from specific engine transform components (like UnityEngine.Transform).
    /// </summary>
    public struct VisualTransform
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public static VisualTransform Default => new VisualTransform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        };
    }

    /// <summary>
    /// Frame-local provenance for VisualTransform.Y after terrain projection.
    /// Consumers can use this to avoid re-sampling the same visual heightmap truth.
    /// </summary>
    public struct ContinuousHeightmapSampleState
    {
        public int FrameId;
        public byte Sampled;

        public readonly bool IsSampledForFrame(int frameId) => Sampled != 0 && FrameId == frameId;
    }
}
