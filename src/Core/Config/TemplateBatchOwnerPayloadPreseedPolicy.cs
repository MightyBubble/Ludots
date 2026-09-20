using System;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Core.Config
{
    internal static class TemplateBatchOwnerPayloadPreseedPolicy
    {
        public static bool CanPreseedOwnerPayloadMarker(
            CompiledPresenterBootstrapRegistry? bootstrap,
            EntityTemplate template,
            int templateKeyId)
        {
            if (bootstrap == null ||
                template == null ||
                templateKeyId <= 0 ||
                !bootstrap.TryGetEntitySpawnCreates(templateKeyId, out CompiledPresenterBootstrapRegistry.BootstrapCreateRule[] rules) ||
                rules.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < rules.Length; i++)
            {
                ref readonly CompiledPresenterBootstrapRegistry.BootstrapCreateRule rule = ref rules[i];
                if (rule.ResolveScopeTag(1) <= 0 || !TemplateSatisfiesBootstrapCondition(template, rule.InlineCondition))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TemplateSatisfiesBootstrapCondition(EntityTemplate template, InlineConditionKind condition)
        {
            return condition switch
            {
                InlineConditionKind.None => true,
                InlineConditionKind.SourceHasVisualTransform => true,
                InlineConditionKind.SourceHasAttributes => template.Components != null && template.Components.ContainsKey("AttributeBuffer"),
                _ => throw new InvalidOperationException($"Unsupported presenter bootstrap inline condition '{condition}'."),
            };
        }
    }
}
