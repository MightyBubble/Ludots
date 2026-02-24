namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Binds a performer parameter (identified by <see cref="ParamKey"/>) to a
    /// data source (<see cref="Value"/>). The PerformerEmitSystem resolves bindings
    /// each frame for visible instances, ensuring data freshness after off-screen
    /// → on-screen transitions.
    ///
    /// ParamKey interpretation is VisualKind-dependent (documented convention):
    ///   GroundOverlay: 0=Radius, 1=InnerRadius, 2=Angle, 3=Rotation, 4=FillColorR, ...
    ///   Marker3D:      0=Scale, 4=ColorR, 5=ColorG, 6=ColorB, 7=ColorA
    ///   WorldText:     0=Value, 4=ColorR, 5=ColorG, 6=ColorB, 7=ColorA
    ///   WorldBar:      0=FillRatio, 4=FillColorR, 5=FillColorG, 6=FillColorB, 7=FillColorA
    /// </summary>
    public struct PerformerParamBinding
    {
        /// <summary>
        /// Application-defined parameter key. Interpretation depends on the
        /// PerformerDefinition's VisualKind.
        /// </summary>
        public int ParamKey;

        /// <summary>
        /// The data source to resolve each frame.
        /// </summary>
        public ValueRef Value;
    }
}
