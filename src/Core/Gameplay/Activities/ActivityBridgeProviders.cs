using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Providers;

namespace Ludots.Core.Gameplay.Activities
{
    public sealed class ActivityOfferEffectHandler : IEffectHandler
    {
        private readonly ActivityRuntimeService _runtime;

        public ActivityOfferEffectHandler(ActivityRuntimeService runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Execute(in ProviderEffectCall call, ProviderExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!call.Parameters.TryGetValue("activity_id", out object? activityIdObj) ||
                activityIdObj is not string activityId ||
                string.IsNullOrWhiteSpace(activityId))
            {
                throw new InvalidOperationException(
                    "activity.offer requires parameter activity_id.");
            }

            _runtime.OfferOrActivate(activityId, context.Subject);
        }
    }

    /// <summary>
    /// Read-only Gate / Execution Condition over the scope host's GAS attribute.
    /// False when the subject is missing or carries no AttributeBuffer; unknown
    /// attribute keys fail closed with the key in the message.
    /// </summary>
    public sealed class SubjectAttributeConditionProvider : IConditionProvider
    {
        public bool Evaluate(ProviderExecutionContext context, IReadOnlyDictionary<string, object?> parameters)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(parameters);

            if (!parameters.TryGetValue("attribute_key", out object? keyObj) ||
                keyObj is not string attributeKey ||
                string.IsNullOrWhiteSpace(attributeKey))
            {
                throw new InvalidOperationException(
                    "world.subject_attribute requires parameter attribute_key.");
            }

            if (!parameters.TryGetValue("op", out object? opObj) ||
                opObj is not string op ||
                string.IsNullOrWhiteSpace(op))
            {
                throw new InvalidOperationException(
                    "world.subject_attribute requires parameter op.");
            }

            if (!parameters.TryGetValue("value", out object? valueObj) ||
                valueObj is not double and not float and not int and not long)
            {
                throw new InvalidOperationException(
                    "world.subject_attribute requires numeric parameter value.");
            }

            int attributeId = AttributeRegistry.GetId(attributeKey);
            if (attributeId < 0)
            {
                throw new InvalidOperationException(
                    $"world.subject_attribute references unknown attribute '{attributeKey}'.");
            }

            if (!context.World.IsAlive(context.Subject) ||
                !context.World.TryGet<AttributeBuffer>(context.Subject, out AttributeBuffer buffer))
            {
                return false;
            }

            float current = buffer.GetCurrent(attributeId);
            float target = Convert.ToSingle(valueObj);
            return op switch
            {
                "greater" => current > target,
                "greater_equal" => current >= target,
                "less" => current < target,
                "less_equal" => current <= target,
                "equal" => MathF.Abs(current - target) < 0.0001f,
                _ => throw new InvalidOperationException(
                    $"world.subject_attribute has unknown op '{op}'."),
            };
        }
    }

    public static class ActivityBridgeProviderInstaller
    {
        public const string SubjectAttributeConditionKey = "world.subject_attribute";

        public static void Install(ProviderServices providers, ActivityRuntimeService runtime)
        {
            ArgumentNullException.ThrowIfNull(providers);
            ArgumentNullException.ThrowIfNull(runtime);

            providers.Gaps.TryResolve("activity.offer", out _);

            if (!providers.Effects.Contains("activity.offer"))
            {
                providers.Effects.Register(
                    "activity.offer",
                    new ActivityOfferEffectHandler(runtime),
                    new ProviderParameterSchema(new[]
                    {
                        new ProviderParameterField("activity_id", ProviderParameterKind.String, required: true),
                    }));
            }

            if (!providers.Conditions.Contains(SubjectAttributeConditionKey))
            {
                providers.Conditions.Register(
                    SubjectAttributeConditionKey,
                    new SubjectAttributeConditionProvider(),
                    new ProviderParameterSchema(new[]
                    {
                        new ProviderParameterField("attribute_key", ProviderParameterKind.String, required: true),
                        new ProviderParameterField("op", ProviderParameterKind.String, required: true),
                        new ProviderParameterField("value", ProviderParameterKind.Float, required: true),
                    }));
            }
        }
    }
}
