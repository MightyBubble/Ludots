namespace Ludots.Core.Gameplay.Components
{
    /// <summary>
    /// Marker component for Team meta-entities in ECS.
    /// Distinguishes Team entities from gameplay entities that carry a <see cref="Team"/> component.
    ///
    /// Team entities can carry GAS components:
    ///   - GameplayTagContainer  (team-level tags,  e.g. "Team.HasBaron")
    ///   - AttributeBuffer       (team-level attrs, e.g. gold pool, score)
    ///   - ActiveEffectContainer  (team-level effects, e.g. team-wide buffs)
    ///
    /// Team entities do NOT have WorldPositionCm — they never enter the spatial partition.
    /// Use <see cref="Teams.TeamEntityLookup"/> to resolve TeamId → Team Entity.
    /// </summary>
    public struct TeamIdentity
    {
        public int TeamId;
    }
}
