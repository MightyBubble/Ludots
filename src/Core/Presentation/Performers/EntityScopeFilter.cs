namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Determines how an entity-scoped <see cref="PerformerDefinition"/> selects
    /// entities to render. When set to a value other than None, the definition
    /// does NOT use <see cref="PerformerInstanceBuffer"/>; instead,
    /// <see cref="Systems.PerformerEmitSystem"/> queries matching entities directly
    /// each frame and applies bindings.
    /// </summary>
    public enum EntityScopeFilter : byte
    {
        /// <summary>
        /// Not entity-scoped. Uses instance-scoped lifecycle
        /// (PerformerInstanceBuffer, PresentationCommands).
        /// </summary>
        None = 0,

        /// <summary>
        /// Applies to all entities with VisualTransform + AttributeBuffer.
        /// Typical use: health bars, attribute-driven status text.
        /// </summary>
        AllWithAttributes = 1,

        /// <summary>
        /// Applies to all entities with VisualTransform.
        /// Typical use: name labels, team markers.
        /// </summary>
        AllWithVisualTransform = 2,
    }
}
