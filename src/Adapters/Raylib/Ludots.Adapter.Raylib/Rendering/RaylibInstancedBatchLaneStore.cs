using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;

namespace Ludots.Adapter.Raylib.Rendering
{
    /// <summary>
    /// Pure state machine mirroring Core-owned typed instanced batch requests into resident
    /// lanes for the renderer. Core stays the single source of truth for instance totals and
    /// chunk scheduling; this store only allocates local matrix buffers, validates chunk
    /// bounds against the declared capacity (fail-loud on divergence), and converts transforms
    /// (position cm → visual meters) once per arriving chunk. It never touches raylib APIs and
    /// never shares state with the ISM bridge bucket cache.
    /// </summary>
    public sealed class RaylibInstancedBatchLaneStore : IRaylibInstancedBatchLaneSource
    {
        public static readonly ServiceKey<RaylibInstancedBatchLaneStore> LaneStoreServiceKey =
            new("Platform.RaylibInstancedBatchLaneStore");

        private readonly Dictionary<LaneKey, ResidentLane> _lanes = new();
        private readonly List<ResidentLane> _residentOrder = new(8);
        private int _nextLaneId = 1;

        public int ResidentLaneCount => _residentOrder.Count;
        public int LastAppliedRequestCount { get; private set; }

        public RaylibInstancedBatchLane GetResidentLane(int index)
        {
            if ((uint)index >= (uint)_residentOrder.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            ResidentLane lane = _residentOrder[index];
            return new RaylibInstancedBatchLane(
                lane.LaneId,
                lane.MeshAssetId,
                lane.MaterialAssetId,
                lane.RenderPath,
                lane.Matrices,
                lane.ResidentCount,
                lane.Revision,
                lane.Visible);
        }

        public void ApplyRequests(ReadOnlySpan<InstancedBatchRequest> requests, InstancedBatchAssetRegistry registry, IContinuousHeightmap? continuousHeightmap)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            LastAppliedRequestCount = requests.Length;
            for (int i = 0; i < requests.Length; i++)
            {
                ref readonly InstancedBatchRequest request = ref requests[i];
                switch (request.Kind)
                {
                    case InstancedBatchRequestKind.CreateOrUpdate:
                        ApplyCreateOrUpdate(in request, registry, continuousHeightmap);
                        break;
                    case InstancedBatchRequestKind.Remove:
                        ApplyRemove(in request);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"RaylibInstancedBatchLaneStore received unknown request kind {request.Kind} for batchAssetId={request.BatchAssetId}.");
                }
            }
        }

        private void ApplyCreateOrUpdate(in InstancedBatchRequest request, InstancedBatchAssetRegistry registry, IContinuousHeightmap? continuousHeightmap)
        {
            if (!registry.TryGet(request.BatchAssetId, out InstancedBatchAsset asset))
            {
                throw new InvalidOperationException(
                    $"RaylibInstancedBatchLaneStore cannot resolve batchAssetId={request.BatchAssetId} referenced by presenterStableId={request.PresenterStableId}.");
            }

            InstancedBatchGroup group = ResolveGroup(asset, in request);
            if (group.Source.IsValid)
            {
                ApplyExternalSourceCreateOrUpdate(asset, in group, in request, continuousHeightmap);
                return;
            }

            InstancedBatchTransform[] transforms = group.Transforms ?? Array.Empty<InstancedBatchTransform>();
            int declaredInstanceCount = transforms.Length;
            if (declaredInstanceCount == 0)
            {
                throw new InvalidOperationException(
                    $"RaylibInstancedBatchLaneStore received a chunk for batch '{asset.Key}' group '{group.Id}' without inline transforms.");
            }

            ApplyChunk(asset, in group, in request, declaredInstanceCount, out ResidentLane lane);
            for (int i = 0; i < request.InstanceCount; i++)
            {
                int index = request.InstanceStart + i;
                lane.Matrices[index] = BuildMatrix(transforms[index]);
            }

            FinishChunk(lane, in request);
        }

        private void ApplyExternalSourceCreateOrUpdate(
            InstancedBatchAsset asset,
            in InstancedBatchGroup group,
            in InstancedBatchRequest request,
            IContinuousHeightmap? continuousHeightmap)
        {
            InstancedBatchFactorizedSource? factorized = group.FactorizedSource;
            if (factorized == null)
            {
                throw new InvalidOperationException(
                    $"RaylibInstancedBatchLaneStore received a chunk for external source batch '{asset.Key}' group '{group.Id}' without loaded factorized data; the factorized source loader must run at config time (no fallback to inline or static presentation).");
            }

            if (factorized.InstanceCount != group.Source.InstanceCount)
            {
                throw new InvalidOperationException(
                    $"RaylibInstancedBatchLaneStore external source batch '{asset.Key}' group '{group.Id}' loaded factorized instanceCount {factorized.InstanceCount} diverges from Core-authored instanceCount {group.Source.InstanceCount}; the lane must size from Core-owned counts.");
            }

            // Core-authored source flag is the SSOT; the loaded factorized copy must match it.
            bool grounded = group.Source.GroundToContinuousHeightmap;
            if (grounded != factorized.GroundToContinuousHeightmap)
            {
                throw new InvalidOperationException(
                    $"RaylibInstancedBatchLaneStore external source batch '{asset.Key}' group '{group.Id}' factorized groundToContinuousHeightmap {factorized.GroundToContinuousHeightmap} diverges from Core-authored {group.Source.GroundToContinuousHeightmap}; the authored source flag is the SSOT.");
            }

            if (grounded && continuousHeightmap == null)
            {
                throw new InvalidOperationException(
                    $"RaylibInstancedBatchLaneStore cannot ground batch '{asset.Key}' group '{group.Id}' because the Core visual heightmap service is unavailable; the adapter must not substitute its own ground height truth.");
            }

            ApplyChunk(asset, in group, in request, group.Source.InstanceCount, out ResidentLane lane);
            for (int i = 0; i < request.InstanceCount; i++)
            {
                int index = request.InstanceStart + i;
                lane.Matrices[index] = BuildMatrix(
                    factorized.PositionCm[index],
                    factorized.Rotation[index],
                    factorized.Scale[index],
                    grounded,
                    continuousHeightmap);
            }

            FinishChunk(lane, in request);
        }

        private void ApplyChunk(
            InstancedBatchAsset asset,
            in InstancedBatchGroup group,
            in InstancedBatchRequest request,
            int declaredInstanceCount,
            out ResidentLane lane)
        {
            if (request.InstanceStart < 0 ||
                request.InstanceCount < 0 ||
                request.InstanceStart + request.InstanceCount > declaredInstanceCount)
            {
                throw new InvalidOperationException(
                    $"RaylibInstancedBatchLaneStore chunk [{request.InstanceStart}..{request.InstanceStart + request.InstanceCount}) exceeds the declared capacity {declaredInstanceCount} of batch '{asset.Key}' group '{group.Id}'.");
            }

            var key = new LaneKey(request.BatchAssetId, request.PresenterStableId, request.Address);
            if (!_lanes.TryGetValue(key, out ResidentLane? existing))
            {
                existing = new ResidentLane(_nextLaneId++, key, declaredInstanceCount);
                _lanes.Add(key, existing);
                _residentOrder.Add(existing);
            }
            else if (existing.DeclaredInstanceCount != declaredInstanceCount)
            {
                // A re-registered batch with a smaller instance total leaves stale tail matrices;
                // reset the whole lane so the incoming chunk stream rebuilds from Core's new declaration.
                existing.Reset(declaredInstanceCount);
            }

            existing.MeshAssetId = request.MeshAssetId;
            existing.MaterialAssetId = request.MaterialAssetId;
            existing.RenderPath = request.RenderPath;
            lane = existing;
        }

        private static void FinishChunk(ResidentLane lane, in InstancedBatchRequest request)
        {
            lane.ResidentCount = Math.Max(lane.ResidentCount, request.InstanceStart + request.InstanceCount);
            lane.Revision++;
            if (request.FinalChunk)
            {
                lane.Completed = true;
            }
        }

        private void ApplyRemove(in InstancedBatchRequest request)
        {
            var key = new LaneKey(request.BatchAssetId, request.PresenterStableId, request.Address);
            if (!_lanes.TryGetValue(key, out ResidentLane? lane))
            {
                return;
            }

            _lanes.Remove(key);
            _residentOrder.Remove(lane);
        }

        private static InstancedBatchGroup ResolveGroup(InstancedBatchAsset asset, in InstancedBatchRequest request)
        {
            InstancedBatchGroup[] groups = asset.Groups ?? Array.Empty<InstancedBatchGroup>();
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i].Address.Equals(request.Address))
                {
                    return groups[i];
                }
            }

            throw new InvalidOperationException(
                $"RaylibInstancedBatchLaneStore cannot resolve address {{batch={request.Address.BatchId}, group={request.Address.Group.Value}}} of batch '{asset.Key}' referenced by presenterStableId={request.PresenterStableId}.");
        }

        private static Matrix4x4 BuildMatrix(in InstancedBatchTransform transform)
        {
            return BuildMatrix(transform.PositionCm, transform.Rotation, transform.Scale, grounded: false, continuousHeightmap: null);
        }

        private static Matrix4x4 BuildMatrix(
            Vector3 positionCm,
            Quaternion rotation,
            Vector3 scale,
            bool grounded,
            IContinuousHeightmap? continuousHeightmap)
        {
            if (grounded)
            {
                if (continuousHeightmap == null)
                {
                    throw new InvalidOperationException(
                        "RaylibInstancedBatchLaneStore cannot ground a transform without the Core visual heightmap service.");
                }

                // Core-owned visual height truth: sample the ground under the authored X/Z and
                // replace only the ground axis. Out-of-bounds samples keep the authored height;
                // the adapter never substitutes its own terrain truth.
                if (continuousHeightmap.TrySampleHeightCm(positionCm.X, positionCm.Z, out float heightCm))
                {
                    positionCm.Y = heightCm;
                }
            }

            Vector3 positionMeters = WorldUnits.CmToM(positionCm);
            return Matrix4x4.CreateScale(scale) *
                   Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(rotation)) *
                   Matrix4x4.CreateTranslation(positionMeters);
        }

        private readonly struct LaneKey : IEquatable<LaneKey>
        {
            public LaneKey(int batchAssetId, int presenterStableId, InstancedBatchAddress address)
            {
                BatchAssetId = batchAssetId;
                PresenterStableId = presenterStableId;
                Address = address;
            }

            public int BatchAssetId { get; }
            public int PresenterStableId { get; }
            public InstancedBatchAddress Address { get; }

            public bool Equals(LaneKey other)
            {
                return BatchAssetId == other.BatchAssetId &&
                       PresenterStableId == other.PresenterStableId &&
                       Address.Equals(other.Address);
            }

            public override bool Equals(object? obj) => obj is LaneKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(BatchAssetId, PresenterStableId, Address);
        }

        private sealed class ResidentLane
        {
            public ResidentLane(int laneId, LaneKey key, int declaredInstanceCount)
            {
                LaneId = laneId;
                Key = key;
                DeclaredInstanceCount = declaredInstanceCount;
                Matrices = new Matrix4x4[declaredInstanceCount];
            }

            public int LaneId { get; }
            public LaneKey Key { get; }
            public int MeshAssetId { get; set; }
            public int MaterialAssetId { get; set; }
            public VisualRenderPath RenderPath { get; set; }
            public Matrix4x4[] Matrices { get; private set; }
            public int ResidentCount { get; set; }
            public int DeclaredInstanceCount { get; private set; }
            public int Revision { get; set; }
            public bool Completed { get; set; }
            public bool Visible { get; set; } = true;

            public void Reset(int declaredInstanceCount)
            {
                DeclaredInstanceCount = declaredInstanceCount;
                Matrices = new Matrix4x4[declaredInstanceCount];
                ResidentCount = 0;
                Completed = false;
                Revision++;
            }
        }
    }
}
