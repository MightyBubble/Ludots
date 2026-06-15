using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceLocalAvatarMoveSystem : ISystem<float>
    {
        private const float DefaultMoveSpeedCmPerSecond = 600f;

        private readonly GameEngine _engine;
        private readonly int _moveSpeedAttributeId;

        public CameraAcceptanceLocalAvatarMoveSystem(GameEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (dt <= 0f ||
                !CameraAcceptanceIds.IsAcceptanceMap(_engine.CurrentMapSession?.MapId.Value) ||
                !_engine.GlobalContext.TryGetValue(CoreServiceKeys.AuthoritativeInput.Name, out object? inputObj) ||
                inputObj is not IInputActionReader input ||
                !_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
                localObj is not Entity local ||
                !_engine.World.IsAlive(local) ||
                !_engine.World.Has<WorldPositionCm>(local))
            {
                return;
            }

            Vector2 moveIntent = input.ReadAction<Vector2>(CameraAcceptanceIds.LocalAvatarMoveActionId);
            if (moveIntent.LengthSquared() <= 0.000001f)
            {
                return;
            }

            moveIntent = WorldPlane2D.NormalizeOrDefault(moveIntent, Vector2.Zero);
            Vector2 move = OrbitCameraDirectionUtil.MoveInputToDirection(_engine.GameSession.Camera.State.Yaw, moveIntent);
            if (move.LengthSquared() <= 0.000001f)
            {
                return;
            }

            float speedCmPerSecond = ResolveMoveSpeedCmPerSecond(local);
            if (speedCmPerSecond <= 0f)
            {
                return;
            }

            ref WorldPositionCm position = ref _engine.World.Get<WorldPositionCm>(local);
            Vector2 current = position.Value.ToVector2();
            Vector2 next = current + (move * speedCmPerSecond * dt);
            WorldCmInt2 clamped = GroundRaycastUtil.ClampWorldCmToBounds(
                new WorldCmInt2((int)MathF.Round(next.X), (int)MathF.Round(next.Y)),
                _engine.CurrentMapSession?.PrimaryBoard?.WorldSize.Bounds ?? _engine.WorldSizeSpec.Bounds,
                out _);

            position = WorldPositionCm.FromCm(clamped.X, clamped.Y);
            UpdateFacing(local, move);
        }

        private float ResolveMoveSpeedCmPerSecond(Entity entity)
        {
            if (_moveSpeedAttributeId != AttributeRegistry.InvalidId &&
                _engine.World.TryGet(entity, out AttributeBuffer attributes))
            {
                float configured = attributes.GetCurrent(_moveSpeedAttributeId);
                if (configured > 0f)
                {
                    return configured;
                }
            }

            return DefaultMoveSpeedCmPerSecond;
        }

        private void UpdateFacing(Entity entity, Vector2 move)
        {
            float facingRad = WorldPlane2D.FacingRadFromDirection(in move);
            if (_engine.World.Has<FacingDirection>(entity))
            {
                ref FacingDirection facing = ref _engine.World.Get<FacingDirection>(entity);
                facing.AngleRad = facingRad;
            }
            else
            {
                _engine.World.Add(entity, new FacingDirection { AngleRad = facingRad });
            }
        }
    }
}
