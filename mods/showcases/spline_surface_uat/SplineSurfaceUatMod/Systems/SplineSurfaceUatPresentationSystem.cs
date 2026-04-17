using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Scripting;
using SplineSurfaceUatMod.Runtime;

namespace SplineSurfaceUatMod.Systems
{
    internal sealed class SplineSurfaceUatPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly SplineSurfaceUatRuntime _runtime;
        private readonly PerformerCommandBuffer? _commands;
        private readonly SurfaceSourcePayloadRegistry? _payloads;
        private readonly PerformerDefinitionRegistry? _performers;
        private readonly PresentationMaterialRegistry? _materials;
        private bool _bound;
        private int _roadPerformerId;
        private int _riverPerformerId;
        private int _lakePerformerId;
        private int _rawPerformerId;

        public SplineSurfaceUatPresentationSystem(GameEngine engine, SplineSurfaceUatRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
            _commands = engine.GetService(CoreServiceKeys.PerformerCommandBuffer);
            _payloads = engine.GetService(CoreServiceKeys.SurfaceSourcePayloadRegistry);
            _performers = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry);
            _materials = engine.GetService(CoreServiceKeys.PresentationMaterialRegistry);
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (_commands == null || _payloads == null || _performers == null || _materials == null)
            {
                return;
            }

            if (!_runtime.IsActive)
            {
                if (_bound)
                {
                    Teardown();
                }

                return;
            }

            if (_bound)
            {
                _runtime.SyncPanel(_engine);
                return;
            }

            ResolvePerformerIds();
            BindSurface(SplineSurfaceUatIds.RoadScopeId, _roadPerformerId, SplineSurfaceUatIds.RoadAnchorWorld);
            _payloads.SetSplineRibbon(SplineSurfaceUatIds.RoadScopeId, BuildRoadSegments());

            BindSurface(SplineSurfaceUatIds.RiverScopeId, _riverPerformerId, SplineSurfaceUatIds.RiverAnchorWorld);
            _payloads.SetSplineRibbon(SplineSurfaceUatIds.RiverScopeId, BuildRiverSegments());

            BindSurface(SplineSurfaceUatIds.LakeScopeId, _lakePerformerId, SplineSurfaceUatIds.LakeAnchorWorld);
            _payloads.SetClosedArea(SplineSurfaceUatIds.LakeScopeId, BuildLakeBoundary());

            BindSurface(SplineSurfaceUatIds.RawScopeId, _rawPerformerId, SplineSurfaceUatIds.RawAnchorWorld);
            _payloads.SetRawProceduralMesh(SplineSurfaceUatIds.RawScopeId, BuildRawMesh(), SplineSurfaceUatIds.RawAnchorWorld);

            _bound = true;
            _runtime.SyncPanel(_engine);
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void ResolvePerformerIds()
        {
            _roadPerformerId = _performers!.GetId(SplineSurfaceUatIds.RoadPerformerId);
            _riverPerformerId = _performers.GetId(SplineSurfaceUatIds.RiverPerformerId);
            _lakePerformerId = _performers.GetId(SplineSurfaceUatIds.LakePerformerId);
            _rawPerformerId = _performers.GetId(SplineSurfaceUatIds.RawPerformerId);
        }

        private void BindSurface(int scopeId, int performerDefinitionId, in Vector3 worldAnchor)
        {
            if (performerDefinitionId <= 0)
            {
                throw new InvalidOperationException("Spline surface UAT performer definitions must be registered before presentation bind.");
            }

            if (!_commands!.TryAdd(new PerformerCommand
                {
                    CommandKind = PerformerCommandKind.CreatePerformer,
                    PerformerDefinitionId = performerDefinitionId,
                    ScopeTag = scopeId,
                    Source = Entity.Null,
                    AnchorKind = PresentationAnchorKind.WorldPosition,
                    Position = worldAnchor,
                }))
            {
                throw new InvalidOperationException($"Spline surface UAT failed to create performer scope {scopeId}.");
            }
        }

        private void Teardown()
        {
            RemoveSurface(SplineSurfaceUatIds.RoadScopeId);
            RemoveSurface(SplineSurfaceUatIds.RiverScopeId);
            RemoveSurface(SplineSurfaceUatIds.LakeScopeId);
            RemoveSurface(SplineSurfaceUatIds.RawScopeId);
            _bound = false;
        }

        private void RemoveSurface(int scopeId)
        {
            _payloads!.Remove(scopeId);
            if (!_commands!.TryAdd(new PerformerCommand
                {
                    CommandKind = PerformerCommandKind.DestroyPerformerScope,
                    ScopeTag = scopeId,
                }))
            {
                throw new InvalidOperationException($"Spline surface UAT failed to destroy performer scope {scopeId}.");
            }
        }

        private SurfaceSplineSegment[] BuildRoadSegments()
        {
            return new[]
            {
                new SurfaceSplineSegment(
                    new Vector3(-18f, 0f, -10f),
                    new Vector3(-16f, 0f, -8f),
                    new Vector3(-12f, 0f, -4f),
                    new Vector3(-8f, 0f, -2f),
                    2.4f),
                new SurfaceSplineSegment(
                    new Vector3(-8f, 0f, -2f),
                    new Vector3(-5f, 0f, -1f),
                    new Vector3(-2f, 0f, -1f),
                    new Vector3(2f, 0f, 0f),
                    2.4f),
            };
        }

        private SurfaceSplineSegment[] BuildRiverSegments()
        {
            return new[]
            {
                new SurfaceSplineSegment(
                    new Vector3(-14f, 0f, 10f),
                    new Vector3(-10f, 0f, 12f),
                    new Vector3(-6f, 0f, 11f),
                    new Vector3(-2f, 0f, 9f),
                    3.2f),
                new SurfaceSplineSegment(
                    new Vector3(-2f, 0f, 9f),
                    new Vector3(1f, 0f, 7f),
                    new Vector3(5f, 0f, 6f),
                    new Vector3(9f, 0f, 7f),
                    3.2f),
            };
        }

        private static Vector3[] BuildLakeBoundary()
        {
            return new[]
            {
                new Vector3(6f, 0f, -8f),
                new Vector3(10f, 0f, -10f),
                new Vector3(14f, 0f, -8f),
                new Vector3(15f, 0f, -4f),
                new Vector3(12f, 0f, -1f),
                new Vector3(8f, 0f, -2f),
                new Vector3(5f, 0f, -5f),
            };
        }

        private ProceduralMeshAssetData BuildRawMesh()
        {
            int materialId = _materials!.GetId(PresentationMaterialRegistry.DefaultSurfaceKey);
            if (materialId <= 0)
            {
                throw new InvalidOperationException("Spline surface UAT raw mesh requires the registered default surface material.");
            }

            var mesh = new ProceduralMeshAssetData(maxVertexCount: 4, maxIndexCount: 6);
            WriteVertex(mesh, 0, new Vector3(-2.5f, 0f, -2.5f), 0f, 0f);
            WriteVertex(mesh, 1, new Vector3(2.5f, 0f, -2.5f), 1f, 0f);
            WriteVertex(mesh, 2, new Vector3(2.5f, 0f, 2.5f), 1f, 1f);
            WriteVertex(mesh, 3, new Vector3(-2.5f, 0f, 2.5f), 0f, 1f);

            mesh.Indices[0] = 0;
            mesh.Indices[1] = 2;
            mesh.Indices[2] = 1;
            mesh.Indices[3] = 0;
            mesh.Indices[4] = 3;
            mesh.Indices[5] = 2;

            mesh.Commit(
                vertexCount: 4,
                indexCount: 6,
                submeshes: new[] { new ProceduralSubmeshDescriptor(0, 6, materialId) },
                localBounds: new ProceduralMeshBounds(Vector3.Zero, new Vector3(2.5f, 0.05f, 2.5f)),
                usageHint: ProceduralMeshUsageHint.Static);
            return mesh;
        }

        private static void WriteVertex(ProceduralMeshAssetData mesh, int vertexIndex, in Vector3 position, float u, float v)
        {
            int posOffset = vertexIndex * 3;
            mesh.Positions[posOffset + 0] = position.X;
            mesh.Positions[posOffset + 1] = position.Y;
            mesh.Positions[posOffset + 2] = position.Z;

            mesh.Normals[posOffset + 0] = 0f;
            mesh.Normals[posOffset + 1] = 1f;
            mesh.Normals[posOffset + 2] = 0f;

            int tangentOffset = vertexIndex * 4;
            mesh.Tangents[tangentOffset + 0] = 1f;
            mesh.Tangents[tangentOffset + 1] = 0f;
            mesh.Tangents[tangentOffset + 2] = 0f;
            mesh.Tangents[tangentOffset + 3] = 1f;

            int uvOffset = vertexIndex * 2;
            mesh.Uv0[uvOffset + 0] = u;
            mesh.Uv0[uvOffset + 1] = v;
        }
    }
}
