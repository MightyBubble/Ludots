using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;

namespace Ludots.Core.Gameplay.GAS
{
    internal static class AbilityActivationPreconditionEvaluator
    {
        public static bool TryPass(
            World world,
            Entity actor,
            in AbilityAttributePreconditions preconditions,
            out AbilityCastFailReason failReason,
            out int attributeId,
            out float requiredValue,
            out float currentValue)
        {
            failReason = AbilityCastFailReason.None;
            attributeId = 0;
            requiredValue = 0f;
            currentValue = 0f;

            if (preconditions.Count <= 0)
            {
                return true;
            }

            ref var attributes = ref world.TryGetRef<AttributeBuffer>(actor, out bool hasAttributes);
            for (int index = 0; index < preconditions.Count; index++)
            {
                attributeId = preconditions.GetAttributeId(index);
                requiredValue = preconditions.GetThreshold(index);
                currentValue = hasAttributes ? attributes.GetCurrent(attributeId) : 0f;
                if (Matches(currentValue, preconditions.GetComparison(index), requiredValue))
                {
                    continue;
                }

                failReason = preconditions.GetFailReason(index);
                if (failReason == AbilityCastFailReason.None)
                {
                    failReason = AbilityCastFailReason.InsufficientResource;
                }

                return false;
            }

            failReason = AbilityCastFailReason.None;
            attributeId = 0;
            requiredValue = 0f;
            currentValue = 0f;
            return true;
        }

        private static bool Matches(float currentValue, AbilityAttributeComparison comparison, float threshold)
        {
            return comparison switch
            {
                AbilityAttributeComparison.GreaterOrEqual => currentValue >= threshold,
                AbilityAttributeComparison.GreaterThan => currentValue > threshold,
                AbilityAttributeComparison.LessOrEqual => currentValue <= threshold,
                AbilityAttributeComparison.LessThan => currentValue < threshold,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(comparison),
                    comparison,
                    $"Unknown {nameof(AbilityAttributeComparison)} '{comparison}'. Update {nameof(AbilityActivationPreconditionEvaluator)} before using it in ability preconditions."),
            };
        }
    }
}
