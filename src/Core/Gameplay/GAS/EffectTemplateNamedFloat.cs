using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Closed catalog of effect-template floats an ability may name for a graph.
    /// Lengths are centimeters and angles are degrees, matching the template.
    /// A value that is absent or not greater than 0 fails closed.
    /// </summary>
    public static class EffectTemplateNamedFloat
    {
        public const string TargetQueryRadius = "targetQuery.radius";
        public const string TargetQueryHalfAngle = "targetQuery.halfAngle";
        public const string TargetQueryHalfWidth = "targetQuery.halfWidth";
        public const string TargetQueryLength = "targetQuery.length";
        public const string ProjectileRange = "projectile.range";
        public const string ProjectileCollisionHalfWidth = "projectile.collisionHalfWidth";
        public const string UnitCreationPlacementRadiusCm = "unitCreation.placementRadiusCm";

        public const string UnknownFieldError = "GAS.EFFECT.ERR.UnknownNamedFloat";
        public const string AbsentFieldError = "GAS.EFFECT.ERR.NamedFloatAbsent";

        private static readonly string[] KnownFieldNames =
        {
            TargetQueryRadius,
            TargetQueryHalfAngle,
            TargetQueryHalfWidth,
            TargetQueryLength,
            ProjectileRange,
            ProjectileCollisionHalfWidth,
            UnitCreationPlacementRadiusCm,
        };

        public static IReadOnlyList<string> KnownFields => KnownFieldNames;

        public static bool IsKnown(string field)
        {
            for (int i = 0; i < KnownFieldNames.Length; i++)
            {
                if (string.Equals(KnownFieldNames[i], field, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static float Require(in EffectTemplateData template, string field, string effectName)
        {
            if (!IsKnown(field))
            {
                throw new InvalidOperationException(
                    $"{UnknownFieldError}: field '{field}' is not one of: {string.Join(", ", KnownFieldNames)}.");
            }

            if (!TryReadPositive(in template, field, out float value))
            {
                throw new InvalidOperationException(
                    $"{AbsentFieldError}: effect '{effectName}' field '{field}' is absent or not greater than 0.");
            }

            return value;
        }

        private static bool TryReadPositive(in EffectTemplateData template, string field, out float value)
        {
            int raw = field switch
            {
                TargetQueryRadius => Spatial(in template, static spatial => spatial.RadiusCm),
                TargetQueryHalfAngle => Spatial(in template, static spatial => spatial.HalfAngleDeg),
                TargetQueryHalfWidth => Spatial(in template, static spatial => spatial.HalfWidthCm),
                TargetQueryLength => Spatial(in template, static spatial => spatial.LengthCm),
                ProjectileRange => template.Projectile.Range,
                ProjectileCollisionHalfWidth => template.Projectile.CollisionHalfWidthCm,
                UnitCreationPlacementRadiusCm => template.UnitCreation.PlacementRadiusCm,
                _ => 0,
            };
            value = raw;
            return raw > 0;
        }

        private static int Spatial(in EffectTemplateData template, Func<BuiltinSpatialDescriptor, int> read)
        {
            if (template.TargetQuery.Kind != TargetResolverKind.BuiltinSpatial)
            {
                return 0;
            }

            return read(template.TargetQuery.Spatial);
        }
    }
}
