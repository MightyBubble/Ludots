using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Input.Orders
{
    /// <summary>
    /// Emits ground overlays from ability indicator metadata while an input mapping is aiming.
    /// </summary>
    public sealed class AbilityIndicatorOverlayBridge
    {
        private const float DefaultSingleTargetRadiusCm = 70f;
        private const float DefaultSelfRadiusCm = 90f;
        private const float DefaultLineWidthCm = 110f;
        private const float OverlayY = 0.03f;

        private readonly World _world;
        private readonly AbilityDefinitionRegistry _abilities;
        private readonly GroundOverlayBuffer _overlays;

        public AbilityIndicatorOverlayBridge(World world, AbilityDefinitionRegistry abilities, GroundOverlayBuffer overlays)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            _overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));
        }

        public void UpdateAiming(Entity actor, InputOrderMapping mapping, bool hasCursorWorldCm, Vector3 cursorWorldCm, Entity hoveredEntity)
        {
            if (!TryResolveIndicator(actor, mapping, out var indicator) ||
                !TryGetWorldPosition(actor, out var actorWorldCm, out var actorVisual))
            {
                return;
            }

            Vector3 aimedWorldCm = hasCursorWorldCm ? cursorWorldCm : actorWorldCm;
            bool valid = true;
            if (indicator.Range > 0f)
            {
                aimedWorldCm = ClampToRange(actorWorldCm, aimedWorldCm, indicator.Range, out valid);
            }

            EmitRangeCircleIfNeeded(actorVisual, indicator);

            switch (indicator.Shape)
            {
                case TargetShape.Circle:
                    EmitCircle(ResolveGroundCenter(mapping.SelectionType, actorWorldCm, aimedWorldCm), indicator, valid);
                    break;

                case TargetShape.Ring:
                    EmitRing(ResolveGroundCenter(mapping.SelectionType, actorWorldCm, aimedWorldCm), indicator, valid);
                    break;

                case TargetShape.Cone:
                    EmitCone(actor, actorWorldCm, actorVisual, indicator, hasCursorWorldCm, aimedWorldCm, valid);
                    break;

                case TargetShape.Line:
                case TargetShape.Rectangle:
                    EmitLine(actor, actorWorldCm, actorVisual, indicator, hasCursorWorldCm, aimedWorldCm, valid);
                    break;

                case TargetShape.Single:
                    EmitSingleTarget(aimedWorldCm, hoveredEntity, indicator, valid);
                    break;

                case TargetShape.Self:
                    EmitSelf(actorWorldCm, indicator, valid);
                    break;
            }
        }

        public void UpdateVectorAiming(Entity actor, InputOrderMapping mapping, Vector3 originWorldCm, Vector3 cursorWorldCm, VectorAimPhase phase)
        {
            if (!TryResolveIndicator(actor, mapping, out var indicator) ||
                !TryGetWorldPosition(actor, out var actorWorldCm, out var actorVisual))
            {
                return;
            }

            EmitRangeCircleIfNeeded(actorVisual, indicator);

            float originDistanceCm = DistanceCm(actorWorldCm, originWorldCm);
            bool originValid = indicator.Range <= 0f || originDistanceCm <= indicator.Range + 0.01f;
            var color = GetStateColor(indicator, originValid);
            var border = GetBorderColor(color);

            if (phase == VectorAimPhase.Origin)
            {
                _overlays.TryAdd(new GroundOverlayItem
                {
                    Shape = GroundOverlayShape.Circle,
                    Center = ToVisualMeters(originWorldCm),
                    Radius = WorldUnits.CmToM(MathF.Max(indicator.InnerRadius, DefaultSingleTargetRadiusCm)),
                    FillColor = color,
                    BorderColor = border,
                    BorderWidth = 0.02f
                });
                return;
            }

            float lengthCm = DistanceCm(originWorldCm, cursorWorldCm);
            float rotation = ResolveRotation(originWorldCm, cursorWorldCm, actor);
            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Line,
                Center = ToVisualMeters(originWorldCm),
                Length = WorldUnits.CmToM(lengthCm),
                Width = WorldUnits.CmToM(MathF.Max(indicator.Radius * 2f, DefaultLineWidthCm)),
                Rotation = rotation,
                FillColor = color,
                BorderColor = border,
                BorderWidth = 0.02f
            });
        }

        private bool TryResolveIndicator(Entity actor, InputOrderMapping mapping, out AbilityIndicatorConfig indicator)
        {
            indicator = default;
            if (!_world.IsAlive(actor) ||
                !_world.Has<AbilityStateBuffer>(actor) ||
                mapping.ArgsTemplate.I0 is null)
            {
                return false;
            }

            int slotIndex = mapping.ArgsTemplate.I0.Value;
            ref var abilities = ref _world.Get<AbilityStateBuffer>(actor);
            if ((uint)slotIndex >= (uint)abilities.Count)
            {
                return false;
            }

            bool hasForm = _world.Has<AbilityFormSlotBuffer>(actor);
            AbilityFormSlotBuffer formSlots = hasForm ? _world.Get<AbilityFormSlotBuffer>(actor) : default;
            bool hasGranted = _world.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer granted = hasGranted ? _world.Get<GrantedSlotBuffer>(actor) : default;
            AbilitySlotState slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in granted, hasGranted, slotIndex);
            if (slot.AbilityId <= 0 ||
                !_abilities.TryGet(slot.AbilityId, out var definition) ||
                !definition.HasIndicator)
            {
                return false;
            }

            indicator = definition.Indicator;
            return true;
        }

        private bool TryGetWorldPosition(Entity entity, out Vector3 worldCm, out Vector3 visualMeters)
        {
            worldCm = default;
            visualMeters = default;
            if (!_world.IsAlive(entity))
            {
                return false;
            }

            if (_world.Has<WorldPositionCm>(entity))
            {
                var position = _world.Get<WorldPositionCm>(entity);
                WorldCmInt2 cm = position.ToWorldCmInt2();
                worldCm = new Vector3(cm.X, 0f, cm.Y);
                visualMeters = WorldUnits.WorldCmToVisualMeters(cm, OverlayY);
                return true;
            }

            if (_world.Has<VisualTransform>(entity))
            {
                visualMeters = _world.Get<VisualTransform>(entity).Position;
                worldCm = new Vector3(WorldUnits.MToCm(visualMeters.X), 0f, WorldUnits.MToCm(visualMeters.Z));
                return true;
            }

            return false;
        }

        private void EmitRangeCircleIfNeeded(Vector3 actorVisual, in AbilityIndicatorConfig indicator)
        {
            if (!indicator.ShowRangeCircle || indicator.Range <= 0f)
            {
                return;
            }

            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = actorVisual,
                Radius = WorldUnits.CmToM(indicator.Range),
                FillColor = indicator.RangeCircleColor,
                BorderColor = GetBorderColor(indicator.RangeCircleColor),
                BorderWidth = 0.02f
            });
        }

        private void EmitCircle(Vector3 centerWorldCm, in AbilityIndicatorConfig indicator, bool valid)
        {
            Vector4 color = GetStateColor(indicator, valid);
            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = ToVisualMeters(centerWorldCm),
                Radius = WorldUnits.CmToM(indicator.Radius),
                FillColor = color,
                BorderColor = GetBorderColor(color),
                BorderWidth = 0.02f
            });
        }

        private void EmitRing(Vector3 centerWorldCm, in AbilityIndicatorConfig indicator, bool valid)
        {
            Vector4 color = GetStateColor(indicator, valid);
            float outerRadiusCm = indicator.Radius;
            float innerRadiusCm = ResolveInnerRadius(indicator);
            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Ring,
                Center = ToVisualMeters(centerWorldCm),
                Radius = WorldUnits.CmToM(outerRadiusCm),
                InnerRadius = WorldUnits.CmToM(innerRadiusCm),
                FillColor = color,
                BorderColor = GetBorderColor(color),
                BorderWidth = 0.02f
            });
        }

        private void EmitCone(Entity actor, Vector3 actorWorldCm, Vector3 actorVisual, in AbilityIndicatorConfig indicator, bool hasCursorWorldCm, Vector3 cursorWorldCm, bool valid)
        {
            Vector4 color = GetStateColor(indicator, valid);
            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Cone,
                Center = actorVisual,
                Radius = WorldUnits.CmToM(indicator.Radius > 0f ? indicator.Radius : indicator.Range),
                Angle = indicator.Angle > 0f ? indicator.Angle : MathF.PI / 6f,
                Rotation = ResolveRotation(actorWorldCm, cursorWorldCm, actor, hasCursorWorldCm),
                FillColor = color,
                BorderColor = GetBorderColor(color),
                BorderWidth = 0.02f
            });
        }

        private void EmitLine(Entity actor, Vector3 actorWorldCm, Vector3 actorVisual, in AbilityIndicatorConfig indicator, bool hasCursorWorldCm, Vector3 cursorWorldCm, bool valid)
        {
            Vector4 color = GetStateColor(indicator, valid);
            float lengthCm = indicator.Range > 0f
                ? MathF.Min(indicator.Range, DistanceCm(actorWorldCm, cursorWorldCm))
                : DistanceCm(actorWorldCm, cursorWorldCm);
            if (lengthCm <= 0f)
            {
                lengthCm = indicator.Range > 0f ? indicator.Range : indicator.Radius;
            }

            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Line,
                Center = actorVisual,
                Length = WorldUnits.CmToM(lengthCm),
                Width = WorldUnits.CmToM(MathF.Max(indicator.Radius * 2f, DefaultLineWidthCm)),
                Rotation = ResolveRotation(actorWorldCm, cursorWorldCm, actor, hasCursorWorldCm),
                FillColor = color,
                BorderColor = GetBorderColor(color),
                BorderWidth = 0.02f
            });
        }

        private void EmitSingleTarget(Vector3 aimedWorldCm, Entity hoveredEntity, in AbilityIndicatorConfig indicator, bool valid)
        {
            Vector3 targetWorldCm = aimedWorldCm;
            if (TryGetWorldPosition(hoveredEntity, out var hoveredWorldCm, out _))
            {
                targetWorldCm = hoveredWorldCm;
            }

            Vector4 color = GetStateColor(indicator, valid);
            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = ToVisualMeters(targetWorldCm),
                Radius = WorldUnits.CmToM(indicator.Radius > 0f ? indicator.Radius : DefaultSingleTargetRadiusCm),
                FillColor = color,
                BorderColor = GetBorderColor(color),
                BorderWidth = 0.02f
            });
        }

        private void EmitSelf(Vector3 actorWorldCm, in AbilityIndicatorConfig indicator, bool valid)
        {
            Vector4 color = GetStateColor(indicator, valid);
            _overlays.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Circle,
                Center = ToVisualMeters(actorWorldCm),
                Radius = WorldUnits.CmToM(indicator.Radius > 0f ? indicator.Radius : DefaultSelfRadiusCm),
                FillColor = color,
                BorderColor = GetBorderColor(color),
                BorderWidth = 0.02f
            });
        }

        private static Vector3 ResolveGroundCenter(OrderSelectionType selectionType, Vector3 actorWorldCm, Vector3 aimedWorldCm)
        {
            return selectionType switch
            {
                OrderSelectionType.None => actorWorldCm,
                OrderSelectionType.Entity => aimedWorldCm,
                _ => aimedWorldCm
            };
        }

        private static float ResolveInnerRadius(in AbilityIndicatorConfig indicator)
        {
            float inner = indicator.InnerRadius > 0f ? indicator.InnerRadius : indicator.Radius * 0.65f;
            return Math.Clamp(inner, 0f, indicator.Radius);
        }

        private static Vector3 ClampToRange(Vector3 originWorldCm, Vector3 targetWorldCm, float rangeCm, out bool valid)
        {
            float distanceCm = DistanceCm(originWorldCm, targetWorldCm);
            valid = rangeCm <= 0f || distanceCm <= rangeCm + 0.01f;
            if (valid || distanceCm <= 0.001f)
            {
                return targetWorldCm;
            }

            float scale = rangeCm / distanceCm;
            return originWorldCm + (targetWorldCm - originWorldCm) * scale;
        }

        private float ResolveRotation(Vector3 fromWorldCm, Vector3 toWorldCm, Entity actor, bool hasCursorWorldCm = true)
        {
            if (hasCursorWorldCm)
            {
                var delta = toWorldCm - fromWorldCm;
                if (delta.LengthSquared() > 0.001f)
                {
                    return MathF.Atan2(delta.Z, delta.X);
                }
            }

            if (_world.IsAlive(actor) && _world.Has<FacingDirection>(actor))
            {
                return _world.Get<FacingDirection>(actor).AngleRad;
            }

            return 0f;
        }

        private static float DistanceCm(Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X;
            float dz = b.Z - a.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static Vector3 ToVisualMeters(Vector3 worldCm)
        {
            return new Vector3(WorldUnits.CmToM(worldCm.X), OverlayY, WorldUnits.CmToM(worldCm.Z));
        }

        private static Vector4 GetStateColor(in AbilityIndicatorConfig indicator, bool valid)
        {
            return valid ? indicator.ValidColor : indicator.InvalidColor;
        }

        private static Vector4 GetBorderColor(Vector4 baseColor)
        {
            return new Vector4(baseColor.X, baseColor.Y, baseColor.Z, MathF.Max(baseColor.W, 0.85f));
        }
    }
}
