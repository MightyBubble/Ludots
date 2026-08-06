namespace Ludots.Core.Physics2D.Components
{
    /// <summary>
    /// Opt-in authoring declaration for the contact event pipeline (issue #732).
    /// Only entities carrying this component participate in contact begin/end edge
    /// detection; entities without it pay zero additional cost. The entity must also
    /// carry an EntityLayer whose category is covered by the configured
    /// contactEventEmitterLayers allowlist, otherwise emission is a contract error.
    /// </summary>
    public struct ContactEventEmitter2D
    {
    }
}
