using System;
using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Presenters
{
    public static class PresenterAttachmentTransform
    {
        internal static void ValidateActiveDrivers(ReadOnlySpan<BehaviorSlot> definitionSlots,
            ReadOnlySpan<BehaviorSlot> instanceSlots, uint mask, string context)
        {
            int drivers = 0;
            bool bone = false;
            bool grounding = false;
            CollectDrivers(definitionSlots, mask, ref drivers, ref bone, ref grounding);
            CollectDrivers(instanceSlots, mask, ref drivers, ref bone, ref grounding);
            if (drivers > 1 || (bone && grounding))
                throw new InvalidOperationException($"PRESENTATION.PRESENTER.ERR.TransformConflict: {context}; only one base transform driver may be active; Bone cannot combine with Grounding.");
        }

        private static void CollectDrivers(ReadOnlySpan<BehaviorSlot> slots, uint mask,
            ref int drivers, ref bool bone, ref bool grounding)
        {
            foreach (ref readonly BehaviorSlot slot in slots)
            {
                if ((mask & (1u << slot.SlotIndex)) == 0) continue;
                if (slot.Kind == BehaviorKind.Attachment)
                {
                    drivers++;
                    bone |= slot.Attachment.Target == AttachmentTarget.Bone;
                }
                else if (slot.Kind == BehaviorKind.Spline && slot.Spline.Usage == SplineUsage.Patrol)
                    drivers++;
                else if (slot.Kind == BehaviorKind.Grounding && slot.Grounding.Mode != GroundingMode.None)
                    grounding = true;
            }
        }

        public static void Validate(in AttachmentConfig config, string context)
        {
            if (!Enum.IsDefined(config.Target) || !Enum.IsDefined(config.UpdatePolicy) ||
                (config.Inherit & ~(AttachmentInheritance.Position | AttachmentInheritance.Rotation | AttachmentInheritance.Scale)) != 0 ||
                (config.Target == AttachmentTarget.Bone ? config.BoneId <= 0 : config.BoneId != 0) ||
                config.LocalPositionParamKey < 0 || config.LocalRotationParamKey < 0 || config.LocalScaleParamKey < 0)
            {
                throw new InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.Config: {context} has an invalid target, boneId, updatePolicy, inherit or parameter reference.");
            }
        }

        public static bool TryResolve(
            World world, Entity entity, in AttachmentConfig config,
            in Vector3 targetPosition, in Quaternion targetRotation, in Vector3 targetScale,
            in PresenterWorldFacing targetFacing, out PresenterResolvedTransform result)
        {
            PresenterAttachmentState state = world.Get<PresenterAttachmentState>(entity);
            if (state.Initialized && config.UpdatePolicy == AttachmentUpdatePolicy.Once)
            {
                result = default;
                return false;
            }

            if (!state.ParametersValid)
            {
                ResolveParameters(world, entity, in config, ref state, default);
            }
            RequireFinite(targetPosition, entity);
            RequireFinite(targetScale, entity);
            Quaternion rotationBasis = (config.Inherit & AttachmentInheritance.Rotation) != 0
                ? RequireRotation(targetRotation, entity) : Quaternion.Identity;
            Vector3 scaleBasis = (config.Inherit & AttachmentInheritance.Scale) != 0 ? targetScale : Vector3.One;

            Vector3 positionOrigin = !state.Initialized || (config.Inherit & AttachmentInheritance.Position) != 0
                ? targetPosition : state.PositionOrigin;
            Quaternion positionRotation = !state.Initialized || (config.Inherit & AttachmentInheritance.Position) != 0
                ? rotationBasis : state.PositionRotation;
            Vector3 positionScale = !state.Initialized || (config.Inherit & AttachmentInheritance.Position) != 0
                ? scaleBasis : state.PositionScale;
            result = new PresenterResolvedTransform
            {
                Position = positionOrigin + Vector3.Transform(positionScale * state.LocalPosition, positionRotation),
                Rotation = Quaternion.Normalize(rotationBasis * state.LocalRotation),
                Scale = scaleBasis * state.LocalScale,
                Facing = (config.Inherit & AttachmentInheritance.Rotation) != 0 ? targetFacing : default,
            };
            RequireFinite(result.Position, entity);
            RequireFinite(result.Scale, entity);
            if (!state.Initialized)
            {
                state.PositionOrigin = targetPosition;
                state.PositionRotation = rotationBasis;
                state.PositionScale = scaleBasis;
                state.Initialized = true;
            }
            world.Get<PresenterAttachmentState>(entity) = state;
            return true;
        }

        internal static void ValidateParameters(World world, Entity entity, in AttachmentConfig config,
            in PresenterParamMutation mutation = default)
        {
            PresenterAttachmentState state = default;
            ResolveParameters(world, entity, in config, ref state, in mutation);
        }

        internal static void ResolveChangedParameter(World world, Entity entity, in AttachmentConfig config,
            ref PresenterAttachmentState state, int key, in PresenterParamMutation mutation = default)
        {
            Vector4 value = RequireVector(world, entity, key, in mutation);
            if (key == config.LocalPositionParamKey || key == config.LocalScaleParamKey)
            {
                if (value.W != 0f)
                    throw new InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.ParamValue: presenter={entity.Id}, position and scale require W=0.");
                if (key == config.LocalPositionParamKey) state.LocalPosition = new(value.X, value.Y, value.Z);
                if (key == config.LocalScaleParamKey) state.LocalScale = new(value.X, value.Y, value.Z);
            }
            if (key == config.LocalRotationParamKey)
                state.LocalRotation = RequireRotation(new(value.X, value.Y, value.Z, value.W), entity);
        }

        private static void ResolveParameters(World world, Entity entity, in AttachmentConfig config,
            ref PresenterAttachmentState state, in PresenterParamMutation mutation)
        {
            Vector4 position = RequireVector(world, entity, config.LocalPositionParamKey, in mutation);
            Vector4 rotation = RequireVector(world, entity, config.LocalRotationParamKey, in mutation);
            Vector4 scale = RequireVector(world, entity, config.LocalScaleParamKey, in mutation);
            if (position.W != 0f || scale.W != 0f)
                throw new InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.ParamValue: presenter={entity.Id}, position and scale require W=0.");
            Quaternion localRotation = RequireRotation(new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), entity);
            state.LocalPosition = new Vector3(position.X, position.Y, position.Z);
            state.LocalRotation = localRotation;
            state.LocalScale = new Vector3(scale.X, scale.Y, scale.Z);
            state.ParametersValid = true;
        }

        private static Vector4 RequireVector(World world, Entity entity, int key, in PresenterParamMutation mutation)
        {
            if (!PresenterParamResolver.TryResolveVector(world, entity, key, out Vector4 value, in mutation, true))
            {
                throw new InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.ParamMissing: presenter={entity.Id}, Vector paramKey={key}.");
            }
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            {
                throw new InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.ParamValue: presenter={entity.Id}, Vector paramKey={key} must be finite.");
            }
            return value;
        }

        private static Quaternion RequireRotation(Quaternion value, Entity entity)
        {
            float length = value.LengthSquared();
            if (!float.IsFinite(length) || length <= 0f)
            {
                throw new InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.Rotation: presenter={entity.Id}, quaternion must be finite and nonzero.");
            }
            return Quaternion.Normalize(value);
        }

        private static void RequireFinite(in Vector3 value, Entity entity)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            {
                throw new InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.Transform: presenter={entity.Id}, transform must be finite.");
            }
        }
    }
}
