using Ludots.Core.Mathematics.FixedPoint;

namespace CapabilityStandardCrowdPhysicsArenaMod.Runtime;

/// <summary>
/// Authored door marker: the pressure-plate consumer opens this door once the plate
/// accumulated <see cref="OpenThresholdContacts"/> agent ContactBegin events.
/// The threshold is template/map data, not code.
/// </summary>
public struct CrowdPhysicsArenaDoor
{
    public int OpenThresholdContacts;
}

/// <summary>
/// Bolts the pressure plate to the arena floor. The plate must stay a Dynamic contact-event
/// emitter so the broadphase pairs it with kinematic squad agents (kinematic×static pairs are
/// solver-meaningless and skipped), but a full squad wave shoves an unconstrained dynamic
/// plate through its static socket in one correction burst and ejects it from the arena.
/// The pressure-plate system therefore re-seats anchored plates on their authored rigid-body
/// position with zero velocity every fixed step (the anchor is captured from the authored
/// Position2D on the first step, keeping the map's RigidBody positionCm the single source
/// of truth).
/// </summary>
public struct CrowdPhysicsArenaPlateAnchor
{
    public byte Captured;
    public Fix64Vec2 AnchorCm;
}
