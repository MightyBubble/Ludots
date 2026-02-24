namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Specifies where a parameter value comes from at evaluation time.
    /// </summary>
    public enum ValueSourceKind : byte
    {
        /// <summary>Use <see cref="ValueRef.ConstantValue"/> directly.</summary>
        Constant = 0,

        /// <summary>Read Attribute[<see cref="ValueRef.SourceId"/>] current value from the Owner entity.</summary>
        Attribute = 1,

        /// <summary>Execute Graph program <see cref="ValueRef.SourceId"/>, read F[0] as the result.</summary>
        Graph = 2,

        /// <summary>
        /// Read Attribute current / base ratio (clamped 0-1) from the Owner entity.
        /// Typical use: health bar fill percentage. SourceId = attribute ID.
        /// </summary>
        AttributeRatio = 3,

        /// <summary>
        /// Read Attribute base (max) value from the Owner entity.
        /// Typical use: health text "current/max" display. SourceId = attribute ID.
        /// </summary>
        AttributeBase = 4,

        /// <summary>
        /// Read a per-entity color channel from an injected resolver.
        /// SourceId = channel index: 0=R, 1=G, 2=B, 3=A.
        /// The resolver is platform/game-specific (e.g. team color, faction color).
        /// If no resolver is injected, returns the DefaultColor channel.
        /// </summary>
        EntityColor = 5,
    }

    /// <summary>
    /// A declarative data source for a single float parameter.
    /// Resolved each frame by PerformerEmitSystem for visible instances.
    /// This ensures parameters are always fresh after off-screen → on-screen transitions.
    /// </summary>
    public struct ValueRef
    {
        /// <summary>The kind of data source.</summary>
        public ValueSourceKind Source;

        /// <summary>
        /// Interpretation depends on <see cref="Source"/>:
        ///   Attribute → the attribute ID to read from the Owner entity.
        ///   Graph     → the registered Graph program ID to execute.
        ///   Constant  → unused.
        /// </summary>
        public int SourceId;

        /// <summary>The literal value when Source == Constant.</summary>
        public float ConstantValue;

        public static ValueRef FromConstant(float value) => new()
        {
            Source = ValueSourceKind.Constant,
            ConstantValue = value
        };

        public static ValueRef FromAttribute(int attributeId) => new()
        {
            Source = ValueSourceKind.Attribute,
            SourceId = attributeId
        };

        public static ValueRef FromGraph(int graphProgramId) => new()
        {
            Source = ValueSourceKind.Graph,
            SourceId = graphProgramId
        };

        public static ValueRef FromAttributeRatio(int attributeId) => new()
        {
            Source = ValueSourceKind.AttributeRatio,
            SourceId = attributeId
        };

        public static ValueRef FromAttributeBase(int attributeId) => new()
        {
            Source = ValueSourceKind.AttributeBase,
            SourceId = attributeId
        };

        /// <param name="channelIndex">0=R, 1=G, 2=B, 3=A</param>
        public static ValueRef FromEntityColor(int channelIndex) => new()
        {
            Source = ValueSourceKind.EntityColor,
            SourceId = channelIndex
        };
    }
}
