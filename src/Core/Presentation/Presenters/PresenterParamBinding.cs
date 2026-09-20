namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Binds a presenter parameter (identified by <see cref="ParamKey"/>) to a
    /// data source (<see cref="Value"/>). The PresenterEmitSystem resolves bindings
    /// each frame for visible instances, ensuring data freshness after off-screen
    /// → on-screen transitions.
    ///
    /// ParamKey interpretation is behavior-owned and documented by <see cref="WellKnownPresenterParamKeys"/>.
    /// </summary>
    public struct PresenterParamBinding
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
