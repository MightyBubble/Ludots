using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardPhysics2DShowcaseMod.Runtime;

internal sealed class CapabilityStandardPhysics2DShowcaseRuntime : IBenchmarkSceneController
{
    private readonly QueryDescription _ownedEntityQuery = new QueryDescription().WithAll<CapabilityStandardPhysics2DShowcaseEntityTag>();
    private readonly QueryDescription _dynamicQuery = new QueryDescription().WithAll<CapabilityStandardPhysics2DShowcaseDynamicTag>();
    private readonly QueryDescription _staticQuery = new QueryDescription().WithAll<CapabilityStandardPhysics2DShowcaseStaticObstacleTag>();
    private readonly QueryDescription _statsQuery = new QueryDescription().WithAll<Physics2DPerfStats>();
    private readonly List<WorldCmInt2> _polygonVertices = new(ManifestationObstaclePolygon2D.MaxVertices);

    private CapabilityStandardPhysics2DShowcaseConfig? _config;
    private RuntimeEntitySpawnRequest[] _spawnScratch = Array.Empty<RuntimeEntitySpawnRequest>();
    private GameEngine? _activeEngine;
    private string _lastAction = "Physics2D showcase ready.";
    private int _dynamicSpawnBatch;
    private int _staticSpawnBatch;
    private int _dynamicShapeIndex = -1;
    private int _dynamicSpawnCursor;
    private int _staticSpawnCursor;
    private int _receiptChannelId;
    private int _nextReceiptId = 1;
    private float _damping = 0.98f;
    private float _friction = 0.50f;
    private float _restitution;
    private bool _polygonDrawMode;

    public bool IsActive => _activeEngine != null && IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);
    public bool SupportsScatterControl => IsActive;
    public bool IsCleanPerformanceScene => false;
    public bool SuppressHostDiagnosticUi => IsActive;
    public bool SuppressHostDebugGuides => false;
    public CapabilityStandardPhysics2DShowcaseConfig ActiveConfig => _config
        ?? throw new InvalidOperationException("Physics2D showcase config has not been loaded.");
    public int ScatterMin => 0;
    public int ScatterMax => _config?.MaxDynamicEntities ?? 0;
    public int ScatterTarget => _dynamicSpawnBatch;
    public int ScatterAppliedTotal => _activeEngine?.World.CountEntities(in _dynamicQuery) ?? 0;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(engine);
        if (!IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            Disable(engine);
            return Task.CompletedTask;
        }

        _activeEngine = engine;
        _dynamicSpawnBatch = Math.Clamp(_dynamicSpawnBatch <= 0 ? config.DynamicSpawnBatch : _dynamicSpawnBatch, 1, config.MaxDynamicEntities);
        _staticSpawnBatch = Math.Clamp(_staticSpawnBatch <= 0 ? config.StaticObstacleSpawnBatch : _staticSpawnBatch, 1, config.MaxStaticObstacles);
        ValidateTemplate(engine, config.StaticObstacleTemplateId);
        ResolveReceiptChannelId(engine, config);
        EnsureDynamicShape(engine, config);
        _lastAction = "Physics2D showcase active. Use the panel to tune policy and spawn/reset scene content.";
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        string? mapId = context.TryGet(CoreServiceKeys.MapId, out var contextMapId)
            ? contextMapId.Value
            : engine?.CurrentMapSession?.MapId.Value;
        if (engine != null && IsShowcaseMap(mapId))
        {
            Disable(engine);
        }

        return Task.CompletedTask;
    }

    public CapabilityStandardPhysics2DShowcasePanelState CapturePanelState(GameEngine engine)
    {
        if (!IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return CapabilityStandardPhysics2DShowcasePanelState.Empty;
        }

        Physics2DTickPolicy tickPolicy = engine.GetService(CoreServiceKeys.Physics2DTickPolicy)
            ?? throw new InvalidOperationException("Physics2D showcase requires Physics2DTickPolicy.");
        Physics2DBroadphasePolicy broadphasePolicy = engine.GetService(CoreServiceKeys.Physics2DBroadphasePolicy)
            ?? throw new InvalidOperationException("Physics2D showcase requires Physics2DBroadphasePolicy.");
        Physics2DPerfStats stats = CaptureStats(engine.World, in _statsQuery);
        int dynamicOwned = engine.World.CountEntities(in _dynamicQuery);
        int staticOwned = engine.World.CountEntities(in _staticQuery);

        return new CapabilityStandardPhysics2DShowcasePanelState(
            Title: "Physics2D Capability",
            LastAction: _lastAction,
            PhysicsHz: tickPolicy.TargetHz,
            PhysicsMaxSteps: tickPolicy.MaxStepsPerFixedTick,
            BroadphaseStrategy: broadphasePolicy.Strategy.ToString(),
            BroadphaseCellSizeCm: broadphasePolicy.CellSizeCm,
            PhysicsUpdateMs: Math.Round(stats.PhysicsUpdateMs, 3),
            PotentialPairs: stats.PotentialPairs,
            ContactPairs: stats.ContactPairs,
            DynamicBodies: stats.DynamicBodies > 0 ? stats.DynamicBodies : dynamicOwned,
            StaticBodies: stats.StaticBodies > 0 ? stats.StaticBodies : staticOwned,
            DirtyStaticBodies: stats.DirtyStaticBodies,
            SpawnBatchDynamic: _dynamicSpawnBatch,
            SpawnBatchStatic: _staticSpawnBatch,
            PolygonDrawMode: _polygonDrawMode,
            DrawnPolygonVertices: _polygonVertices.Count,
            MaterialSummary: $"friction {_friction:0.00}  restitution {_restitution:0.00}  damping {_damping:0.00}",
            ScaleSummary: $"owned dynamic {dynamicOwned}/{EnsureConfig(engine).MaxDynamicEntities}  owned static {staticOwned}/{EnsureConfig(engine).MaxStaticObstacles}");
    }

    public void ResetScene()
    {
        GameEngine engine = RequireActiveEngine();
        engine.World.Destroy(in _ownedEntityQuery);
        ResetStaticObstacleSpawnReceipts(engine, EnsureConfig(engine));
        _polygonVertices.Clear();
        _polygonDrawMode = false;
        _dynamicSpawnCursor = 0;
        _staticSpawnCursor = 0;
        _nextReceiptId = 1;
        _lastAction = "Reset: removed showcase-owned dynamic bodies, static obstacles, and pending spawns.";
    }

    public void SpawnDynamicBatch()
    {
        GameEngine engine = RequireActiveEngine();
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(engine);
        EnsureDynamicShape(engine, config);

        int current = engine.World.CountEntities(in _dynamicQuery);
        int spawnCount = Math.Min(_dynamicSpawnBatch, Math.Max(0, config.MaxDynamicEntities - current));
        for (int i = 0; i < spawnCount; i++)
        {
            SpawnDynamicEntity(engine, config, _dynamicSpawnCursor++);
        }

        _lastAction = $"Spawned {spawnCount} dynamic Physics2D entities.";
    }

    public void SpawnStaticObstacleBatch()
    {
        GameEngine engine = RequireActiveEngine();
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(engine);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Physics2D showcase requires RuntimeEntitySpawnQueue.");
        MapId mapId = RequireCurrentMapId(engine);

        int current = engine.World.CountEntities(in _staticQuery);
        int spawnCount = Math.Min(_staticSpawnBatch, Math.Max(0, config.MaxStaticObstacles - current));
        int receiptChannelId = ResolveReceiptChannelId(engine, config);
        EnsureSpawnScratch(spawnCount);
        for (int i = 0; i < spawnCount; i++)
        {
            var (x, y) = ResolveStaticSpawnPoint(config, _staticSpawnCursor++);
            int receiptId = AllocateReceiptId();
            _spawnScratch[i] = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = config.StaticObstacleTemplateId,
                MapId = mapId,
                WorldPositionCm = Fix64Vec2.FromInt(x, y),
                HasWorldPosition = 1,
                FacingAngleRad = ((_staticSpawnCursor + i) % 16) * (MathF.PI / 64f),
                HasFacing = 1,
                EmitReceipt = 1,
                ReceiptChannelId = receiptChannelId,
                ReceiptId = receiptId
            };
        }

        int written = spawnQueue.EnqueueMany(_spawnScratch.AsSpan(0, spawnCount));
        if (written != spawnCount)
        {
            throw new InvalidOperationException($"Physics2D showcase enqueued {written} static obstacle spawns, expected {spawnCount}.");
        }

        _lastAction = $"Queued {spawnCount} static obstacle template spawns through RuntimeEntitySpawnQueue.";
    }

    public void BindStaticObstacleSpawnReceipts()
    {
        GameEngine engine = RequireActiveEngine();
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(engine);
        RuntimeEntitySpawnReceiptQueue receiptQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("Physics2D showcase requires RuntimeEntitySpawnReceiptQueue.");
        int receiptChannelId = ResolveReceiptChannelId(engine, config);
        int bound = 0;
        while (receiptQueue.TryDequeueForChannel(receiptChannelId, out RuntimeEntitySpawnReceipt receipt))
        {
            if (receipt.Kind != RuntimeEntitySpawnKind.Template)
            {
                throw new InvalidOperationException($"Physics2D showcase expected template spawn receipt, got {receipt.Kind}.");
            }

            if (!string.Equals(receipt.TemplateId, config.StaticObstacleTemplateId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Physics2D showcase spawn receipt template mismatch: expected '{config.StaticObstacleTemplateId}', got '{receipt.TemplateId}'.");
            }

            Entity entity = receipt.Entity;
            if (!engine.World.IsAlive(entity))
            {
                throw new InvalidOperationException($"Physics2D showcase spawn receipt id {receipt.ReceiptId} returned a dead entity.");
            }

            UpsertComponent(engine.World, entity, new CapabilityStandardPhysics2DShowcaseEntityTag());
            UpsertComponent(engine.World, entity, new CapabilityStandardPhysics2DShowcaseStaticObstacleTag());
            bound++;
        }

        if (bound > 0)
        {
            _lastAction = $"Bound {bound} static obstacle spawn receipts.";
        }
    }

    public void TogglePolygonDrawMode()
    {
        _ = RequireActiveEngine();
        _polygonDrawMode = !_polygonDrawMode;
        _lastAction = _polygonDrawMode
            ? "Polygon draw enabled. Command-click the ground to add vertices."
            : "Polygon draw disabled.";
    }

    public void TryAddPolygonVertexFromPointer(in WorldCmInt2 worldCm)
    {
        _ = RequireActiveEngine();
        if (!_polygonDrawMode)
        {
            return;
        }

        if (_polygonVertices.Count >= ManifestationObstaclePolygon2D.MaxVertices)
        {
            _lastAction = "Polygon draw already has the maximum vertex count.";
            return;
        }

        _polygonVertices.Add(worldCm);
        _lastAction = $"Added polygon vertex {_polygonVertices.Count} at {worldCm.X},{worldCm.Y}.";
    }

    public void ClearPolygonDraft()
    {
        _polygonVertices.Clear();
        _lastAction = "Cleared polygon draft.";
    }

    public void CompletePolygonObstacle()
    {
        GameEngine engine = RequireActiveEngine();
        if (_polygonVertices.Count < 3)
        {
            _lastAction = "Polygon requires at least 3 vertices.";
            return;
        }

        AddPolygonObstacle(engine, _polygonVertices);
        _polygonVertices.Clear();
        _polygonDrawMode = false;
        _lastAction = "Created static polygon obstacle through ManifestationObstacle bridge authoring.";
    }

    public Entity AddPolygonObstacle(GameEngine engine, IReadOnlyList<WorldCmInt2> vertices)
    {
        if (vertices.Count < 3 || vertices.Count > ManifestationObstaclePolygon2D.MaxVertices)
        {
            throw new ArgumentOutOfRangeException(nameof(vertices));
        }

        int centerX = 0;
        int centerY = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            centerX += vertices[i].X;
            centerY += vertices[i].Y;
        }

        centerX /= vertices.Count;
        centerY /= vertices.Count;

        var polygon = new ManifestationObstaclePolygon2D { VertexCount = (byte)vertices.Count };
        for (int i = 0; i < vertices.Count; i++)
        {
            polygon.SetVertex(i, new WorldCmInt2(vertices[i].X - centerX, vertices[i].Y - centerY));
        }

        Entity entity = engine.World.Create(
            new CapabilityStandardPhysics2DShowcaseEntityTag(),
            new CapabilityStandardPhysics2DShowcaseStaticObstacleTag(),
            new Position2D { Value = Fix64Vec2.FromInt(centerX, centerY) },
            WorldPositionCm.FromCm(centerX, centerY),
            new ManifestationObstacleIntent2D
            {
                Shape = ManifestationObstacleShape2D.Polygon,
                SinkPhysicsCollider = 1,
                SinkNavigationObstacle = 0,
                NavRadiusCm = 0
            },
            polygon);
        return entity;
    }

    public void AdjustDynamicBatch(int delta)
    {
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(RequireActiveEngine());
        _dynamicSpawnBatch = Math.Clamp(_dynamicSpawnBatch + delta, 1, config.MaxDynamicEntities);
        _lastAction = $"Dynamic spawn batch = {_dynamicSpawnBatch}.";
    }

    public void AdjustStaticBatch(int delta)
    {
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(RequireActiveEngine());
        _staticSpawnBatch = Math.Clamp(_staticSpawnBatch + delta, 1, config.MaxStaticObstacles);
        _lastAction = $"Static obstacle spawn batch = {_staticSpawnBatch}.";
    }

    public void AdjustPhysicsHz(int delta)
    {
        GameEngine engine = RequireActiveEngine();
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(engine);
        Physics2DTickPolicy policy = engine.GetService(CoreServiceKeys.Physics2DTickPolicy)
            ?? throw new InvalidOperationException("Physics2D showcase requires Physics2DTickPolicy.");
        policy.SetTargetHz(Math.Clamp(policy.TargetHz + delta, config.PhysicsHzMin, config.PhysicsHzMax));
        _lastAction = $"Physics Hz = {policy.TargetHz}.";
    }

    public void AdjustMaxSteps(int delta)
    {
        GameEngine engine = RequireActiveEngine();
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(engine);
        Physics2DTickPolicy policy = engine.GetService(CoreServiceKeys.Physics2DTickPolicy)
            ?? throw new InvalidOperationException("Physics2D showcase requires Physics2DTickPolicy.");
        policy.SetMaxStepsPerFixedTick(Math.Clamp(policy.MaxStepsPerFixedTick + delta, config.MaxStepsMin, config.MaxStepsMax));
        _lastAction = $"Physics max steps = {policy.MaxStepsPerFixedTick}.";
    }

    public void ToggleBroadphase()
    {
        GameEngine engine = RequireActiveEngine();
        Physics2DBroadphasePolicy policy = engine.GetService(CoreServiceKeys.Physics2DBroadphasePolicy)
            ?? throw new InvalidOperationException("Physics2D showcase requires Physics2DBroadphasePolicy.");
        policy.SetStrategy(policy.Strategy == Physics2DBroadphaseStrategyKind.SortAndSweep
            ? Physics2DBroadphaseStrategyKind.UniformGrid
            : Physics2DBroadphaseStrategyKind.SortAndSweep);
        _lastAction = $"Broadphase strategy = {policy.Strategy}.";
    }

    public void AdjustBroadphaseCellSize(int delta)
    {
        GameEngine engine = RequireActiveEngine();
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(engine);
        Physics2DBroadphasePolicy policy = engine.GetService(CoreServiceKeys.Physics2DBroadphasePolicy)
            ?? throw new InvalidOperationException("Physics2D showcase requires Physics2DBroadphasePolicy.");
        policy.SetCellSizeCm(Math.Clamp(policy.CellSizeCm + delta, config.BroadphaseCellSizeMinCm, config.BroadphaseCellSizeMaxCm));
        _lastAction = $"Broadphase cell size = {policy.CellSizeCm} cm.";
    }

    public void AdjustFriction(float delta) => AdjustMaterial(ref _friction, delta, c => c.FrictionMin, c => c.FrictionMax, "friction");
    public void AdjustRestitution(float delta) => AdjustMaterial(ref _restitution, delta, c => c.RestitutionMin, c => c.RestitutionMax, "restitution");
    public void AdjustDamping(float delta) => AdjustMaterial(ref _damping, delta, c => c.DampingMin, c => c.DampingMax, "damping");

    public void SetScatterTargetFromRatio(float ratio)
    {
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(RequireActiveEngine());
        _dynamicSpawnBatch = Math.Clamp((int)MathF.Round(Math.Clamp(ratio, 0f, 1f) * config.MaxDynamicEntities), 1, config.MaxDynamicEntities);
    }

    public void ApplyScatterTarget() => SpawnDynamicBatch();

    public void ApplyScatterLayout(int total)
    {
        ResetScene();
        _dynamicSpawnBatch = Math.Clamp(total, 1, EnsureConfig(RequireActiveEngine()).MaxDynamicEntities);
        SpawnDynamicBatch();
    }

    private void AdjustMaterial(ref float value, float delta, Func<CapabilityStandardPhysics2DShowcaseConfig, float> min, Func<CapabilityStandardPhysics2DShowcaseConfig, float> max, string label)
    {
        CapabilityStandardPhysics2DShowcaseConfig config = EnsureConfig(RequireActiveEngine());
        value = Math.Clamp(value + delta, min(config), max(config));
        _lastAction = $"Default dynamic {label} = {value:0.00}. New dynamic spawns use this value.";
    }

    private void SpawnDynamicEntity(GameEngine engine, CapabilityStandardPhysics2DShowcaseConfig config, int index)
    {
        var (x, y) = ResolveDynamicSpawnPoint(config, index);
        var position = Fix64Vec2.FromInt(x, y);
        var velocity = Fix64Vec2.FromInt((index & 1) == 0 ? 45 : -45, ((index / 2) & 1) == 0 ? 18 : -18);
        float metersX = x / 100f;
        float metersY = y / 100f;
        engine.World.Create(
            new CapabilityStandardPhysics2DShowcaseEntityTag(),
            new CapabilityStandardPhysics2DShowcaseDynamicTag(),
            new Position2D { Value = position },
            new PreviousPosition2D { Value = position },
            new Velocity2D { Linear = velocity, Angular = Fix64.Zero },
            Mass2D.FromFloat(1f, 1f),
            new Collider2D { Type = ColliderType2D.Circle, ShapeDataIndex = _dynamicShapeIndex },
            new PhysicsMaterial2D
            {
                Friction = Fix64.FromFloat(_friction),
                Restitution = Fix64.FromFloat(_restitution),
                BaseDamping = Fix64.FromFloat(_damping)
            },
            new WorldPositionCm { Value = position },
            new PreviousWorldPositionCm { Value = position },
            new VisualTransform
            {
                Position = new Vector3(metersX, 0f, metersY),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(0.36f, 0.36f, 0.36f)
            },
            PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.22f, 0.22f, 0.22f)),
            new CullState { IsVisible = true, LOD = LODLevel.High, DistanceToCameraSq = 0f });
    }

    private CapabilityStandardPhysics2DShowcaseConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("Physics2D showcase requires ConfigPipeline before loading config.");
        }

        _config = new CapabilityStandardPhysics2DShowcaseConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        _dynamicSpawnBatch = _config.DynamicSpawnBatch;
        _staticSpawnBatch = _config.StaticObstacleSpawnBatch;
        return _config;
    }

    private void EnsureDynamicShape(GameEngine engine, CapabilityStandardPhysics2DShowcaseConfig config)
    {
        if (_dynamicShapeIndex >= 0)
        {
            return;
        }

        if (engine.GetService(CoreServiceKeys.Physics2DShapeStorage) is not ShapeDataStorage2D shapeStorage)
        {
            throw new InvalidOperationException("Physics2D showcase requires Physics2D shape storage.");
        }

        _dynamicShapeIndex = shapeStorage.RegisterCircle(Fix64.FromInt(config.DynamicRadiusCm));
    }

    private void EnsureSpawnScratch(int count)
    {
        if (_spawnScratch.Length >= count)
        {
            return;
        }

        _spawnScratch = new RuntimeEntitySpawnRequest[count];
    }

    private GameEngine RequireActiveEngine()
    {
        return _activeEngine ?? throw new InvalidOperationException("Physics2D showcase requires an active engine.");
    }

    private MapId RequireCurrentMapId(GameEngine engine)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Physics2D showcase requires an active map session.");
        return session.MapId;
    }

    private bool IsShowcaseMap(string? mapId)
    {
        return _config != null && string.Equals(mapId, _config.MapId, StringComparison.Ordinal);
    }

    private void Disable(GameEngine engine)
    {
        ResetStaticObstacleSpawnReceipts(engine, EnsureConfig(engine));
        _activeEngine = null;
        _polygonVertices.Clear();
        _polygonDrawMode = false;
    }

    private void ResetStaticObstacleSpawnReceipts(GameEngine engine, CapabilityStandardPhysics2DShowcaseConfig config)
    {
        int receiptChannelId = ResolveReceiptChannelId(engine, config);
        RuntimeEntitySpawnQueue? spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue);
        spawnQueue?.RemoveForReceiptChannel(receiptChannelId);
        RuntimeEntitySpawnReceiptQueue? receiptQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue);
        if (receiptQueue != null)
        {
            while (receiptQueue.TryDequeueForChannel(receiptChannelId, out _))
            {
            }
        }
    }

    private int ResolveReceiptChannelId(GameEngine engine, CapabilityStandardPhysics2DShowcaseConfig config)
    {
        if (_receiptChannelId > 0)
        {
            return _receiptChannelId;
        }

        RuntimeEntitySpawnReceiptChannelRegistry channels = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
            ?? throw new InvalidOperationException("Physics2D showcase requires RuntimeEntitySpawnReceiptChannelRegistry.");
        _receiptChannelId = channels.Register(config.RuntimeSpawnReceiptChannelKey);
        return _receiptChannelId;
    }

    private int AllocateReceiptId()
    {
        int receiptId = _nextReceiptId++;
        if (receiptId <= 0)
        {
            _nextReceiptId = 1;
            receiptId = _nextReceiptId++;
        }

        return receiptId;
    }

    private static Physics2DPerfStats CaptureStats(World world, in QueryDescription statsQuery)
    {
        Physics2DPerfStats result = default;
        var found = false;
        world.Query(in statsQuery, (ref Physics2DPerfStats stats) =>
        {
            result = stats;
            found = true;
        });

        return found ? result : default;
    }

    private static void ValidateTemplate(GameEngine engine, string templateId)
    {
        if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
        {
            throw new InvalidOperationException($"Physics2D showcase requires configured entity template '{templateId}'.");
        }
    }

    private static void UpsertComponent<T>(World world, Entity entity, T component)
    {
        if (world.Has<T>(entity))
        {
            world.Set(entity, component);
        }
        else
        {
            world.Add(entity, component);
        }
    }

    private static (int X, int Y) ResolveDynamicSpawnPoint(CapabilityStandardPhysics2DShowcaseConfig config, int index)
    {
        int columns = Math.Max(1, (config.SpawnAreaHalfWidthCm * 2) / 90);
        int x = -config.SpawnAreaHalfWidthCm + (index % columns) * 90;
        int y = -config.SpawnAreaHalfHeightCm + (index / columns) * 90;
        return (x, y);
    }

    private static (int X, int Y) ResolveStaticSpawnPoint(CapabilityStandardPhysics2DShowcaseConfig config, int index)
    {
        int columns = Math.Max(1, (config.SpawnAreaHalfWidthCm * 2) / config.StaticObstacleSpacingCm);
        int x = -config.SpawnAreaHalfWidthCm + (index % columns) * config.StaticObstacleSpacingCm;
        int y = -config.SpawnAreaHalfHeightCm + (index / columns) * config.StaticObstacleSpacingCm;
        return (x, y);
    }
}
