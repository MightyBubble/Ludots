using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace CapabilityStandardAttachmentVehicleParadeMod.Runtime;

public enum AttachmentVehicleParadePhase : byte
{
    Boot = 0,
    Drive = 1,
    TurnTurret = 2,
    Done = 3,
}

public sealed class AttachmentVehicleParadeDemoState
{
    public AttachmentVehicleParadePhase Phase { get; set; }
    public int Tick { get; set; }
    public bool TreeReady { get; set; }
    public float ChassisXCm { get; set; }
    public float TurretFacingRad { get; set; }
    public float BarrelXCm { get; set; }
    public float BarrelYCm { get; set; }
    public string Caption { get; set; } = "装载装甲组合…";
}

/// <summary>
/// 自动阅兵剧本：先开底盘证明多层跟随，再转炮塔证明独立瞄准与炮管 ParentFacing。
/// </summary>
public sealed class AttachmentVehicleParadeDemoSystem : BaseSystem<World, float>
{
    private readonly AttachmentVehicleParadeDemoState _state;
    private Entity _chassis = Entity.Null;
    private Entity _turret = Entity.Null;
    private Entity _barrel = Entity.Null;

    public AttachmentVehicleParadeDemoSystem(World world, AttachmentVehicleParadeDemoState state) : base(world)
    {
        _state = state;
    }

    public override void Update(in float dt)
    {
        if (!_state.TreeReady)
        {
            if (!TryBind())
            {
                return;
            }

            _state.TreeReady = true;
            _state.Phase = AttachmentVehicleParadePhase.Drive;
            _state.Caption = "底盘开动：炮塔与炮管应贴着车走";
        }

        _state.Tick++;
        switch (_state.Phase)
        {
            case AttachmentVehicleParadePhase.Drive:
            {
                float x = Math.Min(2000f, _state.Tick * 40f);
                World.Get<WorldPositionCm>(_chassis).Value = Fix64Vec2.FromFloat(x, 0f);
                if (x >= 2000f)
                {
                    _state.Phase = AttachmentVehicleParadePhase.TurnTurret;
                    _state.Caption = "炮塔独立转向：炮管跟着炮塔朝前伸";
                }

                break;
            }
            case AttachmentVehicleParadePhase.TurnTurret:
            {
                float facing = MathF.PI / 2f;
                World.Get<FacingDirection>(_turret).AngleRad = facing;
                if (_state.Tick > 70)
                {
                    _state.Phase = AttachmentVehicleParadePhase.Done;
                    _state.Caption = "阅兵完成：多层挂接跟随与独立瞄准成立";
                }

                break;
            }
        }

        Snapshot();
    }

    private bool TryBind()
    {
        _chassis = FindByName("Attachment.Vehicle.Chassis");
        _turret = FindByName("Attachment.Vehicle.Turret");
        _barrel = FindByName("Attachment.Vehicle.Barrel");
        return _chassis != Entity.Null && _turret != Entity.Null && _barrel != Entity.Null
            && World.Has<ChildOf>(_turret) && World.Has<ChildOf>(_barrel);
    }

    private void Snapshot()
    {
        if (_chassis == Entity.Null)
        {
            return;
        }

        _state.ChassisXCm = World.Get<WorldPositionCm>(_chassis).Value.X.ToFloat();
        if (_turret != Entity.Null && World.Has<FacingDirection>(_turret))
        {
            _state.TurretFacingRad = World.Get<FacingDirection>(_turret).AngleRad;
        }

        if (_barrel != Entity.Null && World.Has<WorldPositionCm>(_barrel))
        {
            _state.BarrelXCm = World.Get<WorldPositionCm>(_barrel).Value.X.ToFloat();
            _state.BarrelYCm = World.Get<WorldPositionCm>(_barrel).Value.Y.ToFloat();
        }
    }

    private Entity FindByName(string name)
    {
        Entity found = Entity.Null;
        World.Query(in new QueryDescription().WithAll<Name>(), (Entity entity, ref Name componentName) =>
        {
            if (found == Entity.Null && string.Equals(componentName.Value, name, StringComparison.Ordinal))
            {
                found = entity;
            }
        });
        return found;
    }
}
