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
