using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
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

    public readonly record struct ActivityView(
        Entity Entity,
        string ActivityId,
        string DisplayName,
        string Summary,
        ActivityInstanceState State,
        ActivityDispatchPolicy DispatchPolicy,
        int InstanceId);

    public sealed record ActivityRuntimeSnapshot(int NextInstanceId);

    public static class ActivityAdmissionRejections
    {
        public const string UniqueAlreadyResolved = "admission.unique_already_resolved";
        public const string TriggerConditionFailed = "admission.trigger_condition_failed";
        public const string CooldownActive = "admission.cooldown_active";
        public const string MutexOccupied = "admission.mutex_occupied";
    }

    public readonly record struct ActivityAdmissionResult(Entity Instance, string? RejectionCode)
    {
        public bool Accepted => RejectionCode is null;
    }

    public sealed class ActivityRuntimeService
    {
        private static readonly QueryDescription ActivityQuery = new QueryDescription()
            .WithAll<ActivityInstanceCm>();

        private readonly World _world;
        private readonly ActivityDefinitionRegistry _definitions;
        private readonly ProviderServices _providers;
        private readonly ActivityPresentationBuffer _presentation;
        private readonly IClock? _clock;
        private readonly Dictionary<(int DefinitionId, int ScopeKey), Entity> _index = new();
        private readonly List<Entity> _scratch = new(64);
        private readonly ForEachWithEntity<ActivityInstanceCm> _resolvedInstanceCollector;
        private readonly ForEachWithEntity<ActivityInstanceCm> _dispatchTickCollector;
        private readonly ForEachWithEntity<ActivityInstanceCm> _mutexOccupantCollector;
        private int _collectDefinitionId;
        private int _collectScopeKey;
        private int _collectMaxDispatchTick;
        private string _collectMutexGroup = string.Empty;
        private int _nextInstanceId = 1;

        public ActivityRuntimeService(
            World world,
            ActivityDefinitionRegistry definitions,
            ProviderServices providers,
            ActivityPresentationBuffer presentation,
            IClock? clock = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            _clock = clock;
            _resolvedInstanceCollector = CollectResolvedInstance;
            _dispatchTickCollector = CollectDispatchTick;
            _mutexOccupantCollector = CollectMutexOccupant;
            RebuildIndexFromWorld();
        }

        public ActivityPresentationBuffer Presentation => _presentation;

        public ActivityRuntimeSnapshot CaptureSnapshot()
        {
            return new ActivityRuntimeSnapshot(_nextInstanceId);
        }

        public void RestoreSnapshot(ActivityRuntimeSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.NextInstanceId < 1)
            {
                throw new InvalidOperationException(
                    $"Activity snapshot nextInstanceId {snapshot.NextInstanceId} is invalid.");
            }

            _nextInstanceId = snapshot.NextInstanceId;
            RebuildIndexFromWorld();
        }

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

                if (_definitions.TryGet(activity.DefinitionId, out ActivityDefinition definition) &&
                    !TracksPendingInstance(definition.RepeatPolicy))
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
            return OfferOrActivateChecked(activityId, scopeHost, contextBindings).Instance;
        }

        public ActivityAdmissionResult OfferOrActivateChecked(
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

            switch (definition.RepeatPolicy)
            {
                case ActivityRepeatPolicy.PendingDedupe:
                    if (TryGetPendingInstance(key, out Entity pending))
                    {
                        return new ActivityAdmissionResult(pending, null);
                    }

                    break;
                case ActivityRepeatPolicy.Unique:
                    if (TryGetPendingInstance(key, out Entity existing))
                    {
                        return new ActivityAdmissionResult(existing, null);
                    }

                    if (HasResolvedInstance(definitionId, ScopeKey(scopeHost)))
                    {
                        return Reject(definition, scopeHost, ActivityAdmissionRejections.UniqueAlreadyResolved);
                    }

                    break;
                case ActivityRepeatPolicy.Cooldown:
                    if (!TryCooldownAdmit(definition, scopeHost, out ActivityAdmissionResult cooldownResult))
                    {
                        return cooldownResult;
                    }

                    break;
                case ActivityRepeatPolicy.Mutex:
                    if (TryGetMutexOccupantGroup(definition.MutexGroup, ScopeKey(scopeHost), out string occupantGroup))
                    {
                        return Reject(definition, scopeHost, $"{ActivityAdmissionRejections.MutexOccupied}:{occupantGroup}");
                    }

                    break;
                case ActivityRepeatPolicy.Repeatable:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Activity '{definition.Id}' has unknown repeat_policy value '{(int)definition.RepeatPolicy}'.");
            }

            Dictionary<string, object?> bindings = ProviderContextBinding.CreateBindings(contextBindings);
            var context = new ProviderExecutionContext(_world, scopeHost, bindings);
            if (definition.TriggerCondition != null &&
                !EvaluateCondition(definition.TriggerCondition, context))
            {
                return Reject(definition, scopeHost, ActivityAdmissionRejections.TriggerConditionFailed);
            }

            int instanceId = _nextInstanceId++;
            var component = new ActivityInstanceCm
            {
                DefinitionId = definitionId,
                InstanceId = instanceId,
                State = ActivityInstanceState.Pending,
                ScopeHost = scopeHost,
                SelectedOptionIndex = -1,
                Revision = 1,
            };
            if (definition.RepeatPolicy == ActivityRepeatPolicy.Cooldown)
            {
                component.DispatchTick = _clock!.Now(definition.RepeatCooldown!.ClockDomain);
            }

            Entity entity = _world.Create(component);

            if (TracksPendingInstance(definition.RepeatPolicy))
            {
                _index[key] = entity;
            }

            if (definition.DispatchPolicy == ActivityDispatchPolicy.Automatic)
            {
                ResolveAutomatic(entity, definition, context);
                return new ActivityAdmissionResult(entity, null);
            }

            ActivateForPresentation(entity, definition);
            return new ActivityAdmissionResult(entity, null);
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

        public List<ActivityView> CaptureViews()
        {
            var views = new List<ActivityView>();
            _world.Query(in ActivityQuery, (Entity entity, ref ActivityInstanceCm instance) =>
            {
                if (!_definitions.TryGet(instance.DefinitionId, out ActivityDefinition definition))
                {
                    return;
                }

                views.Add(new ActivityView(
                    entity,
                    definition.Id,
                    definition.DisplayName,
                    definition.Summary,
                    instance.State,
                    definition.DispatchPolicy,
                    instance.InstanceId));
            });
            return views;
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
                Dictionary<string, object?> parameters = ProviderParameterValues.NormalizeMap(effect.Parameters);
                schema.Validate(parameters, $"activity.effect.{effect.EffectKey}");
                var call = new ProviderEffectCall(
                    effect.EffectKey,
                    effect.TargetReference,
                    parameters,
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
            Dictionary<string, object?> parameters = ProviderParameterValues.NormalizeMap(condition.Parameters);
            schema.Validate(parameters, $"activity.condition.{condition.ConditionKey}");
            return ConditionWriteGuard.EvaluateReadOnly(provider, context, parameters);
        }

        private bool TryGetPendingInstance((int DefinitionId, int ScopeKey) key, out Entity entity)
        {
            if (_index.TryGetValue(key, out entity) &&
                _world.IsAlive(entity) &&
                _world.Has<ActivityInstanceCm>(entity) &&
                _world.Get<ActivityInstanceCm>(entity).State != ActivityInstanceState.Resolved)
            {
                return true;
            }

            entity = Entity.Null;
            return false;
        }

        private bool TryCooldownAdmit(
            ActivityDefinition definition,
            Entity scopeHost,
            out ActivityAdmissionResult result)
        {
            if (_clock == null)
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' uses cooldown repeat_policy but ActivityRuntimeService has no clock.");
            }

            ActivityRepeatCooldown cooldown = definition.RepeatCooldown!;
            int scopeKey = ScopeKey(scopeHost);
            if (TryGetLastDispatchTick(_definitions.GetId(definition.Id), scopeKey, out int lastDispatchTick))
            {
                int elapsed = _clock.Now(cooldown.ClockDomain) - lastDispatchTick;
                if (elapsed < cooldown.DurationTicks)
                {
                    result = Reject(definition, scopeHost, ActivityAdmissionRejections.CooldownActive);
                    return false;
                }
            }

            result = default;
            return true;
        }

        private ActivityAdmissionResult Reject(ActivityDefinition definition, Entity scopeHost, string reasonCode)
        {
            _presentation.Add(new ActivityPresentationCue(
                ActivityPresentationCueKind.AdmissionRejected,
                definition.Id,
                InstanceId: 0,
                OptionId: string.Empty,
                Reason: reasonCode,
                ScopeKey: ScopeKey(scopeHost)));
            return new ActivityAdmissionResult(Entity.Null, reasonCode);
        }

        private void CollectResolvedInstance(Entity entity, ref ActivityInstanceCm activity)
        {
            if (activity.DefinitionId == _collectDefinitionId &&
                activity.State == ActivityInstanceState.Resolved &&
                ScopeKey(activity.ScopeHost) == _collectScopeKey)
            {
                _scratch.Add(entity);
            }
        }

        private void CollectDispatchTick(Entity entity, ref ActivityInstanceCm activity)
        {
            if (activity.DefinitionId == _collectDefinitionId &&
                ScopeKey(activity.ScopeHost) == _collectScopeKey)
            {
                _scratch.Add(entity);
                if (activity.DispatchTick > _collectMaxDispatchTick)
                {
                    _collectMaxDispatchTick = activity.DispatchTick;
                }
            }
        }

        private void CollectMutexOccupant(Entity entity, ref ActivityInstanceCm activity)
        {
            if (activity.State == ActivityInstanceState.Resolved ||
                ScopeKey(activity.ScopeHost) != _collectScopeKey)
            {
                return;
            }

            if (_definitions.TryGet(activity.DefinitionId, out ActivityDefinition definition) &&
                string.Equals(definition.MutexGroup, _collectMutexGroup, StringComparison.OrdinalIgnoreCase))
            {
                _scratch.Add(entity);
            }
        }

        private bool HasResolvedInstance(int definitionId, int scopeKey)
        {
            _collectDefinitionId = definitionId;
            _collectScopeKey = scopeKey;
            _scratch.Clear();
            _world.Query(in ActivityQuery, _resolvedInstanceCollector);
            return _scratch.Count > 0;
        }

        private bool TryGetLastDispatchTick(int definitionId, int scopeKey, out int tick)
        {
            _collectDefinitionId = definitionId;
            _collectScopeKey = scopeKey;
            _collectMaxDispatchTick = 0;
            _scratch.Clear();
            _world.Query(in ActivityQuery, _dispatchTickCollector);
            tick = _collectMaxDispatchTick;
            return _scratch.Count > 0;
        }

        private bool TryGetMutexOccupantGroup(string mutexGroup, int scopeKey, out string occupantGroup)
        {
            _collectMutexGroup = mutexGroup;
            _collectScopeKey = scopeKey;
            _scratch.Clear();
            _world.Query(in ActivityQuery, _mutexOccupantCollector);
            occupantGroup = _scratch.Count > 0 ? _collectMutexGroup : string.Empty;
            return _scratch.Count > 0;
        }

        private static bool TracksPendingInstance(ActivityRepeatPolicy policy) =>
            policy is ActivityRepeatPolicy.PendingDedupe or ActivityRepeatPolicy.Unique;

        private static int ScopeKey(Entity scopeHost) =>
            scopeHost == Entity.Null ? 0 : scopeHost.Id;
    }
}
