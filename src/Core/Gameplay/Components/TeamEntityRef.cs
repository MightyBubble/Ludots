using Arch.Core;

namespace Ludots.Core.Gameplay.Components
{
    /// <summary>
    /// Optional component linking a gameplay entity to its Team meta-entity.
    /// Used when systems need to access team-level attributes / tags / effects.
    ///
    /// For fast team filtering (friend/foe), use <see cref="Team.Id"/> (int comparison).
    /// For team attribute access, resolve via this ref.
    /// </summary>
    public struct TeamEntityRef
    {
        public Entity Value;
    }
}
