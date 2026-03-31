namespace Ludots.Core.Engine.TimeFlow
{
    public readonly struct TimeFlowDomainSnapshot
    {
        public TimeFlowDomainSnapshot(
            int domainId,
            string name,
            int parentDomainId,
            int baseScalePermille,
            int effectiveScalePermille,
            bool paused,
            int modifierCount)
        {
            DomainId = domainId;
            Name = name;
            ParentDomainId = parentDomainId;
            BaseScalePermille = baseScalePermille;
            EffectiveScalePermille = effectiveScalePermille;
            Paused = paused;
            ModifierCount = modifierCount;
        }

        public int DomainId { get; }
        public string Name { get; }
        public int ParentDomainId { get; }
        public int BaseScalePermille { get; }
        public int EffectiveScalePermille { get; }
        public bool Paused { get; }
        public int ModifierCount { get; }
    }
}
