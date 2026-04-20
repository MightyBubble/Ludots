using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PerformerBehaviorSystem : BaseSystem<World, float>
    {
        private readonly PerformerEntityRuntime _runtime;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PresentationEventStream _events;
        private readonly SoundRequestBuffer _soundRequests;
        private readonly Func<IVisualHeightmap?> _heightmapProvider;
        private readonly IBoneTransformProvider? _boneTransformProvider;
        private readonly Dictionary<int, SoundTrackingState> _soundTracking = new();
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;

        private struct SoundTrackingState
        {
            public uint ActiveMask;
            public int StableId;
            public int DefinitionId;
        }
        public PerformerBehaviorSystem(
            World world,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions,
            PresentationEventStream events,
            SoundRequestBuffer soundRequests,
            IVisualHeightmap? heightmap = null,
            IBoneTransformProvider? boneTransformProvider = null,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : this(world, runtime, definitions, events, soundRequests,
                () => heightmap, boneTransformProvider, timingDiagnostics)
        {
        }

        public PerformerBehaviorSystem(
            World world,
            PerformerEntityRuntime runtime,
            PerformerDefinitionRegistry definitions,
            PresentationEventStream events,
            SoundRequestBuffer soundRequests,
            Func<IVisualHeightmap?> heightmapProvider,
            IBoneTransformProvider? boneTransformProvider = null,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _soundRequests = soundRequests ?? throw new ArgumentNullException(nameof(soundRequests));
            _heightmapProvider = heightmapProvider ?? throw new ArgumentNullException(nameof(heightmapProvider));
            _boneTransformProvider = boneTransformProvider;
            _timingDiagnostics = timingDiagnostics;
        }

        public override void Update(in float dt)
        {
            long start = _timingDiagnostics != null ? Stopwatch.GetTimestamp() : 0L;
            float tickDt = dt;
            var query = new QueryDescription().WithAll<PerformerState, PerformerWorldPosition>();
            World.Query(in query, (Entity entity, ref PerformerState state, ref PerformerWorldPosition pos) =>
            {
                if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition))
                    return;

                Entity owner = state.OwnerEntity;
                bool ownerHasDirtyAttrs = World.IsAlive(owner) && World.Has<DirtyFlags>(owner) && World.Get<DirtyFlags>(owner).IsAnyAttributeDirty();
                bool ownerHasDirtyTags = World.IsAlive(owner) && World.Has<GameplayTagEffectiveChangedBits>(owner) && World.Get<GameplayTagEffectiveChangedBits>(owner).IsAnyBitSet();
                bool isFirstFrame = state.Version <= 1;

                BehaviorSlot[] behaviors = definition.Behaviors;
                ResolveDefaultTransformSource(entity, ref state);
                ApplyBindings(entity, owner, definition);
                HandleReusedSoundSlot(entity, in state, behaviors);
                uint currentSoundMask = 0u;
                for (int i = 0; i < behaviors.Length; i++)
                {
                    ref readonly BehaviorSlot slot = ref behaviors[i];
                    if (!IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                        continue;
                    switch (slot.Kind)
                    {
                        case BehaviorKind.AttributeBinding:
                            if (isFirstFrame || ownerHasDirtyAttrs)
                                ApplyAttributeBinding(entity, owner, slot.AttributeBinding);
                            break;
                        case BehaviorKind.TagBinding:
                            if (isFirstFrame || ownerHasDirtyTags)
                                ApplyTagBinding(entity, owner, slot.TagBinding);
                            break;
                        case BehaviorKind.Material:
                            ApplyMaterialBinding(entity, slot.Material);
                            break;
                        case BehaviorKind.Attachment:
                            ApplyAttachment(entity, ref state, slot.Attachment);
                            break;
                        case BehaviorKind.Sound:
                            ApplySound(entity, in state, slot);
                            currentSoundMask |= 1u << slot.SlotIndex;
                            break;
                        case BehaviorKind.Spline:
                            ApplySpline(entity, ref state, slot.Spline, tickDt);
                            break;
                    }
                }
                StopInactiveSounds(entity, in state, behaviors, currentSoundMask);
                _soundTracking[entity.Id] = new SoundTrackingState
                {
                    ActiveMask = currentSoundMask,
                    StableId = state.StableId,
                    DefinitionId = state.DefId,
                };
                ResolveTransform(entity, ref state, behaviors);
            });

            StopDestroyedSounds();

            if (_timingDiagnostics != null)
                _timingDiagnostics.ObservePerformerBehavior((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
        }
        private void ApplyBindings(Entity entity, Entity owner, PerformerDefinition definition)
        {
            PerformerParamBinding[] bindings = definition.Bindings;
            if (bindings == null || bindings.Length == 0) return;
            for (int i = 0; i < bindings.Length; i++)
            {
                ref readonly PerformerParamBinding binding = ref bindings[i];
                ValueRef value = binding.Value;
                switch (value.Source)
                {
                    case ValueSourceKind.EntityColor:
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveEntityColorChannel(owner, value.SourceId), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.EntityColorVector:
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Vector, 0f, 0, ResolveEntityColor(owner));
                        break;
                    case ValueSourceKind.FacingRadians:
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveFacingRadians(owner), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.FacingDegrees:
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, ResolveFacingRadians(owner) * (180f / MathF.PI), 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Attribute:
                    case ValueSourceKind.AttributeRatio:
                    case ValueSourceKind.AttributeBase:
                        if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner)) continue;
                        ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
                        float resolved = ResolveAttributeValue(ref attributes, value.SourceId, value.Source);
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, resolved, 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Constant:
                        _runtime.SetParam(entity, binding.ParamKey, ParamLane.Float, value.ConstantValue, 0, Vector4.Zero);
                        break;
                    case ValueSourceKind.Graph:
                        break;
                }
            }
        }

        private void ApplyAttributeBinding(Entity entity, Entity owner, in AttributeBindingConfig config)
        {
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner)) return;
            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
            float value = ResolveAttributeValue(ref attributes, config.AttributeId, config.Mode);
            _runtime.SetParam(entity, config.TargetParamKey, ParamLane.Float, value, 0, Vector4.Zero);
            ThresholdMapping[] thresholds = config.Thresholds ?? Array.Empty<ThresholdMapping>();
            bool thresholdMatched = false;
            for (int i = 0; i < thresholds.Length; i++)
            {
                ref readonly ThresholdMapping threshold = ref thresholds[i];
                if (value <= threshold.Threshold)
                {
                    int thresholdIntValue = (int)threshold.OutputValue;
                    _runtime.SetParam(entity, threshold.OutputParamKey, ParamLane.Float, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                    _runtime.SetParam(entity, threshold.OutputParamKey, ParamLane.Int, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                    thresholdMatched = true;
                    break;
                }
            }
            if (!thresholdMatched)
            {
                for (int i = 0; i < thresholds.Length; i++)
                {
                    ref readonly ThresholdMapping threshold = ref thresholds[i];
                    if (World.Has<PerformerFloatParams>(entity))
                    {
                        ref var fp = ref World.Get<PerformerFloatParams>(entity);
                        fp.Clear(threshold.OutputParamKey);
                    }
                    if (World.Has<PerformerIntParams>(entity))
                    {
                        ref var ip = ref World.Get<PerformerIntParams>(entity);
                        ip.Clear(threshold.OutputParamKey);
                    }
                }
            }
        }

        private void ApplyTagBinding(Entity entity, Entity owner, in TagBindingConfig config)
        {
            bool active = false;
            if (World.IsAlive(owner) && World.Has<GameplayTagContainer>(owner))
            {
                ref GameplayTagContainer tags = ref World.Get<GameplayTagContainer>(owner);
                active = tags.HasTag(config.TagId);
            }
            if (config.InvertLogic) active = !active;
            _runtime.SetParam(entity, config.TargetParamKey, ParamLane.Int, 0f, active ? 1 : 0, Vector4.Zero);
        }

        private void ApplyMaterialBinding(Entity entity, in MaterialConfig config)
        {
            int materialId = config.BaseMaterialId;
            if (config.MaterialSwapParamKey >= 0)
            {
                float paramValue = _runtime.ResolveFloat(entity, config.MaterialSwapParamKey, float.NaN);
                MaterialSwapEntry[] swapTable = config.SwapTable ?? Array.Empty<MaterialSwapEntry>();
                if (!float.IsNaN(paramValue))
                {
                    for (int i = 0; i < swapTable.Length; i++)
                    {
                        ref readonly MaterialSwapEntry entry = ref swapTable[i];
                        if (MathF.Abs(entry.ParamValue - paramValue) <= 0.0001f)
                        {
                            materialId = entry.MaterialId;
                            break;
                        }
                    }
                }
            }
            if (materialId > 0 && config.MaterialSwapParamKey >= 0)
                _runtime.SetParam(entity, config.MaterialSwapParamKey, ParamLane.Int, 0f, materialId, Vector4.Zero);
        }
        private void ApplySound(Entity entity, in PerformerState state, in BehaviorSlot slot)
        {
            if (slot.Sound.SoundAssetId <= 0) return;
            float volume = slot.Sound.Volume;
            if (slot.Sound.VolumeParamKey >= 0)
                volume = _runtime.ResolveFloat(entity, slot.Sound.VolumeParamKey, volume);
            Vector3 worldPos = World.Has<PerformerWorldPosition>(entity) ? World.Get<PerformerWorldPosition>(entity).Value : Vector3.Zero;
            _soundRequests.Add(new SoundRequest
            {
                Kind = SoundRequestKind.PlayOrUpdate,
                StableId = PerformerBehaviorRuntimeUtility.ComposeBehaviorStableId(state.StableId, slot.SlotIndex),
                SoundAssetId = slot.Sound.SoundAssetId,
                Loop = slot.Sound.Loop,
                Volume = Math.Clamp(volume, 0f, 1f),
                WorldPosition = worldPos,
                Owner = state.OwnerEntity,
            });
        }

        private void StopInactiveSounds(Entity entity, in PerformerState state, BehaviorSlot[] behaviors, uint currentSoundMask)
        {
            if (!_soundTracking.TryGetValue(entity.Id, out var prev)) return;
            uint stopMask = prev.ActiveMask & ~currentSoundMask;
            if (stopMask == 0u) return;
            Vector3 worldPos = World.Has<PerformerWorldPosition>(entity) ? World.Get<PerformerWorldPosition>(entity).Value : Vector3.Zero;
            EmitStopRequests(stopMask, behaviors, state.StableId, state.OwnerEntity, worldPos);
        }

        private void StopDestroyedSounds()
        {
            ReadOnlySpan<PresentationEvent> events = _events.GetSpan();
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly PresentationEvent evt = ref events[i];
                if (evt.Kind != PresentationEventKind.PerformerDestroyed) continue;
                Entity performer = evt.PerformerEntity;
                if (performer == Entity.Null || !_soundTracking.TryGetValue(performer.Id, out var prev)) continue;
                int stableId = (int)evt.Magnitude;
                if (prev.StableId != stableId) continue;
                if (prev.ActiveMask == 0u || !_definitions.TryGet(evt.KeyId, out PerformerDefinition definition))
                {
                    _soundTracking.Remove(performer.Id);
                    continue;
                }
                EmitStopRequests(prev.ActiveMask, definition.Behaviors, stableId, evt.Source, Vector3.Zero);
                _soundTracking.Remove(performer.Id);
            }
        }

        private void HandleReusedSoundSlot(Entity entity, in PerformerState state, BehaviorSlot[] _)
        {
            if (!_soundTracking.TryGetValue(entity.Id, out var prev)) return;
            if (prev.StableId == 0 || prev.StableId == state.StableId) return;
            if (prev.ActiveMask != 0u && prev.DefinitionId > 0 &&
                _definitions.TryGet(prev.DefinitionId, out PerformerDefinition previousDefinition))
            {
                Vector3 worldPos = World.Has<PerformerWorldPosition>(entity) ? World.Get<PerformerWorldPosition>(entity).Value : Vector3.Zero;
                EmitStopRequests(prev.ActiveMask, previousDefinition.Behaviors, prev.StableId, state.OwnerEntity, worldPos);
            }
            _soundTracking.Remove(entity.Id);
        }

        private void EmitStopRequests(uint stopMask, BehaviorSlot[] behaviors, int stableId, Entity owner, Vector3 worldPosition)
        {
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind != BehaviorKind.Sound || slot.Sound.SoundAssetId <= 0 ||
                    slot.SlotIndex < 0 || slot.SlotIndex >= 32 || (stopMask & (1u << slot.SlotIndex)) == 0)
                    continue;
                _soundRequests.Add(new SoundRequest
                {
                    Kind = SoundRequestKind.Stop,
                    StableId = PerformerBehaviorRuntimeUtility.ComposeBehaviorStableId(stableId, slot.SlotIndex),
                    SoundAssetId = slot.Sound.SoundAssetId,
                    Owner = owner,
                    WorldPosition = worldPosition,
                });
            }
        }
        private void ApplySpline(Entity entity, ref PerformerState state, in SplineConfig config, float dt)
        {
            if (config.Usage != SplineUsage.Patrol || config.ProgressParamKey < 0) return;
            float progress = _runtime.ResolveFloat(entity, config.ProgressParamKey, 0f);
            float speed = config.SpeedParamKey >= 0 ? _runtime.ResolveFloat(entity, config.SpeedParamKey, 0f) : 0f;
            progress += dt * speed;
            if (config.Loop) progress = progress - MathF.Floor(progress);
            else progress = Math.Clamp(progress, 0f, 1f);
            _runtime.SetParam(entity, config.ProgressParamKey, ParamLane.Float, progress, 0, Vector4.Zero);
            if (World.Has<PerformerTransformSource>(entity))
            {
                ref var ts = ref World.Get<PerformerTransformSource>(entity);
                ts.Value = TransformSource.SplineDriven;
            }
            if (World.Has<PerformerWorldPosition>(entity))
            {
                ref var pos = ref World.Get<PerformerWorldPosition>(entity);
                pos.Value = new Vector3(progress, pos.Value.Y, 0f);
            }
            if (World.Has<PerformerWorldRotation>(entity))
            {
                ref var rot = ref World.Get<PerformerWorldRotation>(entity);
                rot.Value = Quaternion.Identity;
            }
        }

        private void ApplyAttachment(Entity entity, ref PerformerState state, in AttachmentConfig config)
        {
            if (!World.Has<PerformerParent>(entity)) return;
            Entity parentEntity = World.Get<PerformerParent>(entity).Parent;
            if (parentEntity == Entity.Null || !World.IsAlive(parentEntity)) return;
            if (!World.Has<PerformerState>(parentEntity)) return;
            PerformerState parentState = World.Get<PerformerState>(parentEntity);
            Vector3 parentPos = World.Has<PerformerWorldPosition>(parentEntity) ? World.Get<PerformerWorldPosition>(parentEntity).Value : Vector3.Zero;
            Quaternion parentRot = World.Has<PerformerWorldRotation>(parentEntity) ? World.Get<PerformerWorldRotation>(parentEntity).Value : Quaternion.Identity;
            Vector3 parentScale = World.Has<PerformerWorldScale>(parentEntity) ? World.Get<PerformerWorldScale>(parentEntity).Value : Vector3.One;
            switch (config.Target)
            {
                case AttachmentTarget.Parent:
                    ApplyParentAttachment(entity, parentPos, parentRot, parentScale, config);
                    return;
                case AttachmentTarget.Bone:
                    ApplyBoneAttachment(entity, parentState.StableId, config);
                    return;
            }
        }

        private void ApplyParentAttachment(Entity entity, Vector3 parentPos, Quaternion parentRot, Vector3 parentScale, in AttachmentConfig config)
        {
            Quaternion normalizedParentRot = NormalizeOrIdentity(parentRot);
            Vector3 normalizedParentScale = NormalizeScale(parentScale);
            Vector3 scaledOffset = config.InheritScale ? normalizedParentScale * config.Offset : config.Offset;
            SetTransform(entity, TransformSource.AttachedToParent,
                parentPos + Vector3.Transform(scaledOffset, normalizedParentRot),
                NormalizeOrIdentity(normalizedParentRot * NormalizeOrIdentity(config.RotationOffset)),
                config.InheritScale ? normalizedParentScale : Vector3.One);
        }

        private void ApplyBoneAttachment(Entity entity, int parentStableId, in AttachmentConfig config)
        {
            if (_boneTransformProvider == null || config.BoneId <= 0) return;
            if (!_boneTransformProvider.TryGetBoneWorldTransform(parentStableId, config.BoneId,
                    out Vector3 bonePosition, out Quaternion boneRotation, out Vector3 boneScale))
                return;
            Quaternion normalizedBoneRotation = NormalizeOrIdentity(boneRotation);
            SetTransform(entity, TransformSource.BoneAttached,
                bonePosition + Vector3.Transform(config.Offset, normalizedBoneRotation),
                NormalizeOrIdentity(normalizedBoneRotation * NormalizeOrIdentity(config.RotationOffset)),
                config.InheritScale ? NormalizeScale(boneScale) : Vector3.One);
        }

        private void SetTransform(Entity entity, TransformSource source, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (World.Has<PerformerTransformSource>(entity))
                World.Get<PerformerTransformSource>(entity).Value = source;
            if (World.Has<PerformerWorldPosition>(entity))
                World.Get<PerformerWorldPosition>(entity).Value = position;
            if (World.Has<PerformerWorldRotation>(entity))
                World.Get<PerformerWorldRotation>(entity).Value = rotation;
            if (World.Has<PerformerWorldScale>(entity))
                World.Get<PerformerWorldScale>(entity).Value = scale;
        }
        private void ResolveTransform(Entity entity, ref PerformerState state, BehaviorSlot[] behaviors)
        {
            AssetBindingConfig assetBinding = new AssetBindingConfig { LocalScale = Vector3.One };
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind == BehaviorKind.AssetBinding && IsBehaviorActive(state.BehaviorActiveMask, slot.SlotIndex))
                {
                    assetBinding = slot.AssetBinding;
                    break;
                }
            }

            Entity parentEntity = World.Has<PerformerParent>(entity) ? World.Get<PerformerParent>(entity).Parent : Entity.Null;
            bool hasParent = parentEntity != Entity.Null && World.IsAlive(parentEntity) && World.Has<PerformerState>(parentEntity);
            PerformerInstance parent = default;
            if (hasParent)
            {
                parent.WorldPosition = World.Has<PerformerWorldPosition>(parentEntity) ? World.Get<PerformerWorldPosition>(parentEntity).Value : Vector3.Zero;
                parent.WorldRotation = World.Has<PerformerWorldRotation>(parentEntity) ? World.Get<PerformerWorldRotation>(parentEntity).Value : Quaternion.Identity;
                parent.WorldScale = World.Has<PerformerWorldScale>(parentEntity) ? World.Get<PerformerWorldScale>(parentEntity).Value : Vector3.One;
            }

            bool hasOwnerTransform = World.IsAlive(state.OwnerEntity) && World.Has<VisualTransform>(state.OwnerEntity);
            VisualTransform ownerTransform = hasOwnerTransform ? World.Get<VisualTransform>(state.OwnerEntity) : default;

            PerformerInstance instance = default;
            instance.WorldPosition = World.Has<PerformerWorldPosition>(entity) ? World.Get<PerformerWorldPosition>(entity).Value : Vector3.Zero;
            instance.WorldRotation = World.Has<PerformerWorldRotation>(entity) ? World.Get<PerformerWorldRotation>(entity).Value : Quaternion.Identity;
            instance.WorldScale = World.Has<PerformerWorldScale>(entity) ? World.Get<PerformerWorldScale>(entity).Value : Vector3.One;
            instance.TransformSource = World.Has<PerformerTransformSource>(entity) ? World.Get<PerformerTransformSource>(entity).Value : TransformSource.EntityTransform;
            instance.AnchorKind = state.AnchorKind;
            instance.ParentHandle = hasParent ? 0 : -1;

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                instance, parent, hasParent, ownerTransform, hasOwnerTransform, assetBinding, _heightmapProvider());

            if (World.Has<PerformerWorldPosition>(entity))
                World.Get<PerformerWorldPosition>(entity).Value = resolved.Position;
            if (World.Has<PerformerWorldRotation>(entity))
                World.Get<PerformerWorldRotation>(entity).Value = resolved.Rotation;
            if (World.Has<PerformerWorldScale>(entity))
                World.Get<PerformerWorldScale>(entity).Value = resolved.Scale;
        }

        private void ResolveDefaultTransformSource(Entity entity, ref PerformerState state)
        {
            if (!World.Has<PerformerTransformSource>(entity)) return;
            ref var ts = ref World.Get<PerformerTransformSource>(entity);
            if (ts.Value is TransformSource.BoneAttached or TransformSource.AttachedToParent)
                return;
            Entity parentEntity = World.Has<PerformerParent>(entity) ? World.Get<PerformerParent>(entity).Parent : Entity.Null;
            if (parentEntity != Entity.Null && World.IsAlive(parentEntity))
            {
                ts.Value = TransformSource.InheritParent;
                return;
            }
            ts.Value = state.AnchorKind == PresentationAnchorKind.Entity
                ? TransformSource.EntityTransform
                : TransformSource.WorldFixed;
        }

        private static float ResolveAttributeValue(ref AttributeBuffer attributes, int attributeId, ValueSourceKind mode)
        {
            return mode switch
            {
                ValueSourceKind.Attribute => attributes.GetCurrent(attributeId),
                ValueSourceKind.AttributeRatio => ResolveAttributeRatio(ref attributes, attributeId),
                ValueSourceKind.AttributeBase => attributes.GetBase(attributeId),
                _ => attributes.GetCurrent(attributeId),
            };
        }

        private static float ResolveAttributeRatio(ref AttributeBuffer attributes, int attributeId)
        {
            float current = attributes.GetCurrent(attributeId);
            float max = attributes.GetBase(attributeId);
            return max <= 0f ? 0f : Math.Clamp(current / max, 0f, 1f);
        }

        private float ResolveEntityColorChannel(Entity owner, int channelIndex)
        {
            Vector4 color = ResolveEntityColor(owner);
            return channelIndex switch { 0 => color.X, 1 => color.Y, 2 => color.Z, 3 => color.W, _ => 0f };
        }

        private Vector4 ResolveEntityColor(Entity owner)
        {
            return World.IsAlive(owner) ? TeamColorResolver.Resolve(World, owner) : TeamColorResolver.DefaultColor;
        }

        private float ResolveFacingRadians(Entity owner)
        {
            if (!World.IsAlive(owner) || !World.Has<FacingDirection>(owner)) return 0f;
            return World.Get<FacingDirection>(owner).AngleRad;
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            return value.LengthSquared() > 0.000001f ? Quaternion.Normalize(value) : Quaternion.Identity;
        }

        private static Vector3 NormalizeScale(Vector3 value)
        {
            return value == Vector3.Zero ? Vector3.One : value;
        }
    }
}




