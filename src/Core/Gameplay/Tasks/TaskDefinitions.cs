using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Tasks
{
    public sealed class TaskObjectiveDefinition
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public TaskObjectiveKind Kind { get; set; } = TaskObjectiveKind.Signal;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("signal_key")]
        public string SignalKey { get; set; } = string.Empty;

        [JsonPropertyName("condition_key")]
        public string ConditionKey { get; set; } = string.Empty;

        [JsonPropertyName("target_count")]
        public int TargetCount { get; set; }

        [JsonPropertyName("accumulate_key")]
        public string AccumulateKey { get; set; } = string.Empty;

        [JsonPropertyName("hint")]
        public string Hint { get; set; } = string.Empty;

        [JsonPropertyName("text_token")]
        public string TextToken { get; set; } = string.Empty;

        [JsonPropertyName("hint_token")]
        public string HintToken { get; set; } = string.Empty;
    }

    public sealed class TaskEffectRef
    {
        [JsonPropertyName("effect_key")]
        public string EffectKey { get; set; } = string.Empty;

        [JsonPropertyName("target_reference")]
        public string TargetReference { get; set; } = "context.subject";

        [JsonPropertyName("parameters")]
        public Dictionary<string, object?> Parameters { get; set; } = new(StringComparer.Ordinal);

        [JsonPropertyName("execution_order")]
        public int ExecutionOrder { get; set; }
    }

    public sealed class TaskDefinition : IIdentifiable
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("start_policy")]
        public TaskStartPolicy StartPolicy { get; set; } = TaskStartPolicy.PlayerAccept;

        [JsonPropertyName("completion_rule")]
        public TaskCompletionRule CompletionRule { get; set; } = TaskCompletionRule.All;

        [JsonPropertyName("objectives")]
        public List<TaskObjectiveDefinition> Objectives { get; set; } = new();

        [JsonPropertyName("reward_effects")]
        public List<TaskEffectRef> RewardEffects { get; set; } = new();

        [JsonPropertyName("failure_effects")]
        public List<TaskEffectRef> FailureEffects { get; set; } = new();

        [JsonPropertyName("next_task_id")]
        public string NextTaskId { get; set; } = string.Empty;

        [JsonPropertyName("on_enter_dialogue_id")]
        public string OnEnterDialogueId { get; set; } = string.Empty;

        [JsonPropertyName("on_enter_cinematic_id")]
        public string OnEnterCinematicId { get; set; } = string.Empty;
    }

    public sealed class TaskDefinitionRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<TaskDefinition?> _definitions = new() { null };

        public IEnumerable<TaskDefinition> Definitions
        {
            get
            {
                for (int i = 1; i < _definitions.Count; i++)
                {
                    if (_definitions[i] != null)
                    {
                        yield return _definitions[i]!;
                    }
                }
            }
        }

        public void Clear()
        {
            _nameToId.Clear();
            _definitions.Clear();
            _definitions.Add(null);
        }

        public int Register(string id, TaskDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Task id is required.", nameof(id));
            }

            ArgumentNullException.ThrowIfNull(definition);
            definition.Id = id;
            if (string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                definition.DisplayName = id;
            }

            if (definition.Objectives.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Task '{id}' must declare at least one objective (empty objectives forbidden).");
            }

            for (int i = 0; i < definition.Objectives.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(definition.Objectives[i].Id))
                {
                    throw new InvalidOperationException(
                        $"Task '{id}' objective[{i}] requires id.");
                }
            }

            if (_nameToId.TryGetValue(id, out int existing))
            {
                _definitions[existing] = definition;
                return existing;
            }

            int next = _definitions.Count;
            _nameToId[id] = next;
            _definitions.Add(definition);
            return next;
        }

        public int GetId(string id) => _nameToId.TryGetValue(id, out int value) ? value : 0;

        public bool TryGet(int id, out TaskDefinition definition)
        {
            if ((uint)id < (uint)_definitions.Count && _definitions[id] != null)
            {
                definition = _definitions[id]!;
                return true;
            }

            definition = null!;
            return false;
        }

        public bool TryGet(string id, out TaskDefinition definition)
        {
            if (_nameToId.TryGetValue(id, out int definitionId))
            {
                return TryGet(definitionId, out definition);
            }

            definition = null!;
            return false;
        }
    }
}
