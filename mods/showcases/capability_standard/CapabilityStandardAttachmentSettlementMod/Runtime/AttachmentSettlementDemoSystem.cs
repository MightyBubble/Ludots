using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;

namespace CapabilityStandardAttachmentSettlementMod.Runtime;

public sealed class AttachmentSettlementDemoState
{
    public bool Bound { get; set; }
    public int StableTicks { get; set; }
    public float HallXCm { get; set; }
    public float HallYCm { get; set; }
    public float AnnexXCm { get; set; }
    public float AnnexYCm { get; set; }
    public float TowerXCm { get; set; }
    public float TowerYCm { get; set; }
    public bool PosesStable { get; set; }
    public string Caption { get; set; } = "装载哨所组合…";
}

/// <summary>
/// 静态父样例：多拍恒重算后附楼/塔楼世界坐标保持声明偏移。
/// </summary>
public sealed class AttachmentSettlementDemoSystem : BaseSystem<World, float>
{
    private readonly AttachmentSettlementDemoState _state;
    private Entity _hall = Entity.Null;
    private Entity _annex = Entity.Null;
    private Entity _tower = Entity.Null;
    private float _annexX0;
    private float _annexY0;
    private float _towerX0;
    private float _towerY0;

    public AttachmentSettlementDemoSystem(World world, AttachmentSettlementDemoState state) : base(world)
    {
        _state = state;
    }

    public override void Update(in float dt)
    {
        if (!_state.Bound)
        {
            _hall = FindByName("Attachment.Settlement.Hall");
            _annex = FindByName("Attachment.Settlement.Annex");
            _tower = FindByName("Attachment.Settlement.Tower");
            if (_hall == Entity.Null || _annex == Entity.Null || _tower == Entity.Null)
            {
                return;
            }

            if (!World.Has<ChildOf>(_annex) || !World.Has<ChildOf>(_tower))
            {
                return;
            }

            Snapshot();
            _annexX0 = _state.AnnexXCm;
            _annexY0 = _state.AnnexYCm;
            _towerX0 = _state.TowerXCm;
            _towerY0 = _state.TowerYCm;
            _state.Bound = true;
            _state.Caption = "哨所静立：附楼与塔楼相对大厅保持偏移";
        }

        Snapshot();
        bool stable =
            MathF.Abs(_state.AnnexXCm - _annexX0) < 1f &&
            MathF.Abs(_state.AnnexYCm - _annexY0) < 1f &&
            MathF.Abs(_state.TowerXCm - _towerX0) < 1f &&
            MathF.Abs(_state.TowerYCm - _towerY0) < 1f;
        if (stable)
        {
            _state.StableTicks++;
        }
        else
        {
            _state.StableTicks = 0;
        }

        _state.PosesStable = _state.StableTicks >= 3;
        if (_state.PosesStable)
        {
            _state.Caption = "静物验收：多拍重派生后位置不变";
        }
    }

    private void Snapshot()
    {
        _state.HallXCm = World.Get<WorldPositionCm>(_hall).Value.X.ToFloat();
        _state.HallYCm = World.Get<WorldPositionCm>(_hall).Value.Y.ToFloat();
        _state.AnnexXCm = World.Get<WorldPositionCm>(_annex).Value.X.ToFloat();
        _state.AnnexYCm = World.Get<WorldPositionCm>(_annex).Value.Y.ToFloat();
        _state.TowerXCm = World.Get<WorldPositionCm>(_tower).Value.X.ToFloat();
        _state.TowerYCm = World.Get<WorldPositionCm>(_tower).Value.Y.ToFloat();
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
