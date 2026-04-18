using System.Numerics;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Presentation.Performers
{
    public struct ChildPerformerRef
    {
        public int DefinitionId;
        public int ScopeTag;
        public ParamDefault[] ParamOverrides;
    }

    public struct ParamDefault
    {
        public int ParamKey;
        public ParamLane Lane;
        public float FloatValue;
        public int IntValue;
        public Vector4 VectorValue;
    }

    /// <summary>
    /// Declarative performer definition. Visual output is authored through behavior slots,
    /// not through legacy visual-kind fields.
    /// </summary>
    public sealed class PerformerDefinition
    {
        public int Id;
        public string Key = string.Empty;
        public string Extends = string.Empty;
        public ChildPerformerRef[] Children = System.Array.Empty<ChildPerformerRef>();
        public BehaviorSlot[] Behaviors = System.Array.Empty<BehaviorSlot>();
        public PerformerRule[] Rules = System.Array.Empty<PerformerRule>();
        public ConditionRef VisibilityCondition;
        public PerformerParamBinding[] Bindings = System.Array.Empty<PerformerParamBinding>();
        public ParamDefault[] ParamDefaults = System.Array.Empty<ParamDefault>();
        public SurfaceAuthoringBlock? Surface;
        public Vector3 PositionOffset;
        public float PositionYDriftPerSecond;
        public bool AlphaFadeOverLifetime;
        public Vector4 DefaultColor = new(1f, 1f, 1f, 1f);
        public float DefaultLifetime;
        public int DefaultFontSize = 16;
        public int DefaultTextId;

        /// <summary>
        /// Transitional format hint for adapters that still consume Id1/Value0/Value1.
        /// The adapter-neutral runtime contract lives in <see cref="PresentationTextPacket"/>.
        /// </summary>
        public WorldHudValueMode LegacyWorldTextMode = WorldHudValueMode.None;

        internal int[] BindingIndex = System.Array.Empty<int>();

        internal void BuildBindingIndex()
        {
            if (Bindings == null || Bindings.Length == 0)
            {
                BindingIndex = System.Array.Empty<int>();
                return;
            }

            int maxKey = 0;
            for (int i = 0; i < Bindings.Length; i++)
            {
                if (Bindings[i].ParamKey > maxKey)
                {
                    maxKey = Bindings[i].ParamKey;
                }
            }

            int[] index = new int[maxKey + 1];
            System.Array.Fill(index, -1);
            for (int i = 0; i < Bindings.Length; i++)
            {
                int key = Bindings[i].ParamKey;
                if (key >= 0 && key < index.Length)
                {
                    index[key] = i;
                }
            }

            BindingIndex = index;
        }
    }
}
