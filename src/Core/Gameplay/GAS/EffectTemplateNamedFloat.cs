using System;
using System.Collections.Generic;
using System.Text;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Closed catalog of effect-template geometry floats a graph may read from the effects an
    /// ability executes. Lengths are centimeters and angles are degrees, matching the template.
    /// A field list is <see cref="FieldListSeparator"/>-joined and read in order: the first field
    /// that any EffectClip / EffectSignal item carries (value greater than 0) wins, and exactly one
    /// item may carry it.
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

        public const char FieldListSeparator = '|';

        public const string UnknownFieldError = "GAS.EFFECT.ERR.UnknownNamedFloat";
        public const string AbsentFieldError = "GAS.EFFECT.ERR.NamedFloatAbsent";
        public const string AmbiguousFieldError = "GAS.EFFECT.ERR.NamedFloatAmbiguous";

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

        public static bool IsKnown(ReadOnlySpan<char> field)
        {
            for (int i = 0; i < KnownFieldNames.Length; i++)
            {
                if (field.SequenceEqual(KnownFieldNames[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Returns null when every entry of the list is a known field, otherwise the reason.</summary>
        public static string? ValidateFieldList(string? fields)
        {
            if (string.IsNullOrWhiteSpace(fields))
            {
                return $"{UnknownFieldError}: field list is empty.";
            }

            ReadOnlySpan<char> rest = fields;
            while (true)
            {
                int cut = rest.IndexOf(FieldListSeparator);
                ReadOnlySpan<char> field = cut < 0 ? rest : rest.Slice(0, cut);
                if (!IsKnown(field))
                {
                    return $"{UnknownFieldError}: field '{field.ToString()}' in '{fields}' is not one of: {string.Join(", ", KnownFieldNames)}.";
                }

                if (cut < 0)
                {
                    return null;
                }

                rest = rest.Slice(cut + 1);
            }
        }

        /// <summary>
        /// Reads the first listed field any executed effect carries. No field carried, or the winning
        /// field carried by more than one effect, fails; the message names every candidate effect.
        /// </summary>
        public static float RequireFromExec(
            in AbilityDefinition definition,
            EffectTemplateRegistry templates,
            string fields,
            string abilityName)
        {
            string? invalid = ValidateFieldList(fields);
            if (invalid != null)
            {
                throw new InvalidOperationException($"ability '{abilityName}': {invalid}");
            }

            if (TryReadFromExec(in definition, templates, fields, out float value, out int hits))
            {
                return value;
            }

            string candidates = DescribeExecEffects(in definition, templates);
            string error = hits == 0 ? AbsentFieldError : AmbiguousFieldError;
            string reason = hits == 0
                ? "no executed effect carries a value greater than 0"
                : $"{hits} executed effects carry the first matching field; exactly one may";
            throw new InvalidOperationException(
                $"{error}: ability '{abilityName}' fields '{fields}': {reason}. Executed effects: {candidates}.");
        }

        private static bool TryReadFromExec(
            in AbilityDefinition definition,
            EffectTemplateRegistry templates,
            string fields,
            out float value,
            out int hits)
        {
            value = 0f;
            hits = 0;
            ref readonly AbilityExecSpec exec = ref definition.ExecSpec;
            ReadOnlySpan<char> rest = fields;
            while (true)
            {
                int cut = rest.IndexOf(FieldListSeparator);
                ReadOnlySpan<char> field = cut < 0 ? rest : rest.Slice(0, cut);
                for (int i = 0; i < exec.ItemCount; i++)
                {
                    ExecItemKind kind = exec.GetKind(i);
                    if (kind is not (ExecItemKind.EffectClip or ExecItemKind.EffectSignal))
                    {
                        continue;
                    }

                    if (templates.TryGet(exec.GetTemplateId(i), out EffectTemplateData template)
                        && TryReadPositive(in template, field, out float read))
                    {
                        hits++;
                        value = read;
                    }
                }

                if (hits > 0 || cut < 0)
                {
                    return hits == 1;
                }

                rest = rest.Slice(cut + 1);
            }
        }

        private static string DescribeExecEffects(in AbilityDefinition definition, EffectTemplateRegistry templates)
        {
            var text = new StringBuilder();
            ref readonly AbilityExecSpec exec = ref definition.ExecSpec;
            for (int i = 0; i < exec.ItemCount; i++)
            {
                ExecItemKind kind = exec.GetKind(i);
                if (kind is not (ExecItemKind.EffectClip or ExecItemKind.EffectSignal))
                {
                    continue;
                }

                int templateId = exec.GetTemplateId(i);
                string name = EffectTemplateIdRegistry.GetName(templateId);
                if (text.Length > 0)
                {
                    text.Append(", ");
                }

                text.Append(string.IsNullOrEmpty(name) ? templateId.ToString() : name);
                if (!templates.TryGet(templateId, out _))
                {
                    text.Append(" (not installed)");
                }
            }

            return text.Length == 0 ? "none" : text.ToString();
        }

        private static bool TryReadPositive(in EffectTemplateData template, ReadOnlySpan<char> field, out float value)
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
