using System;
using Arch.Core;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationImageBindingResolver
    {
        private readonly PresentationImageSourceResolver _imageSourceResolver;

        public PresentationImageBindingResolver(PresentationImageSourceResolver imageSourceResolver)
        {
            _imageSourceResolver = imageSourceResolver ?? throw new ArgumentNullException(nameof(imageSourceResolver));
        }

        public string ResolveRequiredSource(
            World world,
            Entity entity,
            PresentationImageRole role,
            PresentationImageState state = PresentationImageState.Default)
        {
            if (entity == Entity.Null || !world.IsAlive(entity))
            {
                throw new InvalidOperationException("Presentation image binding resolution requires a live entity.");
            }

            if (!world.TryGet(entity, out PresentationImageBinding binding))
            {
                throw new InvalidOperationException($"Entity '{entity.Id}' is missing PresentationImageBinding.");
            }

            if (!binding.TryGet(role, state, out int imageAssetId))
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Id}' does not define a presentation image binding for role '{role}' and state '{state}'.");
            }

            return _imageSourceResolver.ResolveRequiredSource(imageAssetId);
        }
    }
}
