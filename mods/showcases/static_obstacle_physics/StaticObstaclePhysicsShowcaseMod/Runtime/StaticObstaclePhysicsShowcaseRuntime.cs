using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;

namespace StaticObstaclePhysicsShowcaseMod.Runtime;

internal sealed class StaticObstaclePhysicsShowcaseRuntime
{
    private StaticObstaclePhysicsShowcaseConfig? _config;
    private RuntimeEntitySpawnRequest[] _spawnScratch = Array.Empty<RuntimeEntitySpawnRequest>();
    private bool _scenarioSpawned;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        StaticObstaclePhysicsShowcaseConfig config = EnsureConfig(engine);
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (!string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (!_scenarioSpawned)
        {
            SpawnScenario(engine, config);
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null || (!_scenarioSpawned && _config == null))
        {
            return Task.CompletedTask;
        }

        StaticObstaclePhysicsShowcaseConfig config = _config ?? EnsureConfig(engine);
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            _scenarioSpawned = false;
        }

        return Task.CompletedTask;
    }

    private StaticObstaclePhysicsShowcaseConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("Static obstacle physics showcase requires ConfigPipeline before loading config.");
        }

        _config = new StaticObstaclePhysicsShowcaseConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        EnsureSpawnScratch(_config);
        return _config;
    }

    private void SpawnScenario(GameEngine engine, StaticObstaclePhysicsShowcaseConfig config)
    {
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Static obstacle physics showcase requires RuntimeEntitySpawnQueue.");
        MapId mapId = RequireCurrentMapId(engine, config.MapId);
        ValidateTemplate(engine, config.ObstacleTemplateId);

        int requestCount = BuildSpawnRequests(config, mapId);
        if (spawnQueue.FreeCapacity < requestCount)
        {
            throw new InvalidOperationException(
                $"Static obstacle physics showcase requires RuntimeEntitySpawnQueue free capacity {requestCount}, actual {spawnQueue.FreeCapacity}.");
        }

        int written = spawnQueue.EnqueueMany(_spawnScratch.AsSpan(0, requestCount));
        if (written != requestCount)
        {
            throw new InvalidOperationException(
                $"Static obstacle physics showcase enqueued {written} spawn requests, expected {requestCount}.");
        }

        _scenarioSpawned = true;
    }

    private void EnsureSpawnScratch(StaticObstaclePhysicsShowcaseConfig config)
    {
        if (_spawnScratch.Length == config.SpawnScratchCapacity)
        {
            return;
        }

        _spawnScratch = new RuntimeEntitySpawnRequest[config.SpawnScratchCapacity];
    }

    private int BuildSpawnRequests(StaticObstaclePhysicsShowcaseConfig config, MapId mapId)
    {
        int requestIndex = 0;
        for (int regionIndex = 0; regionIndex < config.Regions.Length; regionIndex++)
        {
            StaticObstaclePhysicsRegionConfig region = config.Regions[regionIndex];
            int halfWidthCm = checked((region.Columns - 1) * region.SpacingXCm / 2);
            int halfHeightCm = checked((region.Rows - 1) * region.SpacingYCm / 2);
            float facingRad = region.FacingDeg * (MathF.PI / 180f);

            for (int row = 0; row < region.Rows; row++)
            {
                int rowBaseY = checked(region.CenterYCm + (row * region.SpacingYCm) - halfHeightCm);
                int rowStaggerX = (row & 1) == 0 ? 0 : region.StaggerXCm;
                int rowStaggerY = (row & 1) == 0 ? 0 : region.StaggerYCm;

                for (int column = 0; column < region.Columns; column++)
                {
                    int x = checked(region.CenterXCm + (column * region.SpacingXCm) - halfWidthCm + rowStaggerX);
                    int y = checked(rowBaseY + rowStaggerY);
                    _spawnScratch[requestIndex] = new RuntimeEntitySpawnRequest
                    {
                        Kind = RuntimeEntitySpawnKind.Template,
                        TemplateId = config.ObstacleTemplateId,
                        MapId = mapId,
                        WorldPositionCm = Fix64Vec2.FromInt(x, y),
                        HasWorldPosition = 1,
                        FacingAngleRad = facingRad,
                        HasFacing = 1,
                    };
                    requestIndex++;
                }
            }
        }

        return requestIndex;
    }

    private static MapId RequireCurrentMapId(GameEngine engine, string configuredMapId)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Static obstacle physics showcase requires an active map session before scenario bootstrap.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Static obstacle physics showcase requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }

    private static void ValidateTemplate(GameEngine engine, string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("Static obstacle physics showcase template id must be non-empty.");
        }

        if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
        {
            throw new InvalidOperationException($"Static obstacle physics showcase requires configured entity template '{templateId}'.");
        }

        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("Static obstacle physics showcase requires EntityTemplateKeyRegistry.");
        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            throw new InvalidOperationException($"Static obstacle physics showcase template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
        }
    }
}
