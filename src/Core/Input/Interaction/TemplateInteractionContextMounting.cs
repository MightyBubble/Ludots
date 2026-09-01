using System;
using Arch.Core;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Spawn-time mounting of a template's <c>initialInteractionContext</c> (#1398 S2b,
    /// Case E 01/02): the entity birth chain (map-load lane and runtime spawn queue) mounts
    /// the declared profile as the entity's base
    /// <see cref="InteractionContextInstance"/> — the mechanism is code, which context a
    /// template mounts is configuration. Unknown profile ids fail fast at engine init
    /// (template sweep) and again at mount on drift; an entity that already carries a
    /// context is a pipeline error, never a silent overwrite.
    /// </summary>
    public static class TemplateInteractionContextMounting
    {
        /// <summary>
        /// Mount the template-declared context on a freshly spawned entity. No-op when the
        /// template declares none.
        /// </summary>
        public static void MountInitialContext(
            World world,
            InteractionContextProfileRegistry profiles,
            Entity entity,
            string templateId,
            string? profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return;
            }

            if (world == null || !world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' declares initialInteractionContext '{profileName}' but the spawn produced no live entity.");
            }

            int profileId = profiles.ProfileIdRegistry.GetId(profileName);
            if (!profiles.IsInstalled(profileId))
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' declares initialInteractionContext '{profileName}' which is not installed.");
            }

            if (world.Has<InteractionContextInstance>(entity))
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' declares initialInteractionContext '{profileName}' but entity {entity} already carries an interaction context instance; spawn mounting never overwrites.");
            }

            if (!profiles.TryCreateActiveContext(profileId, entity, InteractionContextInstanceSource.TemplateSpawn, out InteractionContextInstance instance))
            {
                throw new InvalidOperationException(
                    $"Entity template '{templateId}' declares initialInteractionContext '{profileName}' which is not installed.");
            }

            world.Add(entity, instance);
        }

        /// <summary>
        /// Engine-init template sweep: every declared initialInteractionContext must resolve
        /// to an installed profile — the load-chain fail-fast for template declarations.
        /// </summary>
        public static void ValidateTemplates(
            System.Collections.Generic.IEnumerable<Ludots.Core.Config.EntityTemplate> templates,
            InteractionContextProfileRegistry profiles)
        {
            foreach (Ludots.Core.Config.EntityTemplate template in templates)
            {
                if (string.IsNullOrWhiteSpace(template.InitialInteractionContext))
                {
                    continue;
                }

                int profileId = profiles.ProfileIdRegistry.GetId(template.InitialInteractionContext);
                if (!profiles.IsInstalled(profileId))
                {
                    throw new InvalidOperationException(
                        $"Entity template '{template.Id}' declares initialInteractionContext '{template.InitialInteractionContext}' which is not installed.");
                }
            }
        }
    }
}
