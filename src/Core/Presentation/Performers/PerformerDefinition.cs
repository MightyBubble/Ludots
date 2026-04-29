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
        internal bool UsesStableVisualCache;
        internal bool UsesEventDrivenStaticEmit;
        internal bool UsesRetainedPresentationRequest;
        internal bool NeedsByDefinitionIndex;
        internal bool NeedsByOwnerDefinitionIndex;
        internal bool HasOwnerAttributeBindingWork;
        internal bool HasOwnerTagBindingWork;
        internal bool SupportsSingleRequestReplay;
        internal OwnerAttributeWorkItem[] OwnerAttributeWork = System.Array.Empty<OwnerAttributeWorkItem>();
        internal OwnerTagWorkItem[] OwnerTagWork = System.Array.Empty<OwnerTagWorkItem>();
        internal int AnimationProfileId;
        internal int PrimaryAssetBehaviorIndex;
        internal int[] AssetBehaviorIndices = System.Array.Empty<int>();
        internal int[] CacheableAssetBehaviorIndices = System.Array.Empty<int>();
        internal int[] TickBehaviorIndices = System.Array.Empty<int>();
        internal int[] MaterialBehaviorIndices = System.Array.Empty<int>();
        internal int[] MaterialSourceFloatParamKeys = System.Array.Empty<int>();
        internal int[] StaticVisualFloatParamKeys = System.Array.Empty<int>();
        internal int[] StaticVisualIntParamKeys = System.Array.Empty<int>();
        internal int[] StaticVisualVectorParamKeys = System.Array.Empty<int>();
        internal bool SupportsSingleVisualProxyFastEmit;
        internal int SingleVisualProxyFastBehaviorIndex;
        internal bool SupportsSingleAnimatorFastUpdate;
        internal int SingleAnimatorFastBehaviorIndex;

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
            UsesStableVisualCache = false;
            UsesEventDrivenStaticEmit = false;
            UsesRetainedPresentationRequest = false;
            NeedsByDefinitionIndex = Rules != null && Rules.Length > 0;
            NeedsByOwnerDefinitionIndex = NeedsByDefinitionIndex || (Bindings != null && Bindings.Length > 0);
            HasOwnerAttributeBindingWork = false;
            HasOwnerTagBindingWork = false;
            SupportsSingleRequestReplay = false;
            OwnerAttributeWork = System.Array.Empty<OwnerAttributeWorkItem>();
            OwnerTagWork = System.Array.Empty<OwnerTagWorkItem>();
            AnimationProfileId = 0;
            PrimaryAssetBehaviorIndex = -1;
            AssetBehaviorIndices = System.Array.Empty<int>();
            CacheableAssetBehaviorIndices = System.Array.Empty<int>();
            TickBehaviorIndices = System.Array.Empty<int>();
            MaterialBehaviorIndices = System.Array.Empty<int>();
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
            var staticFloatParams = new System.Collections.Generic.HashSet<int>();
            var staticIntParams = new System.Collections.Generic.HashSet<int>();
            var staticVectorParams = new System.Collections.Generic.HashSet<int>();
            var materialSourceFloatParams = new System.Collections.Generic.HashSet<int>();
            System.Collections.Generic.List<int>? materialBehaviorIndices = null;
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
                    }
                }
            }

            if (Behaviors == null || Behaviors.Length == 0)
            {
                OwnerAttributeWork = BuildOwnerAttributeWork(attributeParamBindingMap, attributeBehaviorMap);
                OwnerTagWork = BuildOwnerTagWork(tagBehaviorMap);
                UsesRetainedPresentationRequest =
                    HasSurfaceAuthoring &&
                    DefaultLifetime <= 0f &&
                    PositionYDriftPerSecond == 0f &&
                    VisibilityCondition.GraphProgramId <= 0;
                return;
            }

            System.Collections.Generic.List<int>? assetBehaviorIndices = null;
            System.Collections.Generic.List<int>? cacheableAssetBehaviorIndices = null;
            System.Collections.Generic.List<int>? tickBehaviorIndices = null;
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
                                hasCacheableVisual = true;
                                cacheableAssetBehaviorIndices ??= new System.Collections.Generic.List<int>(4);
                                cacheableAssetBehaviorIndices.Add(i);
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
                        if (slot.Grounding.Mode != GroundingMode.None &&
                            slot.Grounding.UpdatePolicy == GroundingUpdatePolicy.EveryFrame)
                        {
                            tickBehaviorIndices ??= new System.Collections.Generic.List<int>(4);
                            tickBehaviorIndices.Add(i);
                            blocksEventDrivenStaticEmit = true;
                            hasStaticOnlyVisuals = false;
                        }
                        if (slot.Grounding.Mode != GroundingMode.None &&
                            slot.Grounding.UpdatePolicy == GroundingUpdatePolicy.Once)
                        {
                            RequiresBootstrapProcessing = true;
                        }
                        break;
                    case BehaviorKind.Sound:
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
                }
            }

            AssetBehaviorIndices = assetBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
            CacheableAssetBehaviorIndices = cacheableAssetBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
            TickBehaviorIndices = tickBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
            MaterialBehaviorIndices = materialBehaviorIndices?.ToArray() ?? System.Array.Empty<int>();
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
            SupportsSingleVisualProxyFastEmit =
                AssetBehaviorIndices.Length == 1 &&
                SupportsSingleVisualProxyFastEmitFor(Behaviors[AssetBehaviorIndices[0]].AssetBinding);
            SingleVisualProxyFastBehaviorIndex = SupportsSingleVisualProxyFastEmit
                ? AssetBehaviorIndices[0]
                : -1;
            SupportsSingleAnimatorFastUpdate =
                HasAnimatorBehavior &&
                AnimatorSlotMask != 0u &&
                IsPowerOfTwo(AnimatorSlotMask);
            SingleAnimatorFastBehaviorIndex = SupportsSingleAnimatorFastUpdate
                ? FindBehaviorIndexForSlot(Behaviors, TrailingZeroCount(AnimatorSlotMask), BehaviorKind.Animator)
                : -1;
            SupportsSingleAnimatorFastUpdate &= SingleAnimatorFastBehaviorIndex >= 0;
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

        private static bool SupportsSingleVisualProxyFastEmitFor(in AssetBindingConfig asset)
        {
            return asset.AssetKind is AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX &&
                   asset.ScaleParamKey < 0 &&
                   asset.ColorParamKey < 0 &&
                   asset.MaterialParamKey < 0 &&
                   asset.AssetSwapParamKey < 0 &&
                   asset.VisibilityParamKey < 0 &&
                   asset.LocalOffset == Vector3.Zero &&
                   asset.LocalRotation == Quaternion.Identity &&
                   asset.LocalScale == Vector3.One &&
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
                   asset.LocalScale != Vector3.One;
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
            AddIfValid(intParams, asset.AssetSwapParamKey);
            AddIfValid(intParams, asset.VisibilityParamKey);
            AddIfValid(vectorParams, asset.ColorParamKey);
        }

        private static void CollectRetainedPresentationRequestParams(
            System.Collections.Generic.HashSet<int> floatParams,
            System.Collections.Generic.HashSet<int> intParams,
            System.Collections.Generic.HashSet<int> vectorParams,
            in AssetBindingConfig asset)
        {
            AddIfValid(floatParams, asset.ScaleParamKey);
            AddIfValid(floatParams, asset.MaterialParamKey);
            AddIfValid(intParams, asset.AssetSwapParamKey);
            AddIfValid(intParams, asset.VisibilityParamKey);
            AddIfValid(vectorParams, asset.ColorParamKey);
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
