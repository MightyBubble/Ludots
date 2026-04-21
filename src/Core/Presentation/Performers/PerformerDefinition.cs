using System.Numerics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;

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

        /// <summary>
        /// When &gt; 0, only emit for entities whose <see cref="Components.VisualTemplateRef.TemplateId"/>
        /// matches this value. Zero = no template filter (emit for all matching entities, the default).
        /// </summary>
        public int RequiredTemplateId;

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

        /// <summary>Adapter-neutral asset key for typed non-mesh requests.</summary>
        public string VisualAssetKey = string.Empty;

        /// <summary>Optional material key for material/decal/custom-data requests.</summary>
        public string MaterialKey = string.Empty;

        /// <summary>Optional surface layer key for surface/RVT requests.</summary>
        public string SurfaceLayerKey = string.Empty;

        /// <summary>Static typed payload fields submitted with typed non-mesh requests.</summary>
        public PresentationPayloadField[] DefaultPayload = System.Array.Empty<PresentationPayloadField>();

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

        /// <summary>Stable text token ID for WorldText performers.</summary>
        public int DefaultTextId;

        /// <summary>
        /// Transitional format hint for legacy adapters that still consume Id1/Value0/Value1.
        /// The adapter-neutral runtime contract lives in <see cref="PresentationTextPacket"/>.
        /// </summary>
        public WorldHudValueMode LegacyWorldTextMode = WorldHudValueMode.None;

        // ── Binding index (built by PerformerDefinitionRegistry.Register) ──

        /// <summary>
        /// Pre-built index for O(1) legacy binding lookup by ParamKey.
        /// <c>_bindingIndex[paramKey]</c> -> index into <see cref="Bindings"/> array, or -1 if unbound.
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

            for (int i = 0; i < Bindings.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(Bindings[i].FieldName) &&
                    !CanResolveDynamicFieldBinding(Bindings[i].ValueKind))
                {
                    throw new InvalidOperationException(
                        $"PerformerDefinition id={Id} binding[{i}] field '{Bindings[i].FieldName}' valueKind '{Bindings[i].ValueKind}' cannot be resolved from dynamic binding sources. Use static payload or typed command field overrides for this value kind.");
                }
            }

            // Determine required size from max legacy ParamKey.
            int maxKey = 0;
            bool hasLegacyParamKey = false;
            for (int i = 0; i < Bindings.Length; i++)
            {
                if (IsLegacyBinding(in Bindings[i]))
                {
                    hasLegacyParamKey = true;
                    if (Bindings[i].ParamKey > maxKey)
                        maxKey = Bindings[i].ParamKey;
                }
            }

            if (!hasLegacyParamKey)
            {
                BindingIndex = System.Array.Empty<int>();
                return;
            }

            var index = new int[maxKey + 1];
            System.Array.Fill(index, -1);
            for (int i = 0; i < Bindings.Length; i++)
            {
                int key = Bindings[i].ParamKey;
                if (IsLegacyBinding(in Bindings[i]) && key < index.Length)
                    index[key] = i;
            }
            BindingIndex = index;
        }

        private static bool IsLegacyBinding(in PerformerParamBinding binding)
        {
            return binding.ParamKey >= 0 && string.IsNullOrWhiteSpace(binding.FieldName);
        }

        private static bool CanResolveDynamicFieldBinding(PresentationTypedValueKind kind)
        {
            return kind == PresentationTypedValueKind.Bool ||
                kind == PresentationTypedValueKind.Int ||
                kind == PresentationTypedValueKind.Float ||
                kind == PresentationTypedValueKind.Vector4 ||
                kind == PresentationTypedValueKind.Color;
        }
    }
}
