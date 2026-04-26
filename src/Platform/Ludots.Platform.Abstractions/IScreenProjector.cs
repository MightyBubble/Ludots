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
        public ProjectionSnapshot(Matrix4x4 viewProjection, Vector2 resolution)
        {
            ViewProjection = viewProjection;
            Resolution = resolution;
        }

        public Matrix4x4 ViewProjection { get; }
        public Vector2 Resolution { get; }
    }

    public interface IProjectionSnapshotProvider : IProjectionRevisionProvider
    {
        bool TryGetProjectionSnapshot(out ProjectionSnapshot snapshot);
    }
}
