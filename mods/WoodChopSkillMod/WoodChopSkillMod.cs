using Ludots;
using Ludots.Core;
using WoodChopSkillMod.Abilities;
using WoodChopSkillMod.Effects;

namespace WoodChopSkillMod
{
    /// <summary>
    /// The main entry point for the Wood Chop Skill Mod.
    /// This class registers all abilities and effects provided by this mod.
    /// </summary>
    public class WoodChopSkillMod : LudotsMod
    {
        // Required: Unique ID for this mod instance
        public override string Id => "WoodChopSkillMod";

        // Optional: Display name in the Mod Manager UI
        public override string Name => "Wood Chop Skill Showcase";

        /// <summary>
        /// Called when the framework loads and initializes this mod.
        /// This is where we register our components.
        /// </summary>
        public override void Initialize()
        {
            // 1. Register the Ability Definition itself
            this.RegisterAbility(new WoodChopAbility());

            Console.WriteLine($"[WoodChopSkillMod] Successfully initialized and registered Wood Chop Skill!");

            // --- Optional: Simple Test Hook (For immediate verification) ---
            // If we had a way to get an Entity instance immediately, we could test it here.
            // For now, this confirms the registration is successful.
        }

        /// <summary>
        /// Called when the framework shuts down or unloads this mod.
        /// </summary>
        public override void Shutdown()
        {
            Console.WriteLine("[WoodChopSkillMod] Shutting down and cleaning up resources.");
        }
    }
}