using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Rng;

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
        int InstanceId,
        string SelectedOptionId,
        Entity ScopeHost);

    public sealed record ActivityRuntimeSnapshot(
        int NextInstanceId,
        IReadOnlyList<string>? ProcessedSignalIds = null);

    public static class ActivityAdmissionRejections
    {
        public const string UniqueAlreadyResolved = "admission.unique_already_resolved";
        public const string TriggerConditionFailed = "admission.trigger_condition_failed";
        public const string CooldownActive = "admission.cooldown_active";
        public const string MutexOccupied = "admission.mutex_occupied";
        public const string PoolUnavailable = "admission.pool_unavailable";
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
        private readonly ActivityLifecycleBuffer _lifecycle;
        private readonly IClock? _clock;
        private readonly RngPickService? _rngPickService;
        private readonly Dictionary<(int DefinitionId, int ScopeKey), Entity> _index = new();
        private readonly List<Entity> _scratch = new(64);
        private readonly ForEachWithEntity<ActivityInstanceCm> _resolvedInstanceCollector;
        private readonly ForEachWithEntity<ActivityInstanceCm> _dispatchTickCollector;
        private readonly ForEachWithEntity<ActivityInstanceCm> _mutexOccupantCollector;
        private readonly List<int> _subscriptionCandidates = new(8);
        private readonly HashSet<string> _processedSignalIds = new(StringComparer.Ordinal);
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
            IClock? clock = null,
            RngPickService? rngPickService = null,
            ActivityLifecycleBuffer? lifecycle = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            _lifecycle = lifecycle ?? new ActivityLifecycleBuffer();
            _clock = clock;
            _rngPickService = rngPickService;
            _resolvedInstanceCollector = CollectResolvedInstance;
            _dispatchTickCollector = CollectDispatchTick;
            _mutexOccupantCollector = CollectMutexOccupant;
            RebuildIndexFromWorld();
        }

        public ActivityPresentationBuffer Presentation => _presentation;

        public ActivityLifecycleBuffer Lifecycle => _lifecycle;

        public bool TryGetDefinition(string activityId, out ActivityDefinition definition)
            => _definitions.TryGet(activityId, out definition);

        public ActivityRuntimeSnapshot CaptureSnapshot()
        {
            return new ActivityRuntimeSnapshot(
                _nextInstanceId,
                new List<string>(_processedSignalIds));
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
            _processedSignalIds.Clear();
            if (snapshot.ProcessedSignalIds != null)
            {
                for (int i = 0; i < snapshot.ProcessedSignalIds.Count; i++)
                {
                    string id = snapshot.ProcessedSignalIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        throw new InvalidOperationException(
                            $"Activity snapshot contains an invalid processed signal id '{id}'.");
                    }

                    _processedSignalIds.Add(id);
                }
            }

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
            _lifecycle.Clear();
            _processedSignalIds.Clear();
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
            IReadOnlyDictionary<string, object?>? contextBindings = null,
            IReadOnlyDictionary<string, object?>? signalBindings = null)
        {
            if (!_definitions.TryGet(activityId, out ActivityDefinition definition))
            {
                throw new InvalidOperationException($"Unknown activity definition '{activityId}'.");
            }

            int definitionId = _definitions.GetId(activityId);
            var key = (definitionId, ScopeKey(scopeHost));

            if (definition.DispatchPolicy == ActivityDispatchPolicy.Pooled)
            {
                return DispatchPooled(definition, scopeHost, contextBindings, signalBindings);
            }

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

            Dictionary<string, object?> bindings = ProviderContextBinding.CreateBindings(contextBindings, signalBindings);
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

            Entity entity = _world.Create(
                component,
                new Name { Value = RequireInstanceName(definition) });

            EmitLifecycle(ActivityLifecycleKeys.Started, definition, component, string.Empty);

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

        public ActivitySignalIntakeResult IntakeSignal(ActivitySignal signal)
        {
            ArgumentNullException.ThrowIfNull(signal);
            ValidateSignalCompleteness(signal);

            if (!_providers.Sources.Contains(signal.SourceKey))
            {
                throw new InvalidOperationException(
                    $"{ActivitySignalFailures.UnknownSourceKey}: source '{signal.SourceKey}' is not a registered fact source.");
            }

            if (!_processedSignalIds.Add(DedupeKey(signal)))
            {
                return new ActivitySignalIntakeResult(
                    true,
                    Array.Empty<ActivitySignalMatchResult>());
            }

            _subscriptionCandidates.Clear();
            CollectSubscriptionCandidates(signal.SourceKey, _subscriptionCandidates);
            if (_subscriptionCandidates.Count == 0)
            {
                return new ActivitySignalIntakeResult(
                    false,
                    Array.Empty<ActivitySignalMatchResult>());
            }

            Dictionary<string, object?> signalValues = BuildSignalValues(signal);
            var signalContext = new ProviderExecutionContext(
                _world,
                signal.ScopeRef,
                ProviderContextBinding.CreateBindings(null, signalValues));
            var matches = new List<ActivitySignalMatchResult>(_subscriptionCandidates.Count);
            for (int i = 0; i < _subscriptionCandidates.Count; i++)
            {
                if (!_definitions.TryGet(_subscriptionCandidates[i], out ActivityDefinition definition))
                {
                    throw new InvalidOperationException(
                        $"Activity definition id '{_subscriptionCandidates[i]}' is missing from the registry.");
                }

                if (definition.SourceSubscription?.MatchCondition != null &&
                    !EvaluateCondition(definition.SourceSubscription.MatchCondition, signalContext))
                {
                    matches.Add(new ActivitySignalMatchResult(
                        definition.Id,
                        Entity.Null,
                        $"{ActivitySignalFailures.MatchConditionFailed}:{definition.SourceSubscription.MatchCondition.ConditionKey}"));
                    continue;
                }

                ActivityAdmissionResult admission = OfferOrActivateChecked(
                    definition.Id,
                    signal.ScopeRef,
                    contextBindings: null,
                    signalBindings: signalValues);
                matches.Add(new ActivitySignalMatchResult(
                    definition.Id,
                    admission.Instance,
                    admission.RejectionCode));
            }

            return new ActivitySignalIntakeResult(false, matches);
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
            if (instance.State != ActivityInstanceState.Active)
            {
                results.Clear();
                return false;
            }

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
                        reason,
                        ScopeKey(instance.ScopeHost)));
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

            if (instance.State != ActivityInstanceState.Active)
            {
                throw new InvalidOperationException(
                    $"Activity instance {instance.InstanceId} is not active.");
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
                string.Empty,
                ScopeKey(instance.ScopeHost)));
            EmitLifecycle(ActivityLifecycleKeys.OptionSelected, definition, instance, option.Id);
            EmitLifecycle(ActivityLifecycleKeys.Settled, definition, instance, option.Id);
            EmitLifecycle(ActivityLifecycleKeys.Archived, definition, instance, option.Id);
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
                    instance.InstanceId,
                    SelectedOptionId(definition, in instance),
                    instance.ScopeHost));
            });
            return views;
        }

        private ActivityAdmissionResult DispatchPooled(
            ActivityDefinition definition,
            Entity scopeHost,
            IReadOnlyDictionary<string, object?>? contextBindings,
            IReadOnlyDictionary<string, object?>? signalBindings)
        {
            if (_rngPickService == null)
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' uses pooled dispatch_policy but ActivityRuntimeService has no RngPickService.");
            }

            if (definition.TriggerCondition != null)
            {
                Dictionary<string, object?> bindings = ProviderContextBinding.CreateBindings(contextBindings, signalBindings);
                var context = new ProviderExecutionContext(_world, scopeHost, bindings);
                if (!EvaluateCondition(definition.TriggerCondition, context))
                {
                    return Reject(definition, scopeHost, ActivityAdmissionRejections.TriggerConditionFailed);
                }
            }

            int drawnIndex;
            try
            {
                drawnIndex = _rngPickService.Pick(definition.PoolKey);
            }
            catch (InvalidOperationException)
            {
                return Reject(definition, scopeHost,
                    $"{ActivityAdmissionRejections.PoolUnavailable}:{definition.PoolKey}");
            }

            string drawnId = _rngPickService.GetDistribution(definition.PoolKey).GetEntry(drawnIndex).Id;
            if (!_definitions.TryGet(drawnId, out ActivityDefinition candidate))
            {
                throw new InvalidOperationException(
                    $"Pool '{definition.PoolKey}' entry '{drawnId}' does not resolve to a registered activity definition.");
            }

            if (candidate.DispatchPolicy == ActivityDispatchPolicy.Pooled)
            {
                throw new InvalidOperationException(
                    $"Pool '{definition.PoolKey}' entry '{drawnId}' is itself pooled; pools must reference forced or automatic definitions.");
            }

            return OfferOrActivateChecked(drawnId, scopeHost, contextBindings, signalBindings);
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
                string.Empty,
                ScopeKey(instance.ScopeHost)));
            EmitLifecycle(ActivityLifecycleKeys.Presented, definition, instance, string.Empty);
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
                string.Empty,
                ScopeKey(instance.ScopeHost)));
            EmitLifecycle(ActivityLifecycleKeys.Settled, definition, instance, string.Empty);
            EmitLifecycle(ActivityLifecycleKeys.Archived, definition, instance, string.Empty);
        }

        private void EmitLifecycle(
            string key,
            ActivityDefinition definition,
            in ActivityInstanceCm instance,
            string optionId)
        {
            _lifecycle.Add(new ActivityLifecycleEvent(
                key,
                definition.Id,
                instance.InstanceId,
                optionId,
                ScopeKey(instance.ScopeHost)));
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

        private void CollectSubscriptionCandidates(string sourceKey, List<int> results)
        {
            foreach (ActivityDefinition definition in _definitions.Definitions)
            {
                string subscribedSource = definition.SourceSubscription?.SourceKey ?? definition.SourceKey;
                if (string.Equals(subscribedSource, sourceKey, StringComparison.Ordinal))
                {
                    results.Add(_definitions.GetId(definition.Id));
                }
            }
        }

        private static void ValidateSignalCompleteness(ActivitySignal signal)
        {
            var missing = new List<string>(4);
            if (string.IsNullOrWhiteSpace(signal.SourceKey))
            {
                missing.Add("source_key");
            }

            if (string.IsNullOrWhiteSpace(signal.SignalId))
            {
                missing.Add("signal_id");
            }

            if (signal.ObjectRefs is null)
            {
                missing.Add("object_refs");
            }

            if (signal.Parameters is null)
            {
                missing.Add("parameters");
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{ActivitySignalFailures.Malformed}: signal is missing required fields: {string.Join(", ", missing)}.");
            }
        }

        private static string DedupeKey(ActivitySignal signal) =>
            string.Concat(signal.SourceKey, ":", signal.SignalId);

        private static Dictionary<string, object?> BuildSignalValues(ActivitySignal signal)
        {
            Dictionary<string, object?> values = new(signal.Parameters!.Count + 4, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> pair in signal.Parameters!)
            {
                values[pair.Key] = pair.Value;
            }

            values["source_key"] = signal.SourceKey;
            values["signal_id"] = signal.SignalId;
            values["occurred_at"] = signal.OccurredAt;
            values["scope_ref"] = signal.ScopeRef;
            values["object_refs"] = signal.ObjectRefs!;
            return values;
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

        private static string SelectedOptionId(ActivityDefinition definition, in ActivityInstanceCm instance)
        {
            if (instance.State != ActivityInstanceState.Resolved ||
                (uint)instance.SelectedOptionIndex >= (uint)definition.Options.Count)
            {
                return string.Empty;
            }

            return definition.Options[instance.SelectedOptionIndex].Id;
        }

        private static string RequireInstanceName(ActivityDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                throw new InvalidOperationException(
                    $"Activity definition '{definition.Id}' requires a non-empty display name.");
            }

            return definition.DisplayName;
        }

        private static int ScopeKey(Entity scopeHost) =>
            scopeHost == Entity.Null ? 0 : scopeHost.Id;
    }
}
