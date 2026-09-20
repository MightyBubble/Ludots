using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// Receiver surface over the persistent single-static-mesh lane (VisualRenderPath.StaticMesh).
    /// The owner RaylibPrimitiveRenderer registers every visible lane item while drawing persistent
    /// static lanes: GPU mesh + the same TRS the opaque pass used + the item's exact world AABB
    /// (transformed local vertex bounds, following the terrain ChunkGpu pattern). A projected Decal
    /// re-draws intersecting meshes with the decal material, so ground stamps also paint the
    /// up-facing surfaces of props/buildings they overlap.
    /// An empty registry is a legitimate scene (no single static meshes this frame) and draws zero;
    /// the consumer's drawn&lt;=0 contract fails loud when nothing at all was paintable.
    /// Instanced static lanes are not receivers yet; FitYawedStampProjectorCenter always throws
    /// because meshes provide no height sampling — the composite binding must route the fit to a
    /// terrain receiver instead of leaving the authored Y.
    /// </summary>
    public sealed unsafe class RaylibStaticMeshReceiverProjector : IRaylibReceiverMeshProjector
    {
        private readonly List<ReceiverEntry> _entries = new(64);

        internal int RegisteredReceiverCount => _entries.Count;

        internal void BeginFrame()
        {
            _entries.Clear();
        }

        internal void RegisterReceiver(
            int stableId,
            Mesh mesh,
            Mesh[]? submeshes,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in Vector3 localMin,
            in Vector3 localMax)
        {
            Matrix4x4 world = Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(rotation)) *
                Matrix4x4.CreateTranslation(position);
            ComputeWorldAabbMeters(
                in world,
                in localMin,
                in localMax,
                out float minX,
                out float minY,
                out float minZ,
                out float maxX,
                out float maxY,
                out float maxZ);

            _entries.Add(new ReceiverEntry
            {
                StableId = stableId,
                Transform = RaylibMatrix.FromSystemNumerics(world),
                Mesh = mesh,
                Submeshes = submeshes,
                MinX = minX,
                MinY = minY,
                MinZ = minZ,
                MaxX = maxX,
                MaxY = maxY,
                MaxZ = maxZ,
            });
        }

        public int DrawMeshesOverlappingAabbMeters(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            Material material)
        {
            if (!float.IsFinite(minX) || !float.IsFinite(minY) || !float.IsFinite(minZ) ||
                !float.IsFinite(maxX) || !float.IsFinite(maxY) || !float.IsFinite(maxZ))
            {
                throw new ArgumentException(
                    $"{nameof(RaylibStaticMeshReceiverProjector)}.{nameof(DrawMeshesOverlappingAabbMeters)} requires finite AABB bounds.");
            }

            if (minX > maxX || minY > maxY || minZ > maxZ)
            {
                throw new ArgumentException(
                    $"{nameof(RaylibStaticMeshReceiverProjector)}.{nameof(DrawMeshesOverlappingAabbMeters)} AABB min must be <= max.");
            }

            int drawn = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                ReceiverEntry entry = _entries[i];
                if (entry.MaxX < minX || entry.MinX > maxX ||
                    entry.MaxY < minY || entry.MinY > maxY ||
                    entry.MaxZ < minZ || entry.MinZ > maxZ)
                {
                    continue;
                }

                Rl.rlDisableBackfaceCulling();
                if (entry.Submeshes is { Length: > 0 } submeshes)
                {
                    for (int s = 0; s < submeshes.Length; s++)
                    {
                        Rl.DrawMesh(submeshes[s], material, entry.Transform);
                    }
                }
                else
                {
                    Rl.DrawMesh(entry.Mesh, material, entry.Transform);
                }
                Rl.rlEnableBackfaceCulling();
                drawn++;
            }

            return drawn;
        }

        public Vector3 FitYawedStampProjectorCenter(
            in Vector3 stampCenter,
            float yawRad,
            in Vector2 stampSizeMeters,
            int stableId)
        {
            throw new InvalidOperationException(
                $"{nameof(RaylibStaticMeshReceiverProjector)} cannot fit Decal stableId={stableId} stamp height: static mesh receivers provide no height sampling. " +
                $"Route {nameof(FitYawedStampProjectorCenter)} to a terrain receiver (visual heightmap or VertexMap); authored Y must not survive the fit.");
        }

        internal static void ComputeWorldAabbMeters(
            in Matrix4x4 world,
            in Vector3 localMin,
            in Vector3 localMax,
            out float minX,
            out float minY,
            out float minZ,
            out float maxX,
            out float maxY,
            out float maxZ)
        {
            minX = minY = minZ = float.PositiveInfinity;
            maxX = maxY = maxZ = float.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new Vector3(
                    (corner & 1) == 0 ? localMin.X : localMax.X,
                    (corner & 2) == 0 ? localMin.Y : localMax.Y,
                    (corner & 4) == 0 ? localMin.Z : localMax.Z);
                Vector3 v = Vector3.Transform(local, world);
                minX = MathF.Min(minX, v.X);
                minY = MathF.Min(minY, v.Y);
                minZ = MathF.Min(minZ, v.Z);
                maxX = MathF.Max(maxX, v.X);
                maxY = MathF.Max(maxY, v.Y);
                maxZ = MathF.Max(maxZ, v.Z);
            }
        }

        private struct ReceiverEntry
        {
            public int StableId;
            public RaylibMatrix Transform;
            public Mesh Mesh;
            public Mesh[]? Submeshes;
            public float MinX;
            public float MinY;
            public float MinZ;
            public float MaxX;
            public float MaxY;
            public float MaxZ;
        }
    }
}
