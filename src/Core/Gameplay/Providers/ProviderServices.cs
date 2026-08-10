using System;

namespace Ludots.Core.Gameplay.Providers
{
    public sealed class ProviderServices
    {
        public ProviderServices(bool registerDefaultGaps = true, bool allowTestDomainOverride = false)
        {
            Gaps = new ProviderGapCatalog();
            if (registerDefaultGaps)
            {
                Gaps.RegisterDefaultY5kProgramGaps();
            }

            Sources = new SourceProviderRegistry(Gaps, allowTestDomainOverride);
            Selectors = new SelectorProviderRegistry(Gaps, allowTestDomainOverride);
            Conditions = new ConditionProviderRegistry(Gaps, allowTestDomainOverride);
            Effects = new EffectHandlerRegistry(Gaps, allowTestDomainOverride);
            Validator = new ProviderDefinitionValidator(Sources, Selectors, Conditions, Effects, Gaps);
            RegisterCoreSourceStubs();
        }

        private void RegisterCoreSourceStubs()
        {
            // Domain mods may emit through these keys after GameStart; stubs make load-time
            // Activity/Task validation possible before capability mods install.
            Sources.Register("supply.network_changed", new NullSourceProvider(), ProviderParameterSchema.Empty);
            Sources.Register("city_control.defense_breached", new NullSourceProvider(), ProviderParameterSchema.Empty);
            Sources.Register("time.day_started", new NullSourceProvider(), ProviderParameterSchema.Empty);
            Sources.Register("time.season_started", new NullSourceProvider(), ProviderParameterSchema.Empty);
        }

        private sealed class NullSourceProvider : ISourceProvider
        {
            public void Emit(in ProviderSignal signal, ProviderExecutionContext context)
            {
            }
        }

        public ProviderGapCatalog Gaps { get; }
        public SourceProviderRegistry Sources { get; }
        public SelectorProviderRegistry Selectors { get; }
        public ConditionProviderRegistry Conditions { get; }
        public EffectHandlerRegistry Effects { get; }
        public ProviderDefinitionValidator Validator { get; }
    }
}
