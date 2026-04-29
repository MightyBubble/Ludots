using Arch.Core;

namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// Marker on logic owner entities that currently have performer payload.
    /// Hot culling paths use this instead of consulting performer runtime dictionaries.
    /// </summary>
    public struct PresentationOwnerHasPerformerPayload
    {
        public int Count;
        public Entity SingleRootPerformer;
    }
}
