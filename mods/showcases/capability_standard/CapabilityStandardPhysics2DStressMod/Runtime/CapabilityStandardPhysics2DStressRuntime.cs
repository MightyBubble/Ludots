using System;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics2DStressMod.Runtime;

internal sealed class CapabilityStandardPhysics2DStressRuntime
{
    private CapabilityStandardPhysics2DStressConfig? _config;
    private RuntimeEntitySpawnRequest[] _spawnScratch = Array.Empty<RuntimeEntitySpawnRequest>();
    private bool _scenarioSpawned;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        CapabilityStandardPhysics2DStressConfig config = EnsureConfig(engine);
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

        CapabilityStandardPhysics2DStressConfig config = _config ?? EnsureConfig(engine);
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            _scenarioSpawned = false;
        }

        return Task.CompletedTask;
    }

    private CapabilityStandardPhysics2DStressConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("Capability-standard Physics2D stress showcase requires ConfigPipeline before loading config.");
        }

        _config = new CapabilityStandardPhysics2DStressConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        EnsureSpawnScratch(_config);
        return _config;
    }

    private void SpawnScenario(GameEngine engine, CapabilityStandardPhysics2DStressConfig config)
    {
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Capability-standard Physics2D stress showcase requires RuntimeEntitySpawnQueue.");
        MapId mapId = RequireCurrentMapId(engine, config.MapId);
        ValidateTemplates(engine, config);

        int requestCount = BuildSpawnRequests(config, mapId);
        if (spawnQueue.FreeCapacity < requestCount)
        {
            throw new InvalidOperationException(
                $"Capability-standard Physics2D stress showcase requires RuntimeEntitySpawnQueue free capacity {requestCount}, actual {spawnQueue.FreeCapacity}.");
        }

        int written = spawnQueue.EnqueueMany(_spawnScratch.AsSpan(0, requestCount));
        if (written != requestCount)
        {
            throw new InvalidOperationException(
                $"Capability-standard Physics2D stress showcase enqueued {written} spawn requests, expected {requestCount}.");
        }

        _scenarioSpawned = true;
    }

    private void EnsureSpawnScratch(CapabilityStandardPhysics2DStressConfig config)
    {
        if (_spawnScratch.Length == config.SpawnScratchCapacity)
        {
            return;
        }

        _spawnScratch = new RuntimeEntitySpawnRequest[config.SpawnScratchCapacity];
    }

    private int BuildSpawnRequests(CapabilityStandardPhysics2DStressConfig config, MapId mapId)
    {
        int index = 0;
        for (int i = 0; i < config.DynamicBodies; i++)
        {
            Fix64Vec2 worldPosition = i < config.ContactClusterBodies
                ? ContactClusterPosition(config, i)
                : SparseStressPosition(config, i - config.ContactClusterBodies);

            _spawnScratch[index++] = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.DynamicTemplateId,
                MapId = mapId,
                WorldPositionCm = worldPosition,
                HasWorldPosition = 1,
                FacingAngleRad = 0f,
                HasFacing = 1,
            };
        }

        for (int i = 0; i < config.StaticColumns; i++)
        {
            _spawnScratch[index++] = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.StaticTemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt(
                    config.StaticStartXCm + i * config.StaticSpacingCm,
                    config.StaticStartYCm),
                HasWorldPosition = 1,
                FacingAngleRad = 0f,
                HasFacing = 1,
            };
        }

        return index;
    }

    private static Fix64Vec2 ContactClusterPosition(CapabilityStandardPhysics2DStressConfig config, int bodyIndex)
    {
        int column = bodyIndex % config.ContactClusterColumns;
        int row = bodyIndex / config.ContactClusterColumns;
        return Fix64Vec2.FromInt(
            config.ContactClusterStartXCm + column * config.ContactClusterSpacingCm,
            config.ContactClusterStartYCm + row * config.ContactClusterSpacingCm);
    }

    private static Fix64Vec2 SparseStressPosition(CapabilityStandardPhysics2DStressConfig config, int sparseIndex)
    {
        int column = sparseIndex % config.GridColumns;
        int row = sparseIndex / config.GridColumns;
        return Fix64Vec2.FromInt(
            config.StartXCm + column * config.SpacingCm,
            config.StartYCm + row * config.SpacingCm);
    }

    private static MapId RequireCurrentMapId(GameEngine engine, string configuredMapId)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Capability-standard Physics2D stress showcase requires an active map session before scenario bootstrap.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Capability-standard Physics2D stress showcase requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }

    private static void ValidateTemplates(GameEngine engine, CapabilityStandardPhysics2DStressConfig config)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("Capability-standard Physics2D stress showcase requires EntityTemplateKeyRegistry.");

        ValidateTemplate(engine, templateKeys, config.DynamicTemplateId);
        ValidateTemplate(engine, templateKeys, config.StaticTemplateId);
    }

    private static void ValidateTemplate(GameEngine engine, EntityTemplateKeyRegistry templateKeys, string templateId)
    {
        if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D stress showcase requires configured entity template '{templateId}'.");
        }

        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            throw new InvalidOperationException($"Capability-standard Physics2D stress template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
        }
    }
}
