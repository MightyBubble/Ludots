namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Binds a performer parameter (identified by <see cref="ParamKey"/>) to a
    /// data source (<see cref="Value"/>). The PerformerEmitSystem resolves bindings
    /// each frame for visible instances, ensuring data freshness after off-screen
    /// → on-screen transitions.
    ///
    /// ParamKey interpretation is behavior-owned and documented by <see cref="WellKnownPerformerParamKeys"/>.
    /// </summary>
    public struct PerformerParamBinding
    {
        /// <summary>
        /// Application-defined parameter key. Interpretation depends on the behavior that consumes it.
        /// </summary>
        public int ParamKey;

        /// <summary>
        /// The data source to resolve each frame.
        /// </summary>
        public ValueRef Value;
    }
}
