using System;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics2DMod.Runtime;

internal sealed class CapabilityStandardPhysics2DRuntime
{
    private CapabilityStandardPhysics2DConfig? _config;
    private RuntimeEntitySpawnRequest[] _spawnScratch = Array.Empty<RuntimeEntitySpawnRequest>();
    private bool _scenarioSpawned;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        CapabilityStandardPhysics2DConfig config = EnsureConfig(engine);
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

        CapabilityStandardPhysics2DConfig config = _config ?? EnsureConfig(engine);
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            _scenarioSpawned = false;
        }

        return Task.CompletedTask;
    }

    private CapabilityStandardPhysics2DConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("Capability-standard Physics2D showcase requires ConfigPipeline before loading config.");
        }

        _config = new CapabilityStandardPhysics2DConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        EnsureSpawnScratch(_config);
        return _config;
    }

    private void SpawnScenario(GameEngine engine, CapabilityStandardPhysics2DConfig config)
    {
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Capability-standard Physics2D showcase requires RuntimeEntitySpawnQueue.");
        MapId mapId = RequireCurrentMapId(engine, config.MapId);
        ValidateTemplates(engine, config);

        int requestCount = BuildSpawnRequests(config, mapId);
        if (spawnQueue.FreeCapacity < requestCount)
        {
            throw new InvalidOperationException(
                $"Capability-standard Physics2D showcase requires RuntimeEntitySpawnQueue free capacity {requestCount}, actual {spawnQueue.FreeCapacity}.");
        }

        int written = spawnQueue.EnqueueMany(_spawnScratch.AsSpan(0, requestCount));
        if (written != requestCount)
        {
            throw new InvalidOperationException(
                $"Capability-standard Physics2D showcase enqueued {written} spawn requests, expected {requestCount}.");
        }

        _scenarioSpawned = true;
    }

    private void EnsureSpawnScratch(CapabilityStandardPhysics2DConfig config)
    {
        if (_spawnScratch.Length == config.SpawnScratchCapacity)
        {
            return;
        }

        _spawnScratch = new RuntimeEntitySpawnRequest[config.SpawnScratchCapacity];
    }

    private int BuildSpawnRequests(CapabilityStandardPhysics2DConfig config, MapId mapId)
    {
        for (int i = 0; i < config.Spawns.Length; i++)
        {
            CapabilityStandardPhysics2DSpawnConfig spawn = config.Spawns[i];
            _spawnScratch[i] = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = spawn.TemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt(spawn.WorldXCm, spawn.WorldYCm),
                HasWorldPosition = 1,
                FacingAngleRad = spawn.FacingRad,
                HasFacing = 1,
            };
        }

        return config.Spawns.Length;
    }

    private static MapId RequireCurrentMapId(GameEngine engine, string configuredMapId)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Capability-standard Physics2D showcase requires an active map session before scenario bootstrap.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Capability-standard Physics2D showcase requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }

    private static void ValidateTemplates(GameEngine engine, CapabilityStandardPhysics2DConfig config)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("Capability-standard Physics2D showcase requires EntityTemplateKeyRegistry.");

        for (int i = 0; i < config.Spawns.Length; i++)
        {
            string templateId = config.Spawns[i].TemplateId;
            if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
            {
                throw new InvalidOperationException($"Capability-standard Physics2D showcase requires configured entity template '{templateId}'.");
            }

            if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
            {
                throw new InvalidOperationException($"Capability-standard Physics2D template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
            }
        }
    }
}
