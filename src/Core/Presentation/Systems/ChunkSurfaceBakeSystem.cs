using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Surfaces;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class ChunkSurfaceBakeSystem : BaseSystem<World, float>
    {
        private const int SurfaceMeshParamKey = 100_001;
        private const int SurfaceMaterialParamKey = 100_002;
        private const int SurfaceVisibilityParamKey = 100_003;

        private readonly SurfaceSourceRuntimeRegistry _runtime;
        private readonly MeshAssetRegistry _meshes;
        private readonly PresentationMaterialRegistry _materials;
        private readonly PresentationLodProfileRegistry _lodProfiles;
        private readonly PresenterDefinitionRegistry _presenterDefinitions;
        private readonly PresenterCommandBuffer _commands;
        private readonly PresenterEntityRuntime _presenterRuntime;
        private readonly List<int> _completedRemovals = new(64);

        public ChunkSurfaceBakeSystem(
            World world,
            SurfaceSourceRuntimeRegistry runtime,
            MeshAssetRegistry meshes,
            PresentationMaterialRegistry materials,
            PresentationLodProfileRegistry lodProfiles,
            PresenterDefinitionRegistry presenterDefinitions,
            PresenterCommandBuffer commands,
            PresenterEntityRuntime presenterRuntime)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
            _lodProfiles = lodProfiles ?? throw new ArgumentNullException(nameof(lodProfiles));
            _presenterDefinitions = presenterDefinitions ?? throw new ArgumentNullException(nameof(presenterDefinitions));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _presenterRuntime = presenterRuntime ?? throw new ArgumentNullException(nameof(presenterRuntime));
        }

        public override void Update(in float dt)
        {
            _completedRemovals.Clear();
            foreach (SurfaceSourceRecord record in _runtime.Records)
            {
                if (record.PendingRemoval)
                {
                    if (record.Entity == Entity.Null || !World.IsAlive(record.Entity))
                    {
                        _completedRemovals.Add(record.SourceStableId);
                    }

                    continue;
                }

                if (!record.Dirty && record.Entity != Entity.Null && World.IsAlive(record.Entity))
                {
                    EnsureRenderPresenter(record);
                    continue;
                }

                BuildOrUpdateRecord(record);
            }

            for (int i = 0; i < _completedRemovals.Count; i++)
            {
                _runtime.Remove(_completedRemovals[i]);
            }
        }

        private void BuildOrUpdateRecord(SurfaceSourceRecord record)
        {
            ProceduralMeshAssetData mesh;
            Vector3 worldOrigin;
            switch (record.Request.SurfaceKind)
            {
                case PresenterSurfaceKind.SplineRibbon:
                    mesh = BuildSplineRibbonMesh(
                        record.Payload.SplineRibbon,
                        record.Request.Authoring,
                        ResolvePrimaryMaterialId(record.Request.Authoring));
                    worldOrigin = ComputeSplineOrigin(record.Payload.SplineRibbon);
                    break;

                case PresenterSurfaceKind.ClosedArea:
                    mesh = BuildClosedAreaMesh(
                        record.Payload.ClosedArea,
                        record.Request.Authoring,
                        ResolvePrimaryMaterialId(record.Request.Authoring));
                    worldOrigin = ComputeClosedAreaOrigin(record.Payload.ClosedArea);
                    break;

                case PresenterSurfaceKind.RawProceduralMesh:
                    mesh = record.Payload.RawProceduralMesh.ProceduralMesh;
                    worldOrigin = record.Payload.RawProceduralMesh.WorldOrigin;
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported surface kind '{record.Request.SurfaceKind}'.");
            }

            string meshKey = $"surface_source.{record.SourceStableId}";
            int materialId = ResolvePrimaryMaterialId(record.Request.Authoring);
            int meshAssetId = _meshes.Register(meshKey, MeshAssetDescriptor.Procedural(id: 0, mesh));
            PresentationLodProfile lodProfile = ResolveSurfaceLodProfile(record.Request.Authoring);
            Entity entity = EnsureBakedEntity(record, worldOrigin, mesh.LocalBounds, lodProfile);
            record.MeshAssetId = meshAssetId;

            int renderDefinitionId = RegisterRenderDefinition(record, mesh.UsageHint, meshAssetId, materialId);
            record.RenderPresenterDefinitionId = renderDefinitionId;
            record.RenderScopeId = ComposeRenderScopeId(record.ScopeId, record.SourceStableId);

            Entity renderEntity = EnsureRenderPresenter(record);
            if (renderEntity != Entity.Null && World.IsAlive(renderEntity))
            {
                _presenterRuntime.SetParam(renderEntity, SurfaceMeshParamKey, ParamLane.Int, 0f, meshAssetId, Vector4.Zero);
                _presenterRuntime.SetParam(renderEntity, SurfaceMaterialParamKey, ParamLane.Int, 0f, materialId, Vector4.Zero);
                _presenterRuntime.SetParam(renderEntity, SurfaceVisibilityParamKey, ParamLane.Int, 0f, 1, Vector4.Zero);
            }

            record.Dirty = false;
        }

        private Entity EnsureBakedEntity(SurfaceSourceRecord record, in Vector3 worldOrigin, in ProceduralMeshBounds bounds, in PresentationLodProfile lodProfile)
        {
            if (record.Entity != Entity.Null && World.IsAlive(record.Entity))
            {
                UpdateEntity(record.Entity, worldOrigin, bounds, lodProfile);
                return record.Entity;
            }

            WorldPositionCm worldPosition = WorldPositionCm.FromCmFloat(worldOrigin.X * 100f, worldOrigin.Z * 100f);
            record.Entity = World.Create(
                worldPosition,
                new PreviousWorldPositionCm { Value = worldPosition.Value },
                new PresentationStableId { Value = record.SourceStableId },
                new VisualTransform
                {
                    Position = new Vector3(worldOrigin.X, 0f, worldOrigin.Z),
                    Rotation = Quaternion.Identity,
                    Scale = Vector3.One,
                },
                PresentationLocalBounds.Create(bounds.Center, bounds.Extents),
                lodProfile,
                new CullState
                {
                    IsVisible = true,
                    LOD = LODLevel.High,
                });
            return record.Entity;
        }

        private void UpdateEntity(Entity entity, in Vector3 worldOrigin, in ProceduralMeshBounds bounds, in PresentationLodProfile lodProfile)
        {
            WorldPositionCm worldPosition = WorldPositionCm.FromCmFloat(worldOrigin.X * 100f, worldOrigin.Z * 100f);
            World.Set(entity, worldPosition);
            if (World.Has<PreviousWorldPositionCm>(entity))
            {
                World.Set(entity, new PreviousWorldPositionCm { Value = worldPosition.Value });
            }
            else
            {
                World.Add(entity, new PreviousWorldPositionCm { Value = worldPosition.Value });
            }

            VisualTransform transform = World.Has<VisualTransform>(entity)
                ? World.Get<VisualTransform>(entity)
                : VisualTransform.Default;
            transform.Position = new Vector3(worldOrigin.X, 0f, worldOrigin.Z);
            transform.Rotation = Quaternion.Identity;
            transform.Scale = Vector3.One;
            if (World.Has<VisualTransform>(entity))
            {
                World.Set(entity, transform);
            }
            else
            {
                World.Add(entity, transform);
            }

            PresentationLocalBounds localBounds = PresentationLocalBounds.Create(bounds.Center, bounds.Extents);
            if (World.Has<PresentationLocalBounds>(entity))
            {
                World.Set(entity, localBounds);
            }
            else
            {
                World.Add(entity, localBounds);
            }

            if (World.Has<PresentationLodProfile>(entity))
            {
                World.Set(entity, lodProfile);
            }
            else
            {
                World.Add(entity, lodProfile);
            }

            if (World.Has<PresentationDestroyPending>(entity))
            {
                World.Remove<PresentationDestroyPending>(entity);
            }

            if (World.Has<PresentationDestroyEventPublished>(entity))
            {
                World.Remove<PresentationDestroyEventPublished>(entity);
            }
        }

        private int RegisterRenderDefinition(SurfaceSourceRecord record, ProceduralMeshUsageHint usageHint, int meshAssetId, int materialId)
        {
            string definitionKey = $"surface_baked.render.{record.SourceStableId}";
            var definition = new PresenterDefinition
            {
                Key = definitionKey,
                ParamDefaults = new[]
                {
                    new ParamDefault
                    {
                        ParamKey = SurfaceMeshParamKey,
                        Lane = ParamLane.Int,
                        IntValue = meshAssetId,
                    },
                    new ParamDefault
                    {
                        ParamKey = SurfaceMaterialParamKey,
                        Lane = ParamLane.Int,
                        IntValue = materialId,
                    },
                    new ParamDefault
                    {
                        ParamKey = SurfaceVisibilityParamKey,
                        Lane = ParamLane.Int,
                        IntValue = 1,
                    },
                },
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = 0,
                            MaterialId = 0,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = usageHint == ProceduralMeshUsageHint.Static ? VisualMobility.Static : VisualMobility.Movable,
                            LocalOffset = Vector3.Zero,
                            LocalRotation = Quaternion.Identity,
                            LocalScale = Vector3.One,
                            AssetIdParamKey = SurfaceMeshParamKey,
                            MaterialParamKey = SurfaceMaterialParamKey,
                            VisibilityParamKey = SurfaceVisibilityParamKey,
                        },
                    },
                },
            };
            return _presenterDefinitions.Register(definitionKey, definition);
        }

        private Entity EnsureRenderPresenter(SurfaceSourceRecord record)
        {
            if (record.Entity == Entity.Null || !World.IsAlive(record.Entity))
            {
                throw new InvalidOperationException(
                    $"SurfaceSource stableId={record.SourceStableId} requires a baked entity before render presenter creation.");
            }

            if (record.RenderPresenterEntity != Entity.Null && World.IsAlive(record.RenderPresenterEntity) && World.Has<PresenterState>(record.RenderPresenterEntity))
            {
                ref PresenterState active = ref World.Get<PresenterState>(record.RenderPresenterEntity);
                if (active.OwnerEntity == record.Entity &&
                    active.DefId == record.RenderPresenterDefinitionId &&
                    active.ScopeId == record.RenderScopeId)
                {
                    return record.RenderPresenterEntity;
                }

                record.RenderPresenterEntity = Entity.Null;
            }

            if (_presenterRuntime.TryGetActiveScopedInstance(
                    record.RenderPresenterDefinitionId,
                    record.Entity,
                    record.RenderScopeId,
                    PresentationAnchorKind.Entity,
                    Vector3.Zero,
                    out Entity existing))
            {
                record.RenderPresenterEntity = existing;
                return existing;
            }

            if (record.RenderPresenterDefinitionId <= 0)
            {
                throw new InvalidOperationException(
                    $"SurfaceSource stableId={record.SourceStableId} is missing render presenter definition registration.");
            }

            if (!_commands.TryAdd(new PresenterCommand
                {
                    CommandKind = PresenterCommandKind.CreatePresenter,
                    PresenterDefinitionId = record.RenderPresenterDefinitionId,
                    ScopeTag = record.RenderScopeId,
                    Source = record.Entity,
                    AnchorKind = PresentationAnchorKind.Entity,
                }))
            {
                throw new InvalidOperationException(
                    $"SurfaceSource stableId={record.SourceStableId} failed to queue baked render presenter creation.");
            }

            return Entity.Null;
        }

        private static int ComposeRenderScopeId(int sourceScopeId, int stableId)
        {
            int hash = HashCode.Combine(sourceScopeId, stableId, 0x5A17);
            hash &= int.MaxValue;
            return hash == 0 ? 1 : hash;
        }

        private PresentationLodProfile ResolveSurfaceLodProfile(in SurfaceAuthoringBlock authoring)
        {
            if (string.IsNullOrWhiteSpace(authoring.LodProfileId))
            {
                throw new InvalidOperationException("SurfaceSource authoring must declare a non-empty lodProfileId.");
            }

            if (!_lodProfiles.TryGet(authoring.LodProfileId, out PresentationLodProfile profile))
            {
                throw new InvalidOperationException($"SurfaceSource authoring references unknown lodProfileId '{authoring.LodProfileId}'.");
            }

            return profile;
        }

        private int ResolvePrimaryMaterialId(SurfaceAuthoringBlock authoring)
        {
            if (string.IsNullOrWhiteSpace(authoring.MaterialSet.PrimaryMaterialId))
            {
                throw new InvalidOperationException("SurfaceSource authoring must declare materialSet.primaryMaterialId.");
            }

            int materialId = _materials.GetId(authoring.MaterialSet.PrimaryMaterialId);
            if (materialId <= 0)
            {
                throw new InvalidOperationException($"SurfaceSource authoring references unknown material '{authoring.MaterialSet.PrimaryMaterialId}'.");
            }

            return materialId;
        }

        private static Vector3 ComputeSplineOrigin(SurfaceSplineRibbonPayload payload)
        {
            if (payload.Segments.Length == 0)
            {
                return Vector3.Zero;
            }

            Vector3 min = payload.Segments[0].P0;
            Vector3 max = payload.Segments[0].P0;
            for (int i = 0; i < payload.Segments.Length; i++)
            {
                SurfaceSplineSegment segment = payload.Segments[i];
                IncludePoint(ref min, ref max, segment.P0);
                IncludePoint(ref min, ref max, segment.P1);
                IncludePoint(ref min, ref max, segment.P2);
                IncludePoint(ref min, ref max, segment.P3);
            }

            return new Vector3((min.X + max.X) * 0.5f, 0f, (min.Z + max.Z) * 0.5f);
        }

        private static Vector3 ComputeClosedAreaOrigin(SurfaceClosedAreaPayload payload)
        {
            if (payload.BoundaryPoints.Length == 0)
            {
                return Vector3.Zero;
            }

            Vector3 min = payload.BoundaryPoints[0];
            Vector3 max = payload.BoundaryPoints[0];
            for (int i = 1; i < payload.BoundaryPoints.Length; i++)
            {
                IncludePoint(ref min, ref max, payload.BoundaryPoints[i]);
            }

            return new Vector3((min.X + max.X) * 0.5f, 0f, (min.Z + max.Z) * 0.5f);
        }

        private static ProceduralMeshAssetData BuildSplineRibbonMesh(
            SurfaceSplineRibbonPayload payload,
            SurfaceAuthoringBlock authoring,
            int materialId)
        {
            if (payload.Segments.Length == 0)
            {
                throw new InvalidOperationException("Spline ribbon payload requires at least one segment.");
            }

            Vector3 origin = ComputeSplineOrigin(payload);
            int samplesPerSegment = 12;
            int sampleRowCount = (payload.Segments.Length * samplesPerSegment) + 1;
            int vertexCount = sampleRowCount * 2;
            int quadCount = payload.Segments.Length * samplesPerSegment;
            int indexCount = quadCount * 6;
            var mesh = new ProceduralMeshAssetData(vertexCount, indexCount);
            int vertexCursor = 0;
            int indexCursor = 0;
            float accumulatedLength = 0f;
            Vector3 previousPoint = default;
            bool hasPreviousPoint = false;

            for (int segmentIndex = 0; segmentIndex < payload.Segments.Length; segmentIndex++)
            {
                SurfaceSplineSegment segment = payload.Segments[segmentIndex];
                int sampleStart = segmentIndex == 0 ? 0 : 1;
                for (int sample = sampleStart; sample <= samplesPerSegment; sample++)
                {
                    float t = sample / (float)samplesPerSegment;
                    Vector3 point = EvaluateBezier(segment, t);
                    Vector3 derivative = EvaluateBezierDerivative(segment, t);
                    Vector3 forward = Vector3.Normalize(new Vector3(derivative.X, 0f, derivative.Z));
                    if (!float.IsFinite(forward.X) || !float.IsFinite(forward.Z) || forward.LengthSquared() < 1e-5f)
                    {
                        forward = Vector3.UnitX;
                    }

                    Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
                    float halfWidth = MathF.Max(0.05f, segment.WidthMeters * 0.5f);
                    Vector3 leftPoint = point - (right * halfWidth) - origin;
                    Vector3 rightPoint = point + (right * halfWidth) - origin;

                    WriteVertex(mesh, vertexCursor + 0, leftPoint, Vector3.UnitY, new Vector4(forward.X, forward.Y, forward.Z, 1f), accumulatedLength, 0f);
                    WriteVertex(mesh, vertexCursor + 1, rightPoint, Vector3.UnitY, new Vector4(forward.X, forward.Y, forward.Z, 1f), accumulatedLength, 1f);

                    if (vertexCursor > 0)
                    {
                        mesh.Indices[indexCursor + 0] = vertexCursor - 2;
                        mesh.Indices[indexCursor + 1] = vertexCursor + 0;
                        mesh.Indices[indexCursor + 2] = vertexCursor - 1;
                        mesh.Indices[indexCursor + 3] = vertexCursor - 1;
                        mesh.Indices[indexCursor + 4] = vertexCursor + 0;
                        mesh.Indices[indexCursor + 5] = vertexCursor + 1;
                        indexCursor += 6;
                    }

                    if (hasPreviousPoint)
                    {
                        accumulatedLength += Vector3.Distance(previousPoint, point);
                    }

                    previousPoint = point;
                    hasPreviousPoint = true;
                    vertexCursor += 2;
                }
            }

            ProceduralMeshBounds bounds = ComputeBounds(mesh, vertexCursor);
            var submeshes = new[] { new ProceduralSubmeshDescriptor(0, indexCursor, materialId) };
            mesh.Commit(vertexCursor, indexCursor, submeshes, bounds, authoring.ChunkBake.UsageHint);
            return mesh;
        }

        private static ProceduralMeshAssetData BuildClosedAreaMesh(
            SurfaceClosedAreaPayload payload,
            SurfaceAuthoringBlock authoring,
            int materialId)
        {
            if (payload.BoundaryPoints.Length < 3)
            {
                throw new InvalidOperationException("Closed area payload requires at least three boundary points.");
            }

            Vector3 origin = ComputeClosedAreaOrigin(payload);
            int vertexCount = payload.BoundaryPoints.Length;
            int indexCount = (payload.BoundaryPoints.Length - 2) * 3;
            var mesh = new ProceduralMeshAssetData(vertexCount, indexCount);

            Vector3 min = payload.BoundaryPoints[0];
            Vector3 max = payload.BoundaryPoints[0];
            for (int i = 0; i < payload.BoundaryPoints.Length; i++)
            {
                IncludePoint(ref min, ref max, payload.BoundaryPoints[i]);
            }

            float width = MathF.Max(0.01f, max.X - min.X);
            float depth = MathF.Max(0.01f, max.Z - min.Z);
            for (int i = 0; i < payload.BoundaryPoints.Length; i++)
            {
                Vector3 point = payload.BoundaryPoints[i];
                Vector3 local = point - origin;
                float u = (point.X - min.X) / width;
                float v = (point.Z - min.Z) / depth;
                WriteVertex(mesh, i, local, Vector3.UnitY, new Vector4(1f, 0f, 0f, 1f), u, v);
            }

            int indexCursor = 0;
            for (int i = 1; i < payload.BoundaryPoints.Length - 1; i++)
            {
                mesh.Indices[indexCursor + 0] = 0;
                mesh.Indices[indexCursor + 1] = i + 1;
                mesh.Indices[indexCursor + 2] = i;
                indexCursor += 3;
            }

            ProceduralMeshBounds bounds = ComputeBounds(mesh, vertexCount);
            var submeshes = new[] { new ProceduralSubmeshDescriptor(0, indexCursor, materialId) };
            mesh.Commit(vertexCount, indexCursor, submeshes, bounds, authoring.ChunkBake.UsageHint);
            return mesh;
        }

        private static void WriteVertex(
            ProceduralMeshAssetData mesh,
            int vertexIndex,
            in Vector3 position,
            in Vector3 normal,
            in Vector4 tangent,
            float u,
            float v)
        {
            int posOffset = vertexIndex * 3;
            mesh.Positions[posOffset + 0] = position.X;
            mesh.Positions[posOffset + 1] = position.Y;
            mesh.Positions[posOffset + 2] = position.Z;
            mesh.Normals[posOffset + 0] = normal.X;
            mesh.Normals[posOffset + 1] = normal.Y;
            mesh.Normals[posOffset + 2] = normal.Z;
            int tangentOffset = vertexIndex * 4;
            mesh.Tangents[tangentOffset + 0] = tangent.X;
            mesh.Tangents[tangentOffset + 1] = tangent.Y;
            mesh.Tangents[tangentOffset + 2] = tangent.Z;
            mesh.Tangents[tangentOffset + 3] = tangent.W;
            int uvOffset = vertexIndex * 2;
            mesh.Uv0[uvOffset + 0] = u;
            mesh.Uv0[uvOffset + 1] = v;
        }

        private static ProceduralMeshBounds ComputeBounds(ProceduralMeshAssetData mesh, int vertexCount)
        {
            Vector3 min = new(mesh.Positions[0], mesh.Positions[1], mesh.Positions[2]);
            Vector3 max = min;
            for (int i = 1; i < vertexCount; i++)
            {
                int offset = i * 3;
                Vector3 point = new(mesh.Positions[offset + 0], mesh.Positions[offset + 1], mesh.Positions[offset + 2]);
                IncludePoint(ref min, ref max, point);
            }

            Vector3 center = (min + max) * 0.5f;
            Vector3 extents = (max - min) * 0.5f;
            if (extents.X <= 0f) extents.X = 0.01f;
            if (extents.Y <= 0f) extents.Y = 0.01f;
            if (extents.Z <= 0f) extents.Z = 0.01f;
            return new ProceduralMeshBounds(center, extents);
        }

        private static Vector3 EvaluateBezier(SurfaceSplineSegment segment, float t)
        {
            float omt = 1f - t;
            float omt2 = omt * omt;
            float omt3 = omt2 * omt;
            float t2 = t * t;
            float t3 = t2 * t;
            return (segment.P0 * omt3) +
                   (3f * segment.P1 * omt2 * t) +
                   (3f * segment.P2 * omt * t2) +
                   (segment.P3 * t3);
        }

        private static Vector3 EvaluateBezierDerivative(SurfaceSplineSegment segment, float t)
        {
            float omt = 1f - t;
            return (3f * omt * omt * (segment.P1 - segment.P0)) +
                   (6f * omt * t * (segment.P2 - segment.P1)) +
                   (3f * t * t * (segment.P3 - segment.P2));
        }

        private static void IncludePoint(ref Vector3 min, ref Vector3 max, in Vector3 point)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
    }
}
