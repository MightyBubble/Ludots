using System;
using Arch.Core;
using Arch.System;
using AnimationAcceptanceMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace AnimationAcceptanceMod.Systems
{
    public sealed class AnimationAcceptancePrototypeSystem : BaseSystem<World, float>
    {
        private readonly GameEngine _engine;
        private readonly AnimationAcceptanceControlState _controls;
        private readonly PresenterEntityRuntime _instances;
        private readonly PresenterAnimatorStateBuffer _animatorStates;
        private readonly PresenterDefinitionRegistry _definitions;

        private float _elapsed;
        private bool _tankFireGate;
        private bool _humanoidFireGate;
        private int _tankDefinitionId;
        private int _humanoidDefinitionId;

        public AnimationAcceptancePrototypeSystem(GameEngine engine)
            : base(engine.World)
        {
            _engine = engine;
            _controls = engine.GetService(AnimationAcceptanceServiceKeys.ControlState)
                ?? throw new InvalidOperationException("Animation acceptance requires control state service.");
            _instances = engine.GetService(CoreServiceKeys.PresenterEntityRuntime)
                ?? throw new InvalidOperationException("Animation acceptance requires PresenterEntityRuntime.");
            _animatorStates = engine.GetService(CoreServiceKeys.PresenterAnimatorStateBuffer)
                ?? throw new InvalidOperationException("Animation acceptance requires PresenterAnimatorStateBuffer.");
            _definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("Animation acceptance requires PresenterDefinitionRegistry.");
        }

        public override void Update(in float dt)
        {
            float scaledDt = dt * _controls.PlaybackScale;
            _elapsed += scaledDt;
            ResolveDefinitionIds();

            var query = new QueryDescription().WithAll<PresenterState>();
            World.Query(in query, (Entity entity, ref PresenterState instance) =>
            {
                if (!_animatorStates.IsAllocated(entity))
                {
                    return;
                }

                if (instance.AnchorKind != PresentationAnchorKind.Entity || !World.IsAlive(instance.OwnerEntity))
                {
                    return;
                }

                if (!World.Has<WorldPositionCm>(instance.OwnerEntity) || !World.Has<FacingDirection>(instance.OwnerEntity))
                {
                    return;
                }

                ref WorldPositionCm position = ref World.Get<WorldPositionCm>(instance.OwnerEntity);
                ref FacingDirection facing = ref World.Get<FacingDirection>(instance.OwnerEntity);
                ref AnimatorPackedState packed = ref _animatorStates.GetPackedState(entity);
                ref AnimatorRuntimeState runtime = ref _animatorStates.GetRuntimeState(entity);
                ref AnimationOverlayRequest overlay = ref _animatorStates.GetOverlay(entity);

                if (instance.DefId == _tankDefinitionId)
                {
                    UpdateTank(_controls.Tank, ref position, ref facing, ref packed, ref runtime, ref overlay, scaledDt);
                }
                else if (instance.DefId == _humanoidDefinitionId)
                {
                    UpdateHumanoid(_controls.Humanoid, ref position, ref facing, ref packed, ref runtime, ref overlay, scaledDt);
                }
            });
        }

        private void ResolveDefinitionIds()
        {
            if (_tankDefinitionId > 0 && _humanoidDefinitionId > 0)
            {
                return;
            }

            _tankDefinitionId = _definitions.GetId(AnimationAcceptanceIds.TankPresenterDefinitionId);
            _humanoidDefinitionId = _definitions.GetId(AnimationAcceptanceIds.HumanoidPresenterDefinitionId);
            if (_tankDefinitionId <= 0 || _humanoidDefinitionId <= 0)
            {
                throw new InvalidOperationException("Animation acceptance presenter definitions are missing.");
            }
        }

        private void UpdateTank(
            AnimationAcceptanceRigControlSlot slot,
            ref WorldPositionCm position,
            ref FacingDirection facing,
            ref AnimatorPackedState packed,
            ref AnimatorRuntimeState runtime,
            ref AnimationOverlayRequest overlay,
            float dt)
        {
            if (slot.DriverMode == AnimationAcceptanceDriverMode.Manual)
            {
                UpdateManualRig(slot, AnimationAcceptanceRigCatalog.Tank, ref position, ref facing, ref overlay, dt);
                ApplyAnimatorPreview(AnimationAcceptanceRigCatalog.Tank, slot, ref packed, ref runtime, dt);
                return;
            }

            float orbit = _elapsed * 0.45f;
            float xCm = 1600f + MathF.Cos(orbit) * 520f;
            float yCm = 1500f + MathF.Sin(orbit * 0.7f) * 280f;
            position = WorldPositionCm.FromCmFloat(xCm, yCm);

            float velocityX = -MathF.Sin(orbit) * 520f * 0.45f;
            float velocityY = MathF.Cos(orbit * 0.7f) * 280f * 0.315f;
            float speed = MathF.Min(1f, MathF.Sqrt(velocityX * velocityX + velocityY * velocityY) / 220f);
            facing.AngleRad = WorldPlane2D.FacingRadFromDirection(velocityX, velocityY);
            slot.Speed = speed;
            slot.MoveEnabled = true;
            slot.FacingYawRad = facing.AngleRad;

            float shotCycle = Fraction(_elapsed * 0.55f);
            bool firingWindow = shotCycle >= 0.68f && shotCycle <= 0.9f;
            if (firingWindow && !_tankFireGate)
            {
                slot.QueueFire();
                _tankFireGate = true;
            }
            else if (!firingWindow)
            {
                _tankFireGate = false;
            }

            float lowerPhase = Fraction(_elapsed * 1.2f);
            float overlayTime = firingWindow ? Math.Clamp((shotCycle - 0.68f) / 0.22f, 0f, 1f) : 0f;
            float aimYaw = MathF.Sin(_elapsed * 0.9f) * 0.9f;
            slot.OverlayFiring = firingWindow;
            slot.LowerBodyPhase01 = lowerPhase;
            slot.AimYawRad = aimYaw;
            slot.OverlayWeight01 = 1f;
            slot.OverlayNormalizedTime01 = overlayTime;

            overlay.BaseClip = CreateLocomotionClip(lowerPhase, speed);
            overlay.LayerClip = CreateAimClip(aimYaw, 1f);
            overlay.OverlayClip = CreateRecoilClip(overlayTime, firingWindow ? 1f : 0f);
            ApplyAnimatorPreview(AnimationAcceptanceRigCatalog.Tank, slot, ref packed, ref runtime, dt);
        }

        private void UpdateHumanoid(
            AnimationAcceptanceRigControlSlot slot,
            ref WorldPositionCm position,
            ref FacingDirection facing,
            ref AnimatorPackedState packed,
            ref AnimatorRuntimeState runtime,
            ref AnimationOverlayRequest overlay,
            float dt)
        {
            if (slot.DriverMode == AnimationAcceptanceDriverMode.Manual)
            {
                UpdateManualRig(slot, AnimationAcceptanceRigCatalog.Humanoid, ref position, ref facing, ref overlay, dt);
                ApplyAnimatorPreview(AnimationAcceptanceRigCatalog.Humanoid, slot, ref packed, ref runtime, dt);
                return;
            }

            float travel = _elapsed * 0.8f;
            float xCm = 3000f + MathF.Sin(travel) * 340f;
            float yCm = 1800f + MathF.Sin(travel * 0.5f) * 140f;
            position = WorldPositionCm.FromCmFloat(xCm, yCm);

            float velocityX = MathF.Cos(travel) * 340f * 0.8f;
            float velocityY = MathF.Cos(travel * 0.5f) * 140f * 0.4f;
            float speed = MathF.Min(1f, MathF.Sqrt(velocityX * velocityX + velocityY * velocityY) / 240f);
            facing.AngleRad = WorldPlane2D.FacingRadFromDirection(velocityX, velocityY);
            slot.Speed = speed;
            slot.MoveEnabled = true;
            slot.FacingYawRad = facing.AngleRad;

            float burstCycle = Fraction(_elapsed * 0.72f);
            bool firingWindow = burstCycle >= 0.58f && burstCycle <= 0.82f;
            if (firingWindow && !_humanoidFireGate)
            {
                slot.QueueFire();
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
            slot.OverlayFiring = firingWindow;
            slot.LowerBodyPhase01 = lowerPhase;
            slot.AimYawRad = aimYaw;
            slot.OverlayWeight01 = overlayWeight;
            slot.OverlayNormalizedTime01 = overlayTime;

            overlay.BaseClip = CreateLocomotionClip(lowerPhase, speed);
            overlay.LayerClip = CreateAimClip(aimYaw, overlayWeight);
            overlay.OverlayClip = CreateRecoilClip(overlayTime, firingWindow ? 1f : 0f);
            ApplyAnimatorPreview(AnimationAcceptanceRigCatalog.Humanoid, slot, ref packed, ref runtime, dt);
        }

        private static void UpdateManualRig(
            AnimationAcceptanceRigControlSlot slot,
            AnimationAcceptanceRigDefinition definition,
            ref WorldPositionCm position,
            ref FacingDirection facing,
            ref AnimationOverlayRequest overlay,
            float dt)
        {
            position = WorldPositionCm.FromCmFloat(definition.ManualAnchorCm.X, definition.ManualAnchorCm.Y);
            facing.AngleRad = slot.FacingYawRad;

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

        private static AnimationChannelState CreateLocomotionClip(float normalizedTime01, float speed)
        {
            float weight = Math.Clamp(speed, 0f, 1f);
            return AnimationChannelState.Create(
                AnimationChannelRegistry.Register(AnimationChannelRegistry.Locomotion),
                normalizedTime01,
                weight,
                speed);
        }

        private static AnimationChannelState CreateAimClip(float aimYawRad, float weight01)
        {
            return AnimationChannelState.Create(
                AnimationChannelRegistry.Register(AnimationChannelRegistry.AimYaw),
                normalizedTime01: 0f,
                weight01,
                scalar0: aimYawRad);
        }

        private static AnimationChannelState CreateRecoilClip(float normalizedTime01, float weight01)
        {
            return AnimationChannelState.Create(
                AnimationChannelRegistry.Register(AnimationChannelRegistry.Recoil),
                normalizedTime01,
                weight01);
        }

        private static void ApplyAnimatorPreview(
            AnimationAcceptanceRigDefinition definition,
            AnimationAcceptanceRigControlSlot slot,
            ref AnimatorPackedState packed,
            ref AnimatorRuntimeState runtime,
            float dt)
        {
            int controllerId = packed.GetControllerId();
            if (controllerId <= 0)
            {
                controllerId = runtime.ControllerId;
                if (controllerId > 0)
                {
                    packed.SetControllerId(controllerId);
                }
            }

            if (!runtime.Initialized || runtime.ControllerId != controllerId)
            {
                runtime = AnimatorRuntimeState.Create(controllerId);
                runtime.Initialized = true;
                runtime.CurrentStateIndex = ResolveDesiredStateIndex(definition, slot);
            }

            int desiredStateIndex = ResolveDesiredStateIndex(definition, slot);
            if (runtime.CurrentStateIndex != desiredStateIndex)
            {
                if (runtime.NextStateIndex != desiredStateIndex)
                {
                    runtime.NextStateIndex = desiredStateIndex;
                    runtime.TransitionElapsedSeconds = 0f;
                    runtime.TransitionDurationSeconds = ResolveTransitionDurationSeconds(definition, desiredStateIndex);
                }

                runtime.TransitionElapsedSeconds += dt;
                float progress = runtime.TransitionDurationSeconds <= 0f
                    ? 1f
                    : Math.Clamp(runtime.TransitionElapsedSeconds / runtime.TransitionDurationSeconds, 0f, 1f);
                packed.SetSecondaryStateIndex(ResolvePackedStateIndex(definition, desiredStateIndex));
                packed.SetTransitionProgress01(progress);

                if (progress >= 1f)
                {
                    runtime.CurrentStateIndex = desiredStateIndex;
                    runtime.NextStateIndex = AnimatorRuntimeState.NoState;
                    runtime.StateElapsedSeconds = 0f;
                    runtime.TransitionElapsedSeconds = 0f;
                    runtime.TransitionDurationSeconds = 0f;
                    packed.SetSecondaryStateIndex(0);
                    packed.SetTransitionProgress01(0f);
                }
            }
            else
            {
                runtime.NextStateIndex = AnimatorRuntimeState.NoState;
                runtime.TransitionElapsedSeconds = 0f;
                runtime.TransitionDurationSeconds = 0f;
                packed.SetSecondaryStateIndex(0);
                packed.SetTransitionProgress01(0f);
            }

            runtime.StateElapsedSeconds += dt;
            packed.SetPrimaryStateIndex(ResolvePackedStateIndex(definition, runtime.CurrentStateIndex));
            packed.SetNormalizedTime01(ResolveNormalizedTime01(definition, slot, runtime.CurrentStateIndex));

            AnimatorPackedStateFlags flags = AnimatorPackedStateFlags.Active;
            if (IsLoopingState(definition, runtime.CurrentStateIndex))
            {
                flags |= AnimatorPackedStateFlags.Looping;
            }

            if (runtime.IsTransitioning)
            {
                flags |= AnimatorPackedStateFlags.InTransition;
            }

            packed.SetFlags(flags);
            packed.SetParameterBit(definition.LocomotionBoolParameterIndex, slot.MoveEnabled);
            packed.SetParameterBit(definition.FireTriggerParameterIndex, slot.PendingFireTrigger || slot.OverlayFiring);
            slot.PendingFireTrigger = false;
        }

        private static int ResolveDesiredStateIndex(AnimationAcceptanceRigDefinition definition, AnimationAcceptanceRigControlSlot slot)
        {
            return definition.RigId switch
            {
                AnimationAcceptanceRigId.Tank => slot.OverlayFiring ? 2 : slot.MoveEnabled && slot.Speed >= 0.25f ? 1 : 0,
                AnimationAcceptanceRigId.Humanoid => slot.OverlayFiring ? 3 : slot.MoveEnabled && slot.Speed >= 0.75f ? 2 : slot.MoveEnabled && slot.Speed >= 0.20f ? 1 : 0,
                _ => 0,
            };
        }

        private static int ResolvePackedStateIndex(AnimationAcceptanceRigDefinition definition, int stateIndex)
        {
            return definition.RigId switch
            {
                AnimationAcceptanceRigId.Tank => stateIndex switch
                {
                    0 => 31,
                    1 => 32,
                    2 => 33,
                    _ => 31,
                },
                AnimationAcceptanceRigId.Humanoid => stateIndex switch
                {
                    0 => 41,
                    1 => 42,
                    2 => 43,
                    3 => 44,
                    _ => 41,
                },
                _ => 0,
            };
        }

        private static float ResolveTransitionDurationSeconds(AnimationAcceptanceRigDefinition definition, int desiredStateIndex)
        {
            if (definition.RigId == AnimationAcceptanceRigId.Tank)
            {
                return desiredStateIndex == 2 ? 0.03f : 0.12f;
            }

            return desiredStateIndex == 3 ? 0.02f : 0.08f;
        }

        private static float ResolveNormalizedTime01(
            AnimationAcceptanceRigDefinition definition,
            AnimationAcceptanceRigControlSlot slot,
            int stateIndex)
        {
            return IsLoopingState(definition, stateIndex)
                ? AnimationAcceptanceRigControlSlot.Wrap01(slot.LowerBodyPhase01)
                : Math.Clamp(slot.OverlayNormalizedTime01, 0f, 1f);
        }

        private static bool IsLoopingState(AnimationAcceptanceRigDefinition definition, int stateIndex)
        {
            return definition.RigId switch
            {
                AnimationAcceptanceRigId.Tank => stateIndex != 2,
                AnimationAcceptanceRigId.Humanoid => stateIndex != 3,
                _ => true,
            };
        }

        private static float Fraction(float value)
        {
            return value - MathF.Floor(value);
        }
    }
}
