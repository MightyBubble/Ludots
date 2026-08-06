namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Runtime fact that an active Effect is the lockout contributor for an Ability.
    /// Created only after the materialized Effect grants one of that Ability's block tags.
    /// </summary>
    public struct EffectSourceAbilityLockout
    {
        public int AbilityId;
    }
}
