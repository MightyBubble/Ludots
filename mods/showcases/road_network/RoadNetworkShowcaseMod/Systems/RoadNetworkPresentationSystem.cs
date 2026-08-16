using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Gameplay;
using CoreInputMod.Systems;
using RoadNetworkShowcaseMod.Runtime;
using Ludots.Platform.Abstractions;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadNetworkPresentationSystem : ISystem<float>
    {
        private static readonly QueryDescription FortQuery = new QueryDescription()
            .WithAll<WorldPositionCm, Team, Name, RoadFortTag, RoadFortControlState>();

        private static readonly QueryDescription ColumnQuery = new QueryDescription()
            .WithAll<WorldPositionCm, Team, RoadColumnTag>();

        private static readonly Vector4 ChunkFill = new(0.16f, 0.23f, 0.31f, 0.16f);
        private static readonly Vector4 Blue = new(0.20f, 0.66f, 0.98f, 1f);
        private static readonly Vector4 Red = new(0.94f, 0.30f, 0.28f, 1f);
        private static readonly Vector4 Neutral = new(0.84f, 0.73f, 0.32f, 1f);

        private readonly World _world;
        private readonly RoadNetworkShowcaseRuntime _runtime;
        private readonly GameEngine _engine;
        private readonly PrimitiveDrawBuffer? _primitives;
        private readonly ScreenOverlayBuffer? _overlay;
        private readonly PresenterCommandBuffer? _commands;
        private readonly SurfaceSourcePayloadRegistry? _surfacePayloads;
        private readonly PresenterDefinitionRegistry? _presenters;
        private readonly int _cubeMeshId;
        private readonly int _sphereMeshId;
        private readonly Dictionary<long, int> _activeSurfaceScopes = new();
        private int _roadSurfacePresenterDefinitionId;

        public RoadNetworkPresentationSystem(GameEngine engine, RoadNetworkShowcaseRuntime runtime)
        {
            _engine = engine;
            _world = engine.World;
            _runtime = runtime;
            _primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            _overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            _commands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer);
            _surfacePayloads = engine.GetService(CoreServiceKeys.SurfaceSourcePayloadRegistry);
            _presenters = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry);
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
            SyncRoadSurfaceChunks();
            EmitFortsAndColumns();
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
                (int chunkX, int chunkY) = Ludots.Core.Navigation.GraphWorld.GraphChunkKey.Unpack(chunkKey);
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
            }
        }

        private void SyncRoadSurfaceChunks()
        {
            if (_commands == null || _surfacePayloads == null || _presenters == null || _runtime.ActiveBoard == null || _runtime.Scenario == null)
            {
                return;
            }

            PresenterCommandBuffer commandBuffer = _commands;

            if (_roadSurfacePresenterDefinitionId <= 0)
            {
                _roadSurfacePresenterDefinitionId = _presenters.GetId(RoadNetworkShowcaseIds.RoadSurfacePresenterId);
            }

            var desiredChunkKeys = new HashSet<long>(_runtime.ActiveBoard.LoadedChunksSource.ActiveChunkKeys);
            foreach (long chunkKey in _runtime.ActiveBoard.LoadedChunksSource.ActiveChunkKeys)
            {
                if (!_runtime.Scenario.TryGetRoadRibbonChunk(chunkKey, out RoadNetworkScenarioDefinition.RoadRibbonSpec[]? chunkSplines))
                {
                    continue;
                }

                int scopeId = ComposeRoadSurfaceScopeId(chunkKey);
                if (!_activeSurfaceScopes.ContainsKey(chunkKey))
                {
                    if (!commandBuffer.TryAdd(new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = _roadSurfacePresenterDefinitionId,
                            ScopeTag = scopeId,
                            Source = Entity.Null,
                            AnchorKind = PresentationAnchorKind.WorldPosition,
                            Position = ResolveChunkCenter(chunkKey, _runtime.ActiveBoard.LoadedChunksSource.ChunkSizeCm),
                        }))
                    {
                        throw new InvalidOperationException("RoadNetwork showcase failed to queue CreatePresenter for authoritative road surface.");
                    }
                    _activeSurfaceScopes.Add(chunkKey, scopeId);
                }

                var segments = new SurfaceSplineSegment[chunkSplines.Length];
                for (int i = 0; i < chunkSplines.Length; i++)
                {
                    ref readonly RoadNetworkScenarioDefinition.RoadRibbonSpec spec = ref chunkSplines[i];
                    segments[i] = new SurfaceSplineSegment(spec.P0, spec.P1, spec.P2, spec.P3, spec.Width);
                }

                _surfacePayloads.SetSplineRibbon(scopeId, segments);
            }

            List<long>? chunksToRemove = null;
            foreach ((long chunkKey, int scopeId) in _activeSurfaceScopes)
            {
                if (desiredChunkKeys.Contains(chunkKey))
                {
                    continue;
                }

                chunksToRemove ??= new List<long>();
                chunksToRemove.Add(chunkKey);
                _surfacePayloads.Remove(scopeId);
                if (!commandBuffer.TryAdd(new PresenterCommand
                    {
                        CommandKind = PresenterCommandKind.DestroyPresenterScope,
                        ScopeTag = scopeId,
                    }))
                {
                    throw new InvalidOperationException("RoadNetwork showcase failed to queue DestroyPresenterScope for authoritative road surface.");
                }
            }

            if (chunksToRemove == null)
            {
                return;
            }

            for (int i = 0; i < chunksToRemove.Count; i++)
            {
                _activeSurfaceScopes.Remove(chunksToRemove[i]);
            }
        }

        private void EmitFortsAndColumns()
        {
            if (_primitives == null)
            {
                return;
            }

            foreach (ref var fortChunk in _world.Query(in FortQuery))
            {
                Span<WorldPositionCm> positions = fortChunk.GetSpan<WorldPositionCm>();
                Span<Team> teams = fortChunk.GetSpan<Team>();
                Span<RoadFortControlState> states = fortChunk.GetSpan<RoadFortControlState>();

                for (int i = 0; i < fortChunk.Count; i++)
                {
                    Vector3 position = ToMeters(positions[i].Value, yMeters: 0.55f);
                    _primitives.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = _cubeMeshId,
                        Position = position,
                        Scale = new Vector3(1.35f, 1.1f, 1.35f),
                        Color = ResolveTeamColor(teams[i].Id),
                        RenderPath = VisualRenderPath.StaticMesh,
                        Mobility = VisualMobility.Static,
                        Flags = VisualRuntimeFlags.Visible,
                        Visibility = VisualVisibility.Visible
                    });

                    if (states[i].CapturingTeamId > 0 && states[i].CaptureDurationSeconds > 0f)
                    {
                        float ratio = Math.Clamp(states[i].CaptureProgressSeconds / states[i].CaptureDurationSeconds, 0f, 1f);
                        _primitives.TryAdd(new PrimitiveDrawItem
                        {
                            MeshAssetId = _sphereMeshId,
                            Position = position + new Vector3(0f, 1.05f, 0f),
                            Scale = new Vector3(0.22f + (0.55f * ratio)),
                            Color = ResolveTeamColor(states[i].CapturingTeamId),
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            Flags = VisualRuntimeFlags.Visible,
                            Visibility = VisualVisibility.Visible
                        });
                    }
                }
            }

            foreach (ref var columnChunk in _world.Query(in ColumnQuery))
            {
                Span<WorldPositionCm> positions = columnChunk.GetSpan<WorldPositionCm>();
                Span<Team> teams = columnChunk.GetSpan<Team>();

                for (int i = 0; i < columnChunk.Count; i++)
                {
                    Entity entity = columnChunk.Entity(i);
                    bool isCommandSourceActor = ContainsCommandSource(entity);
                    _primitives.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = _sphereMeshId,
                        Position = ToMeters(positions[i].Value, yMeters: isCommandSourceActor ? 0.72f : 0.58f),
                        Scale = isCommandSourceActor ? new Vector3(0.82f) : new Vector3(0.62f),
                        Color = ResolveTeamColor(teams[i].Id),
                        RenderPath = VisualRenderPath.StaticMesh,
                        Mobility = VisualMobility.Movable,
                        Flags = VisualRuntimeFlags.Visible,
                        Visibility = VisualVisibility.Visible
                    });
                }
            }
        }

        private void EmitHud()
        {
            if (_overlay == null)
            {
                return;
            }

            int blueForts = 0;
            int redForts = 0;
            int neutralForts = 0;
            foreach (ref var fortChunk in _world.Query(in FortQuery))
            {
                Span<RoadFortControlState> states = fortChunk.GetSpan<RoadFortControlState>();
                for (int i = 0; i < fortChunk.Count; i++)
                {
                    switch (states[i].ControllerTeamId)
                    {
                        case 1:
                            blueForts++;
                            break;
                        case 2:
                            redForts++;
                            break;
                        default:
                            neutralForts++;
                            break;
                    }
                }
            }

            string groundDebug = ResolveDebugValue(LocalOrderSourceHelper.LastGroundWorldDebugKey, "<none>");
            string orderDebug = ResolveDebugValue(LocalOrderSourceHelper.LastOrderDebugKey, "<none>");
            string selectionDebug = DescribeCommandSourceSummary();

            _overlay.AddRect(12, 12, 980, 170, new Vector4(0.04f, 0.07f, 0.10f, 0.78f), new Vector4(0.35f, 0.51f, 0.60f, 0.92f), stableId: 8000, dirtySerial: 1);
            _overlay.AddText(24, 24, "Road Network Showcase", 22, new Vector4(0.94f, 0.96f, 0.98f, 1f), stableId: 8001, dirtySerial: 1);
            _overlay.AddText(24, 50, $"Blue forts {blueForts} | Red forts {redForts} | Neutral forts {neutralForts}", 16, new Vector4(0.78f, 0.84f, 0.90f, 1f), stableId: 8002, dirtySerial: blueForts ^ (redForts << 8) ^ (neutralForts << 16));
            _overlay.AddText(24, 72, $"Loaded chunks {_runtime.LoadedChunkCount} | Loaded nodes {_runtime.LoadedNodeCount}", 16, new Vector4(0.78f, 0.84f, 0.90f, 1f), stableId: 8003, dirtySerial: _runtime.LoadedChunkCount ^ (_runtime.LoadedNodeCount << 8));
            _overlay.AddText(24, 94, "LMB chooses command columns, RMB dispatches along roads, Shift queues, pan camera to stream chunks, Home resets camera.", 15, new Vector4(0.93f, 0.79f, 0.45f, 1f), stableId: 8004, dirtySerial: 1);
            _overlay.AddText(24, 116, selectionDebug, 13, new Vector4(0.94f, 0.84f, 0.70f, 1f), stableId: 8008, dirtySerial: selectionDebug.GetHashCode(StringComparison.Ordinal));
            _overlay.AddText(24, 138, $"Ground {groundDebug} | Order {orderDebug}", 13, new Vector4(0.68f, 0.83f, 0.96f, 1f), stableId: 8006, dirtySerial: HashCode.Combine(groundDebug.GetHashCode(StringComparison.Ordinal), orderDebug.GetHashCode(StringComparison.Ordinal)));
            string status = _runtime.LastSubmitStatus;
            _overlay.AddText(24, 160, status, 14, new Vector4(0.90f, 0.92f, 0.95f, 1f), stableId: 8005, dirtySerial: status.GetHashCode(StringComparison.Ordinal));
        }

        private string ResolveDebugValue(string key, string fallback)
        {
            if (_engine.GlobalContext.TryGetValue(key, out object? value) &&
                value is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return fallback;
        }

        private string DescribeCommandSourceSummary()
        {
            Entity[] selected = SnapshotCommandSource();
            if (selected.Length <= 0)
            {
                return "Command source 0 | Primary <none>";
            }

            string primary = TryGetCommandSourcePrimary(out Entity actor) && _world.IsAlive(actor)
                ? DescribeActorLabel(actor)
                : "<none>";

            var text = new System.Text.StringBuilder();
            text.Append("Command source ").Append(selected.Length).Append(" | Primary ").Append(primary).Append(" | ");
            for (int i = 0; i < selected.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(DescribeActorLabel(selected[i]));
            }

            return text.ToString();
        }

        private string DescribeActorLabel(Entity actor)
        {
            string name = _world.Has<Name>(actor) ? _world.Get<Name>(actor).Value : $"#{actor.Id}";
            return $"{name}#{actor.Id}";
        }

        private bool TryResolveObservedActor(out Entity actor)
        {
            actor = default;
            if (TryGetCommandSourcePrimary(out Entity selected) &&
                _world.IsAlive(selected))
            {
                actor = selected;
                return true;
            }

            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? actorObj) &&
                actorObj is Entity local &&
                _world.IsAlive(local))
            {
                actor = local;
                return true;
            }

            return false;
        }

        private Entity[] SnapshotCommandSource()
        {
            return TryResolveLocalCommandSourceOwner(out Entity owner)
                ? EntityCollectionContextRuntime.Snapshot(_engine.GlobalContext, owner, EntityCollectionKeys.CommandSource)
                : Array.Empty<Entity>();
        }

        private bool TryGetCommandSourcePrimary(out Entity entity)
        {
            entity = Entity.Null;
            return TryResolveLocalCommandSourceOwner(out Entity owner) &&
                   EntityCollectionContextRuntime.TryGetPrimary(
                       _world,
                       _engine.GlobalContext,
                       owner,
                       EntityCollectionKeys.CommandSource,
                       out entity);
        }

        private bool ContainsCommandSource(Entity entity)
        {
            return TryResolveLocalCommandSourceOwner(out Entity owner) &&
                   EntityCollectionContextRuntime.Contains(
                       _world,
                       _engine.GlobalContext,
                       owner,
                       EntityCollectionKeys.CommandSource,
                       entity);
        }

        private bool TryResolveLocalCommandSourceOwner(out Entity owner)
        {
            owner = Entity.Null;
            Entity local = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (local == Entity.Null || !_world.IsAlive(local))
            {
                return false;
            }

            owner = local;
            return true;
        }

        private static string FormatFixVec(in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 value)
        {
            return $"({value.X.ToFloat():0},{value.Y.ToFloat():0})";
        }

        private static Vector3 ToMeters(in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 worldCm, float yMeters)
        {
            return new Vector3(WorldUnits.CmToM(worldCm.X.ToFloat()), yMeters, WorldUnits.CmToM(worldCm.Y.ToFloat()));
        }

        private static Vector4 ResolveTeamColor(int teamId)
        {
            return teamId switch
            {
                1 => Blue,
                2 => Red,
                _ => Neutral
            };
        }

        private static int ComposeRoadSurfaceScopeId(long chunkKey)
        {
            unchecked
            {
                return 500000000 + (int)(chunkKey ^ (chunkKey >> 32));
            }
        }

        private static Vector3 ResolveChunkCenter(long chunkKey, int chunkSizeCm)
        {
            (int chunkX, int chunkY) = Ludots.Core.Navigation.GraphWorld.GraphChunkKey.Unpack(chunkKey);
            float centerX = WorldUnits.CmToM((chunkX * chunkSizeCm) + (chunkSizeCm / 2f));
            float centerZ = WorldUnits.CmToM((chunkY * chunkSizeCm) + (chunkSizeCm / 2f));
            return new Vector3(centerX, 0f, centerZ);
        }
    }
}
