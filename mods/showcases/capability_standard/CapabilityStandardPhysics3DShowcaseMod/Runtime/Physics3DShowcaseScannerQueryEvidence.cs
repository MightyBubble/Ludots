namespace CapabilityStandardPhysics3DShowcaseMod.Runtime;

internal readonly record struct Physics3DShowcaseScannerQueryEvidence(
    int RayHits,
    float RayFirstDistanceCm,
    int BoxCastHits,
    float BoxCastFirstDistanceCm,
    int SphereCastHits,
    float SphereCastFirstDistanceCm,
    int CapsuleCastHits,
    float CapsuleCastFirstDistanceCm,
    int BoxOverlapHits,
    int SphereOverlapHits,
    int CapsuleOverlapHits)
{
    public static Physics3DShowcaseScannerQueryEvidence Empty { get; } = default;
}
