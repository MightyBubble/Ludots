using System;
using System.Numerics;
using Arch.System;
using ChunkStreamingShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Physics;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Runtime;
using Ludots.Platform.Abstractions;
using Ludots.Core.Client;

namespace ChunkStreamingShowcaseMod.Systems
{
    internal sealed class ChunkStreamingShowcasePresentationSystem : ISystem<float>
    {
        private static readonly (RoadNetworkScenarioDefinition.RoadLandmarkId Id, Vector4 Color, float Scale)[] Landmarks =
        {
            (RoadNetworkScenarioDefinition.RoadLandmarkId.WestGate, new Vector4(0.22f, 0.66f, 0.95f, 1f), 0.62f),
            (RoadNetworkScenarioDefinition.RoadLandmarkId.CentralCrossing, new Vector4(0.92f, 0.76f, 0.28f, 1f), 0.76f),
            (RoadNetworkScenarioDefinition.RoadLandmarkId.EastGate, new Vector4(0.96f, 0.34f, 0.28f, 1f), 0.62f),
            (RoadNetworkScenarioDefinition.RoadLandmarkId.RedCapital, new Vector4(0.96f, 0.42f, 0.38f, 1f), 0.70f),
        };

        private static readonly Vector4 ChunkFill = new(0.16f, 0.23f, 0.31f, 0.16f);
        private static readonly Vector4 ChunkAnchor = new(0.29f, 0.55f, 0.74f, 0.94f);

        private readonly GameEngine _engine;
        private readonly ChunkStreamingShowcaseRuntime _runtime;
        private readonly PrimitiveDrawBuffer? _primitives;
        private readonly SplineRibbonBuffer? _roads;
        private readonly ScreenOverlayBuffer? _overlay;
        private readonly int _cubeMeshId;
        private readonly int _sphereMeshId;

        public ChunkStreamingShowcasePresentationSystem(GameEngine engine, ChunkStreamingShowcaseRuntime runtime)
        {
            _engine = engine;
            _runtime = runtime;
            _primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            _roads = engine.GetService(CoreServiceKeys.SplineRibbonBuffer);
            _overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            MeshAssetRegistry? meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            _cubeMeshId = meshes?.GetId(WellKnownMeshKeys.Cube) ?? 1;
            _sphereMeshId = meshes?.GetId(WellKnownMeshKeys.Sphere) ?? 2;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!_runtime.IsActive || _runtime.ActiveBoard == null || _runtime.Scenario == null)
            {
                return;
            }

            EmitLoadedChunkTiles();
            EmitSplineRibbons();
            EmitLandmarkMarkers();
            EmitHud();
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EmitLoadedChunkTiles()
        {
            if (_primitives == null || _runtime.ActiveBoard == null)
            {
                return;
            }

            int chunkSizeCm = _runtime.ActiveBoard.LoadedChunksSource.ChunkSizeCm;
            float chunkSizeMeters = WorldUnits.CmToM(chunkSizeCm);
            foreach (long chunkKey in _runtime.ActiveBoard.LoadedChunksSource.ActiveChunkKeys)
            {
                (int chunkX, int chunkY) = GraphChunkKey.Unpack(chunkKey);
                float centerX = WorldUnits.CmToM((chunkX * chunkSizeCm) + (chunkSizeCm / 2f));
                float centerZ = WorldUnits.CmToM((chunkY * chunkSizeCm) + (chunkSizeCm / 2f));
                _primitives.TryAdd(new PrimitiveDrawItem
                {
                    MeshAssetId = _cubeMeshId,
                    Position = new Vector3(centerX, 0.01f, centerZ),
                    Scale = new Vector3(chunkSizeMeters, 0.02f, chunkSizeMeters),
                    Color = ChunkFill,
                    RenderPath = VisualRenderPath.StaticMesh,
                    Mobility = VisualMobility.Static,
                    Flags = VisualRuntimeFlags.Visible,
                    Visibility = VisualVisibility.Visible
                });

                _primitives.TryAdd(new PrimitiveDrawItem
                {
                    MeshAssetId = _sphereMeshId,
                    Position = new Vector3(centerX, 0.08f, centerZ),
                    Scale = new Vector3(0.14f),
                    Color = ChunkAnchor,
                    RenderPath = VisualRenderPath.StaticMesh,
                    Mobility = VisualMobility.Static,
                    Flags = VisualRuntimeFlags.Visible,
                    Visibility = VisualVisibility.Visible
                });
            }
        }

        private void EmitSplineRibbons()
        {
            if (_roads == null || _runtime.ActiveBoard == null || _runtime.Scenario == null)
            {
                return;
            }

            foreach (long chunkKey in _runtime.ActiveBoard.LoadedChunksSource.ActiveChunkKeys)
            {
                if (!_runtime.Scenario.TryGetRoadRibbonChunk(chunkKey, out RoadNetworkScenarioDefinition.RoadRibbonSpec[]? chunkSplines))
                {
                    continue;
                }

                for (int i = 0; i < chunkSplines.Length; i++)
                {
                    ref readonly RoadNetworkScenarioDefinition.RoadRibbonSpec spec = ref chunkSplines[i];
                    _roads.TryAdd(
                        spec.StableId,
                        spec.P0,
                        spec.P1,
                        spec.P2,
                        spec.P3,
                        spec.Width,
                        spec.Fill,
                        spec.Border,
                        spec.BorderWidth);
                }
            }
        }

        private void EmitLandmarkMarkers()
        {
            if (_primitives == null || _runtime.Scenario == null)
            {
                return;
            }

            for (int i = 0; i < Landmarks.Length; i++)
            {
                var landmark = Landmarks[i];
                if (!_runtime.Scenario.TryGetLandmarkWorldCm(landmark.Id, out Vector3 worldCm))
                {
                    continue;
                }

                _primitives.TryAdd(new PrimitiveDrawItem
                {
                    MeshAssetId = _sphereMeshId,
                    Position = ToMeters(worldCm, yMeters: 0.42f),
                    Scale = new Vector3(landmark.Scale),
                    Color = landmark.Color,
                    RenderPath = VisualRenderPath.StaticMesh,
                    Mobility = VisualMobility.Static,
                    Flags = VisualRuntimeFlags.Visible,
                    Visibility = VisualVisibility.Visible
                });
            }
        }

        private void EmitHud()
        {
            if (_overlay == null)
            {
                return;
            }

            Vector2 cameraTarget = ClientLocalSeatAccess.ResolveAuthorityCamera(_engine).State.TargetCm;
            string camera = $"Camera ({cameraTarget.X:0},{cameraTarget.Y:0})";
            string chunks = $"Chunks {_runtime.LoadedChunkCount} | Nodes {_runtime.LoadedNodeCount} | Splines {_runtime.LoadedSplineRibbonCount}";
            string status = _runtime.LastStatus;

            _overlay.AddRect(12, 12, 920, 156, new Vector4(0.04f, 0.07f, 0.10f, 0.78f), new Vector4(0.35f, 0.51f, 0.60f, 0.92f), stableId: 9200, dirtySerial: 1);
            _overlay.AddText(24, 24, "Chunk Streaming Showcase", 22, new Vector4(0.94f, 0.96f, 0.98f, 1f), stableId: 9201, dirtySerial: 1);
            _overlay.AddText(24, 52, status, 14, new Vector4(0.90f, 0.92f, 0.95f, 1f), stableId: 9202, dirtySerial: StringHash(status));
            _overlay.AddText(24, 78, camera, 14, new Vector4(0.66f, 0.83f, 0.96f, 1f), stableId: 9203, dirtySerial: StringHash(camera));
            _overlay.AddText(24, 102, chunks, 14, new Vector4(0.94f, 0.83f, 0.57f, 1f), stableId: 9204, dirtySerial: StringHash(chunks));
            _overlay.AddText(24, 126, "Use panel buttons to jump the camera and validate that chunk tiles and road spline batches stream with the camera window.", 13, new Vector4(0.78f, 0.84f, 0.90f, 1f), stableId: 9205, dirtySerial: 1);
        }

        private static int StringHash(string value)
        {
            return StringComparer.Ordinal.GetHashCode(value);
        }

        private static Vector3 ToMeters(in Vector3 worldCm, float yMeters)
        {
            return new Vector3(WorldUnits.CmToM(worldCm.X), yMeters, WorldUnits.CmToM(worldCm.Z));
        }
    }
}
