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
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PresentationRequestBuffer _requests;
        private readonly Dictionary<string, object> _globals;
        private readonly PerformerAnimatorStateBuffer _animatorStates;
        private readonly SoundRequestBuffer _soundRequests;
        private readonly WorldHudPerformBehavior _worldHudBehavior = new();

        public PerformerAssetEmitRuntime(
            World world,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions,
            PresentationRequestBuffer requests,
            Dictionary<string, object> globals,
            PerformerAnimatorStateBuffer animatorStates,
            SoundRequestBuffer soundRequests)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _requests = requests ?? throw new ArgumentNullException(nameof(requests));
            _globals = globals ?? new Dictionary<string, object>();
            _animatorStates = animatorStates;
            _soundRequests = soundRequests;
        }

        public void Emit(Entity entity, int definitionId, in PerformerState state, in PerformerDefinition definition, int slotIndex, in AssetBindingConfig asset, LODLevel lod)
        {
            Vector3 position = ResolvePosition(entity, in state, in definition);
            float alpha = ResolveAlpha(in state, in definition);
            if (lod == LODLevel.Culled || !ResolveAssetVisibility(entity, in asset))
            {
                EmitHiddenSnapshotIfVisual(entity, definitionId, in state, in definition, slotIndex, in asset, lod, position, alpha);
                return;
            }

            switch (asset.AssetKind)
            {
                case AssetKind.Mesh:
                case AssetKind.SkinnedMesh:
                case AssetKind.Decal:
                case AssetKind.VFX:
                    EmitVisualAsset(entity, definitionId, in state, in definition, slotIndex, in asset, lod, position, alpha);
                    return;

                case AssetKind.Sound:
                    EmitSoundAsset(in state, slotIndex, in asset, position);
                    return;

                case AssetKind.Spline:
                    EmitSplineAsset(entity, definitionId, in state, in definition, slotIndex, in asset, lod, position, alpha);
                    return;

                case AssetKind.WorldHud:
                    EmitWorldHudAsset(entity, definitionId, in state, in definition, in asset, lod, position, alpha);
                    return;

                case AssetKind.WorldText:
                    EmitWorldTextAsset(entity, definitionId, in state, in definition, in asset, lod, position, alpha);
                    return;

                case AssetKind.GroundOverlay:
                    EmitGroundOverlayAsset(entity, definitionId, in state, in definition, slotIndex, in asset, lod, position, alpha);
                    return;

                default:
                    throw new InvalidOperationException($"Unsupported performer asset kind '{asset.AssetKind}'.");
            }
        }

        private void EmitVisualAsset(Entity entity, int definitionId, in PerformerState state, in PerformerDefinition definition, int slotIndex, in AssetBindingConfig asset, LODLevel lod, Vector3 position, float alpha)
        {
            VisualRenderPath renderPath = ResolveRenderPath(in asset);
            var proxy = new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                MeshAssetId = ResolveAssetId(entity, in asset),
                Position = position,
                Rotation = NormalizeOrIdentity(GetWorldRotation(entity)),
                Scale = ResolveScale(entity, in state, in asset),
                Color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha),
                StableId = ResolveStableId(state.StableId, slotIndex, asset.AssetKind, definitionId),
                MaterialId = ResolveMaterialId(entity, in asset),
                TemplateId = definitionId,
                AnimationProfileId = ResolveAnimationProfileId(definitionId),
                RenderPath = renderPath,
                Mobility = asset.Mobility,
                Flags = VisualRuntimeFlags.Visible,
                Animator = ResolveAnimator(entity, renderPath),
                AnimationOverlay = ResolveAnimationOverlay(entity, renderPath),
                Visibility = lod == LODLevel.Culled ? VisualVisibility.Culled : VisualVisibility.Visible,
                LOD = lod,
            };
            _requests.Add(PresentationRequest.FromVisualProxy(state.OwnerEntity, proxy));
        }

        private void EmitHiddenSnapshotIfVisual(Entity entity, int definitionId, in PerformerState state, in PerformerDefinition definition, int slotIndex, in AssetBindingConfig asset, LODLevel lod, Vector3 position, float alpha)
        {
            if (asset.AssetKind is not (AssetKind.Mesh or AssetKind.SkinnedMesh or AssetKind.Decal or AssetKind.VFX))
            {
                return;
            }

            VisualRenderPath renderPath = ResolveRenderPath(in asset);
            _requests.Add(PresentationRequest.FromVisualProxy(state.OwnerEntity, new PresentationVisualProxy
            {
                ProxyKind = PresentationVisualProxyKind.Performer,
                MeshAssetId = ResolveAssetId(entity, in asset),
                Position = position,
                Rotation = NormalizeOrIdentity(GetWorldRotation(entity)),
                Scale = ResolveScale(entity, in state, in asset),
                Color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha),
                StableId = ResolveStableId(state.StableId, slotIndex, asset.AssetKind, definitionId),
                MaterialId = ResolveMaterialId(entity, in asset),
                TemplateId = definitionId,
                AnimationProfileId = ResolveAnimationProfileId(definitionId),
                RenderPath = renderPath,
                Mobility = asset.Mobility,
                Flags = VisualRuntimeFlags.Visible,
                Animator = ResolveAnimator(entity, renderPath),
                AnimationOverlay = ResolveAnimationOverlay(entity, renderPath),
                Visibility = lod == LODLevel.Culled ? VisualVisibility.Culled : VisualVisibility.Hidden,
                LOD = lod,
            }));
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

        private void EmitSplineAsset(Entity entity, int definitionId, in PerformerState state, in PerformerDefinition definition, int slotIndex, in AssetBindingConfig asset, LODLevel lod, Vector3 position, float alpha)
        {
            float width = asset.ScaleParamKey >= 0
                ? _runtime.ResolveFloat(entity, asset.ScaleParamKey, 1f)
                : MathF.Max(ResolveScale(entity, in state, in asset).X, 0.001f);
            Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha);
            Vector3 p0 = position;
            Vector3 p3 = p0 + Vector3.Transform(Vector3.UnitZ * MathF.Max(width, 0.001f), NormalizeOrIdentity(GetWorldRotation(entity)));
            Vector3 p1 = Vector3.Lerp(p0, p3, 0.33f);
            Vector3 p2 = Vector3.Lerp(p0, p3, 0.66f);

            _requests.Add(PresentationRequest.FromRoadSpline(state.OwnerEntity, new RoadSplineRequest
            {
                StableId = ResolveStableId(state.StableId, slotIndex, asset.AssetKind, definitionId),
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

        private void EmitWorldHudAsset(Entity entity, int definitionId, in PerformerState state, in PerformerDefinition definition, in AssetBindingConfig asset, LODLevel lod, Vector3 position, float alpha)
        {
            if (TryGetRenderDebugState(out var debug) && !debug.DrawWorldHudBars)
            {
                return;
            }

            if (!_worldHudBehavior.TryResolveProjection(_world, _globals, state.OwnerEntity, lod, out PerformPhaseResult phaseResult))
            {
                return;
            }

            Vector3 scale = ResolveScale(entity, in state, in asset);
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

        private void EmitWorldTextAsset(Entity entity, int definitionId, in PerformerState state, in PerformerDefinition definition, in AssetBindingConfig asset, LODLevel lod, Vector3 position, float alpha)
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

        private void EmitGroundOverlayAsset(Entity entity, int definitionId, in PerformerState state, in PerformerDefinition definition, int slotIndex, in AssetBindingConfig asset, LODLevel lod, Vector3 position, float alpha)
        {
            Vector3 scale = ResolveScale(entity, in state, in asset);
            float radius = MathF.Max(scale.X, 0.001f);
            float innerRadius = Math.Clamp(scale.Y, 0f, radius);
            float length = MathF.Max(scale.X, 0.001f);
            float width = MathF.Max(scale.Y, 0.001f);
            GroundOverlayShape shape = ResolveGroundOverlayShape(asset.AssetId);
            Vector4 color = ApplyAlpha(ResolveColor(entity, in asset, definition.DefaultColor), alpha);

            _requests.Add(PresentationRequest.FromGroundOverlay(state.OwnerEntity, new GroundOverlayItem
            {
                Shape = shape,
                Center = position,
                Radius = radius,
                InnerRadius = shape == GroundOverlayShape.Ring ? innerRadius : 0f,
                Rotation = ResolveYaw(GetWorldRotation(entity)),
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

        private Vector3 ResolveScale(Entity entity, in PerformerState state, in AssetBindingConfig asset)
        {
            Vector3 scale = GetWorldScale(entity);
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

        private int ResolveAnimationProfileId(int definitionId)
        {
            if (!_definitions.TryGet(definitionId, out PerformerDefinition definition))
            {
                return 0;
            }

            BehaviorSlot[] behaviors = definition.Behaviors ?? Array.Empty<BehaviorSlot>();
            for (int i = 0; i < behaviors.Length; i++)
            {
                if (behaviors[i].Kind == BehaviorKind.Animator)
                {
                    return behaviors[i].Animator.AnimationProfileId;
                }
            }

            return 0;
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

        private static int ResolveStableId(int performerStableId, int slotIndex, AssetKind assetKind, int discriminator)
        {
            int seed = PerformerBehaviorRuntimeUtility.ComposeBehaviorStableId(performerStableId, slotIndex);
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + seed;
                hash = hash * 31 + (int)assetKind;
                hash = hash * 31 + discriminator;
                hash &= int.MaxValue;
                return hash == 0 ? 1 : hash;
            }
        }

        private Quaternion GetWorldRotation(Entity entity)
        {
            return _world.Has<PerformerWorldRotation>(entity) ? _world.Get<PerformerWorldRotation>(entity).Value : Quaternion.Identity;
        }

        private Vector3 GetWorldScale(Entity entity)
        {
            return _world.Has<PerformerWorldScale>(entity) ? _world.Get<PerformerWorldScale>(entity).Value : Vector3.One;
        }

        private static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            return value.LengthSquared() > 0.000001f
                ? Quaternion.Normalize(value)
                : Quaternion.Identity;
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

        private Vector3 ResolvePosition(Entity entity, in PerformerState state, in PerformerDefinition definition)
        {
            Vector3 worldPos = _world.Has<PerformerWorldPosition>(entity) ? _world.Get<PerformerWorldPosition>(entity).Value : Vector3.Zero;
            Vector3 position = worldPos + definition.PositionOffset;
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
