using System;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Vision
{
    public sealed class VisionResolver
    {
        private readonly FogLayerRegistry _layers;
        private readonly FogFieldStore _fields;
        private readonly IFogElevationSource? _elevation;
        private readonly IFogOcclusionSource? _occlusion;
        private readonly RelationshipRuntime? _relationships;

        public VisionResolver(
            FogLayerRegistry layers,
            FogFieldStore fields,
            IFogElevationSource? elevation = null,
            IFogOcclusionSource? occlusion = null,
            RelationshipRuntime? relationships = null)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
            _elevation = elevation;
            _occlusion = occlusion;
            _relationships = relationships;
        }

        public int Resolve(in VisionEmitter emitter, ReadOnlySpan<FogLayerId> targetLayers, in FogRulesPolicy policy)
        {
            int changed = 0;
            for (int i = 0; i < targetLayers.Length; i++)
            {
                FogLayerId layerId = targetLayers[i];
                if ((emitter.LayerMask & _layers.ToMask(layerId)) == 0u)
                {
                    continue;
                }

                FogLayerDefinition layer = _layers.Get(layerId);
                FogField field = _fields.GetOrCreate(emitter.ScopeKeyId, in layer);
                changed += RasterizeIntoField(in emitter, field, in policy);
            }

            return changed;
        }

        public int ResolveToScopes(
            in VisionEmitter emitter,
            ReadOnlySpan<FogScopeTarget> targets,
            ReadOnlySpan<FogLayerId> targetLayers,
            in FogRulesPolicy policy,
            FogRelationshipRule relationshipRule = default)
        {
            int changed = 0;
            for (int scopeIndex = 0; scopeIndex < targets.Length; scopeIndex++)
            {
                FogScopeTarget target = targets[scopeIndex];

                if (relationshipRule.SourceScopeHost != default)
                {
                    if (_relationships == null)
                    {
                        throw new InvalidOperationException("Relationship-gated fog resolution requires RelationshipRuntime.");
                    }

                    if (!_relationships.HasLink(relationshipRule.SourceScopeHost, target.ScopeHost, relationshipRule.RelationshipTypeId))
                    {
                        continue;
                    }
                }

                var scoped = new VisionEmitter(
                    target.ScopeKeyId,
                    emitter.Position,
                    emitter.FacingDeg,
                    emitter.LayerMask,
                    emitter.Polarity,
                    emitter.Aperture,
                    emitter.AltitudeBand,
                    emitter.Priority,
                    emitter.TargetScopeSelectorId,
                    emitter.DetectionStrength,
                    emitter.TrueSightStrength);
                changed += Resolve(in scoped, targetLayers, in policy);
            }

            return changed;
        }

        public int RasterizeIntoField(in VisionEmitter emitter, FogField field, in FogRulesPolicy policy)
        {
            FogCell origin = field.WorldToCell(emitter.Position);
            int radiusCells = MathUtil.CeilDiv(emitter.Aperture.RangeCm, field.CellSizeCm);
            if (emitter.Aperture.Kind == VisionApertureKind.Box)
            {
                radiusCells = Math.Max(
                    MathUtil.CeilDiv(emitter.Aperture.RangeCm, field.CellSizeCm),
                    MathUtil.CeilDiv(emitter.Aperture.HalfWidthCm, field.CellSizeCm));
            }

            if (radiusCells < 0)
            {
                return 0;
            }

            var vertical = new VerticalVisionRule(_elevation);
            var lineOfSight = new LineOfSightRule(_occlusion);
            int touched = 0;
            for (int y = origin.Y - radiusCells; y <= origin.Y + radiusCells; y++)
            {
                for (int x = origin.X - radiusCells; x <= origin.X + radiusCells; x++)
                {
                    FogCell cell = new(x, y);
                    if (!ApertureContains(emitter, field, origin, cell))
                    {
                        continue;
                    }

                    if (!vertical.Allows(cell, emitter.AltitudeBand, in policy))
                    {
                        continue;
                    }

                    if (!lineOfSight.Allows(origin, cell, in policy))
                    {
                        continue;
                    }

                    if (emitter.Polarity == VisionPolarity.Deny)
                    {
                        field.SetDenied(cell, policy.DenyMode);
                    }
                    else
                    {
                        field.SetVisible(cell, policy.DenyMode);
                    }

                    touched++;
                }
            }

            return touched;
        }

        private static bool ApertureContains(in VisionEmitter emitter, FogField field, FogCell origin, FogCell cell)
        {
            WorldCmInt2 center = field.CellCenterToWorld(cell);
            int dx = center.X - emitter.Position.X;
            int dy = center.Y - emitter.Position.Y;
            long distanceSq = ((long)dx * dx) + ((long)dy * dy);
            long rangeSq = (long)emitter.Aperture.RangeCm * emitter.Aperture.RangeCm;

            switch (emitter.Aperture.Kind)
            {
                case VisionApertureKind.Disk:
                    return distanceSq <= rangeSq;
                case VisionApertureKind.Cone:
                    return distanceSq <= rangeSq && IsWithinCone(dx, dy, emitter.FacingDeg, emitter.Aperture.HalfAngleDeg);
                case VisionApertureKind.Box:
                    return IsWithinBox(dx, dy, emitter.FacingDeg, emitter.Aperture.HalfWidthCm, emitter.Aperture.RangeCm);
                case VisionApertureKind.Line:
                    return IsWithinLine(dx, dy, emitter.FacingDeg, emitter.Aperture.RangeCm, emitter.Aperture.HalfWidthCm);
                default:
                    throw new ArgumentOutOfRangeException(nameof(emitter.Aperture.Kind), emitter.Aperture.Kind, "Unsupported vision aperture.");
            }
        }

        private static bool IsWithinCone(int dx, int dy, int facingDeg, int halfAngleDeg)
        {
            if (dx == 0 && dy == 0)
            {
                return true;
            }

            int forwardX = MathUtil.Cos(facingDeg);
            int forwardY = MathUtil.Sin(facingDeg);
            long dot = ((long)dx * forwardX) + ((long)dy * forwardY);
            if (dot < 0)
            {
                return false;
            }

            int distance = MathUtil.Sqrt(((long)dx * dx) + ((long)dy * dy));
            long threshold = (long)distance * MathUtil.ScalingFactor * MathUtil.Cos(halfAngleDeg);
            return dot * MathUtil.ScalingFactor >= threshold;
        }

        private static bool IsWithinBox(int dx, int dy, int facingDeg, int halfWidthCm, int halfHeightCm)
        {
            RotateIntoFacing(dx, dy, facingDeg, out int forward, out int side);
            return MathUtil.Abs(forward) <= halfHeightCm && MathUtil.Abs(side) <= halfWidthCm;
        }

        private static bool IsWithinLine(int dx, int dy, int facingDeg, int lengthCm, int halfWidthCm)
        {
            RotateIntoFacing(dx, dy, facingDeg, out int forward, out int side);
            return forward >= 0 && forward <= lengthCm && MathUtil.Abs(side) <= halfWidthCm;
        }

        private static void RotateIntoFacing(int dx, int dy, int facingDeg, out int forward, out int side)
        {
            int cos = MathUtil.Cos(facingDeg);
            int sin = MathUtil.Sin(facingDeg);
            forward = (int)(((long)dx * cos + (long)dy * sin) / MathUtil.ScalingFactor);
            side = (int)((-(long)dx * sin + (long)dy * cos) / MathUtil.ScalingFactor);
        }
    }
}
