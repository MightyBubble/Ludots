namespace Ludots.Core.Gameplay.Components
{
    /// <summary>
    /// Represents the Team ID this entity belongs to.
    /// Used for Friend/Foe identification.
    /// </summary>
    public struct Team
    {
        public int Id;
    }

    /// <summary>
    /// Represents the Player ID that owns/controls this entity.
    /// Used for input routing and ownership logic.
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
