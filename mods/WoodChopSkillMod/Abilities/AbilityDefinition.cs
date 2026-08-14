using Ludots;
using Ludots.Core;
using Ludots.GAS;

namespace WoodChopSkillMod.Abilities
{
    /// <summary>
    /// Defines the "Wood Chop" ability, which grants a temporary boost or deals damage upon activation.
    /// </summary>
    public class WoodChopAbility : AbilityDefinition
    {
        // Unique ID for this ability
        public override string Id => "WoodChop";

        // Display name in UI/Logs
        public override string Name => "Wood Chop";

        // Icon path relative to the mod's assets folder
        public override string IconPath => "icon_woodchop.png"; 

        // The effect that happens when this ability is activated
        public override AbilityEffect Effect => new WoodChopEffect();

        /// <summary>
        /// Defines the cost of using this skill (e.g., Stamina, Mana).
        /// </summary>
        public override AbilityCost Cost => new AbilityCost(AbilityResourceType.Stamina, 15);

        // Optional: Define prerequisites or tags if needed later
        public override IEnumerable<string> Tags => new[] { "Skill", "Combat", "WoodChop" };
    }
}