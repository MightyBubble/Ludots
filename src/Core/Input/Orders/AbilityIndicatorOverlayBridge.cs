using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
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
        private const int PreviewScopeId = -44021;

        private readonly World _world;
        private readonly AbilityDefinitionRegistry _abilities;
        private readonly GroundOverlayBuffer _overlays;
        private readonly PerformerDefinitionRegistry? _performerDefinitions;
        private readonly PerformerEntityRuntime? _performers;
        private readonly Dictionary<string, int> _previewPerformerIds = new(StringComparer.OrdinalIgnoreCase);
        private Entity _previewEntity;
        private int _previewDefinitionId;

        public AbilityIndicatorOverlayBridge(
            World world,
            AbilityDefinitionRegistry abilities,
            GroundOverlayBuffer overlays,
            PerformerDefinitionRegistry? performerDefinitions = null,
            PerformerEntityRuntime? performers = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            _overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));
            _performerDefinitions = performerDefinitions;
            _performers = performers;
        }

        public void UpdateAiming(Entity actor, InputOrderMapping mapping, bool hasCursorWorldCm, Vector3 cursorWorldCm, Entity hoveredEntity)
        {
            if (!TryResolveIndicator(actor, mapping, out var indicator, out var definition) ||
                !TryGetWorldPosition(actor, out var actorWorldCm, out var actorVisual))
            {
                ClearPreview();
                return;
            }

            Vector3 aimedWorldCm = hasCursorWorldCm ? cursorWorldCm : actorWorldCm;
            bool valid = true;
            if (indicator.Range > 0f)
            {
                aimedWorldCm = ClampToRange(actorWorldCm, aimedWorldCm, indicator.Range, out valid);
            }

            EmitRangeCircleIfNeeded(actorVisual, indicator);
            UpdatePreview(actor, mapping.SelectionType, ResolvePreviewCenter(mapping.SelectionType, actorWorldCm, aimedWorldCm), indicator, definition, valid);

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
            if (!TryResolveIndicator(actor, mapping, out var indicator, out _) ||
                !TryGetWorldPosition(actor, out var actorWorldCm, out var actorVisual))
            {
                ClearPreview();
                return;
            }

            EmitRangeCircleIfNeeded(actorVisual, indicator);
            ClearPreview();

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

        public void ClearPreview()
        {
            if (_performers != null && _previewEntity != Entity.Null && _world.IsAlive(_previewEntity))
            {
                _performers.Destroy(_previewEntity);
            }

            _previewEntity = Entity.Null;
            _previewDefinitionId = 0;
        }

        private bool TryResolveIndicator(
            Entity actor,
            InputOrderMapping mapping,
            out AbilityIndicatorConfig indicator,
            out AbilityDefinition definition)
        {
            indicator = default;
            definition = default;
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
            bool hasItemGranted = _world.Has<ItemGrantedSlotBuffer>(actor);
            ItemGrantedSlotBuffer itemGranted = hasItemGranted ? _world.Get<ItemGrantedSlotBuffer>(actor) : default;
            bool hasGranted = _world.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer granted = hasGranted ? _world.Get<GrantedSlotBuffer>(actor) : default;
            AbilitySlotState slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in itemGranted, hasItemGranted, in granted, hasGranted, slotIndex);
            if (slot.AbilityId <= 0 ||
                !_abilities.TryGet(slot.AbilityId, out definition) ||
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

        private void UpdatePreview(
            Entity actor,
            OrderSelectionType selectionType,
            Vector3 centerWorldCm,
            in AbilityIndicatorConfig indicator,
            in AbilityDefinition definition,
            bool valid)
        {
            if (_performers == null ||
                _performerDefinitions == null ||
                !indicator.Preview.IsEnabled)
            {
                ClearPreview();
                return;
            }

            int definitionId = ResolvePreviewDefinitionId(indicator.Preview.PerformerId);
            if (definitionId <= 0)
            {
                ClearPreview();
                return;
            }

            Vector3 worldPosition = ToVisualMeters(centerWorldCm);
            worldPosition.Y += indicator.Preview.OffsetY;

            if (_previewEntity == Entity.Null || !_world.IsAlive(_previewEntity) || _previewDefinitionId != definitionId)
            {
                ClearPreview();
                if (!_performerDefinitions.TryGet(definitionId, out var perfDef))
                {
                    _previewEntity = Entity.Null;
                    _previewDefinitionId = 0;
                    return;
                }

                _previewEntity = _performers.Create(
                    definitionId,
                    actor,
                    PreviewScopeId,
                    PresentationAnchorKind.WorldPosition,
                    worldPosition,
                    stableId: 0,
                    Entity.Null,
                    perfDef);

                _previewDefinitionId = definitionId;
            }

            ref var pos = ref _world.Get<PerformerWorldPosition>(_previewEntity);
            pos.Value = worldPosition;
            ref var previewState = ref _world.Get<PerformerState>(_previewEntity);
            previewState.OwnerEntity = actor;

            var color = ResolvePreviewColor(indicator, definition, valid);
            Vector3 scale = ResolvePreviewScale(selectionType, indicator);
            _performers.SetParam(_previewEntity, WellKnownPerformerParamKeys.MarkerScaleX, ParamLane.Float, scale.X, 0, default);
            _performers.SetParam(_previewEntity, WellKnownPerformerParamKeys.MarkerScaleY, ParamLane.Float, scale.Y, 0, default);
            _performers.SetParam(_previewEntity, WellKnownPerformerParamKeys.MarkerScaleZ, ParamLane.Float, scale.Z, 0, default);
            _performers.SetParam(_previewEntity, WellKnownPerformerParamKeys.MarkerColorR, ParamLane.Float, color.X, 0, default);
            _performers.SetParam(_previewEntity, WellKnownPerformerParamKeys.MarkerColorG, ParamLane.Float, color.Y, 0, default);
            _performers.SetParam(_previewEntity, WellKnownPerformerParamKeys.MarkerColorB, ParamLane.Float, color.Z, 0, default);
            _performers.SetParam(_previewEntity, WellKnownPerformerParamKeys.MarkerColorA, ParamLane.Float, color.W, 0, default);
        }

        private int ResolvePreviewDefinitionId(string performerId)
        {
            if (string.IsNullOrWhiteSpace(performerId) || _performerDefinitions == null)
            {
                return 0;
            }

            if (_previewPerformerIds.TryGetValue(performerId, out int cached))
            {
                return cached;
            }

            int resolved = _performerDefinitions.GetId(performerId);
            _previewPerformerIds[performerId] = resolved;
            return resolved;
        }

        private static Vector3 ResolvePreviewCenter(OrderSelectionType selectionType, Vector3 actorWorldCm, Vector3 aimedWorldCm)
        {
            return selectionType switch
            {
                OrderSelectionType.None => actorWorldCm,
                _ => aimedWorldCm
            };
        }

        private static Vector3 ResolvePreviewScale(OrderSelectionType selectionType, in AbilityIndicatorConfig indicator)
        {
            float footprintMeters = indicator.Radius > 0f
                ? WorldUnits.CmToM(indicator.Radius * 2f)
                : MathF.Max(0.9f, WorldUnits.CmToM(indicator.Range * 0.18f));
            if (selectionType == OrderSelectionType.Entity || selectionType == OrderSelectionType.Entities)
            {
                footprintMeters = MathF.Max(0.8f, WorldUnits.CmToM(indicator.Radius > 0f ? indicator.Radius : DefaultSingleTargetRadiusCm));
            }

            float scaleX = indicator.Preview.ScaleX > 0f ? indicator.Preview.ScaleX : footprintMeters;
            float scaleY = indicator.Preview.ScaleY > 0f ? indicator.Preview.ScaleY : MathF.Max(0.3f, footprintMeters * 0.45f);
            float scaleZ = indicator.Preview.ScaleZ > 0f ? indicator.Preview.ScaleZ : footprintMeters;
            return new Vector3(scaleX, scaleY, scaleZ);
        }

        private static Vector4 ResolvePreviewColor(in AbilityIndicatorConfig indicator, in AbilityDefinition definition, bool valid)
        {
            if (valid &&
                definition.HasPresentation &&
                definition.Presentation != null &&
                TryParseHexColor(definition.Presentation.AccentColorHex, alpha: 0.34f, out var accent))
            {
                return accent;
            }

            Vector4 color = GetStateColor(indicator, valid);
            color.W = MathF.Max(color.W, valid ? 0.3f : 0.26f);
            return color;
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

        private static bool TryParseHexColor(string? value, float alpha, out Vector4 color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string hex = value.Trim();
            if (hex.StartsWith('#'))
            {
                hex = hex[1..];
            }

            if (hex.Length != 6 ||
                !byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out byte r) ||
                !byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) ||
                !byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
            {
                return false;
            }

            color = new Vector4(r / 255f, g / 255f, b / 255f, alpha);
            return true;
        }
    }
}
