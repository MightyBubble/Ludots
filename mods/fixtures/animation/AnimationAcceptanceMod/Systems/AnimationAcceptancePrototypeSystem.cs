using System;
using Arch.Core;
using Arch.System;
using AnimationAcceptanceMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;

namespace AnimationAcceptanceMod.Systems
{
    public sealed class AnimationAcceptancePrototypeSystem : BaseSystem<World, float>
    {
        private readonly GameEngine _engine;
        private readonly AnimationAcceptanceControlState _controls;
        private readonly PerformerInstanceBuffer _performers;

        private float _elapsed;
        private bool _tankFireGate;
        private bool _humanoidFireGate;
        private int _tankControllerId;
        private int _humanoidControllerId;

        public AnimationAcceptancePrototypeSystem(GameEngine engine)
            : base(engine.World)
        {
            _engine = engine;
            _controls = engine.GetService(AnimationAcceptanceServiceKeys.ControlState)
                ?? throw new InvalidOperationException("Animation acceptance requires control state service.");
            _performers = engine.GetService(CoreServiceKeys.PerformerInstanceBuffer)
                ?? throw new InvalidOperationException("Animation acceptance requires PerformerInstanceBuffer.");
        }

        public override void Update(in float dt)
        {
            float scaledDt = dt * _controls.PlaybackScale;
            _elapsed += scaledDt;
            ResolveControllerIds();

            _performers.ProcessAnimatorSlots(scaledDt, UpdatePerformerAnimatorSlot);
        }

        private void UpdatePerformerAnimatorSlot(
            int handle,
            ref PerformerInstance instance,
            int behaviorSlot,
            ref AnimatorPackedState packed,
            ref AnimatorRuntimeState runtime,
            ref AnimatorParameterBuffer parameters,
            ref AnimationOverlayRequest overlay,
            ref AnimatorFeedbackBuffer feedback,
            float dt)
        {
            int controllerId = packed.GetControllerId();
            if (controllerId != _tankControllerId && controllerId != _humanoidControllerId)
            {
                return;
            }

            Entity owner = instance.Owner;
            if (!World.IsAlive(owner) ||
                !World.Has<WorldPositionCm>(owner) ||
                !World.Has<FacingDirection>(owner))
            {
                throw new InvalidOperationException(
                    $"Animation acceptance performer instance '{handle}' requires a live owner with WorldPositionCm and FacingDirection.");
            }

            ref WorldPositionCm position = ref World.Get<WorldPositionCm>(owner);
            ref FacingDirection facing = ref World.Get<FacingDirection>(owner);

            if (controllerId == _tankControllerId)
            {
                UpdateTank(ref position, ref facing, ref parameters, ref overlay, dt);
            }
            else
            {
                UpdateHumanoid(ref position, ref facing, ref parameters, ref overlay, dt);
            }
        }

        private void ResolveControllerIds()
        {
            if (_tankControllerId != 0 && _humanoidControllerId != 0)
            {
                return;
            }

            var registry = _engine.GetService(CoreServiceKeys.AnimatorControllerRegistry)
                ?? throw new InvalidOperationException("Animation acceptance requires AnimatorControllerRegistry.");

            _tankControllerId = registry.GetId(AnimationAcceptanceIds.TankControllerKey);
            _humanoidControllerId = registry.GetId(AnimationAcceptanceIds.HumanoidControllerKey);
        }

        private void UpdateTank(
            ref WorldPositionCm position,
            ref FacingDirection facing,
            ref AnimatorParameterBuffer parameters,
            ref AnimationOverlayRequest overlay,
            float dt)
        {
            var slot = _controls.Tank;
            if (slot.DriverMode == AnimationAcceptanceDriverMode.Manual)
            {
                UpdateManualRig(slot, AnimationAcceptanceRigCatalog.Tank, ref position, ref facing, ref parameters, ref overlay, dt);
                return;
            }

            float orbit = _elapsed * 0.45f;
            float xCm = 1600f + MathF.Cos(orbit) * 520f;
            float yCm = 1500f + MathF.Sin(orbit * 0.7f) * 280f;
            position = WorldPositionCm.FromCmFloat(xCm, yCm);

            float velocityX = -MathF.Sin(orbit) * 520f * 0.45f;
            float velocityY = MathF.Cos(orbit * 0.7f) * 280f * 0.315f;
            float speed = MathF.Min(1f, MathF.Sqrt(velocityX * velocityX + velocityY * velocityY) / 220f);
            facing.AngleRad = MathF.Atan2(velocityY, velocityX);

            parameters.SetFloat(0, speed);
            parameters.SetBool(1, true);

            float shotCycle = Fraction(_elapsed * 0.55f);
            bool firingWindow = shotCycle >= 0.68f && shotCycle <= 0.9f;
            if (firingWindow && !_tankFireGate)
            {
                parameters.SetTrigger(2);
                _tankFireGate = true;
            }
            else if (!firingWindow)
            {
                _tankFireGate = false;
            }

            float lowerPhase = Fraction(_elapsed * 1.2f);
            float overlayTime = firingWindow ? Math.Clamp((shotCycle - 0.68f) / 0.22f, 0f, 1f) : 0f;
            float aimYaw = MathF.Sin(_elapsed * 0.9f) * 0.9f;

            overlay.BaseClip = CreateLocomotionClip(lowerPhase, speed);
            overlay.LayerClip = CreateAimClip(aimYaw, 1f);
            overlay.OverlayClip = CreateRecoilClip(overlayTime, firingWindow ? 1f : 0f);
        }

        private void UpdateHumanoid(
            ref WorldPositionCm position,
            ref FacingDirection facing,
            ref AnimatorParameterBuffer parameters,
            ref AnimationOverlayRequest overlay,
            float dt)
        {
            var slot = _controls.Humanoid;
            if (slot.DriverMode == AnimationAcceptanceDriverMode.Manual)
            {
                UpdateManualRig(slot, AnimationAcceptanceRigCatalog.Humanoid, ref position, ref facing, ref parameters, ref overlay, dt);
                return;
            }

            float travel = _elapsed * 0.8f;
            float xCm = 3000f + MathF.Sin(travel) * 340f;
            float yCm = 1800f + MathF.Sin(travel * 0.5f) * 140f;
            position = WorldPositionCm.FromCmFloat(xCm, yCm);

            float velocityX = MathF.Cos(travel) * 340f * 0.8f;
            float velocityY = MathF.Cos(travel * 0.5f) * 140f * 0.4f;
            float speed = MathF.Min(1f, MathF.Sqrt(velocityX * velocityX + velocityY * velocityY) / 240f);
            facing.AngleRad = MathF.Atan2(velocityY, velocityX);

            parameters.SetFloat(0, speed);
            parameters.SetBool(3, true);

            float burstCycle = Fraction(_elapsed * 0.72f);
            bool firingWindow = burstCycle >= 0.58f && burstCycle <= 0.82f;
            if (firingWindow && !_humanoidFireGate)
            {
                parameters.SetTrigger(4);
                _humanoidFireGate = true;
            }
            else if (!firingWindow)
            {
                _humanoidFireGate = false;
            }

            float lowerPhase = Fraction(_elapsed * 1.8f);
            float overlayWeight = firingWindow ? 1f : 0.45f;
            float overlayTime = firingWindow ? Math.Clamp((burstCycle - 0.58f) / 0.24f, 0f, 1f) : Fraction(_elapsed * 0.5f);
            float aimYaw = MathF.Sin(_elapsed * 1.15f) * 1.1f;

            overlay.BaseClip = CreateLocomotionClip(lowerPhase, speed);
            overlay.LayerClip = CreateAimClip(aimYaw, overlayWeight);
            overlay.OverlayClip = CreateRecoilClip(overlayTime, firingWindow ? 1f : 0f);
        }

        private static void UpdateManualRig(
            AnimationAcceptanceRigControlSlot slot,
            AnimationAcceptanceRigDefinition definition,
            ref WorldPositionCm position,
            ref FacingDirection facing,
            ref AnimatorParameterBuffer parameters,
            ref AnimationOverlayRequest overlay,
            float dt)
        {
            position = WorldPositionCm.FromCmFloat(definition.ManualAnchorCm.X, definition.ManualAnchorCm.Y);
            facing.AngleRad = slot.FacingYawRad;

            parameters.SetFloat(definition.SpeedParameterIndex, slot.Speed);
            parameters.SetBool(definition.LocomotionBoolParameterIndex, slot.MoveEnabled);
            if (slot.PendingFireTrigger)
            {
                parameters.SetTrigger(definition.FireTriggerParameterIndex);
                slot.PendingFireTrigger = false;
            }

            AdvanceManualOverlay(slot, definition, dt);

            overlay.BaseClip = CreateLocomotionClip(slot.LowerBodyPhase01, slot.MoveEnabled ? slot.Speed : slot.Speed * 0.18f);
            overlay.LayerClip = CreateAimClip(slot.AimYawRad, slot.OverlayWeight01);
            overlay.OverlayClip = CreateRecoilClip(slot.OverlayNormalizedTime01, slot.OverlayFiring ? 1f : 0f);
        }

        private static void AdvanceManualOverlay(
            AnimationAcceptanceRigControlSlot slot,
            AnimationAcceptanceRigDefinition definition,
            float dt)
        {
            float locomotionMax = definition.RigId == AnimationAcceptanceRigId.Tank ? 1.35f : 2.05f;
            float locomotionRate = 0.18f + (locomotionMax - 0.18f) * slot.Speed;
            slot.LowerBodyPhase01 = AnimationAcceptanceRigControlSlot.Wrap01(
                slot.LowerBodyPhase01 + dt * locomotionRate * (slot.MoveEnabled ? 1f : 0.18f));

            if (slot.OverlayFiring)
            {
                slot.FireNormalizedTime01 += dt / MathF.Max(0.05f, definition.FireOverlayDurationSeconds);
                slot.OverlayNormalizedTime01 = Math.Clamp(slot.FireNormalizedTime01, 0f, 1f);
                if (slot.FireNormalizedTime01 >= 1f)
                {
                    slot.OverlayFiring = false;
                    slot.FireNormalizedTime01 = 0f;
                }

                return;
            }

            slot.IdleOverlayClock01 = AnimationAcceptanceRigControlSlot.Wrap01(
                slot.IdleOverlayClock01 + dt * (definition.RigId == AnimationAcceptanceRigId.Tank ? 0.35f : 0.55f));
            slot.OverlayNormalizedTime01 = slot.IdleOverlayClock01;
        }

        private static AnimatorBuiltinClipState CreateLocomotionClip(float normalizedTime01, float speed)
        {
            float weight = Math.Clamp(speed, 0f, 1f);
            return AnimatorBuiltinClipState.Create(
                AnimatorBuiltinClipId.LocomotionCycle,
                normalizedTime01,
                weight,
                speed);
        }

        private static AnimatorBuiltinClipState CreateAimClip(float aimYawRad, float weight01)
        {
            return AnimatorBuiltinClipState.Create(
                AnimatorBuiltinClipId.AimYawOffset,
                normalizedTime01: 0f,
                weight01,
                scalar0: aimYawRad);
        }

        private static AnimatorBuiltinClipState CreateRecoilClip(float normalizedTime01, float weight01)
        {
            return AnimatorBuiltinClipState.Create(
                AnimatorBuiltinClipId.RecoilPulse,
                normalizedTime01,
                weight01);
        }

        private static float Fraction(float value)
        {
            return value - MathF.Floor(value);
        }
    }
}
