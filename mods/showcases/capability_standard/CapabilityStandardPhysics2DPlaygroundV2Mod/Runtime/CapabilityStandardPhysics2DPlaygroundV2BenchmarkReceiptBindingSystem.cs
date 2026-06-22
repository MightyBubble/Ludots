using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

internal sealed class CapabilityStandardPhysics2DPlaygroundV2BenchmarkReceiptBindingSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CapabilityStandardPhysics2DPlaygroundV2Config _config;
    private int _receiptChannelId;

    public CapabilityStandardPhysics2DPlaygroundV2BenchmarkReceiptBindingSystem(
        GameEngine engine,
        CapabilityStandardPhysics2DPlaygroundV2Config config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!CapabilityStandardPhysics2DPlaygroundV2State.Enabled)
        {
            return;
        }

        RuntimeEntitySpawnReceiptQueue receipts = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("Physics2D Playground v2 requires RuntimeEntitySpawnReceiptQueue.");
        int receiptChannelId = ResolveReceiptChannelId();
        int boundCount = 0;
        while (receipts.TryDequeueForChannel(receiptChannelId, out RuntimeEntitySpawnReceipt receipt))
        {
            BindBenchmarkReceipt(in receipt);
            boundCount++;
        }

        if (boundCount > 0)
        {
            _engine.GlobalContext[CapabilityStandardPhysics2DPlaygroundV2State.BenchmarkLastSpawnedServiceKey] = boundCount;
        }
    }

    private int ResolveReceiptChannelId()
    {
        if (_receiptChannelId > 0)
        {
            return _receiptChannelId;
        }

        RuntimeEntitySpawnReceiptChannelRegistry channels = _engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
            ?? throw new InvalidOperationException("Physics2D Playground v2 requires RuntimeEntitySpawnReceiptChannelRegistry.");
        _receiptChannelId = channels.Register(CapabilityStandardPhysics2DPlaygroundV2InteractionSystem.BenchmarkReceiptChannelKey);
        return _receiptChannelId;
    }

    private void BindBenchmarkReceipt(in RuntimeEntitySpawnReceipt receipt)
    {
        if (receipt.Kind != RuntimeEntitySpawnKind.Template)
        {
            throw new InvalidOperationException($"Physics2D Playground v2 benchmark expected template receipt, got {receipt.Kind}.");
        }

        if (!string.Equals(receipt.TemplateId, _config.BenchmarkBodyTemplateId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Physics2D Playground v2 benchmark receipt template mismatch: expected '{_config.BenchmarkBodyTemplateId}', got '{receipt.TemplateId}'.");
        }

        Entity entity = receipt.Entity;
        World world = _engine.World;
        if (!world.IsAlive(entity))
        {
            throw new InvalidOperationException($"Physics2D Playground v2 benchmark receipt id {receipt.ReceiptId} returned a dead entity.");
        }

        RequireComponent<Position2D>(world, entity);
        RequireComponent<PreviousPosition2D>(world, entity);
        RequireComponent<Velocity2D>(world, entity);
        RequireComponent<Mass2D>(world, entity);
        RequireComponent<Collider2D>(world, entity);
        RejectComponent<NavAgent2D>(world, entity);
        RejectComponent<NavDesiredVelocity2D>(world, entity);
        RejectComponent<NavGoal2D>(world, entity);
        RejectComponent<NavObstacle2D>(world, entity);

        int ordinal = Math.Max(0, receipt.ReceiptId - 1);
        ref Velocity2D velocity = ref world.Get<Velocity2D>(entity);
        velocity.Linear = ComputeBenchmarkVelocity(ordinal, _config.BenchmarkInitialSpeedCmPerSec);
    }

    private static Fix64Vec2 ComputeBenchmarkVelocity(int ordinal, int speedCmPerSec)
    {
        int slot = ordinal % 12;
        float angle = MathF.Tau * slot / 12f;
        return Fix64Vec2.FromFloat(MathF.Cos(angle) * speedCmPerSec, MathF.Sin(angle) * speedCmPerSec);
    }

    private static void RequireComponent<T>(World world, Entity entity)
    {
        if (!world.Has<T>(entity))
        {
            throw new InvalidOperationException($"Physics2D Playground v2 benchmark body must author component {typeof(T).Name}.");
        }
    }

    private static void RejectComponent<T>(World world, Entity entity)
    {
        if (world.Has<T>(entity))
        {
            throw new InvalidOperationException($"Physics2D Playground v2 benchmark body must not author component {typeof(T).Name}.");
        }
    }
}
