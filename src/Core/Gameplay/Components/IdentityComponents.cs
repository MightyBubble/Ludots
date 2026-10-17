namespace Ludots.Core.Gameplay.Components
{
    /// <summary>
    /// Once a MemberOf edge reaches a team representative, the id is projected from that representative.
    /// </summary>
    public struct Team
    {
        public int Id;
    }

    /// <summary>
    /// Once the Owns chain reaches a player representative, the id is projected from that representative.
    /// </summary>
    public struct PlayerOwner
    {
        public int PlayerId;
    }

    /// <summary>
    /// Marker component for Player representative entities in ECS.
    /// </summary>
    public struct PlayerIdentity
    {
        public int PlayerId;
    }
}
