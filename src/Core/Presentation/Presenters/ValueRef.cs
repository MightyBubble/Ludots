namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Specifies where a parameter value comes from at evaluation time.
    /// </summary>
    public enum ValueSourceKind : byte
    {
        /// <summary>Use <see cref="ValueRef.ConstantValue"/> directly.</summary>
        Constant = 0,

        /// <summary>Read owner attribute current value. Valid for AttributeBinding.mode, not PresenterParamBinding.</summary>
        Attribute = 1,

        /// <summary>
        /// Execute Graph program <see cref="ValueRef.SourceId"/> (Score kind) and write F[0] back to the
        /// binding's ParamKey on the Float lane. Input context seeded by PresenterBehaviorSystem:
        /// E[0]=owner, E[1]=presenter, F[k] (k&gt;=1)=current Param Blackboard float lane key k
        /// (F[0] is reserved as the result register and is never seeded as an input).
        /// </summary>
        Graph = 2,

        /// <summary>
        /// Read owner attribute current / base ratio (clamped 0-1).
        /// Valid for AttributeBinding.mode, not PresenterParamBinding.
        /// </summary>
        AttributeRatio = 3,

        /// <summary>
        /// Read owner attribute base value.
        /// Valid for AttributeBinding.mode, not PresenterParamBinding.
        /// </summary>
        AttributeBase = 4,

        /// <summary>
        /// Read a per-entity color channel from an injected resolver.
        /// SourceId = channel index: 0=R, 1=G, 2=B, 3=A.
        /// The resolver is platform/game-specific (e.g. team color, faction color).
        /// If no resolver is injected, returns the DefaultColor channel.
        /// </summary>
        EntityColor = 5,

        /// <summary>
        /// Read the full per-entity color vector from an injected resolver.
        /// Typical use: bind TeamColorResolver output into the Vector lane for
        /// AssetBinding.ColorParamKey.
        /// </summary>
        EntityColorVector = 8,

        /// <summary>
        /// Read <see cref="Components.FacingDirection.AngleRad"/> from the owner.
        /// </summary>
        FacingRadians = 6,

        /// <summary>
        /// Read <see cref="Components.FacingDirection.AngleRad"/> from the owner and
        /// convert it to degrees.
        /// </summary>
        FacingDegrees = 7,

        /// <summary>
        /// Read a float from the owner entity's <c>BlackboardFloatBuffer</c>.
        /// <see cref="ValueRef.SourceId"/> is the ConfigKeyRegistry id of the blackboard key.
        /// </summary>
        OwnerBlackboardFloat = 9,

        /// <summary>Read the live authoritative pointer screen X (window px).</summary>
        PointerScreenX = 10,

        /// <summary>Read the live authoritative pointer screen Y (window px).</summary>
        PointerScreenY = 11,
    }

    /// <summary>
    /// A declarative data source for a single float parameter.
    /// Resolved each frame by PresenterEmitSystem for visible instances.
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

        /// <summary>
        /// Explicit opt-in for source-missing tolerance (author-facing <c>optional: true</c>;
        /// never a silent default). When set, a source that cannot be resolved right now
        /// (e.g. an owner blackboard key not yet written because the driving state has not
        /// occurred) projects 0 instead of throwing — the consumer decides what absent data
        /// means (e.g. ScreenRect visibility hides the render). Without it, a missing source
        /// remains a hard fail-fast contract error.
        /// </summary>
        public bool Optional;

        public static ValueRef FromConstant(float value) => new()
        {
            Source = ValueSourceKind.Constant,
            ConstantValue = value
        };

        public static ValueRef FromGraph(int graphProgramId) => new()
        {
            Source = ValueSourceKind.Graph,
            SourceId = graphProgramId
        };

        /// <param name="channelIndex">0=R, 1=G, 2=B, 3=A</param>
        public static ValueRef FromEntityColor(int channelIndex) => new()
        {
            Source = ValueSourceKind.EntityColor,
            SourceId = channelIndex
        };

        public static ValueRef FromEntityColorVector() => new()
        {
            Source = ValueSourceKind.EntityColorVector
        };

        public static ValueRef FromFacingRadians() => new()
        {
            Source = ValueSourceKind.FacingRadians
        };

        public static ValueRef FromFacingDegrees() => new()
        {
            Source = ValueSourceKind.FacingDegrees
        };

        public static ValueRef FromOwnerBlackboardFloat(int blackboardKeyId) => new()
        {
            Source = ValueSourceKind.OwnerBlackboardFloat,
            SourceId = blackboardKeyId
        };

        public static ValueRef FromPointerScreenX() => new()
        {
            Source = ValueSourceKind.PointerScreenX
        };

        public static ValueRef FromPointerScreenY() => new()
        {
            Source = ValueSourceKind.PointerScreenY
        };
    }
}
