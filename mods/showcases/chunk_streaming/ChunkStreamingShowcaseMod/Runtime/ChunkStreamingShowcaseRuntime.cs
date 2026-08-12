using System.Numerics;
using System.Threading.Tasks;
using ChunkStreamingShowcaseMod.UI;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Scripting;
using Ludots.UI;
using RoadNetworkShowcaseMod.Runtime;
using Ludots.Core.Client;

namespace ChunkStreamingShowcaseMod.Runtime
{
    internal sealed class ChunkStreamingShowcaseRuntime
    {
        private const string TacticalCameraId = "Camera.Profile.Tactical";

        private string? _activeMapId;
        private Vector2? _commandedCameraTargetCm;
        private readonly ChunkStreamingShowcasePanelController _panelController;

        public ChunkStreamingShowcaseRuntime()
        {
            _panelController = new ChunkStreamingShowcasePanelController(this);
        }

        public NodeGraphBoard? ActiveBoard { get; private set; }
        public RoadNetworkScenarioDefinition? Scenario { get; private set; }
        public bool IsActive => ActiveBoard != null && Scenario != null;
        public string LastStatus { get; private set; } = "Chunk window ready. Use the panel to jump between landmarks and inspect streamed road splines.";

        public int LoadedChunkCount => ActiveBoard?.LoadedChunksSource.ActiveChunkKeys.Count ?? 0;
        public int LoadedNodeCount => ActiveBoard?.GraphRuntime.CurrentGraph.NodeCount ?? 0;

        public int LoadedRoadSplineCount
        {
            get
            {
                if (ActiveBoard == null || Scenario == null)
                {
                    return 0;
                }

                int total = 0;
                foreach (long chunkKey in ActiveBoard.LoadedChunksSource.ActiveChunkKeys)
                {
                    if (Scenario.TryGetRoadSplineChunk(chunkKey, out RoadNetworkScenarioDefinition.RoadSplineSpec[]? chunkSplines))
                    {
                        total += chunkSplines.Length;
                    }
                }

                return total;
            }
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (!ChunkStreamingShowcaseIds.IsShowcaseMap(mapId) ||
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
            engine.GlobalContext[ChunkStreamingShowcaseIds.ScenarioServiceKey] = Scenario;
            board.LoadedChunksSource.ChunkLoaded += HandleChunkLoaded;

            PrimeInitialChunkWindow(engine);
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string mapId = context.Get(CoreServiceKeys.MapId).Value;
            if (ChunkStreamingShowcaseIds.IsShowcaseMap(mapId))
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

            Vector2 target = _commandedCameraTargetCm ?? ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State.TargetCm;
            ActiveBoard.LoadedChunksSource.Update(
                (int)target.X,
                (int)target.Y,
                Scenario.StreamingRadiusCm);
            RefreshPanel(engine);
        }

        public bool TryResetCamera(GameEngine engine)
        {
            if (engine == null || !IsActive)
            {
                return false;
            }

            ApplyCameraTargetAndPrimeChunkWindow(engine, Vector2.Zero);
            LastStatus = "Camera reset to the central chunk window.";
            RefreshPanel(engine);
            return true;
        }

        public bool TryFocusLandmark(GameEngine engine, RoadNetworkScenarioDefinition.RoadLandmarkId landmarkId, string status)
        {
            if (engine == null || !IsActive || Scenario == null || !Scenario.TryGetLandmarkWorldCm(landmarkId, out Vector3 landmarkWorldCm))
            {
                return false;
            }

            ApplyCameraTargetAndPrimeChunkWindow(engine, new Vector2(landmarkWorldCm.X, landmarkWorldCm.Z));
            LastStatus = status;
            RefreshPanel(engine);
            return true;
        }

        public bool TryReloadScenario(GameEngine engine)
        {
            if (engine == null || string.IsNullOrWhiteSpace(_activeMapId))
            {
                return false;
            }

            engine.LoadMap(_activeMapId);
            LastStatus = "Scenario reloaded.";
            RefreshPanel(engine);
            return true;
        }

        public ChunkStreamingShowcasePanelState BuildPanelState(GameEngine engine)
        {
            Vector2 cameraTarget = _commandedCameraTargetCm ?? ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State.TargetCm;
            return new ChunkStreamingShowcasePanelState(
                Title: "Chunk Streaming Showcase",
                Status: LastStatus,
                Camera: $"Camera ({cameraTarget.X:0},{cameraTarget.Y:0})",
                Chunks: $"Chunks {LoadedChunkCount} | Nodes {LoadedNodeCount} | Splines {LoadedRoadSplineCount}",
                Hint: "Jump west/center/east to verify chunk windows, loaded node counts, and spline batches change with the camera.");
        }

        private void PrimeInitialChunkWindow(GameEngine engine)
        {
            if (!IsActive || Scenario == null || ActiveBoard == null)
            {
                return;
            }

            Vector2 target = ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State.TargetCm;
            _commandedCameraTargetCm = target;
            ActiveBoard.LoadedChunksSource.Update(
                (int)target.X,
                (int)target.Y,
                Scenario.StreamingRadiusCm);

            foreach (long chunkKey in ActiveBoard.LoadedChunksSource.ActiveChunkKeys)
            {
                HandleChunkLoaded(chunkKey);
            }
        }

        private void ApplyCameraTargetAndPrimeChunkWindow(GameEngine engine, Vector2 targetCm)
        {
            _commandedCameraTargetCm = targetCm;
            ActivateTacticalCamera(engine, targetCm);

            if (!IsActive || Scenario == null || ActiveBoard == null)
            {
                return;
            }

            ActiveBoard.LoadedChunksSource.Update(
                (int)targetCm.X,
                (int)targetCm.Y,
                Scenario.StreamingRadiusCm);
        }

        private void ActivateTacticalCamera(GameEngine engine, Vector2 targetCm)
        {
            if (engine.GetService(CoreServiceKeys.VirtualCameraRegistry) is VirtualCameraRegistry registry &&
                registry.TryGet(TacticalCameraId, out VirtualCameraDefinition? definition) &&
                definition != null)
            {
                ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ResetVirtualCameras();
                ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ActivateVirtualCamera(
                    TacticalCameraId,
                    blendDurationSeconds: 0f,
                    followTarget: CameraFollowTargetFactory.Build(
                        engine.World,
                        engine.GlobalContext,
                        definition.FollowTargetKind,
                        Arch.Core.Entity.Null,
                        definition.FollowCollectionKey),
                    snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable);
            }

            ClientLocalSeatAccess.ResolveAuthorityCamera(engine).ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = TacticalCameraId,
                TargetCm = targetCm
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

            _panelController.MountOrSync(root, engine, BuildPanelState(engine));
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
                engine.GlobalContext.Remove(ChunkStreamingShowcaseIds.ScenarioServiceKey);
            }

            _activeMapId = null;
            _commandedCameraTargetCm = null;
            ActiveBoard = null;
            Scenario = null;
            LastStatus = "Chunk window ready. Use the panel to jump between landmarks and inspect streamed road splines.";
        }
    }
}
