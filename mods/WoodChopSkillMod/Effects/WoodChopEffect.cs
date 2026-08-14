using Ludots;
using Ludots.Core;
using Ludots.GAS;

namespace WoodChopSkillMod.Effects
{
    /// <summary>
    /// Defines the effect applied when the Wood Chop ability is used.
    /// This implementation deals a fixed amount of damage to the target entity.
    /// </summary>
    public class WoodChopEffect : AbilityEffect
    {
        // The base damage dealt by this skill
        private const float BaseDamage = 25f;

        /// <summary>
        /// Executes the effect logic on the target entity.
        /// </summary>
        /// <param name="ability">The ability that triggered this effect.</param>
        /// <param name="entity">The entity receiving the effect (the target).</param>
        public override void Execute(AbilityDefinition ability, Entity targetEntity, Entity performerEntity)
        {
            // 1. Log the action for visibility in the console/UI
            Console.WriteLine($"[WoodChopSkillMod] Executing Wood Chop on {entity.Name}. Dealing {BaseDamage} damage.");

            // 2. Apply Damage using the Attribute System (assuming 'Health' attribute exists)
            if (entity.HasAttribute(typeof(Health)))
            {
                var health = entity.GetAttribute<Health>();
                
                // Calculate final damage (could incorporate modifiers later, but keeping it simple for now)
                float finalDamage = BaseDamage; 

                // Apply the change to the attribute value
                health.Value -= finalDamage;
                Console.WriteLine($"[WoodChopSkillMod] Health reduced from {health.Value + finalDamage} to {health.Value}.");
            }
            else
            {
                 Console.WriteLine("[WoodChopSkillMod] Warning: Target entity lacks a 'Health' attribute!");
            }

            // 3. Optional: Add visual feedback or other side effects here (e.g., applying a temporary buff)
        }
    }
}