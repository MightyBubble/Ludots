using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Map;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;
using CapabilityStandardPhysics2DPlaygroundV2Mod.Input;

namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

public sealed class CapabilityStandardPhysics2DPlaygroundV2InteractionSystem : ISystem<float>
{
    internal const string BenchmarkReceiptChannelKey = "CapabilityStandardPhysics2DPlaygroundV2.BenchmarkSpawn";

    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly CapabilityStandardPhysics2DPlaygroundV2Config _config;
    private readonly RuntimeEntitySpawnRequest[] _benchmarkSpawnScratch;
    private Entity _primaryPhysicsEntity;
    private Entity _primaryNavEntity;
    private int _moveToOrderTypeId;
    private int _benchmarkSpawnCount;
    private int _benchmarkReceiptChannelId;
    private int _nextBenchmarkReceiptId;
    private int _benchmarkForceTemplateId;
    private int _benchmarkTemplateKeyId;

    public CapabilityStandardPhysics2DPlaygroundV2InteractionSystem(
        GameEngine engine,
        CapabilityStandardPhysics2DPlaygroundV2Config config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _benchmarkSpawnScratch = new RuntimeEntitySpawnRequest[config.SpawnScratchCapacity];
        _benchmarkSpawnCount = config.BenchmarkDefaultSpawnCount;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!CapabilityStandardPhysics2DPlaygroundV2State.Enabled)
        {
            return;
        }

        PublishPartitionCounts();

        if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.TogglePhysicsOnlyMode))
        {
            SetMode(CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly);
        }

        if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.ToggleNavMode))
        {
            SetMode(CapabilityStandardPhysics2DPlaygroundV2Mode.Nav);
        }

        if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.ApplyImpulse))
        {
            ApplyPhysicsImpulse();
        }

        if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.ApplyDisplacement))
        {
            ApplyPhysicsDisplacement();
        }

        HandleBenchmarkSpawnCountHotkeys(input);

        if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.SecondaryClick))
        {
            SpawnBenchmarkBodiesFromPointer(input);
        }

        if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkForcePulse))
        {
            ApplyBenchmarkForcePulse();
        }

        if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.SubmitNavMove))
        {
            SubmitNavMove();
        }
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    public static void SetMode(CapabilityStandardPhysics2DPlaygroundV2Mode mode, GameEngine engine)
    {
        CapabilityStandardPhysics2DPlaygroundV2State.ActiveMode = mode;
        engine.GlobalContext[CapabilityStandardPhysics2DPlaygroundV2State.ActiveModeServiceKey] = mode.ToString();
    }

    public bool ApplyPhysicsImpulse()
    {
        if (!TryFindPrimaryPhysicsEntity(out Entity entity) ||
            !_world.Has<CapabilityStandardPhysics2DPlaygroundV2ModePartition>(entity))
        {
            return false;
        }

        var partition = _world.Get<CapabilityStandardPhysics2DPlaygroundV2ModePartition>(entity);
        if (partition.Mode != CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly)
        {
            return false;
        }

        if (!_world.Has<Velocity2D>(entity) || !_world.Has<Mass2D>(entity))
        {
            return false;
        }

        ref var mass = ref _world.Get<Mass2D>(entity);
        if (mass.IsStatic)
        {
            return false;
        }

        ref var velocity = ref _world.Get<Velocity2D>(entity);
        velocity.Linear = velocity.Linear + Fix64Vec2.FromInt(_config.PhysicsImpulseXCmPerSec, _config.PhysicsImpulseYCmPerSec);
        return true;
    }

    public bool ApplyPhysicsDisplacement()
    {
        if (!TryFindPrimaryPhysicsEntity(out Entity entity))
        {
            return false;
        }

        if (!_world.Has<MovementSuppressed2D>(entity))
        {
            _world.Add(entity, new MovementSuppressed2D());
        }

        if (_world.Has<Velocity2D>(entity))
        {
            ref var velocity = ref _world.Get<Velocity2D>(entity);
            velocity.Linear = Fix64Vec2.Zero;
        }

        EntityCreationHelper.CreateDisplacement(_world, new DisplacementState
        {
            TargetEntity = entity,
            SourceEntity = entity,
            DirectionMode = DisplacementDirectionMode.Fixed,
            FixedDirectionRad = Fix64.Zero,
            TotalDistanceCm = _config.DisplacementDistanceCm,
            RemainingDistanceCm = Fix64.FromInt(_config.DisplacementDistanceCm),
            TotalDurationTicks = _config.DisplacementTicks,
            RemainingTicks = _config.DisplacementTicks,
            OverrideNavigation = true,
            MovementSuppressionApplied = true
        });
        return true;
    }

    public int SetBenchmarkSpawnCountForSlot(int slot)
    {
        if (slot < 1 || slot > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), "Physics2D Playground v2 benchmark spawn count slots are 1..9.");
        }

        _benchmarkSpawnCount = slot * 10;
        PublishBenchmarkState(CountBenchmarkBodies(), lastSpawned: null, forcePulse: null);
        return _benchmarkSpawnCount;
    }

    public bool SpawnBenchmarkBodiesAt(Fix64Vec2 centerCm)
    {
        return SpawnBenchmarkBodiesAt(centerCm, _benchmarkSpawnCount) > 0;
    }

    public int SpawnBenchmarkBodiesAt(Fix64Vec2 centerCm, int count)
    {
        if (CapabilityStandardPhysics2DPlaygroundV2State.ActiveMode != CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly)
        {
            return 0;
        }

        if (count <= 0)
        {
            return 0;
        }

        if (count > _benchmarkSpawnScratch.Length)
        {
            throw new InvalidOperationException(
                $"Physics2D Playground v2 benchmark spawn count {count} exceeds scratch capacity {_benchmarkSpawnScratch.Length}.");
        }

        RuntimeEntitySpawnQueue spawnQueue = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("Physics2D Playground v2 requires RuntimeEntitySpawnQueue.");
        MapId mapId = RequireCurrentMapId();
        int receiptChannelId = ResolveBenchmarkReceiptChannelId();
        int batchId = _nextBenchmarkReceiptId + 1;

        for (int i = 0; i < count; i++)
        {
            Fix64Vec2 offset = ComputeBenchmarkSpawnOffset(i, count, _config.BenchmarkSpawnRadiusCm);
            _benchmarkSpawnScratch[i] = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = _config.BenchmarkBodyTemplateId,
                MapId = mapId,
                WorldPositionCm = centerCm + offset,
                HasWorldPosition = 1,
                FacingAngleRad = 0f,
                HasFacing = 1,
                ReceiptChannelId = receiptChannelId,
                ReceiptId = batchId + i,
                EmitReceipt = 1
            };
        }

        int written = spawnQueue.EnqueueMany(_benchmarkSpawnScratch.AsSpan(0, count));
        if (written != count)
        {
            throw new InvalidOperationException(
                $"Physics2D Playground v2 benchmark enqueued {written} spawn requests, expected {count}.");
        }

        _nextBenchmarkReceiptId = batchId + count - 1;
        PublishBenchmarkState(CountBenchmarkBodies(), count, forcePulse: null);
        return written;
    }

    public bool ApplyBenchmarkForcePulse()
    {
        if (!TryFindPrimaryPhysicsEntity(out Entity target))
        {
            return false;
        }

        if (!_world.Has<AttributeBuffer>(target) || !_world.Has<ForceInput2D>(target))
        {
            return false;
        }

        EffectRequestQueue queue = _engine.GetService(CoreServiceKeys.EffectRequestQueue)
            ?? throw new InvalidOperationException("Physics2D Playground v2 requires EffectRequestQueue for benchmark force pulse.");
        int templateId = ResolveBenchmarkForceTemplateId();
        if (templateId <= 0)
        {
            return false;
        }

        var caller = default(EffectConfigParams);
        caller.TryAddFloat(EffectParamKeys.ForceXAttribute, _config.BenchmarkForceXCmPerSec2);
        caller.TryAddFloat(EffectParamKeys.ForceYAttribute, _config.BenchmarkForceYCmPerSec2);

        queue.Publish(new EffectRequest
        {
            RootId = 0,
            Source = target,
            Target = target,
            TargetContext = target,
            TemplateId = templateId,
            CallerParams = caller,
            HasCallerParams = true
        });
        PublishBenchmarkState(CountBenchmarkBodies(), lastSpawned: null, forcePulse: 1);
        return true;
    }

    public bool SubmitNavMove()
    {
        if (!TryFindPrimaryNavEntity(out Entity actor))
        {
            return false;
        }

        if (_world.Has<MovementSuppressed2D>(actor))
        {
            return false;
        }

        OrderQueue orderQueue = _engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("Physics2D Playground v2 requires OrderQueue.");
        OrderTypeRegistry orderTypes = _engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("Physics2D Playground v2 requires OrderTypeRegistry.");

        if (_moveToOrderTypeId <= 0)
        {
            _moveToOrderTypeId = orderTypes.GetId("moveTo");
            if (_moveToOrderTypeId <= 0)
            {
                throw new InvalidOperationException("Physics2D Playground v2 requires registered moveTo order type.");
            }
        }

        var order = new Order
        {
            Actor = actor,
            OrderTypeId = _moveToOrderTypeId,
            SubmitMode = OrderSubmitMode.Immediate,
            PlayerId = 1,
            Args = new OrderArgs
            {
                Spatial = new OrderSpatial
                {
                    Kind = OrderSpatialKind.WorldCm,
                    Mode = OrderCollectionMode.Single,
                    WorldCm = new Vector3(_config.NavTargetXCm, 0f, _config.NavTargetYCm)
                }
            }
        };

        return orderQueue.TryEnqueue(in order);
    }

    private void SetMode(CapabilityStandardPhysics2DPlaygroundV2Mode mode)
    {
        SetMode(mode, _engine);
    }

    private void HandleBenchmarkSpawnCountHotkeys(IInputActionReader input)
    {
        if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount1))
        {
            SetBenchmarkSpawnCountForSlot(1);
        }
        else if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount2))
        {
            SetBenchmarkSpawnCountForSlot(2);
        }
        else if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount3))
        {
            SetBenchmarkSpawnCountForSlot(3);
        }
        else if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount4))
        {
            SetBenchmarkSpawnCountForSlot(4);
        }
        else if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount5))
        {
            SetBenchmarkSpawnCountForSlot(5);
        }
        else if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount6))
        {
            SetBenchmarkSpawnCountForSlot(6);
        }
        else if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount7))
        {
            SetBenchmarkSpawnCountForSlot(7);
        }
        else if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount8))
        {
            SetBenchmarkSpawnCountForSlot(8);
        }
        else if (input.PressedThisFrame(CapabilityStandardPhysics2DPlaygroundV2InputActions.BenchmarkSpawnCount9))
        {
            SetBenchmarkSpawnCountForSlot(9);
        }
    }

    private bool SpawnBenchmarkBodiesFromPointer(IInputActionReader input)
    {
        if (!TryGetGroundPointer(input, out Fix64Vec2 worldCm))
        {
            return false;
        }

        return SpawnBenchmarkBodiesAt(worldCm);
    }

    private bool TryGetGroundPointer(IInputActionReader input, out Fix64Vec2 worldCm)
    {
        worldCm = default;
        if (_engine.GetService(CoreServiceKeys.ScreenRayProvider) is not IScreenRayProvider rayProvider ||
            !_engine.TryGetService(CoreServiceKeys.WorldSizeSpec, out WorldSizeSpec worldSize))
        {
            return false;
        }

        var pointer = input.ReadAction<Vector2>(CapabilityStandardPhysics2DPlaygroundV2InputActions.PointerPos);
        var ray = rayProvider.GetRay(pointer);
        if (!GroundRaycastUtil.TryGetGroundWorldCmBounded(in ray, worldSize, out WorldCmInt2 hit))
        {
            return false;
        }

        worldCm = Fix64Vec2.FromInt(hit.X, hit.Y);
        return true;
    }

    private bool TryFindPrimaryPhysicsEntity(out Entity entity)
    {
        if (_primaryPhysicsEntity != Entity.Null && _world.IsAlive(_primaryPhysicsEntity))
        {
            entity = _primaryPhysicsEntity;
            return true;
        }

        return TryFindTemplate(_config.PrimaryPhysicsTemplateId, out _primaryPhysicsEntity, out entity);
    }

    private bool TryFindPrimaryNavEntity(out Entity entity)
    {
        if (_primaryNavEntity != Entity.Null && _world.IsAlive(_primaryNavEntity))
        {
            entity = _primaryNavEntity;
            return true;
        }

        return TryFindTemplate(_config.PrimaryNavTemplateId, out _primaryNavEntity, out entity);
    }

    private bool TryFindTemplate(string templateId, out Entity cache, out Entity entity)
    {
        cache = Entity.Null;
        entity = Entity.Null;

        EntityTemplateKeyRegistry templateKeys = _engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            return false;
        }

        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef>();
        _world.Query(in query, (Entity candidate, ref EntityTemplateKeyRef keyRef) =>
        {
            if (found == Entity.Null && keyRef.TemplateKeyId == templateKeyId)
            {
                found = candidate;
            }
        });

        entity = found;
        cache = found;
        return entity != Entity.Null;
    }

    private void PublishPartitionCounts()
    {
        int physicsOnly = 0;
        int nav = 0;
        var query = new QueryDescription().WithAll<CapabilityStandardPhysics2DPlaygroundV2ModePartition>();
        _world.Query(in query, (ref CapabilityStandardPhysics2DPlaygroundV2ModePartition partition) =>
        {
            if (partition.Mode == CapabilityStandardPhysics2DPlaygroundV2Mode.PhysicsOnly)
            {
                physicsOnly++;
            }
            else if (partition.Mode == CapabilityStandardPhysics2DPlaygroundV2Mode.Nav)
            {
                nav++;
            }
        });

        _engine.GlobalContext[CapabilityStandardPhysics2DPlaygroundV2State.PhysicsOnlyEntityCountServiceKey] = physicsOnly;
        _engine.GlobalContext[CapabilityStandardPhysics2DPlaygroundV2State.NavEntityCountServiceKey] = nav;
        PublishBenchmarkState(CountBenchmarkBodies(), lastSpawned: null, forcePulse: null);
    }

    private MapId RequireCurrentMapId()
    {
        MapSession session = _engine.CurrentMapSession
            ?? throw new InvalidOperationException("Physics2D Playground v2 benchmark requires an active map session.");
        if (!string.Equals(session.MapId.Value, _config.MapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Physics2D Playground v2 benchmark requires active map '{_config.MapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }

    private int ResolveBenchmarkReceiptChannelId()
    {
        if (_benchmarkReceiptChannelId > 0)
        {
            return _benchmarkReceiptChannelId;
        }

        RuntimeEntitySpawnReceiptChannelRegistry channels = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
            ?? throw new InvalidOperationException("Physics2D Playground v2 requires RuntimeEntitySpawnReceiptChannelRegistry.");
        _benchmarkReceiptChannelId = channels.Register(BenchmarkReceiptChannelKey);
        return _benchmarkReceiptChannelId;
    }

    private int ResolveBenchmarkForceTemplateId()
    {
        if (_benchmarkForceTemplateId > 0)
        {
            return _benchmarkForceTemplateId;
        }

        _benchmarkForceTemplateId = EffectTemplateIdRegistry.GetId("Effect.Preset.ApplyForce2D");
        return _benchmarkForceTemplateId;
    }

    private int ResolveBenchmarkTemplateKeyId()
    {
        if (_benchmarkTemplateKeyId > 0)
        {
            return _benchmarkTemplateKeyId;
        }

        EntityTemplateKeyRegistry templateKeys = _engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("EntityTemplateKeyRegistry missing.");
        if (!templateKeys.TryGetId(_config.BenchmarkBodyTemplateId, out _benchmarkTemplateKeyId) ||
            _benchmarkTemplateKeyId <= 0)
        {
            _benchmarkTemplateKeyId = templateKeys.Register(_config.BenchmarkBodyTemplateId);
        }

        return _benchmarkTemplateKeyId;
    }

    private int CountBenchmarkBodies()
    {
        int templateKeyId = ResolveBenchmarkTemplateKeyId();
        int count = 0;
        var query = new QueryDescription().WithAll<EntityTemplateKeyRef, Position2D, Velocity2D, Mass2D>();
        _world.Query(in query, (ref EntityTemplateKeyRef keyRef) =>
        {
            if (keyRef.TemplateKeyId == templateKeyId)
            {
                count++;
            }
        });

        return count;
    }

    private void PublishBenchmarkState(int benchmarkBodies, int? lastSpawned, int? forcePulse)
    {
        _engine.GlobalContext[CapabilityStandardPhysics2DPlaygroundV2State.BenchmarkSpawnCountServiceKey] = _benchmarkSpawnCount;
        _engine.GlobalContext[CapabilityStandardPhysics2DPlaygroundV2State.BenchmarkEntityCountServiceKey] = benchmarkBodies;
        if (lastSpawned.HasValue)
        {
            _engine.GlobalContext[CapabilityStandardPhysics2DPlaygroundV2State.BenchmarkLastSpawnedServiceKey] = lastSpawned.Value;
        }

        if (forcePulse.HasValue)
        {
            _engine.GlobalContext[CapabilityStandardPhysics2DPlaygroundV2State.BenchmarkLastForcePulseServiceKey] = forcePulse.Value;
        }
    }

    private static Fix64Vec2 ComputeBenchmarkSpawnOffset(int index, int count, int radiusCm)
    {
        int ring = 1 + index / 12;
        int ordinal = index % 12;
        float angle = (MathF.Tau * ordinal / 12f) + (ring * 0.17364818f);
        float normalizedRing = MathF.Min(1f, ring / MathF.Max(1f, MathF.Ceiling(count / 12f)));
        float radius = radiusCm * (0.25f + 0.75f * normalizedRing);
        return Fix64Vec2.FromFloat(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
    }
}
