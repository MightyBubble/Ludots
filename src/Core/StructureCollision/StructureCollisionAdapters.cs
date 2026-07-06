using System;

namespace Ludots.Core.StructureCollision
{
    public static class StructureCollisionNavigationAdapter
    {
        public static int CollectBlockers(
            StructureCollisionAsset asset,
            StructureCollisionRuntimeState? runtimeState,
            StructureCollisionBlockerKind blockerKind,
            Span<StructureCollisionBlockerView> output)
        {
            return StructureCollisionDerivedViewUtility.CollectBlockers(asset, runtimeState, blockerKind, output);
        }

        public static int CollectDirtyChunkInvalidations(
            StructureCollisionRuntimeState runtimeState,
            Span<StructureChunkRevision> output)
        {
            if (runtimeState == null) throw new ArgumentNullException(nameof(runtimeState));
            return runtimeState.DirtyChunks.CopyDirtyChunks(output);
        }
    }

    public static class StructureCollisionPhysicsAdapter
    {
        public static int CollectCollisionShapes(
            StructureCollisionAsset asset,
            StructureCollisionRuntimeState? runtimeState,
            Span<StructureCollisionBlockerView> output)
        {
            return StructureCollisionDerivedViewUtility.CollectBlockers(asset, runtimeState, StructureCollisionBlockerKind.Movement, output);
        }

        public static int CollectDirtyChunkInvalidations(
            StructureCollisionRuntimeState runtimeState,
            Span<StructureChunkRevision> output)
        {
            if (runtimeState == null) throw new ArgumentNullException(nameof(runtimeState));
            return runtimeState.DirtyChunks.CopyDirtyChunks(output);
        }
    }

    public static class StructureCollisionDebugAdapter
    {
        public static int CollectSurfaceDebugRecords(
            StructureCollisionAsset asset,
            StructureCollisionRuntimeState? runtimeState,
            Span<StructureCollisionDebugRecord> output)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            int written = 0;
            for (int i = 0; i < asset.SurfaceCount && written < output.Length; i++)
            {
                if (runtimeState != null && !runtimeState.IsSurfaceEnabled(i))
                {
                    continue;
                }

                output[written++] = new StructureCollisionDebugRecord(
                    asset.Surfaces.SurfaceIds[i],
                    asset.Surfaces.LayerIds[i],
                    asset.Surfaces.AgentMasks[i],
                    asset.GetPrimaryChunkForSurface(i),
                    asset.Surfaces.MaxHeightCm[i],
                    asset.Surfaces.Flags[i]);
            }

            return written;
        }
    }

    public static class StructureCollisionSelectionAdapter
    {
        public static bool TryResolveGround(
            IGroundSurfaceSampler sampler,
            float worldXCm,
            float worldZCm,
            in GroundSurfaceQueryPolicy policy,
            out int surfaceId,
            out float heightCm)
        {
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));

            Span<float> x = stackalloc float[1];
            Span<float> z = stackalloc float[1];
            Span<float> h = stackalloc float[1];
            Span<float> nx = stackalloc float[1];
            Span<float> ny = stackalloc float[1];
            Span<float> nz = stackalloc float[1];
            Span<int> surfaces = stackalloc int[1];
            Span<int> layers = stackalloc int[1];
            Span<byte> hitMask = stackalloc byte[1];
            x[0] = worldXCm;
            z[0] = worldZCm;

            bool hit = sampler.ResolveGroundBatch(x, z, h, nx, ny, nz, surfaces, layers, hitMask, in policy);
            surfaceId = hit ? surfaces[0] : GroundSurfaceIds.NoSurface;
            heightCm = hit ? h[0] : float.NaN;
            return hit;
        }
    }

    public static class StructureCollisionCameraGroundAdapter
    {
        public static bool TryResolveTargetHeight(
            IGroundSurfaceSampler sampler,
            float worldXCm,
            float worldZCm,
            in GroundSurfaceQueryPolicy policy,
            out int surfaceId,
            out float heightCm)
        {
            return StructureCollisionSelectionAdapter.TryResolveGround(
                sampler,
                worldXCm,
                worldZCm,
                in policy,
                out surfaceId,
                out heightCm);
        }
    }

    internal static class StructureCollisionDerivedViewUtility
    {
        public static int CollectBlockers(
            StructureCollisionAsset asset,
            StructureCollisionRuntimeState? runtimeState,
            StructureCollisionBlockerKind blockerKind,
            Span<StructureCollisionBlockerView> output)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            StructureSurfaceFlags required = blockerKind switch
            {
                StructureCollisionBlockerKind.Movement => StructureSurfaceFlags.BlocksMovement,
                StructureCollisionBlockerKind.Projectile => StructureSurfaceFlags.BlocksProjectiles,
                StructureCollisionBlockerKind.Vision => StructureSurfaceFlags.BlocksVision,
                _ => throw new ArgumentOutOfRangeException(nameof(blockerKind))
            };

            int written = 0;
            for (int i = 0; i < asset.SurfaceCount && written < output.Length; i++)
            {
                if ((asset.Surfaces.Flags[i] & required) == 0)
                {
                    continue;
                }

                if (runtimeState != null && !runtimeState.IsSurfaceEnabled(i))
                {
                    continue;
                }

                output[written++] = new StructureCollisionBlockerView(
                    asset.Surfaces.SurfaceIds[i],
                    asset.Surfaces.LayerIds[i],
                    asset.Surfaces.AgentMasks[i],
                    asset.Surfaces.Bounds[i],
                    asset.Surfaces.Flags[i],
                    asset.Surfaces.ShapeRefs[i],
                    asset.GetPrimaryChunkForSurface(i));
            }

            return written;
        }
    }
}
