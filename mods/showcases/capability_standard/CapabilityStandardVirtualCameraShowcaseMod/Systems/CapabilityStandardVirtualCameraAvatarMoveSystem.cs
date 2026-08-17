using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Client;
using Ludots.Core.Scripting;

namespace CapabilityStandardVirtualCameraShowcaseMod.Systems;

internal sealed class CapabilityStandardVirtualCameraAvatarMoveSystem : ISystem<float>
{
    private const float DefaultMoveSpeedCmPerSecond = 600f;

    private readonly GameEngine _engine;
    private readonly int _moveXAttributeId;
    private readonly int _moveYAttributeId;
    private readonly int _moveSpeedAttributeId;

    public CapabilityStandardVirtualCameraAvatarMoveSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _moveXAttributeId = AttributeRegistry.Register(CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveXAttribute);
        _moveYAttributeId = AttributeRegistry.Register(CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveYAttribute);
        _moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (dt <= 0f)
        {
            return;
        }

        string? mapId = _engine.CurrentMapSession?.MapId.Value;
        if (!CapabilityStandardVirtualCameraShowcaseIds.IsShowcaseMap(mapId))
        {
            return;
        }

        Entity solePossessedRep = RequireSolePossessedRep();
        ref AttributeBuffer attributes = ref ResolveAttributes(solePossessedRep);
        Vector2 moveIntent = new(
            attributes.GetCurrent(_moveXAttributeId),
            attributes.GetCurrent(_moveYAttributeId));

        if (moveIntent.LengthSquared() <= 0.000001f)
        {
            return;
        }

        moveIntent = WorldPlane2D.NormalizeOrDefault(moveIntent, Vector2.Zero);
        Vector2 move = OrbitCameraDirectionUtil.MoveInputToDirection(ClientLocalSeatAccess.ResolveAuthorityCamera(_engine).State.Yaw, moveIntent);
        if (move.LengthSquared() <= 0.000001f)
        {
            return;
        }

        float speedCmPerSecond = ResolveMoveSpeedCmPerSecond(in attributes);
        if (speedCmPerSecond <= 0f)
        {
            return;
        }

        ref WorldPositionCm position = ref _engine.World.Get<WorldPositionCm>(solePossessedRep);
        Vector2 current = position.Value.ToVector2();
        Vector2 next = ClampToWorldBounds(current + (move * speedCmPerSecond * dt));
        position = WorldPositionCm.FromCm((int)MathF.Round(next.X), (int)MathF.Round(next.Y));
        UpdateFacing(solePossessedRep, move);
    }

    private Entity RequireSolePossessedRep()
    {
        if (!ClientLocalSeatAccess.TryGetSolePossessedRep(_engine, out var solePossessedRep) ||
            solePossessedRep == Entity.Null ||
            !_engine.World.IsAlive(solePossessedRep))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase requires a live sole ClientLocalSeat possession avatar.");
        }

        if (!_engine.World.Has<WorldPositionCm>(solePossessedRep))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase sole ClientLocalSeat possession requires WorldPositionCm.");
        }

        if (!_engine.World.Has<FacingDirection>(solePossessedRep))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase sole ClientLocalSeat possession requires FacingDirection.");
        }

        return solePossessedRep;
    }

    private ref AttributeBuffer ResolveAttributes(Entity solePossessedRep)
    {
        if (!_engine.World.Has<AttributeBuffer>(solePossessedRep))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase sole ClientLocalSeat possession requires AttributeBuffer.");
        }

        return ref _engine.World.Get<AttributeBuffer>(solePossessedRep);
    }

    private float ResolveMoveSpeedCmPerSecond(in AttributeBuffer attributes)
    {
        float configured = attributes.GetCurrent(_moveSpeedAttributeId);
        return configured > 0f ? configured : DefaultMoveSpeedCmPerSecond;
    }

    private Vector2 ClampToWorldBounds(Vector2 positionCm)
    {
        WorldAabbCm bounds = _engine.CurrentMapSession?.PrimaryBoard?.WorldSize.Bounds ?? _engine.WorldSizeSpec.Bounds;
        return new Vector2(
            Math.Clamp(positionCm.X, bounds.Left, bounds.Right),
            Math.Clamp(positionCm.Y, bounds.Top, bounds.Bottom));
    }

    private void UpdateFacing(Entity entity, Vector2 move)
    {
        float facingRad = WorldPlane2D.FacingRadFromDirection(in move);
        ref FacingDirection facing = ref _engine.World.Get<FacingDirection>(entity);
        facing.AngleRad = facingRad;
    }
}
