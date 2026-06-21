using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Systems
{
    internal sealed class PerformerAssetEmitRuntime
    {
        private readonly World _world;
        private readonly PerformerEntityRuntime _runtime;
        private readonly PresentationRequestBuffer _requests;
        private readonly Dictionary<string, object> _globals;
        private readonly PerformerAnimatorStateBuffer _animatorStates;
        private readonly SoundRequestBuffer _soundRequests;
        private readonly PerformerVisualStableIdTable? _visualStableIds;
        private readonly WorldHudPerformBehavior _worldHudBehavior = new();

        public PerformerAssetEmitRuntime(
            World world,
            PerformerEntityRuntime runtime,
            PresentationRequestBuffer requests,
            Dictionary<string, object> globals,
            PerformerAnimatorStateBuffer animatorStates,
            SoundRequestBuffer soundRequests,
            PerformerVisualStableIdTable? visualStableIds = null)
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
            in PerformerState state,
            in PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 performerWorldPosition,
            Quaternion performerWorldRotation,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale)
        {
            Vector3 position = ResolvePosition(in state, in definition, performerWorldPosition);
            float alpha = ResolveAlpha(in state, in definition);
            if (lod == LODLevel.Culled || !IsWithinMaxLod(lod, in asset) || !ResolveAssetVisibility(entity, in asset))
            {
                EmitHiddenSnapshotIfVisual(entity, in state, in definition, slotIndex, in asset, lod, position, performerWorldRotation, performerWorldScale, alpha);
                RemoveHiddenWorldHudIfNeeded(state.DefId, in state, in asset);
                return;
            }

            switch (asset.AssetKind)
            {
                case AssetKind.Mesh:
                case AssetKind.SkinnedMesh:
                case AssetKind.Decal:
                case AssetKind.VFX:
                case AssetKind.Surface:
                    EmitVisualAsset(entity, in state, in definition, slotIndex, in asset, lod, position, performerWorldRotation, performerWorldScale, alpha);
                    return;

                case AssetKind.Sound:
                    EmitSoundAsset(in state, slotIndex, in asset, position);
                    return;

                case AssetKind.Spline:
                    EmitSplineAsset(entity, in state, in definition, slotIndex, in asset, lod, position, performerWorldRotation, performerWorldScale, alpha);
                    return;

                case AssetKind.WorldHud:
                    EmitWorldHudAsset(entity, state.DefId, in state, in definition, in asset, lod, position, performerWorldScale, alpha);
                    return;

                case AssetKind.WorldText:
                    EmitWorldTextAsset(entity, state.DefId, in state, in definition, in asset, lod, position, performerWorldScale, alpha);
                    return;

                case AssetKind.GroundOverlay:
                    EmitGroundOverlayAsset(entity, state.DefId, in state, in definition, slotIndex, in asset, lod, position, in performerWorldFacing, performerWorldScale, alpha);
                    return;

                default:
                    throw new InvalidOperationException($"Unsupported performer asset kind '{asset.AssetKind}'.");
            }
        }

        public bool EmitStaticStableVisualDirect(
            Entity entity,
            in PerformerState state,
            in PerformerDefinition definition,
            LODLevel lod,
            Vector3 performerWorldPosition,
            Quaternion performerWorldRotation,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale,
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
            BehaviorSlot[] behaviors = definition.Behaviors;
            Vector3 position = ResolvePosition(in state, in definition, performerWorldPosition);
            for (int i = 0; i < assetBehaviorIndices.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[assetBehaviorIndices[i]];
                if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    continue;
                }

                ref readonly AssetBindingConfig asset = ref slot.AssetBinding;
                if (lod != LODLevel.Culled &&
                    (!IsWithinMaxLod(lod, in asset) || !ResolveAssetVisibility(entity, in asset)))
                {
                    if (TryGetVisualStableId(in state, slot.SlotIndex, asset.AssetKind, state.DefId, out int removedStableId))
                    {
                        stableDrawCache.Remove(removedStableId);
                    }
                    continue;
                }

                VisualRenderPath renderPath = ResolveRenderPath(in asset);
                Quaternion rotation = ResolveRotation(in asset, performerWorldRotation);
                Vector3 scale = ResolveScale(entity, in asset, performerWorldScale);
                Vector3 assetPosition = ResolveAssetPosition(position, performerWorldRotation, performerWorldScale, in asset);
                Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), ResolveAlpha(in state, in definition));
                int stableId = GetOrAllocateVisualStableId(in state, slot.SlotIndex, asset.AssetKind, state.DefId);
                PresentationVisualProxy proxy = new PresentationVisualProxy
                {
                    ProxyKind = PresentationVisualProxyKind.Performer,
                    MeshAssetId = ResolveAssetId(entity, in asset),
                    Position = assetPosition,
                    Rotation = rotation,
                    Scale = scale,
                    Color = color,
                    StableId = stableId,
                    MaterialId = ResolveMaterialId(entity, in asset),
                    TemplateId = state.DefId,
                    AnimationProfileId = definition.AnimationProfileId,
                    RenderPath = renderPath,
                    AssetKind = asset.AssetKind,
                    SurfaceLayerKey = asset.SurfaceLayerKey,
                    SortId = asset.SortId,
                    MaterialCustomData = PerformerMaterialCustomDataResolver.Resolve(_runtime, entity, in asset.MaterialCustomData),
                    Mobility = asset.Mobility,
                    Flags = VisualRuntimeFlags.Visible,
                    Animator = ResolveAnimator(entity, renderPath),
                    AnimationOverlay = default,
                    Visibility = lod == LODLevel.Culled ? VisualVisibility.Culled : VisualVisibility.Visible,
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

            return emitted;
        }

        public void RemoveStaticStableVisuals(
            in PerformerState state,
            in PerformerDefinition definition,
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
            in PerformerState state,
            int slotIndex,
            AssetKind assetKind,
            int discriminator,
            out int stableId)
        {
            return TryGetVisualStableId(in state, slotIndex, assetKind, discriminator, out stableId);
        }

        private int GetOrAllocateVisualStableId(
            in PerformerState state,
            int slotIndex,
            AssetKind assetKind,
            int discriminator)
        {
            PerformerVisualStableIdTable table = _visualStableIds
                ?? throw new InvalidOperationException("Static performer visual stable ids require PerformerVisualStableIdTable.");
            return table.GetOrAllocate(PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(
                    state.StableId,
                    slotIndex,
                    assetKind,
                    discriminator));
        }

        private bool TryGetVisualStableId(
            in PerformerState state,
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
                PerformerBehaviorRuntimeUtility.ComposeVisualStableKey(
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
            in PerformerState state,
            in PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            Quaternion performerWorldRotation,
            Vector3 performerWorldScale,
            float alpha)
        {
            _requests.Add(PresentationRequest.FromVisualProxy(
                state.OwnerEntity,
                BuildVisualProxy(
                    entity,
                    in state,
                    in definition,
                    slotIndex,
                    in asset,
                    lod,
                    position,
                    performerWorldRotation,
                    performerWorldScale,
                    alpha,
                    lod == LODLevel.Culled ? VisualVisibility.Culled : VisualVisibility.Visible)));
        }

        private void EmitHiddenSnapshotIfVisual(
            Entity entity,
            in PerformerState state,
            in PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            Quaternion performerWorldRotation,
            Vector3 performerWorldScale,
            float alpha)
        {
            if (asset.AssetKind is not (AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX or AssetKind.Surface))
            {
                return;
            }

            _requests.Add(PresentationRequest.FromVisualProxy(
                state.OwnerEntity,
                BuildVisualProxy(
                    entity,
                    in state,
                    in definition,
                    slotIndex,
                    in asset,
                    lod,
                    position,
                    performerWorldRotation,
                    performerWorldScale,
                    alpha,
                    lod == LODLevel.Culled ? VisualVisibility.Culled : VisualVisibility.Hidden)));
        }

        private void RemoveHiddenWorldHudIfNeeded(
            int definitionId,
            in PerformerState state,
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

            int stableId = HudItemIdentity.ComposeStableId(state.StableId, kind, definitionId);
            _requests.Add(PresentationRequest.RemoveWorldHud(state.OwnerEntity, stableId));
        }

        private void EmitSoundAsset(in PerformerState state, int slotIndex, in AssetBindingConfig asset, Vector3 position)
        {
            if (_soundRequests == null || asset.AssetId <= 0)
            {
                return;
            }

            _soundRequests.Add(new SoundRequest
            {
                Kind = SoundRequestKind.PlayOrUpdate,
                StableId = PerformerBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, slotIndex),
                SoundAssetId = asset.AssetId,
                Loop = true,
                Volume = 1f,
                WorldPosition = position,
                Owner = state.OwnerEntity,
            });
        }

        private void EmitSplineAsset(
            Entity entity,
            in PerformerState state,
            in PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            Quaternion performerWorldRotation,
            Vector3 performerWorldScale,
            float alpha)
        {
            float width = asset.ScaleParamKey >= 0
                ? RequireFloatParam(entity, asset.ScaleParamKey, "AssetBinding.scaleParamKey")
                : MathF.Max(ResolveScale(entity, in asset, performerWorldScale).X, 0.001f);
            Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha);
            Vector3 p0 = position;
            Vector3 p3 = p0 + Vector3.Transform(Vector3.UnitZ * MathF.Max(width, 0.001f), WorldPlane2D.NormalizeOrIdentity(performerWorldRotation));
            Vector3 p1 = Vector3.Lerp(p0, p3, 0.33f);
            Vector3 p2 = Vector3.Lerp(p0, p3, 0.66f);

            _requests.Add(PresentationRequest.FromRoadSpline(state.OwnerEntity, new RoadSplineRequest
            {
                StableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slotIndex, asset.AssetKind, state.DefId),
                P0 = p0,
                P1 = p1,
                P2 = p2,
                P3 = p3,
                Width = width,
                FillColor = color,
                BorderColor = color,
                BorderWidth = 0f,
                Style = 0,
            }, lod));
        }

        private void EmitWorldHudAsset(Entity entity, int definitionId, in PerformerState state, in PerformerDefinition definition, in AssetBindingConfig asset, LODLevel lod, Vector3 position, Vector3 performerWorldScale, float alpha)
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
                    definition.RequiredAttributeIds,
                    out PerformPhaseResult phaseResult))
            {
                return;
            }

            Vector3 scale = ResolveScale(entity, in asset, performerWorldScale);
            Vector4 foreground = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha);
            Vector4 background = new Vector4(0.2f, 0.2f, 0.2f, foreground.W);
            float value = asset.MaterialParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.MaterialParamKey, "AssetBinding.materialParamKey")
                : 1f;
            float width = scale.X > 0f ? scale.X : 40f;
            float height = scale.Y > 0f ? scale.Y : 6f;

            _requests.Add(PresentationRequest.FromWorldHud(state.OwnerEntity, new WorldHudItem
            {
                StableId = HudItemIdentity.ComposeStableId(state.StableId, WorldHudItemKind.Bar, definitionId),
                DirtySerial = HudItemIdentity.ComposeBarDirtySerial(width, height, value, background, foreground),
                Kind = WorldHudItemKind.Bar,
                WorldPosition = position,
                Value0 = value,
                Width = width,
                Height = height,
                Color0 = background,
                Color1 = foreground,
            }, phaseResult.LOD));
        }

        private void EmitWorldTextAsset(Entity entity, int definitionId, in PerformerState state, in PerformerDefinition definition, in AssetBindingConfig asset, LODLevel lod, Vector3 position, Vector3 performerWorldScale, float alpha)
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
                    definition.RequiredAttributeIds,
                    out PerformPhaseResult phaseResult))
            {
                return;
            }

            Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha);
            int tokenId = ResolveAssetId(entity, in asset);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"WorldText AssetBinding for performer definition '{definition.Key}' resolved invalid asset id {tokenId}.");
            }

            float value0 = asset.ScaleParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.ScaleParamKey, "AssetBinding.scaleParamKey")
                : 0f;
            float value1 = asset.MaterialParamKey >= 0
                ? ResolveWorldHudFloatParam(entity, asset.MaterialParamKey, "AssetBinding.materialParamKey")
                : 0f;
            WorldHudValueMode valueMode = definition.WorldTextMode;
            int fontSize = definition.DefaultFontSize > 0 ? definition.DefaultFontSize : 16;
            int stringTableId = valueMode == WorldHudValueMode.None ? tokenId : 0;
            PresentationTextPacket packet = PresentationTextPacket.FromWorldHudValueMode(tokenId, valueMode, value0, value1);

            _requests.Add(PresentationRequest.FromWorldHud(state.OwnerEntity, new WorldHudItem
            {
                StableId = HudItemIdentity.ComposeStableId(state.StableId, WorldHudItemKind.Text, definitionId),
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
            }, phaseResult.LOD));
        }

        private void EmitGroundOverlayAsset(
            Entity entity,
            int definitionId,
            in PerformerState state,
            in PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            in PerformerWorldFacing performerWorldFacing,
            Vector3 performerWorldScale,
            float alpha)
        {
            Vector3 scale = ResolveScale(entity, in asset, performerWorldScale);
            float radius = MathF.Max(scale.X, 0.001f);
            float innerRadius = Math.Clamp(scale.Y, 0f, radius);
            float length = MathF.Max(scale.X, 0.001f);
            float width = MathF.Max(scale.Y, 0.001f);
            GroundOverlayShape shape = ResolveGroundOverlayShape(asset.AssetId);
            Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha);

            _requests.Add(PresentationRequest.FromGroundOverlay(state.OwnerEntity, new GroundOverlayItem
            {
                StableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slotIndex, asset.AssetKind, definitionId),
                Shape = shape,
                Center = position,
                Radius = radius,
                InnerRadius = shape == GroundOverlayShape.Ring ? innerRadius : 0f,
                Rotation = ResolveGroundOverlayRotation(performerWorldFacing),
                Length = shape == GroundOverlayShape.Line ? length : 0f,
                Width = shape == GroundOverlayShape.Line ? width : 0f,
                FillColor = color,
                BorderColor = color,
                BorderWidth = MathF.Max(scale.Z, 0f),
            }, lod));
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
                        $"Performer AssetBinding assetIdParamKey {asset.AssetIdParamKey} did not resolve to a registered asset id.");
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
                    $"Performer AssetBinding assetSwapParamKey {asset.AssetSwapParamKey} did not resolve to a swap value.");
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
                $"Performer AssetBinding assetSwapParamKey {asset.AssetSwapParamKey} resolved value {resolved} with no matching assetSwapTable entry.");
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

        private Vector3 ResolveScale(Entity entity, in AssetBindingConfig asset, Vector3 performerWorldScale)
        {
            Vector3 scale = performerWorldScale;
            scale = scale == Vector3.Zero ? Vector3.One : scale;
            scale *= asset.LocalScale == Vector3.Zero ? Vector3.One : asset.LocalScale;
            if (asset.ScaleParamKey >= 0)
                scale *= RequireFloatParam(entity, asset.ScaleParamKey, "AssetBinding.scaleParamKey");
            return scale;
        }

        private static Quaternion ResolveRotation(in AssetBindingConfig asset, Quaternion performerWorldRotation)
        {
            return WorldPlane2D.ResolveVisualAssetRotation(in performerWorldRotation, in asset.LocalRotation);
        }

        private static Vector3 ResolveAssetPosition(
            Vector3 position,
            Quaternion performerWorldRotation,
            Vector3 performerWorldScale,
            in AssetBindingConfig asset)
        {
            return WorldPlane2D.ResolveVisualAssetPosition(
                in position,
                in performerWorldRotation,
                in performerWorldScale,
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

        private Vector4 RequireVectorParam(Entity entity, int paramKey, string context)
        {
            if (!_runtime.TryResolveVector(entity, paramKey, out Vector4 value))
            {
                throw new InvalidOperationException($"{context} {paramKey} did not resolve to a vector param value.");
            }

            return value;
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
            return lod != LODLevel.Culled && (!asset.HasMaxLod || lod <= asset.MaxLod);
        }

        private PresentationVisualProxy BuildVisualProxy(
            Entity entity,
            in PerformerState state,
            in PerformerDefinition definition,
            int slotIndex,
            in AssetBindingConfig asset,
            LODLevel lod,
            Vector3 position,
            Quaternion performerWorldRotation,
            Vector3 performerWorldScale,
            float alpha,
            VisualVisibility visibility)
        {
            VisualRenderPath renderPath = ResolveRenderPath(in asset);
            return new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                MeshAssetId = ResolveAssetId(entity, in asset),
                Position = ResolveAssetPosition(position, performerWorldRotation, performerWorldScale, in asset),
                Rotation = ResolveRotation(in asset, performerWorldRotation),
                Scale = ResolveScale(entity, in asset, performerWorldScale),
                Color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha),
                StableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slotIndex, asset.AssetKind, state.DefId),
                MaterialId = ResolveMaterialId(entity, in asset),
                TemplateId = state.DefId,
                AnimationProfileId = definition.AnimationProfileId,
                RenderPath = renderPath,
                AssetKind = asset.AssetKind,
                SurfaceLayerKey = asset.SurfaceLayerKey,
                SortId = asset.SortId,
                MaterialCustomData = PerformerMaterialCustomDataResolver.Resolve(_runtime, entity, in asset.MaterialCustomData),
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

        private static float ResolveGroundOverlayRotation(in PerformerWorldFacing facing)
        {
            return facing.HasValue != 0 && float.IsFinite(facing.AngleRad)
                ? facing.AngleRad
                : 0f;
        }

        internal static Vector3 ResolvePosition(in PerformerState state, in PerformerDefinition definition, Vector3 performerWorldPosition)
        {
            Vector3 position = performerWorldPosition + definition.PositionOffset;
            position.Y += definition.PositionYDriftPerSecond * state.Elapsed;
            return position;
        }

        private static float ResolveAlpha(in PerformerState state, in PerformerDefinition definition)
        {
            if (!definition.AlphaFadeOverLifetime || definition.DefaultLifetime <= 0f) return 1f;
            return Math.Clamp(1f - (state.Elapsed / definition.DefaultLifetime), 0f, 1f);
        }

        private static Vector4 ApplyAlpha(Vector4 color, float alpha)
        {
            color.W *= alpha;
            return color;
        }
    }
}
