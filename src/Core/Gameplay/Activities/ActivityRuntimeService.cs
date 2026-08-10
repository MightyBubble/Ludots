using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Providers;

namespace Ludots.Core.Gameplay.Activities
{
    public readonly record struct ActivityOptionView(
        string OptionId,
        string Title,
        string Body,
        bool IsBaseline,
        bool Visible,
        bool Executable,
        string BlockReason);

    public sealed class ActivityRuntimeService
    {
        private static readonly QueryDescription ActivityQuery = new QueryDescription()
            .WithAll<ActivityInstanceCm>();

        private readonly World _world;
        private readonly ActivityDefinitionRegistry _definitions;
        private readonly ProviderServices _providers;
        private readonly ActivityPresentationBuffer _presentation;
        private readonly Dictionary<(int DefinitionId, int ScopeKey), Entity> _index = new();
        private readonly List<Entity> _scratch = new(64);
        private int _nextInstanceId = 1;

        public ActivityRuntimeService(
            World world,
            ActivityDefinitionRegistry definitions,
            ProviderServices providers,
            ActivityPresentationBuffer presentation)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            RebuildIndexFromWorld();
        }

        public ActivityPresentationBuffer Presentation => _presentation;

        public void ResetState()
        {
            _scratch.Clear();
            _world.Query(in ActivityQuery, (Entity entity, ref ActivityInstanceCm _) =>
            {
                _scratch.Add(entity);
            });

            for (int i = 0; i < _scratch.Count; i++)
            {
                if (_world.IsAlive(_scratch[i]))
                {
                    _world.Destroy(_scratch[i]);
                }
            }

            _index.Clear();
            _presentation.Clear();
            _nextInstanceId = 1;
        }

        public void RebuildIndexFromWorld()
        {
            _index.Clear();
            _world.Query(in ActivityQuery, (Entity entity, ref ActivityInstanceCm activity) =>
            {
                if (activity.State == ActivityInstanceState.Resolved)
                {
                    return;
                }

                _index[(activity.DefinitionId, ScopeKey(activity.ScopeHost))] = entity;
            });
        }

        public Entity OfferOrActivate(
            string activityId,
            Entity scopeHost,
            IReadOnlyDictionary<string, object?>? contextBindings = null)
        {
            if (!_definitions.TryGet(activityId, out ActivityDefinition definition))
            {
                throw new InvalidOperationException($"Unknown activity definition '{activityId}'.");
            }

            int definitionId = _definitions.GetId(activityId);
            var key = (definitionId, ScopeKey(scopeHost));
            if (_index.TryGetValue(key, out Entity existing) &&
                _world.IsAlive(existing) &&
                _world.Has<ActivityInstanceCm>(existing))
            {
                ref ActivityInstanceCm existingActivity = ref _world.Get<ActivityInstanceCm>(existing);
                if (existingActivity.State != ActivityInstanceState.Resolved)
                {
                    return existing;
                }
            }

            Dictionary<string, object?> bindings = ProviderContextBinding.CreateBindings(contextBindings);
            var context = new ProviderExecutionContext(_world, scopeHost, bindings);
            if (definition.TriggerCondition != null &&
                !EvaluateCondition(definition.TriggerCondition, context))
            {
                return Entity.Null;
            }

            int instanceId = _nextInstanceId++;
            Entity entity = _world.Create(
                new ActivityInstanceCm
                {
                    DefinitionId = definitionId,
                    InstanceId = instanceId,
                    State = ActivityInstanceState.Pending,
                    ScopeHost = scopeHost,
                    SelectedOptionIndex = -1,
                    Revision = 1,
                });

            _index[key] = entity;

            if (definition.DispatchPolicy == ActivityDispatchPolicy.Automatic)
            {
                ResolveAutomatic(entity, definition, context);
                return entity;
            }

            ActivateForPresentation(entity, definition);
            return entity;
        }

        public bool TryGetActiveOptions(
            Entity activityEntity,
            IReadOnlyDictionary<string, object?>? contextBindings,
            List<ActivityOptionView> results)
        {
            ArgumentNullException.ThrowIfNull(results);
            results.Clear();
            if (!_world.IsAlive(activityEntity) || !_world.Has<ActivityInstanceCm>(activityEntity))
            {
                return false;
            }

            ActivityInstanceCm instance = _world.Get<ActivityInstanceCm>(activityEntity);
            if (!_definitions.TryGet(instance.DefinitionId, out ActivityDefinition definition))
            {
                return false;
            }

            Dictionary<string, object?> bindings = ProviderContextBinding.CreateBindings(contextBindings);
            var context = new ProviderExecutionContext(_world, instance.ScopeHost, bindings);
            for (int i = 0; i < definition.Options.Count; i++)
            {
                ActivityOptionDefinition option = definition.Options[i];
                bool visible = option.ShowCondition == null || EvaluateCondition(option.ShowCondition, context);
                if (!visible)
                {
                    continue;
                }

                bool executable = option.IsBaseline ||
                    option.ExecuteCondition == null ||
                    EvaluateCondition(option.ExecuteCondition, context);
                string reason = executable
                    ? string.Empty
                    : $"execute_condition_failed:{option.ExecuteCondition?.ConditionKey ?? "unknown"}";

                if (!executable)
                {
                    _presentation.Add(new ActivityPresentationCue(
                        ActivityPresentationCueKind.OptionBlocked,
                        definition.Id,
                        instance.InstanceId,
                        option.Id,
                        reason));
                }

                results.Add(new ActivityOptionView(
                    option.Id,
                    option.Title,
                    option.Body,
                    option.IsBaseline,
                    Visible: true,
                    Executable: executable,
                    BlockReason: reason));
            }

            return true;
        }

        public void ResolveOption(
            Entity activityEntity,
            string optionId,
            IReadOnlyDictionary<string, object?>? contextBindings = null)
        {
            if (!_world.IsAlive(activityEntity) || !_world.Has<ActivityInstanceCm>(activityEntity))
            {
                throw new InvalidOperationException("Activity entity is not alive.");
            }

            ref ActivityInstanceCm instance = ref _world.Get<ActivityInstanceCm>(activityEntity);
            if (instance.State == ActivityInstanceState.Resolved)
            {
                throw new InvalidOperationException(
                    $"Activity instance {instance.InstanceId} is already resolved.");
            }

            if (!_definitions.TryGet(instance.DefinitionId, out ActivityDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Missing activity definition id '{instance.DefinitionId}'.");
            }

            if (definition.DispatchPolicy == ActivityDispatchPolicy.Automatic)
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' is automatic and cannot resolve options.");
            }

            int optionIndex = -1;
            for (int i = 0; i < definition.Options.Count; i++)
            {
                if (string.Equals(definition.Options[i].Id, optionId, StringComparison.OrdinalIgnoreCase))
                {
                    optionIndex = i;
                    break;
                }
            }

            if (optionIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' has no option '{optionId}'.");
            }

            ActivityOptionDefinition option = definition.Options[optionIndex];
            Dictionary<string, object?> bindings = ProviderContextBinding.CreateBindings(contextBindings);
            var context = new ProviderExecutionContext(_world, instance.ScopeHost, bindings);
            if (option.ShowCondition != null && !EvaluateCondition(option.ShowCondition, context))
            {
                throw new InvalidOperationException(
                    $"Option '{option.Id}' is not visible.");
            }

            if (!option.IsBaseline &&
                option.ExecuteCondition != null &&
                !EvaluateCondition(option.ExecuteCondition, context))
            {
                throw new InvalidOperationException(
                    $"Option '{option.Id}' is not executable.");
            }

            ExecuteEffects(option.Effects, context);
            instance.SelectedOptionIndex = optionIndex;
            instance.State = ActivityInstanceState.Resolved;
            instance.Revision++;
            _index.Remove((instance.DefinitionId, ScopeKey(instance.ScopeHost)));
            _presentation.Add(new ActivityPresentationCue(
                ActivityPresentationCueKind.Resolved,
                definition.Id,
                instance.InstanceId,
                option.Id,
                string.Empty));
        }

        public bool TryGetState(Entity activityEntity, out ActivityInstanceState state, out string activityId)
        {
            state = default;
            activityId = string.Empty;
            if (!_world.IsAlive(activityEntity) || !_world.Has<ActivityInstanceCm>(activityEntity))
            {
                return false;
            }

            ActivityInstanceCm instance = _world.Get<ActivityInstanceCm>(activityEntity);
            state = instance.State;
            if (_definitions.TryGet(instance.DefinitionId, out ActivityDefinition definition))
            {
                activityId = definition.Id;
            }

            return true;
        }

        private void ActivateForPresentation(Entity entity, ActivityDefinition definition)
        {
            ref ActivityInstanceCm instance = ref _world.Get<ActivityInstanceCm>(entity);
            instance.State = ActivityInstanceState.Active;
            instance.Revision++;
            _presentation.Add(new ActivityPresentationCue(
                ActivityPresentationCueKind.Presented,
                definition.Id,
                instance.InstanceId,
                string.Empty,
                string.Empty));
        }

        private void ResolveAutomatic(
            Entity entity,
            ActivityDefinition definition,
            ProviderExecutionContext context)
        {
            ExecuteEffects(definition.AutomaticEffects, context);
            ref ActivityInstanceCm instance = ref _world.Get<ActivityInstanceCm>(entity);
            instance.State = ActivityInstanceState.Resolved;
            instance.Revision++;
            _index.Remove((instance.DefinitionId, ScopeKey(instance.ScopeHost)));
            _presentation.Add(new ActivityPresentationCue(
                ActivityPresentationCueKind.AutomaticSettled,
                definition.Id,
                instance.InstanceId,
                string.Empty,
                string.Empty));
        }

        private void ExecuteEffects(List<ActivityEffectRef> effects, ProviderExecutionContext context)
        {
            List<ActivityEffectRef> ordered = new(effects);
            ordered.Sort((a, b) => a.ExecutionOrder.CompareTo(b.ExecutionOrder));
            for (int i = 0; i < ordered.Count; i++)
            {
                ActivityEffectRef effect = ordered[i];
                IEffectHandler handler = _providers.Effects.MustGet(effect.EffectKey, out ProviderParameterSchema schema);
                schema.Validate(effect.Parameters, $"activity.effect.{effect.EffectKey}");
                var call = new ProviderEffectCall(
                    effect.EffectKey,
                    effect.TargetReference,
                    effect.Parameters,
                    effect.ExecutionOrder);
                handler.Execute(in call, context);
            }
        }

        private bool EvaluateCondition(ActivityConditionRef condition, ProviderExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(condition.ConditionKey))
            {
                throw new InvalidOperationException("Condition key is required.");
            }

            IConditionProvider provider = _providers.Conditions.MustGet(
                condition.ConditionKey,
                out ProviderParameterSchema schema);
            schema.Validate(condition.Parameters, $"activity.condition.{condition.ConditionKey}");
            return ConditionWriteGuard.EvaluateReadOnly(provider, context, condition.Parameters);
        }

        private static int ScopeKey(Entity scopeHost) =>
            scopeHost == Entity.Null ? 0 : scopeHost.Id;
    }
}
