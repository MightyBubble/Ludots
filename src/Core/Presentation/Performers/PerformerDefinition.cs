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
        public int[] RequiredAttributeIds = System.Array.Empty<int>();

        /// <summary>
        /// Transitional format hint for adapters that still consume Id1/Value0/Value1.
        /// The adapter-neutral runtime contract lives in <see cref="PresentationTextPacket"/>.
        /// </summary>
        public WorldHudValueMode LegacyWorldTextMode = WorldHudValueMode.None;

        internal int[] BindingIndex = System.Array.Empty<int>();
        internal uint AssetBindingSlotMask;
        internal uint AnimatorSlotMask;
        internal bool HasAssetBindingBehavior;
        internal bool HasAnimatorBehavior;
        internal bool HasSurfaceAuthoring;
        internal bool RequiresBootstrapProcessing;

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

        internal void BuildRequiredAttributeIds()
        {
            if ((Bindings == null || Bindings.Length == 0) &&
                (Behaviors == null || Behaviors.Length == 0))
            {
                RequiredAttributeIds = System.Array.Empty<int>();
                return;
            }

            var required = new System.Collections.Generic.HashSet<int>();
            if (Bindings != null)
            {
                for (int i = 0; i < Bindings.Length; i++)
                {
                    int attributeId = ResolveAttributeId(Bindings[i].Value);
                    if (attributeId >= 0)
                    {
                        required.Add(attributeId);
                    }
                }
            }

            if (Behaviors != null)
            {
                for (int i = 0; i < Behaviors.Length; i++)
                {
                    if (Behaviors[i].Kind == BehaviorKind.AttributeBinding &&
                        Behaviors[i].AttributeBinding.AttributeId >= 0)
                    {
                        required.Add(Behaviors[i].AttributeBinding.AttributeId);
                    }
                }
            }

            if (required.Count == 0)
            {
                RequiredAttributeIds = System.Array.Empty<int>();
                return;
            }

            int[] ids = new int[required.Count];
            required.CopyTo(ids);
            System.Array.Sort(ids);
            RequiredAttributeIds = ids;
        }

        internal void BuildBehaviorMetadata()
        {
            AssetBindingSlotMask = 0u;
            AnimatorSlotMask = 0u;
            HasAssetBindingBehavior = false;
            HasAnimatorBehavior = false;
            HasSurfaceAuthoring = Surface != null;
            RequiresBootstrapProcessing = (Bindings != null && Bindings.Length > 0) || HasSurfaceAuthoring;

            if (Behaviors == null || Behaviors.Length == 0)
            {
                return;
            }

            for (int i = 0; i < Behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref Behaviors[i];
                if (slot.SlotIndex is < 0 or >= 32)
                {
                    continue;
                }

                uint bit = 1u << slot.SlotIndex;
                switch (slot.Kind)
                {
                    case BehaviorKind.AssetBinding:
                        AssetBindingSlotMask |= bit;
                        HasAssetBindingBehavior = true;
                        RequiresBootstrapProcessing = true;
                        break;

                    case BehaviorKind.Animator:
                        AnimatorSlotMask |= bit;
                        HasAnimatorBehavior = true;
                        break;

                    case BehaviorKind.AttributeBinding:
                    case BehaviorKind.TagBinding:
                    case BehaviorKind.Attachment:
                    case BehaviorKind.Sound:
                    case BehaviorKind.Material:
                    case BehaviorKind.Spline:
                        RequiresBootstrapProcessing = true;
                        break;
                }
            }
        }

        private static int ResolveAttributeId(in ValueRef value)
        {
            return value.Source switch
            {
                ValueSourceKind.Attribute => value.SourceId,
                ValueSourceKind.AttributeRatio => value.SourceId,
                ValueSourceKind.AttributeBase => value.SourceId,
                _ => -1,
            };
        }
    }
}
