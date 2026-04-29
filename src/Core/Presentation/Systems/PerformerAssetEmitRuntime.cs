using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
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
        private readonly WorldHudPerformBehavior _worldHudBehavior = new();

        public PerformerAssetEmitRuntime(
            World world,
            PerformerEntityRuntime runtime,
            PresentationRequestBuffer requests,
            Dictionary<string, object> globals,
            PerformerAnimatorStateBuffer animatorStates,
            SoundRequestBuffer soundRequests)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _globals = globals ?? new Dictionary<string, object>();
            _animatorStates = animatorStates;
            _soundRequests = soundRequests;
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
                    EmitGroundOverlayAsset(entity, state.DefId, in state, in definition, slotIndex, in asset, lod, position, performerWorldRotation, performerWorldScale, alpha);
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
                if (!IsWithinMaxLod(lod, in asset) || !ResolveAssetVisibility(entity, in asset))
                {
                    stableDrawCache.Remove(PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, asset.AssetKind, state.DefId));
                    continue;
                }

                VisualRenderPath renderPath = ResolveRenderPath(in asset);
                Quaternion rotation = NormalizeOrIdentity(performerWorldRotation);
                Vector3 scale = ResolveScale(entity, in asset, performerWorldScale);
                Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), ResolveAlpha(in state, in definition));
                PresentationVisualProxy proxy = new PresentationVisualProxy
                {
                    ProxyKind = PresentationVisualProxyKind.Performer,
                    MeshAssetId = ResolveAssetId(entity, in asset),
                    Position = position,
                    Rotation = rotation,
                    Scale = scale,
                    Color = color,
                    StableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slot.SlotIndex, asset.AssetKind, state.DefId),
                    MaterialId = ResolveMaterialId(entity, in asset),
                    TemplateId = state.DefId,
                    AnimationProfileId = definition.AnimationProfileId,
                    RenderPath = renderPath,
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
                stableDrawCache.Remove(PerformerBehaviorRuntimeUtility.ComposeVisualStableId(
                    state.StableId,
                    slot.SlotIndex,
                    slot.AssetBinding.AssetKind,
                    state.DefId));
            }
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
            if (asset.AssetKind is not (AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX))
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
                ? _runtime.ResolveFloat(entity, asset.ScaleParamKey, 1f)
                : MathF.Max(ResolveScale(entity, in asset, performerWorldScale).X, 0.001f);
            Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha);
            Vector3 p0 = position;
            Vector3 p3 = p0 + Vector3.Transform(Vector3.UnitZ * MathF.Max(width, 0.001f), NormalizeOrIdentity(performerWorldRotation));
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

            if (!_worldHudBehavior.TryResolveProjection(_world, _globals, state.OwnerEntity, lod, out PerformPhaseResult phaseResult))
            {
                return;
            }

            Vector3 scale = ResolveScale(entity, in asset, performerWorldScale);
            Vector4 foreground = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha);
            Vector4 background = new Vector4(0.2f, 0.2f, 0.2f, foreground.W);
            float value = asset.MaterialParamKey >= 0 ? _runtime.ResolveFloat(entity, asset.MaterialParamKey, 1f) : 1f;
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

            if (!_worldHudBehavior.TryResolveProjection(_world, _globals, state.OwnerEntity, lod, out PerformPhaseResult phaseResult))
            {
                return;
            }

            Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha);
            int tokenId = ResolveAssetId(entity, in asset);
            if (tokenId <= 0)
            {
                tokenId = definition.DefaultTextId;
            }

            float value0 = asset.ScaleParamKey >= 0 ? _runtime.ResolveFloat(entity, asset.ScaleParamKey, 0f) : 0f;
            float value1 = asset.MaterialParamKey >= 0 ? _runtime.ResolveFloat(entity, asset.MaterialParamKey, 0f) : 0f;
            WorldHudValueMode valueMode = definition.LegacyWorldTextMode;
            int fontSize = definition.DefaultFontSize > 0 ? definition.DefaultFontSize : 16;
            int legacyStringId = valueMode == WorldHudValueMode.None ? tokenId : 0;
            PresentationTextPacket packet = PresentationTextPacket.FromLegacyWorldHud(tokenId, valueMode, value0, value1);

            _requests.Add(PresentationRequest.FromWorldHud(state.OwnerEntity, new WorldHudItem
            {
                StableId = HudItemIdentity.ComposeStableId(state.StableId, WorldHudItemKind.Text, definitionId),
                DirtySerial = HudItemIdentity.ComposeTextDirtySerial(fontSize, legacyStringId, (int)valueMode, value0, value1, color, packet),
                Kind = WorldHudItemKind.Text,
                WorldPosition = position,
                Value0 = value0,
                Value1 = value1,
                Id0 = legacyStringId,
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
            Quaternion performerWorldRotation,
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
                Rotation = ResolveYaw(performerWorldRotation),
                Length = shape == GroundOverlayShape.Line ? length : 0f,
                Width = shape == GroundOverlayShape.Line ? width : 0f,
                FillColor = color,
                BorderColor = color,
                BorderWidth = MathF.Max(scale.Z, 0f),
            }, lod));
        }

        private bool ResolveAssetVisibility(Entity entity, in AssetBindingConfig asset)
        {
            return asset.VisibilityParamKey < 0 || _runtime.ResolveInt(entity, asset.VisibilityParamKey, 1) != 0;
        }

        private int ResolveAssetId(Entity entity, in AssetBindingConfig asset)
        {
            if (asset.AssetSwapParamKey < 0) return asset.AssetId;
            int resolved = _runtime.ResolveInt(entity, asset.AssetSwapParamKey, asset.AssetId);
            AssetSwapEntry[] table = asset.AssetSwapTable ?? Array.Empty<AssetSwapEntry>();
            if (table.Length == 0) return resolved;
            for (int i = 0; i < table.Length; i++)
            {
                ref readonly AssetSwapEntry entry = ref table[i];
                if (MathF.Abs(entry.ParamValue - resolved) <= 0.0001f) return entry.AssetId;
            }
            return asset.AssetId;
        }

        private int ResolveMaterialId(Entity entity, in AssetBindingConfig asset)
        {
            return asset.MaterialParamKey >= 0
                ? _runtime.ResolveInt(entity, asset.MaterialParamKey, asset.MaterialId)
                : asset.MaterialId;
        }

        private Vector3 ResolveScale(Entity entity, in AssetBindingConfig asset, Vector3 performerWorldScale)
        {
            Vector3 scale = performerWorldScale;
            scale = scale == Vector3.Zero ? Vector3.One : scale;
            if (asset.ScaleParamKey >= 0)
                scale *= _runtime.ResolveFloat(entity, asset.ScaleParamKey, 1f);
            return scale;
        }

        private Vector4 ResolveColor(Entity entity, in AssetBindingConfig asset, Vector4 fallback)
        {
            return asset.ColorParamKey >= 0
                ? _runtime.ResolveVector(entity, asset.ColorParamKey, fallback)
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
            if (asset.RenderPath != VisualRenderPath.None)
            {
                return asset.RenderPath;
            }

            return asset.AssetKind == AssetKind.SkinnedMesh
                ? VisualRenderPath.SkinnedMesh
                : VisualRenderPath.StaticMesh;
        }

        private static bool IsWithinMaxLod(LODLevel lod, in AssetBindingConfig asset)
        {
            return lod != LODLevel.Culled && (!asset.HasMaxLod || lod <= asset.MaxLod);
        }

        private static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            return value.LengthSquared() > 0.000001f
                ? Quaternion.Normalize(value)
                : Quaternion.Identity;
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
                Position = position,
                Rotation = NormalizeOrIdentity(performerWorldRotation),
                Scale = ResolveScale(entity, in asset, performerWorldScale),
                Color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha),
                StableId = PerformerBehaviorRuntimeUtility.ComposeVisualStableId(state.StableId, slotIndex, asset.AssetKind, state.DefId),
                MaterialId = ResolveMaterialId(entity, in asset),
                TemplateId = state.DefId,
                AnimationProfileId = definition.AnimationProfileId,
                RenderPath = renderPath,
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

        private static float ResolveYaw(Quaternion rotation)
        {
            rotation = NormalizeOrIdentity(rotation);
            Vector3 forward = Vector3.Transform(Vector3.UnitZ, rotation);
            return MathF.Atan2(forward.Z, forward.X);
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
