using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Core.Presentation.Systems
{
    public sealed class PerformerBehaviorSystem : BaseSystem<World, float>
    {
        private readonly PerformerInstanceBuffer _instances;
        private readonly PerformerDefinitionRegistry _definitions;
        private readonly PresentationEventStream _events;
        private readonly SoundRequestBuffer _soundRequests;
        private readonly Func<IVisualHeightmap?> _heightmapProvider;
        private readonly IBoneTransformProvider? _boneTransformProvider;
        private readonly uint[] _activeSoundMasks;
        private readonly int[] _soundStableIds;
        private readonly int[] _soundDefinitionIds;

        public PerformerBehaviorSystem(
            World world,
            PerformerInstanceBuffer instances,
            PerformerDefinitionRegistry definitions,
            PresentationEventStream events,
            SoundRequestBuffer soundRequests,
            IVisualHeightmap? heightmap = null,
            IBoneTransformProvider? boneTransformProvider = null)
            : this(
                world,
                instances,
                definitions,
                events,
                soundRequests,
                () => heightmap,
                boneTransformProvider)
        {
        }

        public PerformerBehaviorSystem(
            World world,
            PerformerInstanceBuffer instances,
            PerformerDefinitionRegistry definitions,
            PresentationEventStream events,
            SoundRequestBuffer soundRequests,
            Func<IVisualHeightmap?> heightmapProvider,
            IBoneTransformProvider? boneTransformProvider = null)
            : base(world)
        {
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _soundRequests = soundRequests ?? throw new ArgumentNullException(nameof(soundRequests));
            _heightmapProvider = heightmapProvider ?? throw new ArgumentNullException(nameof(heightmapProvider));
            _boneTransformProvider = boneTransformProvider;
            _activeSoundMasks = new uint[_instances.Capacity];
            _soundStableIds = new int[_instances.Capacity];
            _soundDefinitionIds = new int[_instances.Capacity];
        }

        public override void Update(in float dt)
        {
            float tickDt = dt;
            _instances.ProcessActive(0f, (int handle, ref PerformerInstance instance) =>
            {
                if (!_definitions.TryGet(instance.DefId, out PerformerDefinition definition))
                {
                    return;
                }

                BehaviorSlot[] behaviors = definition.Behaviors;
                instance.TransformSource = ResolveDefaultTransformSource(in instance);
                HandleReusedSoundSlot(handle, in instance, behaviors);
                uint currentSoundMask = 0u;
                for (int i = 0; i < behaviors.Length; i++)
                {
                    ref readonly BehaviorSlot slot = ref behaviors[i];
                    if (!IsBehaviorActive(instance.BehaviorActiveMask, slot.SlotIndex))
                    {
                        continue;
                    }

                    switch (slot.Kind)
                    {
                        case BehaviorKind.AttributeBinding:
                            ApplyAttributeBinding(handle, instance.Owner, slot.AttributeBinding);
                            break;

                        case BehaviorKind.TagBinding:
                            ApplyTagBinding(handle, instance.Owner, slot.TagBinding);
                            break;

                        case BehaviorKind.Material:
                            ApplyMaterialBinding(handle, slot.Material);
                            break;

                        case BehaviorKind.Attachment:
                            ApplyAttachment(ref instance, slot.Attachment);
                            break;

                        case BehaviorKind.Sound:
                            ApplySound(handle, in instance, slot);
                            currentSoundMask |= 1u << slot.SlotIndex;
                            break;

                        case BehaviorKind.Spline:
                            ApplySpline(handle, ref instance, slot.Spline, tickDt);
                            break;
                    }
                }

                StopInactiveSounds(handle, in instance, behaviors, currentSoundMask);
                _activeSoundMasks[handle] = currentSoundMask;
                _soundStableIds[handle] = instance.StableId;
                _soundDefinitionIds[handle] = instance.DefId;
                ResolveTransform(handle, ref instance, behaviors);
            });

            StopDestroyedSounds();
        }

        private void ApplyAttributeBinding(int handle, Entity owner, in AttributeBindingConfig config)
        {
            if (!World.IsAlive(owner) || !World.Has<AttributeBuffer>(owner))
            {
                return;
            }

            ref AttributeBuffer attributes = ref World.Get<AttributeBuffer>(owner);
            float value = ResolveAttributeValue(ref attributes, config.AttributeId, config.Mode);
            _instances.SetParam(handle, config.TargetParamKey, ParamLane.Float, value, 0, Vector4.Zero);

            ThresholdMapping[] thresholds = config.Thresholds ?? Array.Empty<ThresholdMapping>();
            for (int i = 0; i < thresholds.Length; i++)
            {
                ref readonly ThresholdMapping threshold = ref thresholds[i];
                if (value <= threshold.Threshold)
                {
                    int thresholdIntValue = (int)threshold.OutputValue;
                    _instances.SetParam(handle, threshold.OutputParamKey, ParamLane.Float, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                    _instances.SetParam(handle, threshold.OutputParamKey, ParamLane.Int, threshold.OutputValue, thresholdIntValue, Vector4.Zero);
                    break;
                }
            }
        }

        private void ApplyTagBinding(int handle, Entity owner, in TagBindingConfig config)
        {
            bool active = false;
            if (World.IsAlive(owner) && World.Has<GameplayTagContainer>(owner))
            {
                ref GameplayTagContainer tags = ref World.Get<GameplayTagContainer>(owner);
                active = tags.HasTag(config.TagId);
            }

            if (config.InvertLogic)
            {
                active = !active;
            }

            _instances.SetParam(handle, config.TargetParamKey, ParamLane.Int, 0f, active ? 1 : 0, Vector4.Zero);
        }

        private void ApplyMaterialBinding(int handle, in MaterialConfig config)
        {
            int materialId = config.BaseMaterialId;
            if (config.MaterialSwapParamKey >= 0)
            {
                float paramValue = _instances.ResolveFloat(handle, config.MaterialSwapParamKey, float.NaN);
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
            {
                _instances.SetParam(handle, config.MaterialSwapParamKey, ParamLane.Int, 0f, materialId, Vector4.Zero);
            }
        }

        private void ApplySound(int handle, in PerformerInstance instance, in BehaviorSlot slot)
        {
            if (slot.Sound.SoundAssetId <= 0)
            {
                return;
            }

            float volume = slot.Sound.Volume;
            if (slot.Sound.VolumeParamKey >= 0)
            {
                volume = _instances.ResolveFloat(handle, slot.Sound.VolumeParamKey, volume);
            }

            _soundRequests.Add(new SoundRequest
            {
                Kind = SoundRequestKind.PlayOrUpdate,
                StableId = PerformerBehaviorRuntimeUtility.ComposeBehaviorStableId(instance.StableId, slot.SlotIndex),
                SoundAssetId = slot.Sound.SoundAssetId,
                Loop = slot.Sound.Loop,
                Volume = Math.Clamp(volume, 0f, 1f),
                WorldPosition = instance.WorldPosition,
                Owner = instance.Owner,
            });
        }

        private void StopInactiveSounds(int handle, in PerformerInstance instance, BehaviorSlot[] behaviors, uint currentSoundMask)
        {
            uint previousSoundMask = _activeSoundMasks[handle];
            uint stopMask = previousSoundMask & ~currentSoundMask;
            if (stopMask == 0u)
            {
                return;
            }

            EmitStopRequests(stopMask, behaviors, instance.StableId, instance.Owner, instance.WorldPosition);
        }

        private void StopDestroyedSounds()
        {
            ReadOnlySpan<PresentationEvent> events = _events.GetSpan();
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly PresentationEvent evt = ref events[i];
                if (evt.Kind != PresentationEventKind.PerformerDestroyed)
                {
                    continue;
                }

                int handle = evt.PayloadA;
                if ((uint)handle >= (uint)_activeSoundMasks.Length)
                {
                    continue;
                }

                uint previousSoundMask = _activeSoundMasks[handle];
                int stableId = (int)evt.Magnitude;
                if (_soundStableIds[handle] != stableId)
                {
                    continue;
                }

                if (previousSoundMask == 0u || !_definitions.TryGet(evt.KeyId, out PerformerDefinition definition))
                {
                    _activeSoundMasks[handle] = 0u;
                    _soundStableIds[handle] = 0;
                    _soundDefinitionIds[handle] = 0;
                    continue;
                }

                EmitStopRequests(previousSoundMask, definition.Behaviors, stableId, evt.Source, Vector3.Zero);
                _activeSoundMasks[handle] = 0u;
                _soundStableIds[handle] = 0;
                _soundDefinitionIds[handle] = 0;
            }
        }

        private void HandleReusedSoundSlot(int handle, in PerformerInstance instance, BehaviorSlot[] _)
        {
            if (_soundStableIds[handle] == 0 || _soundStableIds[handle] == instance.StableId)
            {
                return;
            }

            uint previousSoundMask = _activeSoundMasks[handle];
            if (previousSoundMask != 0u &&
                _soundDefinitionIds[handle] > 0 &&
                _definitions.TryGet(_soundDefinitionIds[handle], out PerformerDefinition previousDefinition))
            {
                EmitStopRequests(previousSoundMask, previousDefinition.Behaviors, _soundStableIds[handle], instance.Owner, instance.WorldPosition);
            }

            _activeSoundMasks[handle] = 0u;
            _soundStableIds[handle] = 0;
            _soundDefinitionIds[handle] = 0;
        }

        private void EmitStopRequests(uint stopMask, BehaviorSlot[] behaviors, int stableId, Entity owner, Vector3 worldPosition)
        {
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind != BehaviorKind.Sound ||
                    slot.Sound.SoundAssetId <= 0 ||
                    slot.SlotIndex < 0 ||
                    slot.SlotIndex >= 32 ||
                    (stopMask & (1u << slot.SlotIndex)) == 0)
                {
                    continue;
                }

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

        private void ApplySpline(int handle, ref PerformerInstance instance, in SplineConfig config, float dt)
        {
            if (config.Usage != SplineUsage.Patrol || config.ProgressParamKey < 0)
            {
                return;
            }

            float progress = _instances.ResolveFloat(handle, config.ProgressParamKey, 0f);
            float speed = config.SpeedParamKey >= 0 ? _instances.ResolveFloat(handle, config.SpeedParamKey, 0f) : 0f;
            progress += dt * speed;

            if (config.Loop)
            {
                progress = progress - MathF.Floor(progress);
            }
            else
            {
                progress = Math.Clamp(progress, 0f, 1f);
            }

            _instances.SetParam(handle, config.ProgressParamKey, ParamLane.Float, progress, 0, Vector4.Zero);
            instance.TransformSource = TransformSource.SplineDriven;
            instance.WorldPosition = new Vector3(progress, instance.WorldPosition.Y, 0f);
            instance.WorldRotation = Quaternion.Identity;
        }

        private void ResolveTransform(int handle, ref PerformerInstance instance, BehaviorSlot[] behaviors)
        {
            AssetBindingConfig assetBinding = new AssetBindingConfig
            {
                LocalScale = Vector3.One,
            };
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind == BehaviorKind.AssetBinding && IsBehaviorActive(instance.BehaviorActiveMask, slot.SlotIndex))
                {
                    assetBinding = slot.AssetBinding;
                    break;
                }
            }

            bool hasParent = instance.ParentHandle >= 0 && _instances.IsActive(instance.ParentHandle);
            PerformerInstance parent = hasParent ? _instances.Get(instance.ParentHandle) : default;
            bool hasOwnerTransform = World.IsAlive(instance.Owner) && World.Has<VisualTransform>(instance.Owner);
            VisualTransform ownerTransform = hasOwnerTransform ? World.Get<VisualTransform>(instance.Owner) : default;

            PerformerResolvedTransform resolved = PerformerGroundingUtility.ResolveTransform(
                instance,
                parent,
                hasParent,
                ownerTransform,
                hasOwnerTransform,
                assetBinding,
                _heightmapProvider());

            instance.WorldPosition = resolved.Position;
            instance.WorldRotation = resolved.Rotation;
            instance.WorldScale = resolved.Scale;
        }

        private void ApplyAttachment(ref PerformerInstance instance, in AttachmentConfig config)
        {
            if (_boneTransformProvider == null ||
                config.BoneId <= 0 ||
                instance.ParentHandle < 0 ||
                !_instances.IsActive(instance.ParentHandle))
            {
                return;
            }

            PerformerInstance parent = _instances.Get(instance.ParentHandle);
            if (!_boneTransformProvider.TryGetBoneWorldTransform(
                    parent.StableId,
                    config.BoneId,
                    out Vector3 bonePosition,
                    out Quaternion boneRotation,
                    out Vector3 boneScale))
            {
                return;
            }

            Quaternion normalizedBoneRotation = NormalizeOrIdentity(boneRotation);
            instance.TransformSource = TransformSource.BoneAttached;
            instance.WorldPosition = bonePosition + Vector3.Transform(config.Offset, normalizedBoneRotation);
            instance.WorldRotation = NormalizeOrIdentity(normalizedBoneRotation * NormalizeOrIdentity(config.RotationOffset));
            instance.WorldScale = config.InheritScale ? NormalizeScale(boneScale) : Vector3.One;
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
            if (max <= 0f)
            {
                return 0f;
            }

            return Math.Clamp(current / max, 0f, 1f);
        }

        private static bool IsBehaviorActive(uint mask, int slotIndex)
        {
            return slotIndex is >= 0 and < 32 && (mask & (1u << slotIndex)) != 0;
        }

        private static TransformSource ResolveDefaultTransformSource(in PerformerInstance instance)
        {
            if (instance.ParentHandle >= 0)
            {
                return TransformSource.InheritParent;
            }

            return instance.AnchorKind == PresentationAnchorKind.Entity
                ? TransformSource.EntityTransform
                : TransformSource.WorldFixed;
        }

        private static Quaternion NormalizeOrIdentity(Quaternion value)
        {
            return value.LengthSquared() > 0.000001f
                ? Quaternion.Normalize(value)
                : Quaternion.Identity;
        }

        private static Vector3 NormalizeScale(Vector3 value)
        {
            return value == Vector3.Zero ? Vector3.One : value;
        }
    }
}
