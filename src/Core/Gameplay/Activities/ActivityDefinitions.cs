using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Engine;

namespace Ludots.Core.Gameplay.Activities
{
    public sealed class ActivityRepeatCooldown
    {
        [JsonPropertyName("duration_ticks")]
        public int DurationTicks { get; set; }

        [JsonPropertyName("clock_domain")]
        public ClockDomainId ClockDomain { get; set; } = ClockDomainId.Step;
    }

    public sealed class ActivityConditionRef
    {
        [JsonPropertyName("condition_key")]
        public string ConditionKey { get; set; } = string.Empty;

        [JsonPropertyName("parameters")]
        public Dictionary<string, object?> Parameters { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed class ActivityEffectRef
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

    public sealed class ActivityOptionDefinition
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("is_baseline")]
        public bool IsBaseline { get; set; }

        [JsonPropertyName("show_condition")]
        public ActivityConditionRef? ShowCondition { get; set; }

        [JsonPropertyName("execute_condition")]
        public ActivityConditionRef? ExecuteCondition { get; set; }

        [JsonPropertyName("effects")]
        public List<ActivityEffectRef> Effects { get; set; } = new();
    }

    public sealed class ActivityDefinition : IIdentifiable
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("source_key")]
        public string SourceKey { get; set; } = string.Empty;

        [JsonPropertyName("dispatch_policy")]
        public ActivityDispatchPolicy DispatchPolicy { get; set; } = ActivityDispatchPolicy.Forced;

        [JsonPropertyName("repeat_policy")]
        public ActivityRepeatPolicy RepeatPolicy { get; set; } = ActivityRepeatPolicy.PendingDedupe;

        [JsonPropertyName("repeat_cooldown")]
        public ActivityRepeatCooldown? RepeatCooldown { get; set; }

        [JsonPropertyName("mutex_group")]
        public string MutexGroup { get; set; } = string.Empty;

        [JsonPropertyName("trigger_condition")]
        public ActivityConditionRef? TriggerCondition { get; set; }

        [JsonPropertyName("options")]
        public List<ActivityOptionDefinition> Options { get; set; } = new();

        [JsonPropertyName("automatic_effects")]
        public List<ActivityEffectRef> AutomaticEffects { get; set; } = new();

        [JsonPropertyName("presentation_cue")]
        public string PresentationCue { get; set; } = "activity_presented";
    }

    public sealed class ActivityDefinitionRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ActivityDefinition?> _definitions = new() { null };

        public IEnumerable<ActivityDefinition> Definitions
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

        public int Register(string id, ActivityDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Activity id is required.", nameof(id));
            }

            ArgumentNullException.ThrowIfNull(definition);
            definition.Id = id;
            if (string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                definition.DisplayName = id;
            }

            ValidateDefinition(definition);

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

        public bool TryGet(int id, out ActivityDefinition definition)
        {
            if ((uint)id < (uint)_definitions.Count && _definitions[id] != null)
            {
                definition = _definitions[id]!;
                return true;
            }

            definition = null!;
            return false;
        }

        public bool TryGet(string id, out ActivityDefinition definition)
        {
            if (_nameToId.TryGetValue(id, out int definitionId))
            {
                return TryGet(definitionId, out definition);
            }

            definition = null!;
            return false;
        }

        private static void ValidateDefinition(ActivityDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(definition.SourceKey))
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' requires source_key.");
            }

            if (!Enum.IsDefined(typeof(ActivityRepeatPolicy), definition.RepeatPolicy))
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' has unknown repeat_policy value '{(int)definition.RepeatPolicy}'.");
            }

            if (definition.RepeatPolicy == ActivityRepeatPolicy.Cooldown)
            {
                if (definition.RepeatCooldown == null)
                {
                    throw new InvalidOperationException(
                        $"Activity '{definition.Id}' uses cooldown repeat_policy and requires repeat_cooldown.");
                }

                if (definition.RepeatCooldown.DurationTicks <= 0)
                {
                    throw new InvalidOperationException(
                        $"Activity '{definition.Id}' repeat_cooldown.duration_ticks must be positive.");
                }

                if (!Enum.IsDefined(typeof(ClockDomainId), definition.RepeatCooldown.ClockDomain))
                {
                    throw new InvalidOperationException(
                        $"Activity '{definition.Id}' repeat_cooldown.clock_domain is unknown.");
                }
            }
            else if (definition.RepeatCooldown != null)
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' declares repeat_cooldown but repeat_policy is not cooldown.");
            }

            if (definition.RepeatPolicy == ActivityRepeatPolicy.Mutex)
            {
                if (string.IsNullOrWhiteSpace(definition.MutexGroup))
                {
                    throw new InvalidOperationException(
                        $"Activity '{definition.Id}' uses mutex repeat_policy and requires mutex_group.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(definition.MutexGroup))
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' declares mutex_group but repeat_policy is not mutex.");
            }

            if (definition.DispatchPolicy == ActivityDispatchPolicy.Automatic)
            {
                if (definition.Options.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Activity '{definition.Id}' is automatic and must not declare options.");
                }

                return;
            }

            if (definition.Options.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' requires at least one option.");
            }

            bool hasBaseline = false;
            for (int i = 0; i < definition.Options.Count; i++)
            {
                ActivityOptionDefinition option = definition.Options[i];
                if (string.IsNullOrWhiteSpace(option.Id))
                {
                    throw new InvalidOperationException(
                        $"Activity '{definition.Id}' option[{i}] requires id.");
                }

                for (int j = 0; j < option.Effects.Count; j++)
                {
                    if (string.IsNullOrWhiteSpace(option.Effects[j].EffectKey))
                    {
                        throw new InvalidOperationException(
                            $"Activity '{definition.Id}' option '{option.Id}' effect[{j}] requires effect_key.");
                    }
                }

                if (option.IsBaseline)
                {
                    hasBaseline = true;
                }
            }

            if (!hasBaseline)
            {
                throw new InvalidOperationException(
                    $"Activity '{definition.Id}' requires a baseline option.");
            }
        }
    }
}
