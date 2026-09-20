using System;

namespace Ludots.Core.Gameplay.Providers
{
    public sealed class ProviderServices
    {
        public ProviderServices(bool allowTestDomainOverride = false)
        {
            Gaps = new ProviderGapCatalog();
            Gaps.RegisterFrameworkGaps();
            Sources = new SourceProviderRegistry(Gaps, allowTestDomainOverride);
            Selectors = new SelectorProviderRegistry(Gaps, allowTestDomainOverride);
            Conditions = new ConditionProviderRegistry(Gaps, allowTestDomainOverride);
            Effects = new EffectHandlerRegistry(Gaps, allowTestDomainOverride);
            Validator = new ProviderDefinitionValidator(Sources, Selectors, Conditions, Effects, Gaps);
        }

        public ProviderGapCatalog Gaps { get; }
        public SourceProviderRegistry Sources { get; }
        public SelectorProviderRegistry Selectors { get; }
        public ConditionProviderRegistry Conditions { get; }
        public EffectHandlerRegistry Effects { get; }
        public ProviderDefinitionValidator Validator { get; }
    }
}
