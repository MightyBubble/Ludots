using Arch.Core;
using Ludots.Core.Gameplay.Components;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Normalized audience/viewer context consumed by perform phase resolution.
    /// Raw Team/PlayerOwner components are folded into this contract upstream.
    /// </summary>
    public struct PerformAudienceContext
    {
        public Entity Viewer;
        public Team ViewerTeam;
        public bool HasViewerTeam;
        public PlayerOwner ViewerOwner;
        public bool HasViewerOwner;
        public bool RevealHidden;

        public readonly bool HasViewer => Viewer != Entity.Null;

        public static readonly PerformAudienceContext Default = new()
        {
            Viewer = Entity.Null,
        };
    }
}
