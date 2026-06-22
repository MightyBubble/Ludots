using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using CapabilityStandardPhysics2DPlaygroundV2Mod.Input;

namespace CapabilityStandardPhysics2DPlaygroundV2Mod.Runtime;

public sealed class CapabilityStandardPhysics2DPlaygroundV2InteractionSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly CapabilityStandardPhysics2DPlaygroundV2Config _config;
    private Entity _primaryPhysicsEntity;
    private Entity _primaryNavEntity;
    private int _moveToOrderTypeId;

    public CapabilityStandardPhysics2DPlaygroundV2InteractionSystem(
        GameEngine engine,
        CapabilityStandardPhysics2DPlaygroundV2Config config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
        _config = config ?? throw new ArgumentNullException(nameof(config));
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
    }
}
