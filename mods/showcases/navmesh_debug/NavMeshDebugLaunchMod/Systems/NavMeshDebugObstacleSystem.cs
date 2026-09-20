using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Scripting;
using NavMeshDebugLaunchMod.Input;
using NavMeshDebugLaunchMod.Runtime;

namespace NavMeshDebugLaunchMod.Systems
{
    public sealed class NavMeshDebugObstacleSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly NavMeshDebugShowcaseConfig _config;
        private readonly bool _autoObstacle;
        private readonly List<Entity> _scratch = new List<Entity>(16);
        private PlayerInputHandler? _input;
        private int _framesSinceReady;
        private bool _autoSpawned;

        public NavMeshDebugObstacleSystem(GameEngine engine)
        {
            _engine = engine;
            _config = LoadConfig(engine);
            _autoObstacle = string.Equals(
                Environment.GetEnvironmentVariable(_config.AutoObstacleEnvironmentVariable),
                "1",
                StringComparison.OrdinalIgnoreCase);
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (!RuntimeNavMeshReady())
            {
                return;
            }

            if (_autoObstacle && !_autoSpawned)
            {
                _framesSinceReady++;
                if (_framesSinceReady >= _config.AutoObstacleDelayFrames)
                {
                    _autoSpawned = true;
                    SpawnConfiguredObstacle("auto");
                }
            }

            ResolveInput();
            if (_input == null) return;

            if (_input.PressedThisFrame(NavMeshDebugInputActions.SpawnObstacle))
            {
                var camera = ClientLocalSeatAccess.ResolveAuthorityCamera(_engine);
                System.Numerics.Vector2 target = camera.State.TargetCm;
                (int clampedX, int clampedY) = ClampToTerrainExtent((int)target.X, (int)target.Y);
                SpawnObstacle(clampedX, clampedY, _config.DefaultObstacleRadiusCm, reason: "keypress");
            }

            if (_input.PressedThisFrame(NavMeshDebugInputActions.ClearObstacles))
            {
                ClearObstacles();
            }
        }

        private static NavMeshDebugShowcaseConfig LoadConfig(GameEngine engine)
        {
            if (engine.ConfigPipeline == null)
            {
                throw new InvalidOperationException("NavMesh Debug showcase requires ConfigPipeline before loading config.");
            }

            return new NavMeshDebugShowcaseConfigLoader(engine.ConfigPipeline)
                .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
        }

        /// <summary>
        /// 相机目标可能落在地形范围之外（虚拟相机未收拢、窗口外拖拽后的残余机位）；
        /// 直接以界外坐标创建实体会触发 WorldPositionOutOfBounds 未处理异常。
        /// 按键生成路径必须把落点钳回地形范围。
        /// </summary>
        private (int XCm, int YCm) ClampToTerrainExtent(int xCm, int yCm)
        {
            LogicTerrainField? terrain = null;
            if (_engine.TryGetService(CoreServiceKeys.LogicTerrain, out LogicTerrainField? loaded) && loaded != null)
            {
                terrain = loaded;
            }

            if (terrain == null)
            {
                return (xCm, yCm);
            }

            terrain.GetWorldPositionMeters(0, 0, out float minWorldX, out float minWorldZ);
            terrain.GetWorldPositionMeters(terrain.WidthCells - 1, terrain.HeightCells - 1, out float maxWorldX, out float maxWorldZ);
            int minX = (int)MathF.Floor(MathF.Min(minWorldX, maxWorldX) * 100f);
            int maxX = (int)MathF.Ceiling(MathF.Max(minWorldX, maxWorldX) * 100f);
            int minY = (int)MathF.Floor(MathF.Min(minWorldZ, maxWorldZ) * 100f);
            int maxY = (int)MathF.Ceiling(MathF.Max(minWorldZ, maxWorldZ) * 100f);
            return (Math.Clamp(xCm, minX, maxX), Math.Clamp(yCm, minY, maxY));
        }

        private bool RuntimeNavMeshReady()
        {
            return _engine.CurrentMapSession != null &&
                   _engine.TryGetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, out RuntimeIncrementalNavMeshRebuildQueue? queue) &&
                   queue != null;
        }

        private void ResolveInput()
        {
            if (_input != null) return;
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                inputObj is PlayerInputHandler input)
            {
                _input = input;
            }
        }

        private void SpawnConfiguredObstacle(string reason)
        {
            string mapId = _engine.CurrentMapSession?.MapId.Value
                ?? throw new InvalidOperationException("NavMesh Debug showcase cannot spawn an obstacle before a map is loaded.");
            NavMeshDebugSpawnPointConfig point = _config.RequireSpawnPoint(mapId);
            SpawnObstacle(point.XCm, point.YCm, point.ObstacleRadiusCm, reason);
        }

        private void SpawnObstacle(int xCm, int yCm, int radiusCm, string reason)
        {
            _engine.World.Create(
                WorldPositionCm.FromCm(xCm, yCm),
                new NavMeshDebugSpawnedObstacle(),
                new RuntimeNavMeshStructuralObstacle(),
                new ManifestationObstacleIntent2D
                {
                    Shape = ManifestationObstacleShape2D.Circle,
                    SinkNavigationObstacle = 1,
                    RadiusCm = radiusCm,
                    NavRadiusCm = radiusCm,
                });
            Console.WriteLine($"[NavMeshDebugObstacle] spawned circle r={radiusCm}cm at ({xCm},{yCm}) reason={reason}");

            LogQueueState();
        }

        private void ClearObstacles()
        {
            var query = new QueryDescription().WithAll<RuntimeNavMeshStructuralObstacle, NavMeshDebugSpawnedObstacle>();
            _scratch.Clear();
            _engine.World.Query(in query, (Entity entity) => _scratch.Add(entity));
            int removed = _scratch.Count;
            for (int i = 0; i < removed; i++)
            {
                _engine.World.Destroy(_scratch[i]);
            }
            Console.WriteLine($"[NavMeshDebugObstacle] cleared {removed} obstacles");

            if (removed > 0)
            {
                LogQueueState();
            }
        }

        private void LogQueueState()
        {
            if (_engine.TryGetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, out RuntimeIncrementalNavMeshRebuildQueue? queue) &&
                queue != null &&
                _engine.TryGetService(CoreServiceKeys.NavQueryServices, out NavQueryServiceRegistry? registry) &&
                registry != null &&
                registry.TryGetStore(0, 0, out NavTileStore? store) &&
                store != null)
            {
                Console.WriteLine($"[NavMeshDebugObstacle] queue pending={queue.PendingTileCount} storeRevision={store.Revision}");
            }
        }
    }

    internal struct NavMeshDebugSpawnedObstacle
    {
    }
}
