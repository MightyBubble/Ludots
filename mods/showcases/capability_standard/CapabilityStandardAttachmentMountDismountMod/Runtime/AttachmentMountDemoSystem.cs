using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Movement;
using Ludots.Core.Scripting;

namespace CapabilityStandardAttachmentMountDismountMod.Runtime;

public enum AttachmentMountPhase : byte
{
    Boot = 0,
    Mount = 1,
    Ride = 2,
    Dismount = 3,
    Done = 4,
}

public sealed class AttachmentMountDemoState
{
    public AttachmentMountPhase Phase { get; set; }
    public int Tick { get; set; }
    public bool Bound { get; set; }
    public bool RiderAttached { get; set; }
    public PoseAuthorityKind RiderAuthority { get; set; }
    public float CarrierXCm { get; set; }
    public float RiderXCm { get; set; }
    public float RiderYCm { get; set; }
    public string Caption { get; set; } = "等待乘员与载具就位…";
}

/// <summary>
/// 自动上下车剧本：Effect Attach → 载具前移跟车 → Effect 周界 Detach。
/// </summary>
public sealed class AttachmentMountDemoSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly AttachmentMountDemoState _state;
    private Entity _carrier = Entity.Null;
    private Entity _rider = Entity.Null;
    private bool _attachPublished;
    private bool _detachPublished;
    private int _rideStartTick;

    public AttachmentMountDemoSystem(GameEngine engine, AttachmentMountDemoState state) : base(engine.World)
    {
        _engine = engine;
        _state = state;
    }

    public override void Update(in float dt)
    {
        if (!_state.Bound)
        {
            _carrier = FindByName("Attachment.Mount.Carrier");
            _rider = FindByName("Attachment.Mount.Rider");
            if (_carrier == Entity.Null || _rider == Entity.Null)
            {
                return;
            }

            _state.Bound = true;
            _state.Phase = AttachmentMountPhase.Mount;
            _state.Caption = "上车：乘员挂到载具座位";
        }

        _state.Tick++;
        EffectRequestQueue queue = _engine.GetService(CoreServiceKeys.EffectRequestQueue)
            ?? throw new InvalidOperationException("Mount showcase requires EffectRequestQueue.");

        switch (_state.Phase)
        {
            case AttachmentMountPhase.Mount:
                if (!_attachPublished)
                {
                    Publish(queue, "Effect.AttachmentMount.AttachRider", _carrier, _rider);
                    _attachPublished = true;
                }

                if (World.Has<ChildOf>(_rider) && World.Has<AttachedLocalPose>(_rider))
                {
                    _rideStartTick = _state.Tick;
                    _state.Phase = AttachmentMountPhase.Ride;
                    _state.Caption = "跟车：载具前移，乘员保持座位偏移";
                }

                break;
            case AttachmentMountPhase.Ride:
            {
                int rideTicks = Math.Max(0, _state.Tick - _rideStartTick);
                float x = Math.Min(3500f, rideTicks * 50f);
                World.Get<WorldPositionCm>(_carrier).Value = Fix64Vec2.FromFloat(x, 0f);
                if (x >= 3500f)
                {
                    _state.Phase = AttachmentMountPhase.Dismount;
                    _state.Caption = "下车：周边散布落位";
                }

                break;
            }
            case AttachmentMountPhase.Dismount:
                if (!_detachPublished)
                {
                    Publish(queue, "Effect.AttachmentMount.DetachRiderScatter", _carrier, _rider);
                    _detachPublished = true;
                }

                if (!World.Has<ChildOf>(_rider) && !World.Has<AttachedLocalPose>(_rider))
                {
                    _state.Phase = AttachmentMountPhase.Done;
                    _state.Caption = "上下车完成：关系解除，落点在车旁";
                }

                break;
        }

        Snapshot();
    }

    private void Snapshot()
    {
        _state.RiderAttached = _rider != Entity.Null && World.Has<ChildOf>(_rider);
        if (_carrier != Entity.Null && World.Has<WorldPositionCm>(_carrier))
        {
            _state.CarrierXCm = World.Get<WorldPositionCm>(_carrier).Value.X.ToFloat();
        }

        if (_rider != Entity.Null && World.Has<WorldPositionCm>(_rider))
        {
            _state.RiderXCm = World.Get<WorldPositionCm>(_rider).Value.X.ToFloat();
            _state.RiderYCm = World.Get<WorldPositionCm>(_rider).Value.Y.ToFloat();
        }

        if (_rider != Entity.Null && World.Has<PoseAuthority>(_rider))
        {
            _state.RiderAuthority = World.Get<PoseAuthority>(_rider).Value;
        }
    }

    private static void Publish(EffectRequestQueue queue, string templateKey, Entity source, Entity target)
    {
        int templateId = EffectTemplateIdRegistry.GetId(templateKey);
        if (templateId <= 0)
        {
            throw new InvalidOperationException($"Effect template '{templateKey}' is not registered.");
        }

        queue.Publish(new EffectRequest
        {
            RootId = 0,
            Source = source,
            Target = target,
            TargetContext = target,
            TemplateId = templateId,
        });
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
