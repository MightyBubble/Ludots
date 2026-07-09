using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using CameraAcceptanceMod.UI;
using CoreInputMod;
using CoreInputMod.ViewMode;
using CoreInputMod.Triggers;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace CameraAcceptanceMod.Runtime
{
    internal sealed class CameraAcceptanceRuntime
    {
        private const string AcceptanceModePrefix = "Camera.Acceptance.Mode.";
        private const float TwoPiRadians = 6.2831855f;
        private const float GoldenAngleRadians = 2.3999631f;
        private const float ProjectionScatterSpacingCm = 120f;
        private const float ProjectionScatterJitterCm = 42f;
        private const string CommandSourceTitle = "Camera acceptance command source";
        private const string CommandSourceSummary = "Map-owned camera actors.";

        private CameraAcceptancePanelController? _panelController;
        private bool _commandSourceAcquiredCallbacksInstalled;
        private const string ProjectionCueFixturePrefabKey = "camera_acceptance_projection_cue_fixture_prefab";
        private int _cueMarkerPrefabId;
        private string _lastConfiguredMapId = string.Empty;

        internal static void InitializeProjectionSpawnCount(GameEngine engine)
        {
            if (!engine.GlobalContext.ContainsKey(CameraAcceptanceIds.ProjectionSpawnCountKey))
            {
                engine.GlobalContext[CameraAcceptanceIds.ProjectionSpawnCountKey] = CameraAcceptanceIds.ProjectionSpawnCountDefault;
            }
        }

        internal static int ResolveProjectionSpawnCount(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CameraAcceptanceIds.ProjectionSpawnCountKey, out var value) &&
                   value is int count &&
                   count >= 0
                ? count
                : CameraAcceptanceIds.ProjectionSpawnCountDefault;
        }

        internal static int AdjustProjectionSpawnCount(GameEngine engine, int delta)
        {
            int next = ResolveProjectionSpawnCount(engine) + delta;
            if (next < 0)
            {
                next = 0;
            }

            engine.GlobalContext[CameraAcceptanceIds.ProjectionSpawnCountKey] = next;
            return next;
        }

        public void InstallCommandSourceAcquiredCallbacks(GameEngine engine)
        {
            if (_commandSourceAcquiredCallbacksInstalled)
            {
                return;
            }

            if (!CoreInputRuntimeServices.TryGetCommandSourceAcquiredCallbacks(engine, out var callbacks))
            {
                throw new System.InvalidOperationException(
                    "CameraAcceptanceMod requires CoreInputMod command-source acquisition callbacks to be installed before GameStart handlers run.");
            }

            callbacks.Add((worldCm, entity) => HandleSelectionConfirmed(engine, worldCm, entity));
            _commandSourceAcquiredCallbacksInstalled = true;
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (CameraAcceptanceIds.IsAcceptanceMap(activeMapId))
            {
                EnsureLocalCommandSourceOwner(engine, activeMapId, out Entity owner);
                if (string.Equals(activeMapId, CameraAcceptanceIds.FollowMapId, System.StringComparison.OrdinalIgnoreCase) &&
                    owner != Entity.Null)
                {
                    RequestCollectionFollowCamera(engine, CameraAcceptanceIds.FollowCloseCameraId, owner);
                }
            }

            SyncMapScopedInputOwnership(engine);
            BindFlatGroundHeightmap(engine);
            ConfigureRenderDefaultsForMap(engine);
            RefreshPanel(engine);

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine != null)
            {
                SyncMapScopedInputOwnership(engine);
            }

            var mapId = context.Get(CoreServiceKeys.MapId);
            if (CameraAcceptanceIds.IsAcceptanceMap(mapId.Value))
            {
                if (string.Equals(_lastConfiguredMapId, mapId.Value, System.StringComparison.OrdinalIgnoreCase))
                {
                    _lastConfiguredMapId = string.Empty;
                }

                ClearPanelIfOwned(context);
            }

            return Task.CompletedTask;
        }

        private static bool EnsureLocalCommandSourceOwner(GameEngine engine, string? mapId, out Entity owner)
        {
            owner = Entity.Null;
            if (engine == null || !CameraAcceptanceIds.IsAcceptanceMap(mapId))
            {
                return false;
            }

            if (!TryFindEntityByName(engine.World, CameraAcceptanceIds.HeroName, out Entity hero))
            {
                return false;
            }

            owner = hero;
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, owner);
            if (engine.CurrentMapSession != null)
            {
                engine.CurrentMapSession.LocalPlayerEntity = owner;
            }

            if (TryResolvePlayerId(engine.World, owner, out int playerId))
            {
                engine.SetService(CoreServiceKeys.LocalPlayerId, playerId);
                if (engine.CurrentMapSession != null)
                {
                    engine.CurrentMapSession.LocalPlayerId = playerId;
                }
            }

            PublishEmptyCommandSourceCollection(engine, owner);
            PublishLocalKnowledge(engine, owner);
            return true;
        }

        private static bool TryResolvePlayerId(World world, Entity owner, out int playerId)
        {
            playerId = 0;
            if (owner == Entity.Null || !world.IsAlive(owner) || !world.Has<PlayerOwner>(owner))
            {
                return false;
            }

            playerId = world.Get<PlayerOwner>(owner).PlayerId;
            return playerId > 0;
        }

        private static void PublishEmptyCommandSourceCollection(GameEngine engine, Entity owner)
        {
            if (engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
            {
                return;
            }

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                contextEntity: owner,
                primaryEntity: Entity.Null,
                title: CommandSourceTitle,
                summary: CommandSourceSummary);
            collections.Replace(owner, in descriptor, ReadOnlySpan<Entity>.Empty, owner);
        }

        private static void PublishLocalKnowledge(GameEngine engine, Entity owner)
        {
            if (engine.GetService(CoreServiceKeys.KnowledgeProjectionStore) is not KnowledgeProjectionStore knowledge)
            {
                return;
            }

            PublishLiveKnowledge(engine, knowledge, owner, owner);
            if (TryFindEntityByName(engine.World, CameraAcceptanceIds.ScoutName, out Entity scout))
            {
                PublishLiveKnowledge(engine, knowledge, owner, scout);
            }

            if (TryFindEntityByName(engine.World, CameraAcceptanceIds.CaptainName, out Entity captain))
            {
                PublishLiveKnowledge(engine, knowledge, owner, captain);
            }
        }

        private static void PublishLiveKnowledge(
            GameEngine engine,
            KnowledgeProjectionStore knowledge,
            Entity owner,
            Entity target)
        {
            if (owner == Entity.Null ||
                target == Entity.Null ||
                !engine.World.IsAlive(owner) ||
                !engine.World.IsAlive(target))
            {
                return;
            }

            var record = new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                KnowledgeIdMask256.Empty,
                owner,
                engine.GameSession?.CurrentTick ?? 0,
                expiryTick: 0,
                confidencePermille: 1000,
                revision: 0);
            knowledge.Upsert(owner, target, in record);
        }

        private static void RequestCollectionFollowCamera(GameEngine engine, string cameraId, Entity owner)
        {
            if (owner == Entity.Null ||
                !engine.World.IsAlive(owner) ||
                engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is not VirtualCameraRegistry registry ||
                !registry.TryGet(cameraId, out var definition) ||
                definition == null)
            {
                return;
            }

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = cameraId,
                BlendDurationSeconds = 0f,
                FollowTargetKindOverride = CameraFollowTargetKind.EntityCollectionPrimary,
                FollowCollectionOwnerOverride = owner,
                FollowCollectionKeyOverride = EntityCollectionKeys.CommandSource,
                SnapToFollowTargetWhenAvailable = definition.SnapToFollowTargetWhenAvailable,
                ResetRuntimeState = true,
                ReplaceActiveStack = true
            });
        }

        private static bool TryFindEntityByName(World world, string name, out Entity result)
        {
            result = Entity.Null;
            if (world == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (found == Entity.Null &&
                    string.Equals(entityName.Value, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    found = entity;
                }
            });
            result = found;
            return result != Entity.Null && world.IsAlive(result);
        }

        private static void BindFlatGroundHeightmap(GameEngine engine)
        {
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (!CameraAcceptanceIds.IsAcceptanceMap(activeMapId) ||
                engine.GetService(CoreServiceKeys.VisualHeightmap) != null)
            {
                return;
            }

            var heightmap = new FlatVisualHeightmap();
            engine.CurrentMapSession!.VisualHeightmap = heightmap;
            engine.SetService(CoreServiceKeys.VisualHeightmap, heightmap);
        }

        internal static void SyncMapScopedInputOwnership(GameEngine engine)
        {
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            bool isAcceptanceMap = CameraAcceptanceIds.IsAcceptanceMap(activeMapId);

            if (engine.GetService(CoreServiceKeys.InputHandler) is PlayerInputHandler input)
            {
                if (isAcceptanceMap)
                {
                    input.PushContext(CameraAcceptanceIds.InputContextId);
                }
                else
                {
                    input.PopContext(CameraAcceptanceIds.InputContextId);
                }
            }

            if (!CoreInputRuntimeServices.TryGetViewModeManager(engine, out var manager))
            {
                return;
            }

            string activeModeId = manager.ActiveMode?.Id ?? string.Empty;
            if (!isAcceptanceMap &&
                activeModeId.StartsWith(AcceptanceModePrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                manager.ClearActiveMode();
            }
        }

        private CameraAcceptancePanelController EnsurePanelController(GameEngine engine)
        {
            if (_panelController != null)
            {
                return _panelController;
            }

            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _panelController = new CameraAcceptancePanelController(textMeasurer, imageSizeProvider);
            return _panelController;
        }

        public void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            if (!EnsureUiSurfaceHost(engine, root))
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug &&
                !renderDebug.DrawSkiaUi)
            {
                ClearPanelIfOwned(engine);
                return;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (!CameraAcceptanceIds.IsAcceptanceMap(activeMapId))
            {
                ClearPanelIfOwned(engine);
                return;
            }

            var panelController = EnsurePanelController(engine);
            panelController.MountOrSync(root, engine);
            if (engine.GetService(CameraAcceptanceServiceKeys.DiagnosticsState) is CameraAcceptanceDiagnosticsState diagnostics)
            {
                diagnostics.ObservePanelUpdate(
                    panelController.LastUpdateStats,
                    panelController.LastUpdateMetrics,
                    panelController.LastSelectionRowsTouched,
                    panelController.RowPoolSize,
                    panelController.FullRecomposeCount,
                    panelController.IncrementalPatchCount);
            }
        }

        private static bool EnsureUiSurfaceHost(GameEngine engine, UIRoot root)
        {
            if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost)
            {
                return true;
            }

            if (engine.GetService(CoreServiceKeys.UiTextMeasurer) is not IUiTextMeasurer textMeasurer ||
                engine.GetService(CoreServiceKeys.UiImageSizeProvider) is not IUiImageSizeProvider imageSizeProvider)
            {
                return false;
            }

            engine.SetService(CoreServiceKeys.UiSurfaceHost, (object)new UiSurfaceHost(root, textMeasurer, imageSizeProvider));
            return true;
        }

        private void ClearPanelIfOwned(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return;
            }

            ClearPanelIfOwned(engine);
        }

        private void ClearPanelIfOwned(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController?.ClearIfOwned(root);
        }

        private void ConfigureRenderDefaultsForMap(GameEngine engine)
        {
            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (string.IsNullOrWhiteSpace(mapId) ||
                !CameraAcceptanceIds.IsAcceptanceMap(mapId) ||
                string.Equals(_lastConfiguredMapId, mapId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
            {
                return;
            }

            bool isHotpathMap = string.Equals(mapId, CameraAcceptanceIds.HotpathMapId, System.StringComparison.OrdinalIgnoreCase);
            renderDebug.DrawSkiaUi = true;
            renderDebug.DrawPrimitives = true;
            renderDebug.DrawTerrain = !isHotpathMap;
            renderDebug.DrawDebugDraw = !isHotpathMap;
            _lastConfiguredMapId = mapId;
        }

        private void HandleSelectionConfirmed(GameEngine engine, in WorldCmInt2 worldCm, Entity selectedEntity)
        {
            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                if (engine.World.IsAlive(selectedEntity))
                {
                    return;
                }

                EnqueueProjectionSpawnBatch(engine, worldCm);
                EmitCueMarker(engine, worldCm);
                return;
            }

            if (string.Equals(mapId, CameraAcceptanceIds.BlendMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                string cameraId = ResolveActiveBlendCameraId(engine);
                engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
                {
                    Id = cameraId,
                    SnapToFollowTargetWhenAvailable = false,
                    ResetRuntimeState = true
                });
                engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
                {
                    VirtualCameraId = cameraId,
                    TargetCm = new Vector2(worldCm.X, worldCm.Y)
                });
            }
        }

        private void EmitCueMarker(GameEngine engine, in WorldCmInt2 worldCm)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.TransientMarkerBuffer.Name, out var markersObj) ||
                markersObj is not TransientMarkerBuffer markers)
            {
                throw new System.InvalidOperationException("TransientMarkerBuffer is required for projection verification.");
            }

            markers.TryAddPrefab(
                ResolveCueMarkerPrefabId(engine),
                WorldUnits.WorldCmToVisualMeters(worldCm, yMeters: 0.15f),
                new Vector3(0.45f),
                new Vector4(0.15f, 0.88f, 1f, 1f),
                0.45f);
        }

        private static void EnqueueProjectionSpawnBatch(GameEngine engine, in WorldCmInt2 worldCm)
        {
            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                throw new System.InvalidOperationException("RuntimeEntitySpawnQueue is required for projection verification.");
            }

            var bounds = engine.CurrentMapSession?.PrimaryBoard?.WorldSize.Bounds ?? engine.WorldSizeSpec.Bounds;
            int spawnCount = ResolveProjectionSpawnCount(engine);
            for (int i = 0; i < spawnCount; i++)
            {
                WorldCmInt2 spawnWorldCm = ResolveProjectionSpawnPosition(worldCm, i);
                spawnWorldCm = GroundRaycastUtil.ClampWorldCmToBounds(spawnWorldCm, bounds, out _);
                var request = new RuntimeEntitySpawnRequest
                {
                    Kind = RuntimeEntitySpawnKind.Template,
                    TemplateId = CameraAcceptanceIds.ProjectionSpawnTemplateId,
                    WorldPositionCm = Fix64Vec2.FromInt(spawnWorldCm.X, spawnWorldCm.Y),
                    HasWorldPosition = 1,
                    MapId = engine.CurrentMapSession?.MapId ?? default,
                };

                if (!spawnQueue.TryEnqueue(request))
                {
                    throw new System.InvalidOperationException("Projection verification spawn queue is full.");
                }
            }
        }

        private static WorldCmInt2 ResolveProjectionSpawnPosition(in WorldCmInt2 center, int index)
        {
            if (index <= 0)
            {
                return center;
            }

            uint seed = Hash((uint)center.X) ^ RotateLeft(Hash((uint)center.Y), 13) ^ RotateLeft((uint)index * 0x9E3779B9u, 7);
            float baseAngle = Hash01(seed ^ 0xA511E9B3u) * TwoPiRadians;
            float jitterAngle = (Hash01(seed ^ 0x63D83595u) - 0.5f) * 0.42f;
            float ringRadius = ProjectionScatterSpacingCm * MathF.Sqrt(index);
            float jitterRadius = (Hash01(seed ^ 0xC2B2AE35u) - 0.5f) * ProjectionScatterJitterCm;
            float radius = MathF.Max(ProjectionScatterSpacingCm * 0.4f, ringRadius + jitterRadius);
            float angle = baseAngle + index * GoldenAngleRadians + jitterAngle;
            int x = center.X + (int)MathF.Round(MathF.Cos(angle) * radius);
            int y = center.Y + (int)MathF.Round(MathF.Sin(angle) * radius);
            return new WorldCmInt2(x, y);
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }

        private static uint RotateLeft(uint value, int amount)
        {
            return (value << amount) | (value >> (32 - amount));
        }

        private static float Hash01(uint seed)
        {
            return (Hash(seed) & 0x00FFFFFFu) / 16777216f;
        }

        private int ResolveCueMarkerPrefabId(GameEngine engine)
        {
            if (_cueMarkerPrefabId != 0)
            {
                return _cueMarkerPrefabId;
            }

            if (engine.GetService(CoreServiceKeys.PresentationPrefabRegistry) is not PrefabRegistry prefabs)
            {
                throw new System.InvalidOperationException("PresentationPrefabRegistry is required for projection verification.");
            }

            _cueMarkerPrefabId = prefabs.GetId(ProjectionCueFixturePrefabKey);
            if (_cueMarkerPrefabId == 0)
            {
                throw new System.InvalidOperationException($"Prefab '{ProjectionCueFixturePrefabKey}' is required for projection verification.");
            }

            return _cueMarkerPrefabId;
        }

        private static string ResolveActiveBlendCameraId(GameEngine engine)
        {
            return engine.GlobalContext.TryGetValue(CameraAcceptanceIds.ActiveBlendCameraIdKey, out var value) &&
                   value is string cameraId &&
                   !string.IsNullOrWhiteSpace(cameraId)
                ? cameraId
                : CameraAcceptanceIds.BlendSmoothCameraId;
        }

    }
}
