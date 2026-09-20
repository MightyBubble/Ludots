using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public interface IScreenProjector
    {
        Vector2 WorldToScreen(Vector3 worldPosition);
    }

    public interface IProjectionRevisionProvider
    {
        int ProjectionRevision { get; }
    }

    public readonly struct ProjectionSnapshot
    {
        public ProjectionSnapshot(Matrix4x4 viewProjection, Vector2 resolution, Vector3 cameraPosition = default)
        {
            ViewProjection = viewProjection;
            Resolution = resolution;
            CameraPosition = cameraPosition;
        }

        public Matrix4x4 ViewProjection { get; }
        public Vector2 Resolution { get; }

        /// <summary>
        /// 世界空间相机位置。供遮挡缓存等需要相机身份/位置的投影派生结果使用。
        /// 未提供时为默认值（调用方自行判定是否可用）。
        /// </summary>
        public Vector3 CameraPosition { get; }
    }

    public interface IProjectionSnapshotProvider : IProjectionRevisionProvider
    {
        bool TryGetProjectionSnapshot(out ProjectionSnapshot snapshot);
    }
}
