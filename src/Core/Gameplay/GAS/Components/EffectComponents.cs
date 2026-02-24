namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Stores the EffectTemplateRegistry template ID on an effect entity,
    /// so that EffectApplicationSystem can look up TargetResolver descriptors.
    /// </summary>
    public struct EffectTemplateRef
    {
        public int TemplateId;
    }
}
