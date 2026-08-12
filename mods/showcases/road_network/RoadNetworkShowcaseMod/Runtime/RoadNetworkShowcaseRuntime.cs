using Arch.Core;
using Ludots.Core.Components;
using System.Threading.Tasks;
using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Knowledge;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using CoreInputMod.ViewMode;
using RoadNetworkShowcaseMod.Gameplay;
using RoadNetworkShowcaseMod.UI;
using Ludots.UI;
using Ludots.Core.Gameplay.GAS.Orders;

namespace RoadNetworkShowcaseMod.Runtime
{
    internal sealed class RoadNetworkShowcaseRuntime
    {
        private static readonly QueryDescription SelectableKnowledgeQuery = new QueryDescription()
            .WithAll<CommandSourceSelectableTag, MapEntity>();

        private const string PrimaryPlayerColumnName = "Blue Vanguard";
        private const string TacticalCameraModeId = "Camera.Mode.Tactical";
        private const string TacticalCameraId = "Camera.Profile.Tactical";
        private string? _activeMapId;
        private readonly RoadNetworkShowcasePanelController _panelController;
        private readonly RoadNetworkShowcaseDebugLogWriter _debugLogWriter;

        public enum ShowcaseCommandPreset : byte
        {
            None = 0,
            LongHaulToRedCapital = 1,
            NorthFlankToNorthWatch = 2,
            SouthGuardToSouthWatch = 3,
        }

        public RoadNetworkShowcaseRuntime()
        {
            _panelController = new RoadNetworkShowcasePanelController(this);
            _debugLogWriter = new RoadNetworkShowcaseDebugLogWriter();
        }

        public NodeGraphBoard? ActiveBoard { get; private set; }
        public RoadNetworkScenarioDefinition? Scenario { get; private set; }
        public bool IsActive => ActiveBoard != null && Scenario != null;
        public string LastSubmitStatus { get; private set; } = "Road command ready. Use the command action near a road or fort.";
        public string? LatestDebugSnapshotPath => _debugLogWriter.SnapshotPath;

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (!RoadNetworkShowcaseIds.IsShowcaseMap(mapId) ||
                engine.CurrentMapSession?.PrimaryBoard is not NodeGraphBoard board)
            {
                Unbind(engine);
                return Task.CompletedTask;
            }

            if (ReferenceEquals(board, ActiveBoard) && string.Equals(mapId, _activeMapId))
            {
                return Task.CompletedTask;
            }

            Unbind(engine);
            _activeMapId = mapId;
            ActiveBoard = board;
            Scenario = RoadNetworkScenarioDefinition.Create(board.LoadedChunksSource.ChunkSizeCm);
            engine.GlobalContext[RoadNetworkShowcaseIds.ScenarioServiceKey] = Scenario;
            engine.GlobalContext[RoadNetworkShowcaseIds.GraphLoadedChunksServiceKey] = board.LoadedChunksSource;
            board.LoadedChunksSource.ChunkLoaded += HandleChunkLoaded;

            EnsureShowcaseProfiles(engine.World);
            Vector2 initialCameraTargetCm = ApplyInitialPlayableCamera(engine);
            PrimeInitialChunkWindow(engine, initialCameraTargetCm);
            EnsurePrimaryPlayerControl(engine);
            PublishSelectableKnowledge(engine);
            RefreshPanel(engine);

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            var mapId = context.Get(CoreServiceKeys.MapId);
            if (RoadNetworkShowcaseIds.IsShowcaseMap(mapId.Value))
            {
                Unbind(engine);
            }

            return Task.CompletedTask;
        }

        public void UpdateLoadedChunks(GameEngine engine)
        {
            if (!IsActive || Scenario == null || ActiveBoard == null)
            {
                return;
            }

            EnsurePrimaryPlayerControl(engine);
            PublishSelectableKnowledge(engine);

            if (engine.GlobalContext.TryGetValue(RoadMoveOrderExpander.LastSubmitStatusKey, out var statusObj) &&
                statusObj is string status &&
                !string.IsNullOrWhiteSpace(status))
            {
                LastSubmitStatus = status;
            }

            var target = engine.GameSession.Camera.State.TargetCm;
            ActiveBoard.LoadedChunksSource.Update(
                (int)target.X,
                (int)target.Y,
                Scenario.StreamingRadiusCm);
            RefreshPanel(engine);
        }

        public int LoadedChunkCount => ActiveBoard?.LoadedChunksSource.ActiveChunkKeys.Count ?? 0;
        public int LoadedNodeCount => ActiveBoard?.GraphRuntime.CurrentGraph.NodeCount ?? 0;

        public bool TryResetCamera(GameEngine engine)
        {
            if (engine == null || !IsActive)
            {
                return false;
            }

            ApplyInitialPlayableCamera(engine);
            LastSubmitStatus = "Camera reset to blue staging view.";
            return true;
        }

        public bool TryFocusLandmark(GameEngine engine, RoadNetworkScenarioDefinition.RoadLandmarkId landmarkId, string status)
        {
            if (!IsActive || Scenario == null || !Scenario.TryGetLandmarkWorldCm(landmarkId, out Vector3 landmarkWorldCm))
            {
                return false;
            }

            RequestTacticalCameraTarget(engine, new Vector2(landmarkWorldCm.X, landmarkWorldCm.Z));
            LastSubmitStatus = status;
            RefreshPanel(engine);
            return true;
        }

        public bool TryReloadScenario(GameEngine engine)
        {
            if (engine == null || string.IsNullOrWhiteSpace(_activeMapId))
            {
                return false;
            }

            string mapId = _activeMapId;
            engine.LoadMap(mapId);
            LastSubmitStatus = "Scenario reloaded.";
            RefreshPanel(engine);
            return true;
        }

        public bool TryRunPreset(GameEngine engine, ShowcaseCommandPreset preset)
        {
            if (engine == null ||
                !TryResolvePreset(engine, preset, out Entity actor, out Vector3 targetWorldCm, out string status))
            {
                return false;
            }

            if (engine.GetService(CoreServiceKeys.OrderQueue) is not OrderQueue orders)
            {
                return false;
            }

            RoadNetworkOrderTypeIds orderTypeIds = RoadNetworkOrderTypeIds.Require(engine.GlobalContext);

            var expander = new RoadMoveOrderExpander(engine.World, engine.GlobalContext, orders, RoadNetworkShowcaseIds.PathPlannerAgentTypeId);
            var order = new Order
            {
                OrderTypeId = orderTypeIds.MoveTo,
                Actor = actor,
                PlayerId = 1,
                SubmitMode = OrderSubmitMode.Immediate
            };
            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.Single;
            order.Args.Spatial.WorldCm = targetWorldCm;
            OrderSubmitResult submitResult = expander.TrySubmit(in order);
            bool submitted = OrderSubmitResultSemantics.IsAccepted(submitResult);
            if (submitted)
            {
                LastSubmitStatus = status;
            }

            RefreshPanel(engine);
            return submitted;
        }

        public RoadNetworkShowcasePanelState BuildPanelState(GameEngine engine)
        {
            return new RoadNetworkShowcasePanelStateBuilder(engine, this).Build();
        }

        private static void ActivateTacticalViewMode(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out object? managerObj) &&
                managerObj is ViewModeManager viewModeManager)
            {
                viewModeManager.SwitchTo(TacticalCameraModeId);
            }
        }

        private static void RequestTacticalCameraTarget(GameEngine engine, Vector2 targetCm)
        {
            ActivateTacticalViewMode(engine);
            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = TacticalCameraId,
                BlendDurationSeconds = 0f,
                FollowTargetKindOverride = CameraFollowTargetKind.None,
                SnapToFollowTargetWhenAvailable = false,
                ResetRuntimeState = true,
                ReplaceActiveStack = true
            });
            engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                VirtualCameraId = TacticalCameraId,
                TargetCm = targetCm
            });
        }

        private Vector2 ApplyInitialPlayableCamera(GameEngine engine)
        {
            Vector2 targetCm = ResolveInitialCameraTarget(engine);
            RequestTacticalCameraTarget(engine, targetCm);
            return targetCm;
        }

        private void EnsurePrimaryPlayerControl(GameEngine engine)
        {
            Entity owner = ResolveNamedEntity(engine, PrimaryPlayerColumnName);
            if (owner == Entity.Null)
            {
                return;
            }

            ClientLocalSeatBindings.BindSoleSeat(engine.GlobalContext, owner);
            if (engine.World.TryGet(owner, out PlayerOwner playerOwner) && playerOwner.PlayerId > 0)
            {
            }

            if (engine.GetService(CoreServiceKeys.EntityCollectionStore) is EntityCollectionStore collections)
            {
                EnsureCommandSourceComponents(engine.World, owner);
                if (ShouldSeedCommandSource(engine.World, collections, owner))
                {
                    Span<Entity> initialCommandSource = stackalloc Entity[1];
                    initialCommandSource[0] = owner;
                    var descriptor = EntityCollectionDescriptor.Create(
                        EntityCollectionKeys.CommandSource,
                        EntityCollectionSourceKind.Explicit,
                        EntityCollectionRoleKind.CommandSource,
                        owner,
                        owner,
                        "Road network command source",
                        "Primary road-network actor.");
                    collections.Replace(owner, descriptor, initialCommandSource, owner);
                }
            }
        }

        private Vector2 ResolveInitialCameraTarget(GameEngine engine)
        {
            Entity owner = ResolveNamedEntity(engine, PrimaryPlayerColumnName);
            if (owner != Entity.Null &&
                engine.World.IsAlive(owner) &&
                engine.World.Has<WorldPositionCm>(owner))
            {
                return engine.World.Get<WorldPositionCm>(owner).Value.ToVector2();
            }

            if (Scenario != null &&
                Scenario.TryGetLandmarkWorldCm(RoadNetworkScenarioDefinition.RoadLandmarkId.WestGate, out Vector3 westGateWorldCm))
            {
                return new Vector2(westGateWorldCm.X, westGateWorldCm.Z);
            }

            return Vector2.Zero;
        }

        private void PrimeInitialChunkWindow(GameEngine engine, Vector2 cameraTargetCm)
        {
            if (!IsActive || Scenario == null || ActiveBoard == null)
            {
                return;
            }

            ActiveBoard.LoadedChunksSource.Update(
                (int)cameraTargetCm.X,
                (int)cameraTargetCm.Y,
                Scenario.StreamingRadiusCm);

            foreach (long chunkKey in ActiveBoard.LoadedChunksSource.ActiveChunkKeys)
            {
                HandleChunkLoaded(chunkKey);
            }
        }

        private static void EnsureCommandSourceComponents(World world, Entity owner)
        {
            if (!world.Has<CommandSourceDragState>(owner))
            {
                world.Add(owner, default(CommandSourceDragState));
            }
        }

        private void PublishSelectableKnowledge(GameEngine engine)
        {
            if (string.IsNullOrWhiteSpace(_activeMapId) ||
                !ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out var viewer) ||
                !engine.World.IsAlive(viewer))
            {
                return;
            }

            KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
                ?? throw new System.InvalidOperationException("RoadNetwork showcase requires KnowledgeProjectionStore before publishing selectable visibility.");
            var empty = KnowledgeIdMask256.Empty;
            int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
            string activeMapId = _activeMapId;
            engine.World.Query(in SelectableKnowledgeQuery, (Entity target, ref CommandSourceSelectableTag _, ref MapEntity mapEntity) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, activeMapId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                knowledge.Upsert(
                    viewer,
                    target,
                    new KnowledgeDisclosureRecord(
                        KnowledgePresence.LiveVisible,
                        KnowledgePositionAccess.Live,
                        empty,
                        empty,
                        empty,
                        viewer,
                        observedTick,
                        expiryTick: 0,
                        confidencePermille: 1000,
                        revision: 0));
            });
        }

        private static bool ShouldSeedCommandSource(World world, EntityCollectionStore collections, Entity owner)
        {
            if (!collections.TryGetView(owner, EntityCollectionKeys.CommandSource, out EntityCollectionView view))
            {
                return true;
            }

            if (view.Count <= 0)
            {
                return true;
            }

            var selected = new Entity[view.Count];
            int written = collections.CopyEntities(owner, EntityCollectionKeys.CommandSource, selected);
            for (int i = 0; i < written; i++)
            {
                if (world.IsAlive(selected[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static Entity ResolveNamedEntity(GameEngine engine, string entityName)
        {
            Entity resolved = Entity.Null;
            engine.World.Query(new QueryDescription().WithAll<Name, PlayerOwner>(), (Entity entity, ref Name name, ref PlayerOwner owner) =>
            {
                if (resolved != Entity.Null ||
                    owner.PlayerId != 1 ||
                    !string.Equals(name.Value, entityName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                resolved = entity;
            });
            return resolved;
        }

        private static void EnsureShowcaseProfiles(World world)
        {
            world.Query(new QueryDescription().WithAll<Name, RoadColumnTag>(), (Entity entity, ref Name name, ref RoadColumnTag roadColumn) =>
            {
                RoadMoveProfileRef? profile = name.Value switch
                {
                    "Blue North Column" => new RoadMoveProfileRef
                    {
                        PlannerPresetId = RoadRouteProfileCatalog.PlannerNorthScout,
                        ExecutionPresetId = RoadRouteProfileCatalog.ExecutionCourier,
                        PreviewPaletteId = RoadRouteProfileCatalog.PreviewCyan,
                    },
                    "Blue South Column" => new RoadMoveProfileRef
                    {
                        PlannerPresetId = RoadRouteProfileCatalog.PlannerSouthGuard,
                        ExecutionPresetId = RoadRouteProfileCatalog.ExecutionSiege,
                        PreviewPaletteId = RoadRouteProfileCatalog.PreviewCrimson,
                    },
                    "Red North Column" => new RoadMoveProfileRef
                    {
                        PlannerPresetId = RoadRouteProfileCatalog.PlannerNorthScout,
                        ExecutionPresetId = RoadRouteProfileCatalog.ExecutionCourier,
                        PreviewPaletteId = RoadRouteProfileCatalog.PreviewCyan,
                    },
                    "Red South Column" => new RoadMoveProfileRef
                    {
                        PlannerPresetId = RoadRouteProfileCatalog.PlannerSouthGuard,
                        ExecutionPresetId = RoadRouteProfileCatalog.ExecutionSiege,
                        PreviewPaletteId = RoadRouteProfileCatalog.PreviewCrimson,
                    },
                    "Blue Vanguard" or "Red Vanguard" => new RoadMoveProfileRef
                    {
                        PlannerPresetId = RoadRouteProfileCatalog.PlannerDefault,
                        ExecutionPresetId = RoadRouteProfileCatalog.ExecutionVanguard,
                        PreviewPaletteId = RoadRouteProfileCatalog.PreviewAmber,
                    },
                    _ => null,
                };

                if (!profile.HasValue || world.Has<RoadMoveProfileRef>(entity))
                {
                    return;
                }

                world.Add(entity, profile.Value);
            });
        }

        private void HandleChunkLoaded(long chunkKey)
        {
            if (ActiveBoard == null || Scenario == null)
            {
                return;
            }

            if (Scenario.TryGetGraphChunk(chunkKey, out GraphChunkData chunk))
            {
                ActiveBoard.GraphStore.AddOrReplace(chunkKey, chunk);
            }
        }

        private void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            RoadNetworkShowcasePanelState state = BuildPanelState(engine);
            _panelController.MountOrSync(root, engine, state);
            _debugLogWriter.WriteLatest(state);
        }

        private void Unbind(GameEngine? engine = null)
        {
            if (engine?.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.ClearIfOwned(root);
            }

            if (ActiveBoard != null)
            {
                ActiveBoard.LoadedChunksSource.ChunkLoaded -= HandleChunkLoaded;
                ActiveBoard.LoadedChunksSource.Reset();
            }

            if (engine != null)
            {
                engine.GlobalContext.Remove(RoadNetworkShowcaseIds.ScenarioServiceKey);
                engine.GlobalContext.Remove(RoadNetworkShowcaseIds.GraphLoadedChunksServiceKey);
            }

            _activeMapId = null;
            ActiveBoard = null;
            Scenario = null;
            LastSubmitStatus = "Road command ready. Use the command action near a road or fort.";
        }

        private bool TryResolvePreset(GameEngine engine, ShowcaseCommandPreset preset, out Entity actor, out Vector3 targetWorldCm, out string status)
        {
            actor = ResolveCurrentActor(engine);
            targetWorldCm = default;
            status = string.Empty;
            if (actor == Entity.Null || !engine.World.IsAlive(actor) || Scenario == null)
            {
                return false;
            }

            return preset switch
            {
                ShowcaseCommandPreset.LongHaulToRedCapital => ResolvePresetTarget(
                    RoadNetworkScenarioDefinition.RoadLandmarkId.RedCapital,
                    "Preset launched: Long Haul to Red Capital.",
                    out targetWorldCm,
                    out status),
                ShowcaseCommandPreset.NorthFlankToNorthWatch => ResolvePresetTarget(
                    RoadNetworkScenarioDefinition.RoadLandmarkId.NorthWatch,
                    "Preset launched: North Flank to North Watch.",
                    out targetWorldCm,
                    out status),
                ShowcaseCommandPreset.SouthGuardToSouthWatch => ResolvePresetTarget(
                    RoadNetworkScenarioDefinition.RoadLandmarkId.SouthWatch,
                    "Preset launched: South Guard to South Watch.",
                    out targetWorldCm,
                    out status),
                _ => false,
            };
        }

        private bool ResolvePresetTarget(
            RoadNetworkScenarioDefinition.RoadLandmarkId landmarkId,
            string status,
            out Vector3 targetWorldCm,
            out string resolvedStatus)
        {
            targetWorldCm = default;
            resolvedStatus = status;
            return Scenario != null && Scenario.TryGetLandmarkWorldCm(landmarkId, out targetWorldCm);
        }

        private Entity ResolveCurrentActor(GameEngine engine)
        {
            if (TryResolveLocalCommandSourceOwner(engine, out Entity owner) &&
                EntityCollectionContextRuntime.TryGetPrimary(
                    engine.World,
                    engine.GlobalContext,
                    owner,
                    EntityCollectionKeys.CommandSource,
                    out Entity selected) &&
                engine.World.IsAlive(selected))
            {
                return selected;
            }

            if (ClientLocalSeatAccess.TryGetSolePossessedRep(engine.GlobalContext, out Entity actor) &&
                engine.World.IsAlive(actor))
            {
                return actor;
            }

            return ResolveNamedEntity(engine, PrimaryPlayerColumnName);
        }

        private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
        {
            owner = Entity.Null;
            Entity local = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (local == Entity.Null || !engine.World.IsAlive(local))
            {
                return false;
            }

            owner = local;
            return true;
        }

        private static string DescribeActor(GameEngine engine, Entity actor)
        {
            string name = engine.World.Has<Name>(actor) ? engine.World.Get<Name>(actor).Value : $"#{actor.Id}";
            if (!engine.World.Has<WorldPositionCm>(actor))
            {
                return name;
            }

            var worldCm = engine.World.Get<WorldPositionCm>(actor).Value;
            return $"{name} ({worldCm.X.ToFloat():0},{worldCm.Y.ToFloat():0})";
        }
    }
}
