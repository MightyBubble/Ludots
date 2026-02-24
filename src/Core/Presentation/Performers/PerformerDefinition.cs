using System.Numerics;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// A complete declarative definition of a performer: what it looks like, when it
    /// reacts, when it is visible, and how its parameters are resolved.
    ///
    /// Registered in <see cref="PerformerDefinitionRegistry"/> by integer ID.
    /// </summary>
    public sealed class PerformerDefinition
    {
        /// <summary>Unique definition ID.</summary>
        public int Id;

        /// <summary>Visual output category — determines which draw buffer to write to.</summary>
        public PerformerVisualKind VisualKind;

        /// <summary>
        /// Event-driven rules. The PerformerRuleSystem evaluates these when
        /// PresentationEvents arrive.
        /// </summary>
        public PerformerRule[] Rules = System.Array.Empty<PerformerRule>();

        /// <summary>
        /// Declarative visibility condition — controls the Active / Dormant lifecycle.
        /// Default (all zeroes) = always visible.
        /// When false: instance survives but stops emitting; when true again, bindings
        /// are resolved fresh so data is always in sync.
        /// </summary>
        public ConditionRef VisibilityCondition;

        /// <summary>
        /// Declarative parameter bindings resolved each frame for visible instances.
        /// Parameters not bound here fall through to the static defaults below.
        /// </summary>
        public PerformerParamBinding[] Bindings = System.Array.Empty<PerformerParamBinding>();

        // ── Entity-scoped mode ──

        /// <summary>
        /// When not None, this definition is entity-scoped: PerformerEmitSystem queries
        /// matching entities directly each frame instead of using PerformerInstanceBuffer.
        /// Instance-scoped fields (Rules, DefaultLifetime) are ignored for entity-scoped definitions.
        /// </summary>
        public EntityScopeFilter EntityScope;

        // ── Time-based modulation (instance-scoped only) ──

        /// <summary>
        /// World-space offset added to the Owner entity's position.
        /// For entity-scoped definitions, this is the static offset each frame.
        /// </summary>
        public Vector3 PositionOffset;

        /// <summary>
        /// Y-axis drift speed in meters per second. Applied based on Elapsed time.
        /// Typical use: floating combat text rising upward (e.g., 0.8).
        /// </summary>
        public float PositionYDriftPerSecond;

        /// <summary>
        /// When true, linearly fade the alpha channel from 1.0 → 0.0 over DefaultLifetime.
        /// Typical use: floating combat text fading out.
        /// </summary>
        public bool AlphaFadeOverLifetime;

        // ── Static default values (used when no Binding or Override exists) ──

        /// <summary>Mesh asset ID (Marker3D) or GroundOverlayShape ordinal (GroundOverlay).</summary>
        public int MeshOrShapeId;

        /// <summary>Default color (RGBA).</summary>
        public Vector4 DefaultColor = new(1f, 1f, 1f, 1f);

        /// <summary>Default scale / radius.</summary>
        public float DefaultScale = 1f;

        /// <summary>
        /// Lifetime in seconds. Values &lt;= 0 mean persistent (no auto-expiry).
        /// Elapsed ticks even when dormant, so duration-based performers expire on time.
        /// </summary>
        public float DefaultLifetime;

        /// <summary>Default font size for WorldText performers.</summary>
        public int DefaultFontSize = 16;

        // ── Binding index (built by PerformerDefinitionRegistry.Register) ──

        /// <summary>
        /// Pre-built index for O(1) binding lookup by ParamKey.
        /// <c>_bindingIndex[paramKey]</c> → index into <see cref="Bindings"/> array, or -1 if unbound.
        /// Built once at registration time; never mutated at runtime.
        /// </summary>
        internal int[] BindingIndex = System.Array.Empty<int>();

        /// <summary>
        /// Build the O(1) binding index from <see cref="Bindings"/>.
        /// Called by <see cref="PerformerDefinitionRegistry.Register"/> after the definition is stored.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        internal void BuildBindingIndex()
        {
            if (Bindings == null || Bindings.Length == 0)
            {
                BindingIndex = System.Array.Empty<int>();
                return;
            }

            // Determine required size from max ParamKey
            int maxKey = 0;
            for (int i = 0; i < Bindings.Length; i++)
            {
                if (Bindings[i].ParamKey > maxKey)
                    maxKey = Bindings[i].ParamKey;
            }

            var index = new int[maxKey + 1];
            System.Array.Fill(index, -1);
            for (int i = 0; i < Bindings.Length; i++)
            {
                int key = Bindings[i].ParamKey;
                if (key >= 0 && key < index.Length)
                    index[key] = i;
            }
            BindingIndex = index;
        }
    }
}
