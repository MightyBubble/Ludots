using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using System;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Utility for building merged ConfigParams from template + caller overrides.
    /// Used by Effect processing systems before executing phase graphs/handlers.
    /// </summary>
    public static class ConfigParamsMerger
    {
        /// <summary>
        /// Build merged ConfigParams for an effect entity.
        /// If the entity carries a pre-merged <see cref="EffectConfigParams"/> component
        /// (attached at creation time), returns that directly.
        /// Otherwise falls back to the shared template params.
        /// </summary>
        public static EffectConfigParams BuildMergedConfig(
            World world,
            Entity effectEntity,
            in EffectConfigParams templateParams)
        {
            if (world.IsAlive(effectEntity) && world.Has<EffectConfigParams>(effectEntity))
            {
                return world.Get<EffectConfigParams>(effectEntity);
            }

            return templateParams;
        }

        /// <summary>
        /// Build merged ConfigParams from template + request-level CallerParams.
        /// Used for instant effects processed directly from EffectRequest (no entity).
        /// </summary>
        public static EffectConfigParams BuildMergedConfig(
            in EffectConfigParams templateParams,
            in EffectRequest request)
        {
            var merged = templateParams;

            if (request.HasCallerParams)
            {
                merged.MergeFrom(in request.CallerParams);
            }

            return merged;
        }

        public static int ResolveDurationTicks(in EffectTemplateData template, in EffectConfigParams mergedParams)
        {
            int durationTicks = ResolveTickParam(
                in mergedParams,
                EffectParamKeys.DurationTicks,
                "_ep.durationTicks",
                template.DurationTicks);

            if (template.LifetimeKind == EffectLifetimeKind.After && durationTicks <= 0)
            {
                throw new InvalidOperationException(
                    $"GAS.CONFIG_PARAMS.ERR.InvalidDurationTicks: key=_ep.durationTicks, lifetime={template.LifetimeKind}, value={durationTicks}.");
            }

            return durationTicks;
        }

        public static int ResolvePeriodTicks(in EffectTemplateData template, in EffectConfigParams mergedParams)
        {
            return ResolveTickParam(
                in mergedParams,
                EffectParamKeys.PeriodTicks,
                "_ep.periodTicks",
                template.PeriodTicks);
        }

        public static int ResolvePayloadEffectTemplateId(in TargetDispatchDescriptor dispatch, in EffectConfigParams mergedParams)
        {
            if (!TryReadIntegralParam(
                    in mergedParams,
                    EffectParamKeys.PayloadEffectId,
                    "_ep.payloadEffectId",
                    allowFloat: true,
                    allowEffectTemplate: true,
                    out int templateId))
            {
                return dispatch.PayloadEffectTemplateId;
            }

            if (templateId < 0)
            {
                throw new InvalidOperationException(
                    $"GAS.CONFIG_PARAMS.ERR.InvalidPayloadEffectId: key=_ep.payloadEffectId, value={templateId}.");
            }

            return templateId;
        }

        private static int ResolveTickParam(
            in EffectConfigParams mergedParams,
            int keyId,
            string keyName,
            int templateValue)
        {
            if (!TryReadIntegralParam(
                    in mergedParams,
                    keyId,
                    keyName,
                    allowFloat: true,
                    allowEffectTemplate: false,
                    out int value))
            {
                return templateValue;
            }

            if (value < 0)
            {
                throw new InvalidOperationException(
                    $"GAS.CONFIG_PARAMS.ERR.InvalidTickValue: key={keyName}, value={value}.");
            }

            return value;
        }

        private static bool TryReadIntegralParam(
            in EffectConfigParams mergedParams,
            int keyId,
            string keyName,
            bool allowFloat,
            bool allowEffectTemplate,
            out int value)
        {
            value = 0;
            if (keyId <= 0 || !mergedParams.TryGetRawValue(keyId, out ConfigParamType type, out int rawValue))
            {
                return false;
            }

            switch (type)
            {
                case ConfigParamType.Int:
                    value = rawValue;
                    return true;

                case ConfigParamType.EffectTemplateId when allowEffectTemplate:
                    value = rawValue;
                    return true;

                case ConfigParamType.Float when allowFloat:
                    float floatValue = BitConverter.Int32BitsToSingle(rawValue);
                    if (!float.IsFinite(floatValue))
                    {
                        throw new InvalidOperationException(
                            $"GAS.CONFIG_PARAMS.ERR.InvalidNumericParam: key={keyName}, value={floatValue}.");
                    }

                    float rounded = MathF.Round(floatValue);
                    if (MathF.Abs(floatValue - rounded) > 0.0001f)
                    {
                        throw new InvalidOperationException(
                            $"GAS.CONFIG_PARAMS.ERR.FractionalTickParam: key={keyName}, value={floatValue}.");
                    }

                    value = (int)rounded;
                    return true;

                default:
                    string expected = allowEffectTemplate ? "Int or EffectTemplate" : "Int";
                    if (allowFloat)
                    {
                        expected += " with a whole-number Float";
                    }

                    throw new InvalidOperationException(
                        $"GAS.CONFIG_PARAMS.ERR.InvalidParamType: key={keyName}, type={type}, expected={expected}.");
            }
        }
    }
}
