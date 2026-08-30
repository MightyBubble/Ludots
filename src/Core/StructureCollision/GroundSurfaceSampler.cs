using System;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.StructureCollision
{
    public interface IGroundSurfaceSampler
    {
        bool SampleTerrainBatch(
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldZCm,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask,
            int terrainLayerIndex = -1);

        bool SampleStructureSurfaceBatch(
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldZCm,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask,
            in GroundSurfaceQueryPolicy policy,
            StructureGroundingDiagnostics? diagnostics = null);

        bool ResolveGroundBatch(
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldZCm,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask,
            in GroundSurfaceQueryPolicy policy,
            StructureGroundingDiagnostics? diagnostics = null);
    }

    public sealed class GroundSurfaceSampler : IGroundSurfaceSampler
    {
        private readonly IContinuousHeightmap? _terrain;
        private readonly StructureCollisionAsset? _structureAsset;
        private readonly StructureCollisionRuntimeState? _runtimeState;

        public GroundSurfaceSampler(
            IContinuousHeightmap? terrain,
            StructureCollisionAsset? structureAsset,
            StructureCollisionRuntimeState? runtimeState = null)
        {
            _terrain = terrain;
            _structureAsset = structureAsset;
            _runtimeState = runtimeState;
        }

        public bool SampleTerrainBatch(
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldZCm,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask,
            int terrainLayerIndex = -1)
        {
            ValidateSpans(worldXCm, worldZCm, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
            FillNoHit(worldXCm.Length, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
            if (_terrain == null)
            {
                return false;
            }

            bool layerResolved = _terrain.SampleHeightsCm(worldXCm, worldZCm, outHeightCm, terrainLayerIndex);
            bool anyHit = false;
            for (int i = 0; i < worldXCm.Length; i++)
            {
                if (layerResolved && float.IsFinite(outHeightCm[i]))
                {
                    outNormalX[i] = 0f;
                    outNormalY[i] = 1f;
                    outNormalZ[i] = 0f;
                    outSurfaceId[i] = GroundSurfaceIds.TerrainSurface;
                    outLayerId[i] = GroundSurfaceIds.TerrainLayer;
                    outHitMask[i] = (byte)(GroundSurfaceHitMask.Terrain | GroundSurfaceHitMask.Walkable);
                    anyHit = true;
                }
                else
                {
                    WriteNoHit(i, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
                }
            }

            return anyHit;
        }

        public bool SampleStructureSurfaceBatch(
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldZCm,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask,
            in GroundSurfaceQueryPolicy policy,
            StructureGroundingDiagnostics? diagnostics = null)
        {
            ValidateSpans(worldXCm, worldZCm, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
            FillNoHit(worldXCm.Length, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
            if (_structureAsset == null)
            {
                return false;
            }

            diagnostics?.ResetCounters();
            if (diagnostics != null)
            {
                diagnostics.TotalSurfaces = _structureAsset.SurfaceCount;
                diagnostics.LoadedChunks = _structureAsset.ChunkCount;
            }

            bool anyHit = false;
            for (int i = 0; i < worldXCm.Length; i++)
            {
                if (TryResolveStructureSample(
                        worldXCm[i],
                        worldZCm[i],
                        in policy,
                        diagnostics,
                        out int surfaceIndex,
                        out float heightCm,
                        out byte hitMask))
                {
                    WriteStructureHit(i, surfaceIndex, heightCm, hitMask, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
                    anyHit = true;
                }
            }

            return anyHit;
        }

        public bool ResolveGroundBatch(
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldZCm,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask,
            in GroundSurfaceQueryPolicy policy,
            StructureGroundingDiagnostics? diagnostics = null)
        {
            ValidateSpans(worldXCm, worldZCm, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
            SampleTerrainBatch(worldXCm, worldZCm, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
            if (_structureAsset == null)
            {
                return HasAnyHit(outHitMask.Slice(0, worldXCm.Length));
            }

            diagnostics?.ResetCounters();
            if (diagnostics != null)
            {
                diagnostics.TotalSurfaces = _structureAsset.SurfaceCount;
                diagnostics.LoadedChunks = _structureAsset.ChunkCount;
            }

            for (int i = 0; i < worldXCm.Length; i++)
            {
                if (TryResolveStructureSample(
                        worldXCm[i],
                        worldZCm[i],
                        in policy,
                        diagnostics,
                        out int surfaceIndex,
                        out float heightCm,
                        out byte hitMask))
                {
                    WriteStructureHit(i, surfaceIndex, heightCm, hitMask, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
                }
            }

            return HasAnyHit(outHitMask.Slice(0, worldXCm.Length));
        }

        private bool TryResolveStructureSample(
            float worldXCm,
            float worldZCm,
            in GroundSurfaceQueryPolicy policy,
            StructureGroundingDiagnostics? diagnostics,
            out int selectedSurfaceIndex,
            out float selectedHeightCm,
            out byte selectedHitMask)
        {
            selectedSurfaceIndex = -1;
            selectedHeightCm = float.NaN;
            selectedHitMask = (byte)GroundSurfaceHitMask.None;

            StructureCollisionAsset asset = _structureAsset!;
            if (!asset.TryGetChunkIndex(worldXCm, worldZCm, out int chunkIndex))
            {
                diagnostics?.RecordSample(0, visitedChunk: false);
                return false;
            }

            StructureChunkIndexEntry chunk = asset.Chunks[chunkIndex];
            int candidateCount = chunk.SurfaceCount;
            bool hasSelection = false;
            for (int i = 0; i < chunk.SurfaceCount; i++)
            {
                int surfaceIndex = asset.ChunkSurfaceIndices[chunk.SurfaceStart + i];
                if (_runtimeState != null && !_runtimeState.IsSurfaceEnabled(surfaceIndex))
                {
                    continue;
                }

                StructureSurfaceFlags flags = asset.Surfaces.Flags[surfaceIndex];
                if (policy.WalkableOnly && (flags & StructureSurfaceFlags.Walkable) == 0)
                {
                    continue;
                }

                if (!policy.AllowsLayer(asset.Surfaces.LayerIds[surfaceIndex]) ||
                    !policy.AllowsAgent(asset.Surfaces.AgentMasks[surfaceIndex]) ||
                    asset.Surfaces.SlopeDegrees[surfaceIndex] > policy.MaxSlopeDegrees)
                {
                    continue;
                }

                if (!asset.TryEvaluateSurfaceHeight(surfaceIndex, worldXCm, worldZCm, out float heightCm) ||
                    !policy.AllowsHeight(heightCm))
                {
                    continue;
                }

                if (!hasSelection || IsBetterCandidate(in policy, heightCm, selectedHeightCm, asset.Surfaces.SurfaceIds[surfaceIndex], asset.Surfaces.SurfaceIds[selectedSurfaceIndex]))
                {
                    selectedSurfaceIndex = surfaceIndex;
                    selectedHeightCm = heightCm;
                    selectedHitMask = BuildHitMask(flags, asset.Surfaces.Kinds[surfaceIndex]);
                    hasSelection = true;
                }
            }

            diagnostics?.RecordSample(candidateCount, visitedChunk: true);
            return hasSelection;
        }

        private static bool IsBetterCandidate(
            in GroundSurfaceQueryPolicy policy,
            float candidateHeightCm,
            float selectedHeightCm,
            int candidateSurfaceId,
            int selectedSurfaceId)
        {
            if (policy.SelectionMode == StructureGroundSelectionMode.ClosestToReferenceHeight &&
                float.IsFinite(policy.ReferenceHeightCm))
            {
                float candidateDelta = MathF.Abs(candidateHeightCm - policy.ReferenceHeightCm);
                float selectedDelta = MathF.Abs(selectedHeightCm - policy.ReferenceHeightCm);
                if (candidateDelta != selectedDelta)
                {
                    return candidateDelta < selectedDelta;
                }
            }
            else if (candidateHeightCm != selectedHeightCm)
            {
                return candidateHeightCm > selectedHeightCm;
            }

            return candidateSurfaceId < selectedSurfaceId;
        }

        private void WriteStructureHit(
            int outputIndex,
            int surfaceIndex,
            float heightCm,
            byte hitMask,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask)
        {
            StructureCollisionAsset asset = _structureAsset!;
            outHeightCm[outputIndex] = heightCm;
            outNormalX[outputIndex] = asset.Surfaces.NormalX[surfaceIndex];
            outNormalY[outputIndex] = asset.Surfaces.NormalY[surfaceIndex];
            outNormalZ[outputIndex] = asset.Surfaces.NormalZ[surfaceIndex];
            outSurfaceId[outputIndex] = asset.Surfaces.SurfaceIds[surfaceIndex];
            outLayerId[outputIndex] = asset.Surfaces.LayerIds[surfaceIndex];
            outHitMask[outputIndex] = hitMask;
        }

        private static byte BuildHitMask(StructureSurfaceFlags flags, StructureSurfaceKind kind)
        {
            GroundSurfaceHitMask mask = GroundSurfaceHitMask.Structure;
            if ((flags & StructureSurfaceFlags.Walkable) != 0)
            {
                mask |= GroundSurfaceHitMask.Walkable;
            }

            if ((flags & (StructureSurfaceFlags.BlocksMovement | StructureSurfaceFlags.BlocksProjectiles | StructureSurfaceFlags.BlocksVision)) != 0)
            {
                mask |= GroundSurfaceHitMask.Blocker;
            }

            if (kind == StructureSurfaceKind.Portal)
            {
                mask |= GroundSurfaceHitMask.Portal;
            }

            return (byte)mask;
        }

        private static void ValidateSpans(
            ReadOnlySpan<float> worldXCm,
            ReadOnlySpan<float> worldZCm,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask)
        {
            int count = worldXCm.Length;
            if (worldZCm.Length != count ||
                outHeightCm.Length < count ||
                outNormalX.Length < count ||
                outNormalY.Length < count ||
                outNormalZ.Length < count ||
                outSurfaceId.Length < count ||
                outLayerId.Length < count ||
                outHitMask.Length < count)
            {
                throw new ArgumentException("Ground sampling spans must all cover the input count.");
            }
        }

        private static void FillNoHit(
            int count,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask)
        {
            for (int i = 0; i < count; i++)
            {
                WriteNoHit(i, outHeightCm, outNormalX, outNormalY, outNormalZ, outSurfaceId, outLayerId, outHitMask);
            }
        }

        private static void WriteNoHit(
            int index,
            Span<float> outHeightCm,
            Span<float> outNormalX,
            Span<float> outNormalY,
            Span<float> outNormalZ,
            Span<int> outSurfaceId,
            Span<int> outLayerId,
            Span<byte> outHitMask)
        {
            outHeightCm[index] = float.NaN;
            outNormalX[index] = 0f;
            outNormalY[index] = 1f;
            outNormalZ[index] = 0f;
            outSurfaceId[index] = GroundSurfaceIds.NoSurface;
            outLayerId[index] = GroundSurfaceIds.TerrainLayer;
            outHitMask[index] = (byte)GroundSurfaceHitMask.None;
        }

        private static bool HasAnyHit(ReadOnlySpan<byte> hitMask)
        {
            for (int i = 0; i < hitMask.Length; i++)
            {
                if (hitMask[i] != 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
