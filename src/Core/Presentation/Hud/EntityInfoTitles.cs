using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.Presentation.Hud
{
    public interface IEntityInfoTitleProfiles
    {
        bool TryGetProfileTitleTokenId(int templateKeyId, out int tokenId);
    }

    public static class EntityInfoTitles
    {
        public static bool TryGetTokenId(
            World world,
            Entity entity,
            PresentationTextCatalog catalog,
            IEntityInfoTitleProfiles? profiles,
            out int tokenId)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            if (world.TryGet(entity, out EntityInfoTitleToken placed))
            {
                if (string.IsNullOrWhiteSpace(placed.Value))
                {
                    throw new InvalidOperationException("Entity info title token is blank.");
                }

                tokenId = catalog.GetTokenId(placed.Value);
                if (tokenId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Entity info title token '{placed.Value}' is not registered.");
                }

                return true;
            }

            if (profiles == null)
            {
                throw new InvalidOperationException("Entity info titles are not installed.");
            }

            tokenId = 0;
            if (!world.TryGet(entity, out EntityTemplateKeyRef templateKey))
            {
                return false;
            }

            return profiles.TryGetProfileTitleTokenId(templateKey.TemplateKeyId, out tokenId) && tokenId > 0;
        }
    }
}
