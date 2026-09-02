using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    internal sealed class PresenterAssetEmitRuntime
    {
        private readonly World _world;
        private readonly PresenterEntityRuntime _runtime;
        private readonly PresentationRequestBuffer _requests;
        private readonly Dictionary<string, object> _globals;
        private readonly PresenterAnimatorStateBuffer _animatorStates;
        private readonly SoundRequestBuffer _soundRequests;
        private readonly PresenterVisualStableIdTable? _visualStableIds;
        private readonly WorldHudPresentBehavior _worldHudBehavior = new();

        public PresenterAssetEmitRuntime(
            World world,
            PresenterEntityRuntime runtime,
            PresentationRequestBuffer requests,
            Dictionary<string, object> globals,
            PresenterAnimatorStateBuffer animatorStates,
            SoundRequestBuffer soundRequests,
            PresenterVisualStableIdTable? visualStableIds = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _globals = globals ?? new Dictionary<string, object>();
            _animatorStates = animatorStates;
            _soundRequests = soundRequests;
            _visualStableIds = visualStableIds;
        }

        public void Emit(
            Entity entity,
            in PresenterState state,
            in PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            LODLevel lod,
            bool ownerCullVisible,
            Vector3 presenterWorldPosition,
            Quaternion presenterWorldRotation,
            in PresenterWorldFacing presenterWorldFacing,
            Vector3 presenterWorldScale,
            ref uint localOffsetConsumedMask)
        {
            PresenterLocalOffsetConsumption.MarkSlotConsumed(slot.SlotIndex, in asset, state.DefId, ref localOffsetConsumedMask);
            Vector3 position = ResolvePosition(in state, presenterWorldPosition, slot.Motion.YDriftPerSecond);
            float alpha = ResolveAlpha(in state, in definition, slot.Style.AlphaPolicy);
            if (!ownerCullVisible || !IsWithinMaxLod(lod, in asset) || !ResolveAssetVisibility(entity, in asset))
            {
                EmitHiddenSnapshotIfVisual(entity, in state, in definition, in slot, in asset, lod, position, presenterWorldRotation, presenterWorldScale, alpha);
                RemoveHiddenWorldHudIfNeeded(state.DefId, in state, in slot, in asset);
                RemoveHiddenLaneEntriesIfNeeded(in state, in slot, in asset);
                return;
            }

            switch (asset.AssetKind)
            {
                case AssetKind.Mesh:
                case AssetKind.SkinnedMesh:
                case AssetKind.Decal:
                case AssetKind.VFX:
                case AssetKind.Surface:
                    EmitVisualAsset(entity, in state, in definition, in slot, in asset, lod, position, presenterWorldRotation, presenterWorldScale, alpha);
                    return;

                case AssetKind.Sound:
                    EmitSoundAsset(in state, slot.SlotIndex, in asset, position);
                    return;

                case AssetKind.Spline:
                    EmitSplineAsset(entity, in state, in definition, in slot, in asset, lod, position, presenterWorldRotation, presenterWorldScale, alpha);
                    return;

                case AssetKind.WorldHud:
                    EmitWorldHudAsset(entity, state.DefId, in state, in definition, in slot, in asset, lod, position, presenterWorldScale, alpha);
                    return;

                case AssetKind.WorldText:
                    EmitWorldTextAsset(entity, state.DefId, in state, in definition, in slot, in asset, lod, position, presenterWorldScale, alpha);
                    return;

                case AssetKind.GroundOverlay:
                    EmitGroundOverlayAsset(entity, state.DefId, in state, in definition, in slot, in asset, lod, position, in presenterWorldFacing, presenterWorldScale, alpha);
                    return;

                default:
                    throw new InvalidOperationException($"Unsupported presenter asset kind '{asset.AssetKind}'.");
            }
        }

        public bool EmitStaticStableVisualDirect(
            Entity entity,
            in PresenterState state,
            in PresenterDefinition definition,
            LODLevel lod,
            bool ownerCullVisible,
            Vector3 presenterWorldPosition,
            Quaternion presenterWorldRotation,
            in PresenterWorldFacing presenterWorldFacing,
            Vector3 presenterWorldScale,
            StableDrawCache stableDrawCache,
            bool addOnly)
        {
            if (stableDrawCache == null)
            {
                throw new ArgumentNullException(nameof(stableDrawCache));
            }

            int[] assetBehaviorIndices = definition.AssetBehaviorIndices;
            if (assetBehaviorIndices.Length == 0)
            {
                return false;
            }

            bool emitted = false;
            bool removedAny = false;
            BehaviorSlot[] behaviors = definition.Behaviors;
            uint localOffsetConsumedMask = 0u;
            for (int i = 0; i < assetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[assetBehaviorIndices[i]];
                if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    continue;
                }

                ref readonly AssetBindingConfig asset = ref slot.AssetBinding;
                if (!ownerCullVisible ||
                    !IsWithinMaxLod(lod, in asset) ||
                    !ResolveAssetVisibility(entity, in asset))
                {
                    if (TryGetVisualStableId(in state, slot.SlotIndex, asset.AssetKind, state.DefId, out int removedStableId))
                    {
                        stableDrawCache.Remove(removedStableId);
                        removedAny = true;
                    }
                    continue;
                }

                PresenterLocalOffsetConsumption.MarkSlotConsumed(slot.SlotIndex, in asset, state.DefId, ref localOffsetConsumedMask);
                Vector3 position = ResolvePosition(in state, presenterWorldPosition, slot.Motion.YDriftPerSecond);
                VisualRenderPath renderPath = ResolveRenderPath(in asset);
                Quaternion rotation = ResolveRotation(in asset, presenterWorldRotation);
                Vector3 scale = ResolveScale(entity, in asset, presenterWorldScale);
                Vector3 assetPosition = ResolveAssetPosition(position, presenterWorldRotation, presenterWorldScale, in asset);
                Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, ResolveAuthoredColor(in slot)), ResolveAlpha(in state, in definition, slot.Style.AlphaPolicy));
                int stableId = GetOrAllocateVisualStableId(in state, slot.SlotIndex, asset.AssetKind, state.DefId);
                PresentationVisualProxy proxy = new PresentationVisualProxy
                {
                    ProxyKind = PresentationVisualProxyKind.Presenter,
                    MeshAssetId = ResolveAssetId(entity, in asset),
                    Position = assetPosition,
                    Rotation = rotation,
                    Scale = scale,
                    Color = color,
                    OwnerStableId = state.OwnerStableId,
                    StableId = stableId,
                    MaterialId = ResolveMaterialId(entity, in asset),
                    TemplateId = state.DefId,
                    AnimationProfileId = definition.AnimationProfileId,
                    RenderPath = renderPath,
                    AssetKind = asset.AssetKind,
                    SurfaceLayerKey = asset.SurfaceLayerKey,
                    SortId = asset.SortId,
                    MaterialCustomData = PresenterMaterialCustomDataResolver.Resolve(_runtime, entity, in asset.MaterialCustomData),
                    Mobility = asset.Mobility,
                    Flags = VisualRuntimeFlags.Visible,
                    Animator = ResolveAnimator(entity, renderPath),
                    AnimationOverlay = default,
                    Visibility = VisualVisibility.Visible,
                    LOD = lod,
                };
                if (addOnly)
                {
                    stableDrawCache.AddNew(proxy);
                }
                else
                {
                    stableDrawCache.Upsert(proxy);
                }
                emitted = true;
            }

            return emitted || removedAny;
        }

        public void RemoveStaticStableVisuals(
            in PresenterState state,
            in PresenterDefinition definition,
            StableDrawCache stableDrawCache)
        {
            if (stableDrawCache == null)
            {
                throw new ArgumentNullException(nameof(stableDrawCache));
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            int[] cacheableAssetBehaviorIndices = definition.CacheableAssetBehaviorIndices;
            for (int i = 0; i < cacheableAssetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[cacheableAssetBehaviorIndices[i]];
                if (TryGetVisualStableId(
                    in state,
                    slot.SlotIndex,
                    slot.AssetBinding.AssetKind,
                    state.DefId,
                    out int stableId))
                {
                    stableDrawCache.Remove(stableId);
                }
            }
        }

        public bool TryGetStaticStableVisualId(
            in PresenterState state,
            int slotIndex,
            AssetKind assetKind,
            int discriminator,
            out int stableId)
        {
            return TryGetVisualStableId(in state, slotIndex, assetKind, discriminator, out stableId);
        }

        private int GetOrAllocateVisualStableId(
            in PresenterState state,
            int slotIndex,
            AssetKind assetKind,
            int discriminator)
        {
            PresenterVisualStableIdTable table = _visualStableIds
                ?? throw new InvalidOperationException("Static presenter visual stable ids require PresenterVisualStableIdTable.");
            return table.GetOrAllocate(PresenterBehaviorRuntimeUtility.ComposeVisualStableKey(
                    state.StableId,
                    slotIndex,
                    assetKind,
                    discriminator));
        }

        private bool TryGetVisualStableId(
            in PresenterState state,
            int slotIndex,
            AssetKind assetKind,
            int discriminator,
            out int stableId)
        {
            if (_visualStableIds == null)
            {
                stableId = 0;
                return false;
            }

            return _visualStableIds.TryGet(
                PresenterBehaviorRuntimeUtility.ComposeVisualStableKey(
                    state.StableId,
                    slotIndex,
                    assetKind,
                    discriminator),
                out stableId);
        }

        public bool TryGetAnimatorPackedState(Entity entity, out AnimatorPackedState state)
        {
            if (_animatorStates == null)
            {
                state = default;
                return false;
            }

            return _animatorStates.TryGetPackedState(entity, out state);
        }

        public AnimatorPackedState GetAnimatorPackedStateBySlot(int slot)
        {
            if (_animatorStates == null || slot < 0)
            {
                return default;
            }

            return _animatorStates.GetPackedStateBySlot(slot);
        }

        public AnimationOverlayRequest GetAnimationOverlayBySlot(int slot)
        {
            if (_animatorStates == null || slot < 0)
            {
                return default;
            }

            return _animatorStates.GetOverlayBySlot(slot);
        }

        public bool TryGetAnimationOverlay(Entity entity, out AnimationOverlayRequest overlay)
        {
            if (_animatorStates == null || !_animatorStates.IsAllocated(entity))
            {
                overlay = default;
                return false;
            }

            overlay = _animatorStates.GetOverlay(entity);
            return true;
        }

        private void EmitVisualAsset(
            Entity entity,
            in PresenterState state,
            in PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            Quaternion presenterWorldRotation,
            Vector3 presenterWorldScale,
            float alpha)
        {
            PresentationVisualProxy proxy = BuildVisualProxy(
                entity,
                in state,
                in definition,
                in slot,
                in asset,
                lod,
                position,
                presenterWorldRotation,
                presenterWorldScale,
                alpha,
                VisualVisibility.Visible);
            _requests.AddVisualProxy(state.OwnerEntity, in proxy);
        }

        private void EmitHiddenSnapshotIfVisual(
            Entity entity,
            in PresenterState state,
            in PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            Quaternion presenterWorldRotation,
            Vector3 presenterWorldScale,
            float alpha)
        {
            if (asset.AssetKind is not (AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX or AssetKind.Surface))
            {
                return;
            }

            PresentationVisualProxy proxy = BuildVisualProxy(
                entity,
                in state,
                in definition,
                in slot,
                in asset,
                lod,
                position,
                presenterWorldRotation,
                presenterWorldScale,
                alpha,
                VisualVisibility.Hidden);
            _requests.AddVisualProxy(state.OwnerEntity, in proxy);
        }

        private void RemoveHiddenLaneEntriesIfNeeded(
            in PresenterState state,
            in BehaviorSlot slot,
            in AssetBindingConfig asset)
        {
            switch (asset.AssetKind)
            {
                case AssetKind.Spline:
                    _requests.RemoveSplineRibbon(
                        state.OwnerEntity,
                        PresenterBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, asset.AssetKind, state.DefId));
                    break;

                case AssetKind.GroundOverlay:
                    _requests.RemoveGroundOverlay(
                        state.OwnerEntity,
                        PresenterBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, asset.AssetKind, state.DefId));
                    break;
            }
        }

        private void RemoveHiddenWorldHudIfNeeded(
            int definitionId,
            in PresenterState state,
            in BehaviorSlot slot,
            in AssetBindingConfig asset)
        {
            WorldHudItemKind kind = asset.AssetKind switch
            {
                AssetKind.WorldHud => WorldHudItemKind.Bar,
                AssetKind.WorldText => WorldHudItemKind.Text,
                _ => default
            };

            if (kind == default)
            {
                return;
            }

            int stableId = HudItemIdentity.ComposePresenterStableId(state.StableId, kind, definitionId, slot.SlotIndex);
            _requests.RemoveWorldHud(state.OwnerEntity, stableId);
        }

        private void EmitSoundAsset(in PresenterState state, int slotIndex, in AssetBindingConfig asset, Vector3 position)
        {
            if (_soundRequests == null || asset.AssetId <= 0)
            {
                return;
            }

            _soundRequests.Add(new SoundRequest
            {
                Kind = SoundRequestKind.PlayOrUpdate,
                StableId = PresenterBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, slotIndex),
                SoundAssetId = asset.AssetId,
                Loop = true,
                Volume = 1f,
                WorldPosition = position,
                Owner = state.OwnerEntity,
            });
        }

        private void EmitSplineAsset(
            Entity entity,
            in PresenterState state,
            in PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            Quaternion presenterWorldRotation,
            Vector3 presenterWorldScale,
            float alpha)
        {
            Vector3 fallbackScale = ResolveScale(entity, in asset, presenterWorldScale);
            float authoredWidth = asset.ScaleParamKey >= 0
                ? RequireFloatParam(entity, asset.ScaleParamKey, "AssetBinding.scaleParamKey")
                : fallbackScale.X;
            float width = MathF.Max(
                ResolveOptionalFloatParam(entity, WellKnownPresenterParamKeys.SplineWidth, authoredWidth),
                0.001f);
            Vector4 authoredColor = ResolveColor(entity, in asset, ResolveAuthoredColor(in slot));
            Vector4 fillColor = ApplyAlpha(
                ResolveOptionalVectorParam(entity, WellKnownPresenterParamKeys.SplineFillColor, authoredColor),
                alpha);
            Vector4 borderColor = ApplyAlpha(
                ResolveOptionalVectorParam(entity, WellKnownPresenterParamKeys.SplineBorderColor, authoredColor),
                alpha);
            float borderWidth = MathF.Max(
                ResolveOptionalFloatParam(entity, WellKnownPresenterParamKeys.SplineBorderWidth, 0f),
                0f);
            Vector3 p0 = ResolveOptionalVector3Param(entity, WellKnownPresenterParamKeys.SplineP0, position);
            Vector3 defaultP3 = p0 + Vector3.Transform(Vector3.UnitZ * MathF.Max(width, 0.001f), VisualMath.NormalizeOrIdentity(presenterWorldRotation));
            Vector3 p3 = ResolveOptionalVector3Param(entity, WellKnownPresenterParamKeys.SplineP3, defaultP3);
            Vector3 p1 = ResolveOptionalVector3Param(entity, WellKnownPresenterParamKeys.SplineP1, Vector3.Lerp(p0, p3, 1f / 3f));
            Vector3 p2 = ResolveOptionalVector3Param(entity, WellKnownPresenterParamKeys.SplineP2, Vector3.Lerp(p0, p3, 2f / 3f));

            SplineRibbonRequest request = new SplineRibbonRequest
            {
                StableId = PresenterBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, asset.AssetKind, state.DefId),
                P0 = p0,
                P1 = p1,
                P2 = p2,
                P3 = p3,
                Width = width,
                FillColor = fillColor,
                BorderColor = borderColor,
                BorderWidth = borderWidth,
            };
            _requests.AddSplineRibbon(state.OwnerEntity, in request, lod);
        }

        private void EmitWorldHudAsset(Entity entity, int definitionId, in PresenterState state, in PresenterDefinition definition, in BehaviorSlot slot, in AssetBindingConfig asset, LODLevel lod, Vector3 position, Vector3 presenterWorldScale, float alpha)
        {
            if (TryGetRenderDebugState(out var debug) && !debug.DrawWorldHudBars)
            {
                return;
            }

            if (!_worldHudBehavior.TryResolveProjection(
                    _world,
                    _globals,
                    state.OwnerEntity,
                    lod,
                    WorldHudItemKind.Bar,
                    definition.RequiredAttributeIds,
                    out PresentPhaseResult phaseResult))
            {
                return;
            }

            Vector3 scale = ResolveScale(entity, in asset, presenterWorldScale);
            Vector4 foreground = ApplyAlpha(ResolveColor(entity, in asset, ResolveAuthoredColor(in slot)), alpha);
            Vector4 background = new Vector4(0.2f, 0.2f, 0.2f, foreground.W);
            float value = asset.MaterialParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.MaterialParamKey, "AssetBinding.materialParamKey")
                : 1f;
            float width = scale.X > 0f ? scale.X : 40f;
            float height = scale.Y > 0f ? scale.Y : 6f;

            WorldHudItem item = new WorldHudItem
            {
                StableId = HudItemIdentity.ComposePresenterStableId(state.StableId, WorldHudItemKind.Bar, definitionId, slot.SlotIndex),
                DirtySerial = HudItemIdentity.ComposeBarDirtySerial(width, height, value, background, foreground),
                Kind = WorldHudItemKind.Bar,
                WorldPosition = position,
                Value0 = value,
                Width = width,
                Height = height,
                Color0 = background,
                Color1 = foreground,
            };
            _requests.AddWorldHud(state.OwnerEntity, in item, phaseResult.LOD);
        }

        private void EmitWorldTextAsset(Entity entity, int definitionId, in PresenterState state, in PresenterDefinition definition, in BehaviorSlot slot, in AssetBindingConfig asset, LODLevel lod, Vector3 position, Vector3 presenterWorldScale, float alpha)
        {
            if (TryGetRenderDebugState(out var debug) && !debug.DrawWorldHudText)
            {
                return;
            }

            if (!_worldHudBehavior.TryResolveProjection(
                    _world,
                    _globals,
                    state.OwnerEntity,
                    lod,
                    WorldHudItemKind.Text,
                    definition.RequiredAttributeIds,
                    out PresentPhaseResult phaseResult))
            {
                return;
            }

            Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, ResolveAuthoredColor(in slot)), alpha);
            int tokenId = ResolveAssetId(entity, in asset);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"WorldText AssetBinding for presenter definition '{definition.Key}' resolved invalid asset id {tokenId}.");
            }

            float value0 = asset.ScaleParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.ScaleParamKey, "AssetBinding.scaleParamKey")
                : 0f;
            float value1 = asset.MaterialParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.MaterialParamKey, "AssetBinding.materialParamKey")
                : 0f;
            WorldHudValueMode valueMode = slot.WorldText.Mode;
            int fontSize = slot.WorldText.FontSize > 0 ? slot.WorldText.FontSize : 16;
            int stringTableId = valueMode == WorldHudValueMode.None ? tokenId : 0;
            PresentationTextPacket packet = PresentationTextPacket.FromWorldHudValueMode(tokenId, valueMode, value0, value1);

            WorldHudItem item = new WorldHudItem
            {
                StableId = HudItemIdentity.ComposePresenterStableId(state.StableId, WorldHudItemKind.Text, definitionId, slot.SlotIndex),
                DirtySerial = HudItemIdentity.ComposeTextDirtySerial(fontSize, stringTableId, (int)valueMode, value0, value1, color, packet),
                Kind = WorldHudItemKind.Text,
                WorldPosition = position,
                Value0 = value0,
                Value1 = value1,
                Id0 = stringTableId,
                Id1 = (int)valueMode,
                FontSize = fontSize,
                Color0 = color,
                Text = packet,
            };
            _requests.AddWorldHud(state.OwnerEntity, in item, phaseResult.LOD);
        }

        private void EmitGroundOverlayAsset(
            Entity entity,
            int definitionId,
            in PresenterState state,
            in PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            in PresenterWorldFacing presenterWorldFacing,
            Vector3 presenterWorldScale,
            float alpha)
        {
            Vector3 scale = ResolveScale(entity, in asset, presenterWorldScale);
            float radius = MathF.Max(
                ResolveOptionalFloatParam(entity, WellKnownPresenterParamKeys.OverlayRadius, scale.X),
                0.001f);
            float innerRadius = Math.Clamp(
                ResolveOptionalFloatParam(entity, WellKnownPresenterParamKeys.OverlayInnerRadius, scale.Y),
                0f,
                radius);
            float length = MathF.Max(
                ResolveOptionalFloatParam(entity, WellKnownPresenterParamKeys.OverlayLength, scale.X),
                0.001f);
            float width = MathF.Max(
                ResolveOptionalFloatParam(entity, WellKnownPresenterParamKeys.OverlayWidth, scale.Y),
                0.001f);
            float angle = MathF.Max(
                DegreesToRadians(ResolveOptionalFloatParam(entity, WellKnownPresenterParamKeys.OverlayAngle, 45f)),
                0f);
            float rotation = DegreesToRadians(ResolveOptionalFloatParam(
                entity,
                WellKnownPresenterParamKeys.OverlayRotation,
                RadiansToDegrees(ResolveGroundOverlayRotation(presenterWorldFacing))));
            GroundOverlayShape shape = ResolveGroundOverlayShape(asset.AssetId);
            Vector4 fallbackColor = ResolveColor(entity, in asset, ResolveAuthoredColor(in slot));
            Vector4 fillColor = ApplyAlpha(
                ResolveOptionalColorParams(
                    entity,
                    WellKnownPresenterParamKeys.OverlayFillR,
                    WellKnownPresenterParamKeys.OverlayFillG,
                    WellKnownPresenterParamKeys.OverlayFillB,
                    WellKnownPresenterParamKeys.OverlayFillA,
                    fallbackColor),
                alpha);
            Vector4 borderColor = ApplyAlpha(
                ResolveOptionalColorParams(
                    entity,
                    WellKnownPresenterParamKeys.OverlayBorderR,
                    WellKnownPresenterParamKeys.OverlayBorderG,
                    WellKnownPresenterParamKeys.OverlayBorderB,
                    WellKnownPresenterParamKeys.OverlayBorderA,
                    fallbackColor),
                alpha);
            float borderWidth = MathF.Max(
                ResolveOptionalFloatParam(entity, WellKnownPresenterParamKeys.OverlayBorderWidth, scale.Z),
                0f);

            GroundOverlayItem item = new GroundOverlayItem
            {
                StableId = PresenterBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, asset.AssetKind, definitionId),
                Shape = shape,
                Center = position,
                Radius = radius,
                InnerRadius = shape == GroundOverlayShape.Ring ? innerRadius : 0f,
                Angle = shape == GroundOverlayShape.Cone ? angle : 0f,
                Rotation = rotation,
                Length = shape == GroundOverlayShape.Line ? length : 0f,
                Width = shape == GroundOverlayShape.Line ? width : 0f,
                FillColor = fillColor,
                BorderColor = borderColor,
                BorderWidth = borderWidth,
            };
            _requests.AddGroundOverlay(state.OwnerEntity, in item, lod);
        }

        private bool ResolveAssetVisibility(Entity entity, in AssetBindingConfig asset)
        {
            return asset.VisibilityParamKey < 0 ||
                RequireIntParam(entity, asset.VisibilityParamKey, "AssetBinding.visibilityParamKey") != 0;
        }

        private int ResolveAssetId(Entity entity, in AssetBindingConfig asset)
        {
            if (asset.AssetIdParamKey >= 0)
            {
                if (!_runtime.TryResolveInt(entity, asset.AssetIdParamKey, out int assetId) || assetId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Presenter AssetBinding assetIdParamKey {asset.AssetIdParamKey} did not resolve to a registered asset id.");
                }

                return assetId;
            }

            if (asset.AssetSwapParamKey < 0)
            {
                return asset.AssetId;
            }

            if (!_runtime.TryResolveInt(entity, asset.AssetSwapParamKey, out int resolved))
            {
                throw new InvalidOperationException(
                    $"Presenter AssetBinding assetSwapParamKey {asset.AssetSwapParamKey} did not resolve to a swap value.");
            }

            AssetSwapEntry[] table = asset.AssetSwapTable ?? Array.Empty<AssetSwapEntry>();
            for (int i = 0; i < table.Length; i++)
            {
                ref readonly AssetSwapEntry entry = ref table[i];
                if (MathF.Abs(entry.ParamValue - resolved) <= 0.0001f)
                {
                    return entry.AssetId;
                }
            }

            throw new InvalidOperationException(
                $"Presenter AssetBinding assetSwapParamKey {asset.AssetSwapParamKey} resolved value {resolved} with no matching assetSwapTable entry.");
        }

        private int ResolveMaterialId(Entity entity, in AssetBindingConfig asset)
        {
            if (asset.MaterialParamKey < 0)
            {
                return asset.MaterialId;
            }

            int materialId = RequireIntParam(entity, asset.MaterialParamKey, "AssetBinding.materialParamKey");
            if (materialId <= 0)
            {
                throw new InvalidOperationException(
                    $"AssetBinding.materialParamKey {asset.MaterialParamKey} resolved invalid material id {materialId}.");
            }

            return materialId;
        }

        internal Vector3 ResolveScale(Entity entity, in AssetBindingConfig asset, Vector3 presenterWorldScale)
        {
            float scaleParamMultiplier = 1f;
            if (asset.ScaleParamKey >= 0)
            {
                scaleParamMultiplier = RequireFloatParam(entity, asset.ScaleParamKey, "AssetBinding.scaleParamKey");
            }

            return AssetBindingVisualScale.Resolve(in asset, presenterWorldScale, scaleParamMultiplier);
        }

        private static Quaternion ResolveRotation(in AssetBindingConfig asset, Quaternion presenterWorldRotation)
        {
            return WorldPlane2D.ResolveVisualAssetRotation(in presenterWorldRotation, in asset.LocalRotation);
        }

        private static Vector3 ResolveAssetPosition(
            Vector3 position,
            Quaternion presenterWorldRotation,
            Vector3 presenterWorldScale,
            in AssetBindingConfig asset)
        {
            return WorldPlane2D.ResolveVisualAssetPosition(
                in position,
                in presenterWorldRotation,
                in presenterWorldScale,
                in asset.LocalOffset);
        }

        private Vector4 ResolveColor(Entity entity, in AssetBindingConfig asset, Vector4 defaultColor)
        {
            return asset.ColorParamKey >= 0
                ? RequireVectorParam(entity, asset.ColorParamKey, "AssetBinding.colorParamKey")
                : defaultColor;
        }

        private float ResolveWorldHudFloatParam(Entity entity, int paramKey, string context)
        {
            return RequireFloatParam(entity, paramKey, context);
        }

        private int RequireIntParam(Entity entity, int paramKey, string context)
        {
            if (!_runtime.TryResolveInt(entity, paramKey, out int value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to an int param value.");
            }

            return value;
        }

        private float RequireFloatParam(Entity entity, int paramKey, string context)
        {
            if (!_runtime.TryResolveFloat(entity, paramKey, out float value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to a float param value.");
            }

            return value;
        }

        private float ResolveOptionalFloatParam(Entity entity, int paramKey, float fallback)
        {
            return _runtime.TryResolveFloat(entity, paramKey, out float value) ? value : fallback;
        }

        private Vector4 ResolveOptionalColorParams(
            Entity entity,
            int rParamKey,
            int gParamKey,
            int bParamKey,
            int aParamKey,
            Vector4 fallback)
        {
            return new Vector4(
                ResolveOptionalFloatParam(entity, rParamKey, fallback.X),
                ResolveOptionalFloatParam(entity, gParamKey, fallback.Y),
                ResolveOptionalFloatParam(entity, bParamKey, fallback.Z),
                ResolveOptionalFloatParam(entity, aParamKey, fallback.W));
        }

        private Vector4 RequireVectorParam(Entity entity, int paramKey, string context)
        {
            if (!_runtime.TryResolveVector(entity, paramKey, out Vector4 value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to a vector param value.");
            }

            return value;
        }

        private Vector4 ResolveOptionalVectorParam(Entity entity, int paramKey, Vector4 fallback)
        {
            return _runtime.TryResolveVector(entity, paramKey, out Vector4 value) ? value : fallback;
        }

        private Vector3 ResolveOptionalVector3Param(Entity entity, int paramKey, Vector3 fallback)
        {
            return _runtime.TryResolveVector(entity, paramKey, out Vector4 value)
                ? new Vector3(value.X, value.Y, value.Z)
                : fallback;
        }

        private AnimatorPackedState ResolveAnimator(Entity entity, VisualRenderPath renderPath)
        {
            if (!renderPath.SupportsAnimatorPackedState() || _animatorStates == null || !_animatorStates.IsAllocated(entity))
                return default;
            return _animatorStates.GetPackedState(entity);
        }

        private AnimationOverlayRequest ResolveAnimationOverlay(Entity entity, VisualRenderPath renderPath)
        {
            if (!renderPath.SupportsAnimatorPackedState() || _animatorStates == null || !_animatorStates.IsAllocated(entity))
                return default;
            return _animatorStates.GetOverlay(entity);
        }

        private bool TryGetRenderDebugState(out RenderDebugState debug)
        {
            if (_globals.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out var obj) && obj is RenderDebugState state)
            {
                debug = state;
                return true;
            }

            debug = default;
            return false;
        }

        private static VisualRenderPath ResolveRenderPath(in AssetBindingConfig asset)
        {
            if (asset.RenderPath == VisualRenderPath.None)
            {
                throw new InvalidOperationException(
                    $"Visual AssetBinding assetKind '{asset.AssetKind}' requires an explicit renderPath.");
            }

            return asset.RenderPath;
        }

        private static bool IsWithinMaxLod(LODLevel lod, in AssetBindingConfig asset)
        {
            return !asset.HasMaxLod || lod <= asset.MaxLod;
        }

        private PresentationVisualProxy BuildVisualProxy(
            Entity entity,
            in PresenterState state,
            in PresenterDefinition definition,
            in BehaviorSlot slot,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            Quaternion presenterWorldRotation,
            Vector3 presenterWorldScale,
            float alpha,
            VisualVisibility visibility)
        {
            VisualRenderPath renderPath = ResolveRenderPath(in asset);
            return new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Presenter,
                MeshAssetId = ResolveAssetId(entity, in asset),
                Position = ResolveAssetPosition(position, presenterWorldRotation, presenterWorldScale, in asset),
                Rotation = ResolveRotation(in asset, presenterWorldRotation),
                Scale = ResolveScale(entity, in asset, presenterWorldScale),
                Color = ApplyAlpha(ResolveColor(entity, in asset, ResolveAuthoredColor(in slot)), alpha),
                OwnerStableId = state.OwnerStableId,
                StableId = PresenterBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, asset.AssetKind, state.DefId),
                MaterialId = ResolveMaterialId(entity, in asset),
                TemplateId = state.DefId,
                AnimationProfileId = definition.AnimationProfileId,
                RenderPath = renderPath,
                AssetKind = asset.AssetKind,
                SurfaceLayerKey = asset.SurfaceLayerKey,
                SortId = asset.SortId,
                MaterialCustomData = PresenterMaterialCustomDataResolver.Resolve(_runtime, entity, in asset.MaterialCustomData),
                Mobility = asset.Mobility,
                Flags = VisualRuntimeFlags.Visible,
                Animator = ResolveAnimator(entity, renderPath),
                AnimationOverlay = ResolveAnimationOverlay(entity, renderPath),
                Visibility = visibility,
                LOD = lod,
            };
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private static GroundOverlayShape ResolveGroundOverlayShape(int assetId)
        {
            return assetId switch
            {
                0 => GroundOverlayShape.Circle,
                1 => GroundOverlayShape.Cone,
                2 => GroundOverlayShape.Line,
                3 => GroundOverlayShape.Ring,
                _ => throw new InvalidOperationException($"GroundOverlay AssetBinding has invalid shape id '{assetId}'."),
            };
        }

        private static float ResolveGroundOverlayRotation(in PresenterWorldFacing facing)
        {
            return facing.HasValue != 0 && float.IsFinite(facing.AngleRad)
                ? facing.AngleRad
                : 0f;
        }

        private static float DegreesToRadians(float degrees)
        {
            return degrees * (MathF.PI / 180f);
        }

        private static float RadiansToDegrees(float radians)
        {
            return radians * (180f / MathF.PI);
        }

        internal static Vector3 ResolvePosition(in PresenterState state, Vector3 resolvedRootPosition, float yDriftPerSecond)
        {
            Vector3 position = resolvedRootPosition;
            position.Y += yDriftPerSecond * state.Elapsed;
            return position;
        }

        private static float ResolveAlpha(in PresenterState state, in PresenterDefinition definition, BehaviorAlphaPolicy alphaPolicy)
        {
            if (alphaPolicy != BehaviorAlphaPolicy.FadeOverLifetime || definition.DefaultLifetime <= 0f) return 1f;
            return Math.Clamp(1f - (state.Elapsed / definition.DefaultLifetime), 0f, 1f);
        }

        private static Vector4 ResolveAuthoredColor(in BehaviorSlot slot)
        {
            return slot.Style.HasColor ? slot.Style.Color : Vector4.One;
        }

        private static Vector4 ApplyAlpha(Vector4 color, float alpha)
        {
            color.W *= alpha;
            return color;
        }
    }
}
