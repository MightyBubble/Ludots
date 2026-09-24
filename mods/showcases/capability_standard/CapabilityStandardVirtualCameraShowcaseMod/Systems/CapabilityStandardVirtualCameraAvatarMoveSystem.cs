using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;

namespace CapabilityStandardVirtualCameraShowcaseMod.Systems;

internal sealed class CapabilityStandardVirtualCameraAvatarMoveSystem : ISystem<float>
{
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

        Entity controlledEntity = ResolveCommandSourcePrimary();
        ref AttributeBuffer attributes = ref ResolveAttributes(controlledEntity);
        Vector2 moveIntent = new(
            attributes.GetCurrent(_moveXAttributeId),
            attributes.GetCurrent(_moveYAttributeId));

        if (moveIntent.LengthSquared() <= 0.000001f)
        {
            return;
        }

        moveIntent = WorldPlane2D.NormalizeOrDefault(moveIntent, Vector2.Zero);
        Vector2 move = MoveInputToAvatarDirection(_engine.GameSession.Camera.State, moveIntent);
        if (move.LengthSquared() <= 0.000001f)
        {
            return;
        }

        float speedCmPerSecond = ResolveMoveSpeedCmPerSecond(in attributes);

        ref WorldPositionCm position = ref _engine.World.Get<WorldPositionCm>(controlledEntity);
        Vector2 current = position.Value.ToVector2();
        Vector2 next = ClampToWorldBounds(current + (move * speedCmPerSecond * dt));
        position = WorldPositionCm.FromCm((int)MathF.Round(next.X), (int)MathF.Round(next.Y));
        UpdateFacing(controlledEntity, move);
    }

    private Entity ResolveCommandSourcePrimary()
    {
        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity localPlayer ||
            localPlayer == Entity.Null ||
            !_engine.World.IsAlive(localPlayer))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase requires a live LocalPlayerEntity avatar.");
        }

        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? collectionsObj) ||
            collectionsObj is not EntityCollectionStore collections)
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase avatar movement requires EntityCollectionStore.");
        }

        if (!EntityCollectionContextRuntime.TryGetPrimary(
                _engine.World,
                collections,
                localPlayer,
                EntityCollectionKeys.CommandSource,
                out Entity controlledEntity))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase avatar movement requires a command source primary entity.");
        }

        if (!_engine.World.Has<WorldPositionCm>(controlledEntity))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase command source primary requires WorldPositionCm.");
        }

        if (!_engine.World.Has<FacingDirection>(controlledEntity))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase command source primary requires FacingDirection.");
        }

        return controlledEntity;
    }

    private ref AttributeBuffer ResolveAttributes(Entity controlledEntity)
    {
        if (!_engine.World.Has<AttributeBuffer>(controlledEntity))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase command source primary requires AttributeBuffer.");
        }

        return ref _engine.World.Get<AttributeBuffer>(controlledEntity);
    }

    private float ResolveMoveSpeedCmPerSecond(in AttributeBuffer attributes)
    {
        float configured = attributes.GetCurrent(_moveSpeedAttributeId);
        if (!float.IsFinite(configured) || configured <= 0f)
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera showcase command source primary requires positive MoveSpeed.");
        }

        return configured;
    }

    private static Vector2 MoveInputToAvatarDirection(CameraState cameraState, Vector2 move)
    {
        Vector2 cameraForward = WorldPlane2D.CameraForwardFromYawDegrees(cameraState.Yaw);
        Vector2 cameraRight = WorldPlane2D.CameraScreenRightFromYawDegrees(cameraState.Yaw);

        Vector2 direction = (cameraForward * move.Y) + (cameraRight * move.X);
        return WorldPlane2D.NormalizeOrDefault(direction, Vector2.Zero);
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
