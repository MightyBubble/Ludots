using Arch.Core;
using Ludots.Core.Gameplay.Components;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Normalized audience/viewer context consumed by present phase resolution.
    /// Raw Team/PlayerOwner components are folded into this contract upstream.
    /// </summary>
    public struct PresentAudienceContext
    {
        public Entity Viewer;
        public Team ViewerTeam;
        public bool HasViewerTeam;
        public PlayerOwner ViewerOwner;
        public bool HasViewerOwner;
        public bool RevealHidden;

        public readonly bool HasViewer => Viewer != Entity.Null;

        public static readonly PresentAudienceContext Default = new()
        {
            Viewer = Entity.Null,
        };
    }
}
