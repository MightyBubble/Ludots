using System;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;
using CapabilityStandardPhysics2DPlaygroundV2Mod.Input;

namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

internal sealed class CapabilityStandardPhysics2DPlaygroundV2Runtime
{
    private readonly IModContext _context;
    private CapabilityStandardPhysics2DPlaygroundV2Config? _config;
    private RuntimeEntitySpawnRequest[] _spawnScratch = Array.Empty<RuntimeEntitySpawnRequest>();
    private bool _scenarioSpawned;
    private bool _systemsInstalled;
    private bool _inputContextActive;

    public CapabilityStandardPhysics2DPlaygroundV2Runtime(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        CapabilityStandardPhysics2DPlaygroundV2Config config = EnsureConfig(engine);
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (!string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            Disable(engine);
            return Task.CompletedTask;
        }

        EnsureProductionServices(engine);
        EnsureSystemsInstalled(engine, config);
        EnsureInputContext(engine);
        EnsureCamera(engine);

        CapabilityStandardPhysics2DPlaygroundV2State.Enabled = true;
        CapabilityStandardPhysics2DPlaygroundV2State.ActiveMode = CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly;
        PublishMode(engine);

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

        CapabilityStandardPhysics2DPlaygroundV2Config config = _config ?? EnsureConfig(engine);
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (string.Equals(mapId, config.MapId, StringComparison.Ordinal))
        {
            _scenarioSpawned = false;
            Disable(engine);
        }

        return Task.CompletedTask;
    }

    private void EnsureProductionServices(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.Physics2DShapeStorage) == null)
        {
            throw new InvalidOperationException("Physics2D Playground v2 requires production Physics2D shape storage from game.json physics2D/navigation2D enablement.");
        }

        if (engine.GetService(CoreServiceKeys.Physics2DController) == null)
        {
            throw new InvalidOperationException("Physics2D Playground v2 requires production Physics2DController.");
        }

        if (engine.GetService(CoreServiceKeys.Navigation2DRuntime) is not Navigation2DRuntime navRuntime)
        {
            throw new InvalidOperationException("Physics2D Playground v2 requires Navigation2DRuntime for its Nav partition.");
        }

        navRuntime.FlowEnabled = true;
    }

    private void EnsureSystemsInstalled(GameEngine engine, CapabilityStandardPhysics2DPlaygroundV2Config config)
    {
        if (_systemsInstalled)
        {
            return;
        }

        var debugDrawBuffer = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer) ?? new DebugDrawCommandBuffer();
        engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDrawBuffer);
        engine.RegisterSystem(new CapabilityStandardPhysics2DPlaygroundV2InteractionSystem(engine, config), SystemGroup.InputCollection);
        engine.RegisterSystem(new CapabilityStandardPhysics2DPlaygroundV2BenchmarkReceiptBindingSystem(engine, config), SystemGroup.EffectProcessing);
        engine.RegisterPresentationSystem(new CapabilityStandardPhysics2DPlaygroundV2HudSystem(engine, config));
        _systemsInstalled = true;
        _context.Log("[CapabilityStandardPhysics2DPlaygroundV2Mod] Installed interaction layer on production Physics2D/Nav systems.");
    }

    private void EnsureInputContext(GameEngine engine)
    {
        if (_inputContextActive || engine.GetService(CoreServiceKeys.InputHandler) is not PlayerInputHandler input)
        {
            return;
        }

        EnsurePlaygroundInputSchema(input);
        input.PushContext(CapabilityStandardPhysics2DPlaygroundV2InputContexts.Playground);
        _inputContextActive = true;
    }

    private void TryPopInputContext(GameEngine engine)
    {
        if (!_inputContextActive || engine.GetService(CoreServiceKeys.InputHandler) is not PlayerInputHandler input)
        {
            return;
        }

        input.PopContext(CapabilityStandardPhysics2DPlaygroundV2InputContexts.Playground);
        _inputContextActive = false;
    }

    private CapabilityStandardPhysics2DPlaygroundV2Config EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("Capability-standard Physics2D Playground v2 requires ConfigPipeline before loading config.");
        }

        _config = new CapabilityStandardPhysics2DPlaygroundV2ConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        EnsureSpawnScratch(_config);
        return _config;
    }

    private void SpawnScenario(GameEngine engine, CapabilityStandardPhysics2DPlaygroundV2Config config)
    {
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Capability-standard Physics2D Playground v2 requires RuntimeEntitySpawnQueue.");
        MapId mapId = RequireCurrentMapId(engine, config.MapId);
        ValidateTemplates(engine, config);

        int requestCount = BuildSpawnRequests(config, mapId);
        if (spawnQueue.FreeCapacity < requestCount)
        {
            throw new InvalidOperationException(
                $"Capability-standard Physics2D Playground v2 requires RuntimeEntitySpawnQueue free capacity {requestCount}, actual {spawnQueue.FreeCapacity}.");
        }

        int written = spawnQueue.EnqueueMany(_spawnScratch.AsSpan(0, requestCount));
        if (written != requestCount)
        {
            throw new InvalidOperationException(
                $"Capability-standard Physics2D Playground v2 enqueued {written} spawn requests, expected {requestCount}.");
        }

        _scenarioSpawned = true;
    }

    private void EnsureSpawnScratch(CapabilityStandardPhysics2DPlaygroundV2Config config)
    {
        if (_spawnScratch.Length == config.SpawnScratchCapacity)
        {
            return;
        }

        _spawnScratch = new RuntimeEntitySpawnRequest[config.SpawnScratchCapacity];
    }

    private int BuildSpawnRequests(CapabilityStandardPhysics2DPlaygroundV2Config config, MapId mapId)
    {
        for (int i = 0; i < config.Spawns.Length; i++)
        {
            CapabilityStandardPhysics2DPlaygroundV2SpawnConfig spawn = config.Spawns[i];
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
            ?? throw new InvalidOperationException("Capability-standard Physics2D Playground v2 requires an active map session before scenario bootstrap.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Capability-standard Physics2D Playground v2 requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }

    private static void ValidateTemplates(GameEngine engine, CapabilityStandardPhysics2DPlaygroundV2Config config)
    {
        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("Capability-standard Physics2D Playground v2 requires EntityTemplateKeyRegistry.");

        for (int i = 0; i < config.Spawns.Length; i++)
        {
            string templateId = config.Spawns[i].TemplateId;
            if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
            {
                throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 requires configured entity template '{templateId}'.");
            }

            if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
            {
                throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
            }
        }

        ValidateTemplate(engine, templateKeys, config.BenchmarkBodyTemplateId);
        ValidateTemplate(engine, templateKeys, config.StaticPolygonTemplateId);
        ValidateTemplate(engine, templateKeys, config.FrictionZoneLowTemplateId);
        ValidateTemplate(engine, templateKeys, config.FrictionZoneMediumTemplateId);
        ValidateTemplate(engine, templateKeys, config.FrictionZoneHighTemplateId);
    }

    private static void ValidateTemplate(GameEngine engine, EntityTemplateKeyRegistry templateKeys, string templateId)
    {
        if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
        {
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 requires configured entity template '{templateId}'.");
        }

        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            throw new InvalidOperationException($"Capability-standard Physics2D Playground v2 template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
        }
    }

    private static void EnsureCamera(GameEngine engine)
    {
        engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
        {
            Id = "Camera.Profile.Tactical"
        });
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = "Camera.Profile.Tactical",
            TargetCm = new System.Numerics.Vector2(40f, -120f),
            Pitch = 58f,
            DistanceCm = 2600f,
            FovYDeg = 50f
        });
    }

    private static void PublishMode(GameEngine engine)
    {
        engine.GlobalContext[CapabilityStandardPhysics2DPlaygroundV2State.ActiveModeServiceKey] =
            CapabilityStandardPhysics2DPlaygroundV2State.ActiveMode.ToString();
    }

    private void Disable(GameEngine engine)
    {
        CapabilityStandardPhysics2DPlaygroundV2State.Enabled = false;
        TryPopInputContext(engine);
    }

    private static void EnsurePlaygroundInputSchema(PlayerInputHandler input)
    {
        if (!input.HasContext(CapabilityStandardPhysics2DPlaygroundV2InputContexts.Playground))
        {
            throw new InvalidOperationException($"Missing input context: {CapabilityStandardPhysics2DPlaygroundV2InputContexts.Playground}");
        }

        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.PointerPos);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.PrimaryClick);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.SecondaryClick);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.TogglePhysicsOnlyMode);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.ToggleNavMode);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.ApplyImpulse);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.SubmitNavMove);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.ApplyDisplacement);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkForcePulse);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.SpawnStaticPolygon);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.SpawnFrictionZones);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.ApplyExplosionForce);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount1);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount2);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount3);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount4);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount5);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount6);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount7);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount8);
        RequireAction(input, CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount9);
    }

    private static void RequireAction(PlayerInputHandler input, string actionId)
    {
        if (!input.HasAction(actionId))
        {
            throw new InvalidOperationException($"Missing input action: {actionId}");
        }
    }
}
