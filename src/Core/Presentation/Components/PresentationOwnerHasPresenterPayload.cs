using Arch.Core;

namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// Marker on logic owner entities that currently have presenter payload.
    /// Hot culling paths use this instead of consulting presenter runtime dictionaries.
    /// </summary>
    public struct PresentationOwnerHasPresenterPayload
    {
        public int Count;
        public int RootCount;
        public Entity SingleRootPresenter;
        public byte SingleRootTransformSync;
    }
}
