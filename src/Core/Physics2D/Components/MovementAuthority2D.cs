namespace Ludots.Core.Physics2D.Components
{
    /// <summary>
    /// Marks entities whose movement authority is temporarily owned by crowd control
    /// or displacement instead of navigation velocity sync. While this tag is present,
    /// locomotion linear velocity is cleared every Physics2D tick; movement may still
    /// come from displacement-authored Position2D steps and collision response.
    /// </summary>
    public struct MovementSuppressed2D
    {
    }
}
