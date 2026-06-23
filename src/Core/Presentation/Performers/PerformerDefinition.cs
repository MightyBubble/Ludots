using System.Numerics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Instancing;

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
    /// not through visual-kind side channels.
    /// </summary>
    public sealed class PerformerDefinition
    {
        internal readonly struct OwnerAttributeWorkItem
        {
            public readonly int AttributeId;
            public readonly int[] ParamBindingIndices;
            public readonly int[] BehaviorIndices;

            public OwnerAttributeWorkItem(int attributeId, int[] paramBindingIndices, int[] behaviorIndices)
            {
                AttributeId = attributeId;
                ParamBindingIndices = paramBindingIndices ?? System.Array.Empty<int>();
                BehaviorIndices = behaviorIndices ?? System.Array.Empty<int>();
            }
        }

        internal readonly struct OwnerTagWorkItem
        {
            public readonly int TagId;
            public readonly int[] BehaviorIndices;

            public OwnerTagWorkItem(int tagId, int[] behaviorIndices)
            {
                TagId = tagId;
                BehaviorIndices = behaviorIndices ?? System.Array.Empty<int>();
            }
        }

        internal readonly struct MinimapMarkerWorkItem
        {
            public readonly int SlotIndex;
            public readonly MinimapMarkerConfig Marker;

            public MinimapMarkerWorkItem(int slotIndex, in MinimapMarkerConfig marker)
            {
                SlotIndex = slotIndex;
                Marker = marker;
            }
        }

        public int Id;
        public string Key = string.Empty;
        public string Extends = string.Empty;
        public ChildPerformerRef[] Children = System.Array.Empty<ChildPerformerRef>();
        public BehaviorSlot[] Behaviors = System.Array.Empty<BehaviorSlot>();
        public InstancedBatchBinding[] InstancedBatches = System.Array.Empty<InstancedBatchBinding>();
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
        public int[] RequiredAttributeIds = System.Array.Empty<int>();

        /// <summary>
        /// Transitional format hint for adapters that still consume Id1/Value0/Value1.
        /// The adapter-neutral runtime contract lives in <see cref="PresentationTextPacket"/>.
        /// </summary>
        public WorldHudValueMode WorldTextMode = WorldHudValueMode.None;

        internal int[] BindingIndex = System.Array.Empty<int>();
        internal uint AssetBindingSlotMask;
        internal uint AnimatorSlotMask;
        internal bool HasAssetBindingBehavior;
        internal bool HasInstancedBatchBindings;
        internal bool HasAnimatorBehavior;
        internal bool HasSoundBehavior;
        internal bool HasMinimapMarkerBehavior;
        internal bool HasSurfaceAuthoring;
        internal bool RequiresBootstrapProcessing;
        internal bool UsesStableVisualCache;
        internal bool UsesEventDrivenStaticEmit;
        internal bool UsesRetainedPresentationRequest;
        internal bool NeedsRetainedPresentationRequestLifecycleTick;
        internal bool NeedsByDefinitionIndex;
        internal bool NeedsByOwnerDefinitionIndex;
        internal bool HasOwnerAttributeBindingWork;
        internal bool HasOwnerTagBindingWork;
        internal bool HasOwnerFacingBindingWork;
        internal bool SupportsSingleRequestReplay;
        internal bool SupportsVisualProxyFastEmit;
        internal OwnerAttributeWorkItem[] OwnerAttributeWork = System.Array.Empty<OwnerAttributeWorkItem>();
        internal OwnerTagWorkItem[] OwnerTagWork = System.Array.Empty<OwnerTagWorkItem>();
        internal int[] OwnerFacingParamBindingIndices = System.Array.Empty<int>();
        internal int AnimationProfileId;
        internal int PrimaryAssetBehaviorIndex;
        internal int[] AssetBehaviorIndices = System.Array.Empty<int>();
        internal int[] CacheableAssetBehaviorIndices = System.Array.Empty<int>();
        internal int[] TickBehaviorIndices = System.Array.Empty<int>();
        internal int[] BootstrapGroundingBehaviorIndices = System.Array.Empty<int>();
        internal int[] MaterialBehaviorIndices = System.Array.Empty<int>();
        internal int[] MinimapMarkerBehaviorIndices = System.Array.Empty<int>();
        internal MinimapMarkerWorkItem[] MinimapMarkerWorkItems = System.Array.Empty<MinimapMarkerWorkItem>();
        internal bool HasEveryFrameGroundingWork;
        internal bool TickBehaviorsAreGroundingOnly;
        internal int[] MaterialSourceFloatParamKeys = System.Array.Empty<int>();
        internal int[] StaticVisualFloatParamKeys = System.Array.Empty<int>();
        internal int[] StaticVisualIntParamKeys = System.Array.Empty<int>();
        internal int[] StaticVisualVectorParamKeys = System.Array.Empty<int>();
        internal bool SupportsSingleVisualProxyFastEmit;
        internal int SingleVisualProxyFastBehaviorIndex;
        internal bool SupportsSingleAnimatorFastUpdate;
        internal int SingleAnimatorFastBehaviorIndex;
        internal bool SupportsFastParentAttachmentTick;
        internal int FastParentAttachmentBehaviorIndex;
        internal int[] SparseBindingParamKeys = System.Array.Empty<int>();
        internal int[] SparseBindingIndices = System.Array.Empty<int>();

        internal void BuildBindingIndex()
        {
            if (Bindings == null || Bindings.Length == 0)
            {
                BindingIndex = System.Array.Empty<int>();
                SparseBindingParamKeys = System.Array.Empty<int>();
                SparseBindingIndices = System.Array.Empty<int>();
                return;
            }

            const int DenseBindingIndexLimit = 1024;
            int maxDenseKey = -1;
            int sparseCount = 0;
            for (int i = 0; i < Bindings.Length; i++)
            {
                int key = Bindings[i].ParamKey;
                if (key is >= 0 and < DenseBindingIndexLimit)
                {
                    if (key > maxDenseKey)
                    {
                        maxDenseKey = key;
                    }
                }
                else if (key >= DenseBindingIndexLimit)
                {
                    sparseCount++;
                }
            }

            int[] index = maxDenseKey >= 0 ? new int[maxDenseKey + 1] : System.Array.Empty<int>();
            System.Array.Fill(index, -1);
            int[] sparseKeys = sparseCount == 0 ? System.Array.Empty<int>() : new int[sparseCount];
            int[] sparseIndices = sparseCount == 0 ? System.Array.Empty<int>() : new int[sparseCount];
            int sparseCursor = 0;
            for (int i = 0; i < Bindings.Length; i++)
            {
                int key = Bindings[i].ParamKey;
                if (key >= 0 && key < index.Length)
                {
                    index[key] = i;
                }
                else if (key >= DenseBindingIndexLimit)
                {
                    sparseKeys[sparseCursor] = key;
                    sparseIndices[sparseCursor] = i;
                    sparseCursor++;
                }
            }

            SortSparseBindings(sparseKeys, sparseIndices);
            BindingIndex = index;
            SparseBindingParamKeys = sparseKeys;
            SparseBindingIndices = sparseIndices;
        }

        private static void SortSparseBindings(int[] keys, int[] indices)
        {
            for (int i = 1; i < keys.Length; i++)
            {
                int key = keys[i];
                int index = indices[i];
                int j = i - 1;
                while (j >= 0 && keys[j] > key)
                {
                    keys[j + 1] = keys[j];
                    indices[j + 1] = indices[j];
                    j--;
                }

                keys[j + 1] = key;
                indices[j + 1] = index;
            }
        }

        internal bool TryGetBindingIndex(int paramKey, out int bindingIndex)
        {
            if (paramKey >= 0 && paramKey < BindingIndex.Length)
            {
                bindingIndex = BindingIndex[paramKey];
                return bindingIndex >= 0;
            }

            int sparseIndex = System.Array.BinarySearch(SparseBindingParamKeys, paramKey);
            if (sparseIndex >= 0)
            {
                bindingIndex = SparseBindingIndices[sparseIndex];
                return true;
            }

            bindingIndex = -1;
            return false;
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
            HasInstancedBatchBindings = InstancedBatches != null && InstancedBatches.Length != 0;
            HasAnimatorBehavior = false;
            HasSoundBehavior = false;
            HasMinimapMarkerBehavior = false;
            HasSurfaceAuthoring = Surface != null;
            RequiresBootstrapProcessing = (Bindings != null && Bindings.Length > 0) || HasSurfaceAuthoring || HasInstancedBatchBindings;
            UsesStableVisualCache = false;
            UsesEventDrivenStaticEmit = false;
            UsesRetainedPresentationRequest = false;
            NeedsByDefinitionIndex = Rules != null && Rules.Length > 0;
            NeedsByOwnerDefinitionIndex = NeedsByDefinitionIndex;
            HasOwnerAttributeBindingWork = false;
            HasOwnerTagBindingWork = false;
            HasOwnerFacingBindingWork = false;
            SupportsSingleRequestReplay = false;
            SupportsVisualProxyFastEmit = false;
            OwnerAttributeWork = System.Array.Empty<OwnerAttributeWorkItem>();
            OwnerTagWork = System.Array.Empty<OwnerTagWorkItem>();
            OwnerFacingParamBindingIndices = System.Array.Empty<int>();
            AnimationProfileId = 0;
            PrimaryAssetBehaviorIndex = -1;
            AssetBehaviorIndices = System.Array.Empty<int>();
            CacheableAssetBehaviorIndices = System.Array.Empty<int>();
            TickBehaviorIndices = System.Array.Empty<int>();
            BootstrapGroundingBehaviorIndices = System.Array.Empty<int>();
            MaterialBehaviorIndices = System.Array.Empty<int>();
            MinimapMarkerBehaviorIndices = System.Array.Empty<int>();
            MinimapMarkerWorkItems = System.Array.Empty<MinimapMarkerWorkItem>();
            HasEveryFrameGroundingWork = false;
            TickBehaviorsAreGroundingOnly = false;
            MaterialSourceFloatParamKeys = System.Array.Empty<int>();
            StaticVisualFloatParamKeys = System.Array.Empty<int>();
            StaticVisualIntParamKeys = System.Array.Empty<int>();
            StaticVisualVectorParamKeys = System.Array.Empty<int>();
            SupportsSingleVisualProxyFastEmit = false;
            SingleVisualProxyFastBehaviorIndex = -1;
            SupportsSingleAnimatorFastUpdate = false;
            SingleAnimatorFastBehaviorIndex = -1;
            System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>? attributeParamBindingMap = null;
            System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>? attributeBehaviorMap = null;
            System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>? tagBehaviorMap = null;
            System.Collections.Generic.List<int>? ownerFacingParamBindingIndices = null;
            var staticFloatParams = new System.Collections.Generic.HashSet<int>();
            var staticIntParams = new System.Collections.Generic.HashSet<int>();
            var staticVectorParams = new System.Collections.Generic.HashSet<int>();
            var materialSourceFloatParams = new System.Collections.Generic.HashSet<int>();
            System.Collections.Generic.List<int>? materialBehaviorIndices = null;
            System.Collections.Generic.List<int>? minimapMarkerBehaviorIndices = null;
            System.Collections.Generic.List<MinimapMarkerWorkItem>? minimapMarkerWorkItems = null;
            bool blocksEventDrivenStaticEmit = HasSurfaceAuthoring;

            if (Bindings != null)
            {
                for (int i = 0; i < Bindings.Length; i++)
                {
                    switch (Bindings[i].Value.Source)
                    {
                        case ValueSourceKind.Attribute:
                        case ValueSourceKind.AttributeRatio:
                        case ValueSourceKind.AttributeBase:
                            HasOwnerAttributeBindingWork = true;
                            attributeParamBindingMap ??= new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
                            AddIndex(attributeParamBindingMap, Bindings[i].Value.SourceId, i);
                            break;
                        case ValueSourceKind.FacingRadians:
                        case ValueSourceKind.FacingDegrees:
                            HasOwnerFacingBindingWork = true;
                            ownerFacingParamBindingIndices ??= new System.Collections.Generic.List<int>(2);
                            ownerFacingParamBindingIndices.Add(i);
                            break;
                    }
                }
            }

            OwnerFacingParamBindingIndices = ownerFacingParamBindingIndices?.ToArray() ?? System.Array.Empty<int>();
            if (Behaviors == null || Behaviors.Length == 0)
            {
                OwnerAttributeWork = BuildOwnerAttributeWork(attributeParamBindingMap, attributeBehaviorMap);
                OwnerTagWork = BuildOwnerTagWork(tagBehaviorMap);
                UsesRetainedPresentationRequest =
                    HasSurfaceAuthoring &&
                    DefaultLifetime <= 0f &&
                    PositionYDriftPerSecond == 0f &&
                    VisibilityCondition.GraphProgramId <= 0;
                NeedsRetainedPresentationRequestLifecycleTick = UsesRetainedPresentationRequest;
                return;
            }

            System.Collections.Generic.List<int>? assetBehaviorIndices = null;
            System.Collections.Generic.List<int>? cacheableAssetBehaviorIndices = null;
            System.Collections.Generic.List<int>? tickBehaviorIndices = null;
            System.Collections.Generic.List<int>? bootstrapGroundingBehaviorIndices = null;
            bool hasCacheableVisual = false;
            bool hasDynamicVisualLane = false;
            bool hasStaticOnlyVisuals = true;
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
                        if (PrimaryAssetBehaviorIndex < 0)
                        {
                            PrimaryAssetBehaviorIndex = i;
                        }

                        if (AssetBindingNeedsBootstrapProcessing(slot.AssetBinding))
                        {
                            RequiresBootstrapProcessing = true;
                        }

                        assetBehaviorIndices ??= new System.Collections.Generic.List<int>(4);
                        assetBehaviorIndices.Add(i);
                        switch (slot.AssetBinding.AssetKind)
                        {
                            case AssetKind.Mesh:
                            case AssetKind.SkinnedMesh:
                            case AssetKind.Decal:
                            case AssetKind.VFX:
                            case AssetKind.Surface:
                                if (AssetBindingSupportsEventDrivenStaticEmit(slot.AssetBinding))
                                {
                                    hasCacheableVisual = true;
                                    cacheableAssetBehaviorIndices ??= new System.Collections.Generic.List<int>(4);
                                    cacheableAssetBehaviorIndices.Add(i);
                                }
                                else
                                {
                                    hasDynamicVisualLane = true;
                                    hasStaticOnlyVisuals = false;
                                }

                                hasStaticOnlyVisuals &= AssetBindingSupportsEventDrivenStaticEmit(slot.AssetBinding);
                                CollectStaticVisualParams(staticFloatParams, staticIntParams, staticVectorParams, slot.AssetBinding);
                                break;

                            case AssetKind.WorldHud:
                            case AssetKind.WorldText:
                            case AssetKind.Spline:
                            case AssetKind.GroundOverlay:
                                hasDynamicVisualLane = true;
                                hasStaticOnlyVisuals = false;
                                CollectRetainedPresentationRequestParams(
                                    staticFloatParams,
                                    staticIntParams,
                                    staticVectorParams,
                                    slot.AssetBinding);
                                break;

                            default:
                                hasStaticOnlyVisuals = false;
                                break;
                        }
                        break;

                    case BehaviorKind.Animator:
                        AnimatorSlotMask |= bit;
                        HasAnimatorBehavior = true;
                        blocksEventDrivenStaticEmit = true;
                        hasStaticOnlyVisuals = false;
                        if (AnimationProfileId == 0)
                        {
                            AnimationProfileId = slot.Animator.AnimationProfileId;
                        }
                        break;

                    case BehaviorKind.AttributeBinding:
                        HasOwnerAttributeBindingWork = true;
                        RequiresBootstrapProcessing = true;
                        NeedsByOwnerDefinitionIndex = true;
                        attributeBehaviorMap ??= new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
                        AddIndex(attributeBehaviorMap, slot.AttributeBinding.AttributeId, i);
                        break;
                    case BehaviorKind.TagBinding:
                        HasOwnerTagBindingWork = true;
                        RequiresBootstrapProcessing = true;
                        NeedsByOwnerDefinitionIndex = true;
                        tagBehaviorMap ??= new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
                        AddIndex(tagBehaviorMap, slot.TagBinding.TagId, i);
                        break;
                    case BehaviorKind.Attachment:
                        tickBehaviorIndices ??= new System.Collections.Generic.List<int>(4);
                        tickBehaviorIndices.Add(i);
                        if (slot.Attachment.Target != AttachmentTarget.Parent)
                        {
                            blocksEventDrivenStaticEmit = true;
                            hasStaticOnlyVisuals = false;
                        }
                        break;
                    case BehaviorKind.Grounding:
                        if (slot.Grounding.Mode == GroundingMode.None)
                        {
                            break;
                        }

                        if (slot.Grounding.UpdatePolicy == GroundingUpdatePolicy.EveryFrame)
                        {
                            tickBehaviorIndices ??= new System.Collections.Generic.List<int>(4);
                            tickBehaviorIndices.Add(i);
                            HasEveryFrameGroundingWork = true;
                            blocksEventDrivenStaticEmit = true;
                            hasStaticOnlyVisuals = false;
                            RequiresBootstrapProcessing = true;
                            bootstrapGroundingBehaviorIndices ??= new System.Collections.Generic.List<int>(2);
                            bootstrapGroundingBehaviorIndices.Add(i);
                            break;
                        }

                        RequiresBootstrapProcessing = true;
                        bootstrapGroundingBehaviorIndices ??= new System.Collections.Generic.List<int>(2);
                        bootstrapGroundingBehaviorIndices.Add(i);
                        break;
                    case BehaviorKind.Sound:
                        HasSoundBehavior = true;
                        tickBehaviorIndices ??= new System.Collections.Generic.List<int>(4);
                        tickBehaviorIndices.Add(i);
                        blocksEventDrivenStaticEmit = true;
                        hasStaticOnlyVisuals = false;
                        break;
                    case BehaviorKind.Spline:
                        tickBehaviorIndices ??= new System.Collections.Generic.List<int>(4);
                        tickBehaviorIndices.Add(i);
                        blocksEventDrivenStaticEmit = true;
                        hasStaticOnlyVisuals = false;
                        break;
                    case BehaviorKind.Material:
                        RequiresBootstrapProcessing = true;
                        materialBehaviorIndices ??= new System.Collections.Generic.List<int>(2);
                        materialBehaviorIndices.Add(i);
                        AddIfValid(materialSourceFloatParams, slot.Material.MaterialSwapParamKey);
                        break;
                    case BehaviorKind.MinimapMarker:
                        HasMinimapMarkerBehavior = true;
                        minimapMarkerBehaviorIndices ??= new System.Collections.Generic.List<int>(2);
                        minimapMarkerBehaviorIndices.Add(i);
                        minimapMarkerWorkItems ??= new System.Collections.Generic.List<MinimapMarkerWorkItem>(2);
                        minimapMarkerWorkItems.Add(new MinimapMarkerWorkItem(slot.SlotIndex, in slot.MinimapMarker));
                        break;
                }
            }

            AssetBehaviorIndices = assetBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
            CacheableAssetBehaviorIndices = cacheableAssetBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
            TickBehaviorIndices = tickBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
            BootstrapGroundingBehaviorIndices = bootstrapGroundingBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
            MaterialBehaviorIndices = materialBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
            MinimapMarkerBehaviorIndices = minimapMarkerBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
            MinimapMarkerWorkItems = minimapMarkerWorkItems?.ToArray() ?? System.Array.Empty<MinimapMarkerWorkItem>();
            TickBehaviorsAreGroundingOnly = HasEveryFrameGroundingWork &&
                                           TickBehaviorIndices.Length != 0 &&
                                           TickBehaviorIndices.Length == CountEveryFrameGroundingTickBehaviors(Behaviors, TickBehaviorIndices);
            MaterialSourceFloatParamKeys = materialSourceFloatParams.Count == 0 ? System.Array.Empty<int>() : Sort(materialSourceFloatParams);
            StaticVisualFloatParamKeys = staticFloatParams.Count == 0 ? System.Array.Empty<int>() : Sort(staticFloatParams);
            StaticVisualIntParamKeys = staticIntParams.Count == 0 ? System.Array.Empty<int>() : Sort(staticIntParams);
            StaticVisualVectorParamKeys = staticVectorParams.Count == 0 ? System.Array.Empty<int>() : Sort(staticVectorParams);
            OwnerAttributeWork = BuildOwnerAttributeWork(attributeParamBindingMap, attributeBehaviorMap);
            OwnerTagWork = BuildOwnerTagWork(tagBehaviorMap);
            UsesStableVisualCache =
                hasCacheableVisual &&
                !hasDynamicVisualLane &&
                !blocksEventDrivenStaticEmit;
            UsesEventDrivenStaticEmit =
                UsesStableVisualCache &&
                hasStaticOnlyVisuals &&
                DefaultLifetime <= 0f &&
                PositionYDriftPerSecond == 0f &&
                VisibilityCondition.Inline == InlineConditionKind.None &&
                VisibilityCondition.GraphProgramId <= 0;
            SupportsSingleRequestReplay =
                AssetBehaviorIndices.Length == 1 &&
                SupportsReplayableSingleRequest(Behaviors[AssetBehaviorIndices[0]].AssetBinding.AssetKind);
            UsesRetainedPresentationRequest =
                (SupportsSingleRequestReplay || HasSurfaceAuthoring) &&
                DefaultLifetime <= 0f &&
                PositionYDriftPerSecond == 0f &&
                VisibilityCondition.GraphProgramId <= 0;
            NeedsRetainedPresentationRequestLifecycleTick = UsesRetainedPresentationRequest;
            SupportsSingleVisualProxyFastEmit =
                AssetBehaviorIndices.Length == 1 &&
                SupportsVisualProxyFastEmitFor(Behaviors[AssetBehaviorIndices[0]].AssetBinding);
            SingleVisualProxyFastBehaviorIndex = SupportsSingleVisualProxyFastEmit
                ? AssetBehaviorIndices[0]
                : -1;
            SupportsVisualProxyFastEmit =
                AssetBehaviorIndices.Length != 0 &&
                SupportsVisualProxyFastEmitForAll(Behaviors, AssetBehaviorIndices);
            SupportsSingleAnimatorFastUpdate =
                HasAnimatorBehavior &&
                AnimatorSlotMask != 0u &&
                IsPowerOfTwo(AnimatorSlotMask);
            SingleAnimatorFastBehaviorIndex = SupportsSingleAnimatorFastUpdate
                ? FindBehaviorIndexForSlot(Behaviors, TrailingZeroCount(AnimatorSlotMask), BehaviorKind.Animator)
                : -1;
            SupportsSingleAnimatorFastUpdate &= SingleAnimatorFastBehaviorIndex >= 0;
            SupportsFastParentAttachmentTick =
                SupportsRetainedParentAttachmentFastTick(Behaviors, TickBehaviorIndices, out int fastParentAttachmentBehaviorIndex);
            FastParentAttachmentBehaviorIndex = fastParentAttachmentBehaviorIndex;
        }

        private static int FindBehaviorIndexForSlot(BehaviorSlot[] behaviors, int slotIndex, BehaviorKind kind)
        {
            if (behaviors == null || slotIndex < 0)
            {
                return -1;
            }

            for (int i = 0; i < behaviors.Length; i++)
            {
                if (behaviors[i].Kind == kind && behaviors[i].SlotIndex == slotIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsPowerOfTwo(uint value)
        {
            return value != 0u && (value & (value - 1u)) == 0u;
        }

        private static int TrailingZeroCount(uint value)
        {
            int count = 0;
            while ((value & 1u) == 0u)
            {
                count++;
                value >>= 1;
            }

            return count;
        }

        private static bool SupportsVisualProxyFastEmitForAll(BehaviorSlot[] behaviors, int[] assetBehaviorIndices)
        {
            for (int i = 0; i < assetBehaviorIndices.Length; i++)
            {
                if (!SupportsVisualProxyFastEmitFor(behaviors[assetBehaviorIndices[i]].AssetBinding))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SupportsVisualProxyFastEmitFor(in AssetBindingConfig asset)
        {
            return asset.AssetKind is AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX or AssetKind.Surface &&
                   asset.ScaleParamKey < 0 &&
                   asset.ColorParamKey < 0 &&
                   asset.MaterialParamKey < 0 &&
                   asset.AssetIdParamKey < 0 &&
                   asset.AssetSwapParamKey < 0 &&
                   asset.VisibilityParamKey < 0 &&
                   (asset.MaterialCustomData.Slots == null || asset.MaterialCustomData.Slots.Length == 0) &&
                   !HasAssetSwapTable(asset);
        }

        private static bool HasAssetSwapTable(in AssetBindingConfig asset)
        {
            return asset.AssetSwapTable != null && asset.AssetSwapTable.Length != 0;
        }

        private static bool SupportsReplayableSingleRequest(AssetKind kind)
        {
            return kind is AssetKind.WorldHud or AssetKind.WorldText or AssetKind.Spline or AssetKind.GroundOverlay;
        }

        private static bool SupportsRetainedParentAttachmentFastTick(
            BehaviorSlot[] behaviors,
            int[] tickBehaviorIndices,
            out int behaviorIndex)
        {
            behaviorIndex = -1;
            if (behaviors == null ||
                tickBehaviorIndices == null ||
                tickBehaviorIndices.Length != 1)
            {
                return false;
            }

            int candidateIndex = tickBehaviorIndices[0];
            if ((uint)candidateIndex >= (uint)behaviors.Length)
            {
                return false;
            }

            ref readonly BehaviorSlot slot = ref behaviors[candidateIndex];
            if (slot.Kind != BehaviorKind.Attachment ||
                slot.Attachment.Target != AttachmentTarget.Parent)
            {
                return false;
            }

            behaviorIndex = candidateIndex;
            return true;
        }

        private static int CountEveryFrameGroundingTickBehaviors(BehaviorSlot[] behaviors, int[] tickBehaviorIndices)
        {
            int count = 0;
            for (int i = 0; i < tickBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[tickBehaviorIndices[i]];
                if (slot.Kind == BehaviorKind.Grounding &&
                    slot.Grounding.Mode != GroundingMode.None &&
                    slot.Grounding.UpdatePolicy == GroundingUpdatePolicy.EveryFrame)
                {
                    count++;
                }
            }

            return count;
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

        private static bool AssetBindingNeedsBootstrapProcessing(in AssetBindingConfig asset)
        {
            return asset.LocalOffset != Vector3.Zero ||
                   asset.LocalRotation != Quaternion.Identity ||
                   (asset.LocalScale != Vector3.One && !AssetBindingUsesRetainedPresentationScale(asset.AssetKind));
        }

        private static bool AssetBindingUsesRetainedPresentationScale(AssetKind kind)
        {
            return kind is AssetKind.WorldHud or AssetKind.WorldText or AssetKind.Spline or AssetKind.GroundOverlay;
        }

        private static bool AssetBindingSupportsEventDrivenStaticEmit(in AssetBindingConfig asset)
        {
            return asset.Mobility == Components.VisualMobility.Static;
        }

        internal bool AffectsStaticVisualParam(int paramKey, ParamLane lane)
        {
            if (paramKey < 0)
            {
                return false;
            }

            return lane switch
            {
                ParamLane.Float => Contains(StaticVisualFloatParamKeys, paramKey),
                ParamLane.Int => Contains(StaticVisualIntParamKeys, paramKey),
                ParamLane.Vector => Contains(StaticVisualVectorParamKeys, paramKey),
                _ => false,
            };
        }

        internal bool AffectsMaterialSourceParam(int paramKey, ParamLane lane)
        {
            return lane == ParamLane.Float &&
                   paramKey >= 0 &&
                   Contains(MaterialSourceFloatParamKeys, paramKey);
        }

        private static void CollectStaticVisualParams(
            System.Collections.Generic.HashSet<int> floatParams,
            System.Collections.Generic.HashSet<int> intParams,
            System.Collections.Generic.HashSet<int> vectorParams,
            in AssetBindingConfig asset)
        {
            AddIfValid(floatParams, asset.ScaleParamKey);
            AddIfValid(intParams, asset.MaterialParamKey);
            AddIfValid(intParams, asset.AssetIdParamKey);
            AddIfValid(intParams, asset.AssetSwapParamKey);
            AddIfValid(intParams, asset.VisibilityParamKey);
            AddIfValid(vectorParams, asset.ColorParamKey);
            CollectMaterialCustomDataParams(floatParams, intParams, vectorParams, in asset.MaterialCustomData);
        }

        private static void CollectRetainedPresentationRequestParams(
            System.Collections.Generic.HashSet<int> floatParams,
            System.Collections.Generic.HashSet<int> intParams,
            System.Collections.Generic.HashSet<int> vectorParams,
            in AssetBindingConfig asset)
        {
            AddIfValid(floatParams, asset.ScaleParamKey);
            AddIfValid(floatParams, asset.MaterialParamKey);
            AddIfValid(intParams, asset.AssetIdParamKey);
            AddIfValid(intParams, asset.AssetSwapParamKey);
            AddIfValid(intParams, asset.VisibilityParamKey);
            AddIfValid(vectorParams, asset.ColorParamKey);
            if (asset.AssetKind == AssetKind.GroundOverlay)
            {
                CollectGroundOverlayParams(floatParams);
            }

            CollectMaterialCustomDataParams(floatParams, intParams, vectorParams, in asset.MaterialCustomData);
        }

        private static void CollectGroundOverlayParams(System.Collections.Generic.HashSet<int> floatParams)
        {
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayRadius);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayInnerRadius);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayAngle);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayRotation);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayFillR);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayFillG);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayFillB);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayFillA);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayBorderR);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayBorderG);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayBorderB);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayBorderA);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayBorderWidth);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayLength);
            AddIfValid(floatParams, WellKnownPerformerParamKeys.OverlayWidth);
        }

        private static void CollectMaterialCustomDataParams(
            System.Collections.Generic.HashSet<int> floatParams,
            System.Collections.Generic.HashSet<int> intParams,
            System.Collections.Generic.HashSet<int> vectorParams,
            in MaterialCustomDataBinding materialCustomData)
        {
            MaterialCustomDataSlotBinding[] slots = materialCustomData.Slots;
            if (slots == null || slots.Length == 0)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                ref readonly MaterialCustomDataSlotBinding slot = ref slots[i];
                switch (slot.Lane)
                {
                    case MaterialCustomDataLane.Float:
                        AddIfValid(floatParams, slot.ParamKey);
                        break;
                    case MaterialCustomDataLane.Int:
                        AddIfValid(intParams, slot.ParamKey);
                        break;
                    case MaterialCustomDataLane.Vector:
                        AddIfValid(vectorParams, slot.ParamKey);
                        break;
                }
            }
        }

        private static void AddIfValid(System.Collections.Generic.HashSet<int> set, int key)
        {
            if (key >= 0)
            {
                set.Add(key);
            }
        }

        private static int[] Sort(System.Collections.Generic.HashSet<int> set)
        {
            int[] values = new int[set.Count];
            set.CopyTo(values);
            System.Array.Sort(values);
            return values;
        }

        private static bool Contains(int[] sortedKeys, int key)
        {
            return sortedKeys != null &&
                   sortedKeys.Length != 0 &&
                   System.Array.BinarySearch(sortedKeys, key) >= 0;
        }

        internal bool TryGetOwnerAttributeWork(int attributeId, out OwnerAttributeWorkItem work)
        {
            OwnerAttributeWorkItem[] entries = OwnerAttributeWork;
            int lo = 0;
            int hi = entries.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                ref readonly OwnerAttributeWorkItem candidate = ref entries[mid];
                if (candidate.AttributeId == attributeId)
                {
                    work = candidate;
                    return true;
                }

                if (candidate.AttributeId < attributeId)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            work = default;
            return false;
        }

        internal bool TryGetOwnerTagWork(int tagId, out OwnerTagWorkItem work)
        {
            OwnerTagWorkItem[] entries = OwnerTagWork;
            int lo = 0;
            int hi = entries.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                ref readonly OwnerTagWorkItem candidate = ref entries[mid];
                if (candidate.TagId == tagId)
                {
                    work = candidate;
                    return true;
                }

                if (candidate.TagId < tagId)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            work = default;
            return false;
        }

        private static void AddIndex(
            System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>> map,
            int key,
            int index)
        {
            if (!map.TryGetValue(key, out System.Collections.Generic.List<int>? indices))
            {
                indices = new System.Collections.Generic.List<int>(2);
                map[key] = indices;
            }

            indices.Add(index);
        }

        private static OwnerAttributeWorkItem[] BuildOwnerAttributeWork(
            System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>? paramBindingMap,
            System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>? behaviorMap)
        {
            if ((paramBindingMap == null || paramBindingMap.Count == 0) &&
                (behaviorMap == null || behaviorMap.Count == 0))
            {
                return System.Array.Empty<OwnerAttributeWorkItem>();
            }

            var keys = new System.Collections.Generic.HashSet<int>();
            if (paramBindingMap != null)
            {
                foreach (int key in paramBindingMap.Keys)
                {
                    keys.Add(key);
                }
            }

            if (behaviorMap != null)
            {
                foreach (int key in behaviorMap.Keys)
                {
                    keys.Add(key);
                }
            }

            int[] sortedKeys = new int[keys.Count];
            keys.CopyTo(sortedKeys);
            System.Array.Sort(sortedKeys);

            var items = new OwnerAttributeWorkItem[sortedKeys.Length];
            for (int i = 0; i < sortedKeys.Length; i++)
            {
                int key = sortedKeys[i];
                int[] paramBindingIndices = paramBindingMap != null && paramBindingMap.TryGetValue(key, out System.Collections.Generic.List<int>? paramsForKey)
                    ? paramsForKey.ToArray()
                    : System.Array.Empty<int>();
                int[] behaviorIndices = behaviorMap != null && behaviorMap.TryGetValue(key, out System.Collections.Generic.List<int>? behaviorsForKey)
                    ? behaviorsForKey.ToArray()
                    : System.Array.Empty<int>();
                items[i] = new OwnerAttributeWorkItem(key, paramBindingIndices, behaviorIndices);
            }

            return items;
        }

        private static OwnerTagWorkItem[] BuildOwnerTagWork(
            System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>? behaviorMap)
        {
            if (behaviorMap == null || behaviorMap.Count == 0)
            {
                return System.Array.Empty<OwnerTagWorkItem>();
            }

            int[] sortedKeys = new int[behaviorMap.Count];
            behaviorMap.Keys.CopyTo(sortedKeys, 0);
            System.Array.Sort(sortedKeys);

            var items = new OwnerTagWorkItem[sortedKeys.Length];
            for (int i = 0; i < sortedKeys.Length; i++)
            {
                int key = sortedKeys[i];
                items[i] = new OwnerTagWorkItem(key, behaviorMap[key].ToArray());
            }

            return items;
        }
    }
}
