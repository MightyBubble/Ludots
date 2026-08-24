using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Providers;

namespace Ludots.Core.Gameplay.Tasks
{
    public readonly record struct TaskObjectiveProgressView(
        string ObjectiveId,
        string Title,
        TaskObjectiveKind Kind,
        bool Completed,
        int Current,
        int Target);

    public readonly record struct TaskView(
        Entity Entity,
        string TaskId,
        string DisplayName,
        TaskInstanceState State,
        TaskCompletionRule CompletionRule,
        int InstanceId,
        IReadOnlyList<TaskObjectiveProgressView> Objectives);

    public sealed record TaskRuntimeSnapshot(
        IReadOnlyDictionary<string, int> Signals,
        IReadOnlyDictionary<string, int> Accumulators,
        int NextInstanceId);

    public sealed class TaskRuntimeService
    {
        private static readonly QueryDescription TaskQuery = new QueryDescription()
            .WithAll<TaskInstanceCm>();

        private readonly World _world;
        private readonly TaskDefinitionRegistry _definitions;
        private readonly ProviderServices _providers;
        private readonly TaskPresentationBuffer _presentation;
        private readonly Dictionary<(int DefinitionId, int ScopeKey), Entity> _index = new();
        private readonly Dictionary<string, int> _signals = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _accumulators = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Entity> _scratch = new(64);
        private int _nextInstanceId = 1;

        public TaskRuntimeService(
            World world,
            TaskDefinitionRegistry definitions,
            ProviderServices providers,
            TaskPresentationBuffer presentation)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            RebuildIndexFromWorld();
        }

        public TaskPresentationBuffer Presentation => _presentation;
        public IReadOnlyDictionary<string, int> Signals => _signals;

        public TaskRuntimeSnapshot CaptureSnapshot()
        {
            return new TaskRuntimeSnapshot(
                new Dictionary<string, int>(_signals, StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, int>(_accumulators, StringComparer.OrdinalIgnoreCase),
                _nextInstanceId);
        }

        public void RestoreSnapshot(TaskRuntimeSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (snapshot.NextInstanceId < 1)
            {
                throw new InvalidOperationException(
                    $"Task snapshot nextInstanceId {snapshot.NextInstanceId} is invalid.");
            }

            _signals.Clear();
            foreach (KeyValuePair<string, int> pair in snapshot.Signals)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new InvalidOperationException(
                        $"Task signal snapshot contains invalid key '{pair.Key}'.");
                }

                _signals[pair.Key] = pair.Value;
            }

            _accumulators.Clear();
            foreach (KeyValuePair<string, int> pair in snapshot.Accumulators)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new InvalidOperationException(
                        $"Task accumulator snapshot contains invalid key '{pair.Key}'.");
                }

                _accumulators[pair.Key] = pair.Value;
            }

            _nextInstanceId = snapshot.NextInstanceId;
            RebuildIndexFromWorld();
        }

        public void ResetState()
        {
            _scratch.Clear();
            _world.Query(in TaskQuery, (Entity entity, ref TaskInstanceCm _) => _scratch.Add(entity));
            for (int i = 0; i < _scratch.Count; i++)
            {
                if (_world.IsAlive(_scratch[i]))
                {
                    _world.Destroy(_scratch[i]);
                }
            }

            _index.Clear();
            _signals.Clear();
            _accumulators.Clear();
            _presentation.Clear();
            _nextInstanceId = 1;
        }

        public void RebuildIndexFromWorld()
        {
            _index.Clear();
            _world.Query(in TaskQuery, (Entity entity, ref TaskInstanceCm task) =>
            {
                if (task.State is TaskInstanceState.Completed or TaskInstanceState.Failed or TaskInstanceState.Abandoned)
                {
                    return;
                }

                _index[(task.DefinitionId, ScopeKey(task.ScopeHost))] = entity;
            });
        }

        public Entity OfferOrStart(string taskId, Entity scopeHost = default)
        {
            if (!_definitions.TryGet(taskId, out TaskDefinition definition))
            {
                throw new InvalidOperationException($"Unknown task definition '{taskId}'.");
            }

            int definitionId = _definitions.GetId(taskId);
            var key = (definitionId, ScopeKey(scopeHost));
            if (_index.TryGetValue(key, out Entity existing) &&
                _world.IsAlive(existing) &&
                _world.Has<TaskInstanceCm>(existing))
            {
                return existing;
            }

            TaskInstanceState initial = definition.StartPolicy == TaskStartPolicy.Automatic
                ? TaskInstanceState.Active
                : TaskInstanceState.Offered;

            int instanceId = _nextInstanceId++;
            Entity entity = _world.Create(new TaskInstanceCm
            {
                DefinitionId = definitionId,
                InstanceId = instanceId,
                State = initial,
                ScopeHost = scopeHost,
                ObjectiveMask = 0,
                Revision = 1,
            });
            _index[key] = entity;

            if (initial == TaskInstanceState.Offered)
            {
                _presentation.Add(new TaskPresentationCue(
                    TaskPresentationCueKind.Offered,
                    definition.Id,
                    instanceId,
                    string.Empty,
                    string.Empty));
            }

            EmitStateSource(definition.Id, initial, scopeHost);
            return entity;
        }

        public void Accept(Entity taskEntity)
        {
            ref TaskInstanceCm task = ref Require(taskEntity);
            if (task.State != TaskInstanceState.Offered)
            {
                throw new InvalidOperationException(
                    $"Task instance {task.InstanceId} cannot accept from state {task.State}.");
            }

            if (!_definitions.TryGet(task.DefinitionId, out TaskDefinition definition))
            {
                throw new InvalidOperationException($"Missing task definition '{task.DefinitionId}'.");
            }

            task.State = TaskInstanceState.Active;
            task.Revision++;
            EmitStateSource(definition.Id, task.State, task.ScopeHost);
        }

        public void EmitSignal(string signalKey, int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(signalKey) || amount <= 0)
            {
                throw new InvalidOperationException("Signal key/amount invalid.");
            }

            _signals.TryGetValue(signalKey, out int current);
            _signals[signalKey] = current + amount;
            RefreshActiveTasks();
        }

        public void Accumulate(string accumulateKey, int delta)
        {
            if (string.IsNullOrWhiteSpace(accumulateKey))
            {
                throw new InvalidOperationException("Accumulate key is required.");
            }

            _accumulators.TryGetValue(accumulateKey, out int current);
            _accumulators[accumulateKey] = current + delta;
            RefreshActiveTasks();
        }

        public void Abandon(Entity taskEntity, string reason)
        {
            ref TaskInstanceCm task = ref Require(taskEntity);
            if (task.State is not (TaskInstanceState.Offered or TaskInstanceState.Active))
            {
                throw new InvalidOperationException(
                    $"Task instance {task.InstanceId} cannot abandon from state {task.State}.");
            }

            if (!_definitions.TryGet(task.DefinitionId, out TaskDefinition definition))
            {
                throw new InvalidOperationException($"Missing task definition '{task.DefinitionId}'.");
            }

            task.State = TaskInstanceState.Abandoned;
            task.Revision++;
            _index.Remove((task.DefinitionId, ScopeKey(task.ScopeHost)));
            _presentation.Add(new TaskPresentationCue(
                TaskPresentationCueKind.Abandoned,
                definition.Id,
                task.InstanceId,
                string.Empty,
                reason ?? string.Empty));
            EmitStateSource(definition.Id, task.State, task.ScopeHost);
        }

        public void Fail(Entity taskEntity, string reason)
        {
            ref TaskInstanceCm task = ref Require(taskEntity);
            if (!_definitions.TryGet(task.DefinitionId, out TaskDefinition definition))
            {
                throw new InvalidOperationException($"Missing task definition '{task.DefinitionId}'.");
            }

            ExecuteEffects(definition.FailureEffects, task.ScopeHost);
            task.State = TaskInstanceState.Failed;
            task.Revision++;
            _index.Remove((task.DefinitionId, ScopeKey(task.ScopeHost)));
            _presentation.Add(new TaskPresentationCue(
                TaskPresentationCueKind.Failed,
                definition.Id,
                task.InstanceId,
                string.Empty,
                reason ?? string.Empty));
            EmitStateSource(definition.Id, task.State, task.ScopeHost);
        }

        public bool TryGetView(Entity taskEntity, out TaskView view)
        {
            view = default;
            if (!_world.IsAlive(taskEntity) || !_world.Has<TaskInstanceCm>(taskEntity))
            {
                return false;
            }

            TaskInstanceCm task = _world.Get<TaskInstanceCm>(taskEntity);
            if (!_definitions.TryGet(task.DefinitionId, out TaskDefinition definition))
            {
                return false;
            }

            var objectives = new List<TaskObjectiveProgressView>(definition.Objectives.Count);
            for (int i = 0; i < definition.Objectives.Count; i++)
            {
                TaskObjectiveDefinition objective = definition.Objectives[i];
                bool completed = (task.ObjectiveMask & (1 << i)) != 0;
                int current = ReadObjectiveCurrent(objective);
                int target = objective.Kind is TaskObjectiveKind.Count or TaskObjectiveKind.Accumulate
                    ? Math.Max(1, objective.TargetCount)
                    : 1;
                objectives.Add(new TaskObjectiveProgressView(
                    objective.Id,
                    objective.Title,
                    objective.Kind,
                    completed,
                    current,
                    target));
            }

            view = new TaskView(
                taskEntity,
                definition.Id,
                definition.DisplayName,
                task.State,
                definition.CompletionRule,
                task.InstanceId,
                objectives);
            return true;
        }

        public List<TaskView> CaptureViews()
        {
            var views = new List<TaskView>();
            _world.Query(in TaskQuery, (Entity entity, ref TaskInstanceCm _) =>
            {
                if (TryGetView(entity, out TaskView view))
                {
                    views.Add(view);
                }
            });
            return views;
        }

        private void RefreshActiveTasks()
        {
            _scratch.Clear();
            _world.Query(in TaskQuery, (Entity entity, ref TaskInstanceCm task) =>
            {
                if (task.State == TaskInstanceState.Active)
                {
                    _scratch.Add(entity);
                }
            });

            for (int i = 0; i < _scratch.Count; i++)
            {
                EvaluateActive(_scratch[i]);
            }
        }

        private void EvaluateActive(Entity entity)
        {
            ref TaskInstanceCm task = ref _world.Get<TaskInstanceCm>(entity);
            if (!_definitions.TryGet(task.DefinitionId, out TaskDefinition definition))
            {
                return;
            }

            int mask = 0;
            int completedCount = 0;
            for (int i = 0; i < definition.Objectives.Count; i++)
            {
                if (IsObjectiveComplete(definition.Objectives[i], task.ScopeHost))
                {
                    mask |= 1 << i;
                    completedCount++;
                    if ((task.ObjectiveMask & (1 << i)) == 0)
                    {
                        _presentation.Add(new TaskPresentationCue(
                            TaskPresentationCueKind.Progress,
                            definition.Id,
                            task.InstanceId,
                            definition.Objectives[i].Id,
                            string.Empty));
                    }
                }
            }

            task.ObjectiveMask = mask;
            task.Revision++;

            bool done = definition.CompletionRule == TaskCompletionRule.All
                ? completedCount == definition.Objectives.Count
                : completedCount > 0;

            if (!done)
            {
                return;
            }

            ExecuteEffects(definition.RewardEffects, task.ScopeHost);
            task.State = TaskInstanceState.Completed;
            task.Revision++;
            _index.Remove((task.DefinitionId, ScopeKey(task.ScopeHost)));
            _presentation.Add(new TaskPresentationCue(
                TaskPresentationCueKind.Completed,
                definition.Id,
                task.InstanceId,
                string.Empty,
                string.Empty));
            EmitStateSource(definition.Id, task.State, task.ScopeHost);

            if (!string.IsNullOrWhiteSpace(definition.NextTaskId))
            {
                OfferOrStart(definition.NextTaskId, task.ScopeHost);
            }
        }

        private bool IsObjectiveComplete(TaskObjectiveDefinition objective, Entity scopeHost)
        {
            return objective.Kind switch
            {
                TaskObjectiveKind.Signal =>
                    !string.IsNullOrWhiteSpace(objective.SignalKey) &&
                    _signals.TryGetValue(objective.SignalKey, out int signalCount) &&
                    signalCount > 0,
                TaskObjectiveKind.Count =>
                    !string.IsNullOrWhiteSpace(objective.SignalKey) &&
                    _signals.TryGetValue(objective.SignalKey, out int count) &&
                    count >= Math.Max(1, objective.TargetCount),
                TaskObjectiveKind.Accumulate =>
                    !string.IsNullOrWhiteSpace(objective.AccumulateKey) &&
                    _accumulators.TryGetValue(objective.AccumulateKey, out int acc) &&
                    acc >= Math.Max(1, objective.TargetCount),
                TaskObjectiveKind.Condition => EvaluateCondition(objective.ConditionKey, scopeHost),
                _ => false,
            };
        }

        private int ReadObjectiveCurrent(TaskObjectiveDefinition objective)
        {
            return objective.Kind switch
            {
                TaskObjectiveKind.Signal => _signals.TryGetValue(objective.SignalKey, out int s) ? Math.Min(1, s) : 0,
                TaskObjectiveKind.Count => _signals.TryGetValue(objective.SignalKey, out int c) ? c : 0,
                TaskObjectiveKind.Accumulate => _accumulators.TryGetValue(objective.AccumulateKey, out int a) ? a : 0,
                TaskObjectiveKind.Condition => 0,
                _ => 0,
            };
        }

        private bool EvaluateCondition(string conditionKey, Entity scopeHost)
        {
            if (string.IsNullOrWhiteSpace(conditionKey))
            {
                return false;
            }

            IConditionProvider condition = _providers.Conditions.MustGet(conditionKey, out ProviderParameterSchema schema);
            schema.Validate(new Dictionary<string, object?>(), $"task.condition.{conditionKey}");
            var context = new ProviderExecutionContext(
                _world,
                scopeHost,
                ProviderContextBinding.CreateBindings());
            return ConditionWriteGuard.EvaluateReadOnly(condition, context, new Dictionary<string, object?>());
        }

        private void ExecuteEffects(List<TaskEffectRef> effects, Entity scopeHost)
        {
            var ordered = new List<TaskEffectRef>(effects);
            ordered.Sort((a, b) => a.ExecutionOrder.CompareTo(b.ExecutionOrder));
            var context = new ProviderExecutionContext(
                _world,
                scopeHost,
                ProviderContextBinding.CreateBindings());
            for (int i = 0; i < ordered.Count; i++)
            {
                TaskEffectRef effect = ordered[i];
                IEffectHandler handler = _providers.Effects.MustGet(effect.EffectKey, out ProviderParameterSchema schema);
                schema.Validate(effect.Parameters, $"task.effect.{effect.EffectKey}");
                var call = new ProviderEffectCall(
                    effect.EffectKey,
                    effect.TargetReference,
                    effect.Parameters,
                    effect.ExecutionOrder);
                handler.Execute(in call, context);
            }
        }

        private void EmitStateSource(string taskId, TaskInstanceState state, Entity scopeHost)
        {
            if (!_providers.Sources.Contains("task.state_changed"))
            {
                return;
            }

            ISourceProvider source = _providers.Sources.MustGet("task.state_changed", out _);
            var signal = new ProviderSignal(
                "task.state_changed",
                $"{taskId}:{state}",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                scopeHost,
                Array.Empty<Entity>(),
                new Dictionary<string, object?>
                {
                    ["task_id"] = taskId,
                    ["state"] = state.ToString(),
                });
            source.Emit(
                in signal,
                new ProviderExecutionContext(_world, scopeHost, ProviderContextBinding.CreateBindings()));
        }

        private ref TaskInstanceCm Require(Entity taskEntity)
        {
            if (!_world.IsAlive(taskEntity) || !_world.Has<TaskInstanceCm>(taskEntity))
            {
                throw new InvalidOperationException("Task entity is not alive.");
            }

            return ref _world.Get<TaskInstanceCm>(taskEntity);
        }

        private static int ScopeKey(Entity scopeHost) =>
            scopeHost == Entity.Null ? 0 : scopeHost.Id;
    }
}
