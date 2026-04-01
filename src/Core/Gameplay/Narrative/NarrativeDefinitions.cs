using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Narrative
{
    public enum NarrativeValueKind
    {
        Int = 0,
        Float = 1,
        Bool = 2,
        String = 3,
    }

    public enum NarrativeComparisonOperator
    {
        Equals = 0,
        NotEquals = 1,
        Greater = 2,
        GreaterOrEqual = 3,
        Less = 4,
        LessOrEqual = 5,
        Truthy = 6,
        Falsy = 7,
    }

    public enum NarrativeConditionKind
    {
        Variable = 0,
        QuestState = 1,
        SignalCount = 2,
        EntityTag = 3,
        EntityAttribute = 4,
    }

    public enum NarrativeActionKind
    {
        SetVariable = 0,
        AddVariable = 1,
        StartQuest = 2,
        AdvanceQuestStage = 3,
        StartDialogue = 4,
        StartCinematic = 5,
        EmitSignal = 6,
        CompleteQuest = 7,
        FailQuest = 8,
        ActivateCamera = 9,
        ClearCamera = 10,
    }

    public enum NarrativeQuestState
    {
        Inactive = 0,
        Active = 1,
        Completed = 2,
        Failed = 3,
    }

    public sealed class NarrativeVariableDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public NarrativeValueKind Kind { get; set; } = NarrativeValueKind.Int;
        public int DefaultInt { get; set; }
        public float DefaultFloat { get; set; }
        public bool DefaultBool { get; set; }
        public string DefaultString { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class NarrativeConditionDefinition
    {
        public NarrativeConditionKind Kind { get; set; } = NarrativeConditionKind.Variable;
        public string VariableId { get; set; } = string.Empty;
        public NarrativeComparisonOperator Operator { get; set; } = NarrativeComparisonOperator.Equals;
        public int IntValue { get; set; }
        public float FloatValue { get; set; }
        public bool BoolValue { get; set; }
        public string StringValue { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public NarrativeQuestState QuestState { get; set; } = NarrativeQuestState.Active;
        public string SignalId { get; set; } = string.Empty;
        public string EntityAlias { get; set; } = string.Empty;
        public string TagId { get; set; } = string.Empty;
        public string AttributeId { get; set; } = string.Empty;
    }

    public sealed class NarrativeActionDefinition
    {
        public NarrativeActionKind Kind { get; set; } = NarrativeActionKind.EmitSignal;
        public string VariableId { get; set; } = string.Empty;
        public NarrativeValueKind ValueKind { get; set; } = NarrativeValueKind.Int;
        public int IntValue { get; set; }
        public float FloatValue { get; set; }
        public bool BoolValue { get; set; }
        public string StringValue { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public string StageId { get; set; } = string.Empty;
        public string DialogueId { get; set; } = string.Empty;
        public string CinematicId { get; set; } = string.Empty;
        public string SignalId { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
    }

    public sealed class NarrativeQuestStageDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ObjectiveText { get; set; } = string.Empty;
        public string ObjectiveHint { get; set; } = string.Empty;
        public string DialogueOnEnterId { get; set; } = string.Empty;
        public string CinematicOnEnterId { get; set; } = string.Empty;
        public List<string> RequiredSignals { get; set; } = new();
        public List<NarrativeConditionDefinition> CompletionConditions { get; set; } = new();
        public List<NarrativeActionDefinition> OnEnter { get; set; } = new();
        public List<NarrativeActionDefinition> OnComplete { get; set; } = new();
    }

    public sealed class NarrativeQuestDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<NarrativeActionDefinition> OnStart { get; set; } = new();
        public List<NarrativeActionDefinition> OnComplete { get; set; } = new();
        public List<NarrativeActionDefinition> OnFail { get; set; } = new();
        public List<NarrativeQuestStageDefinition> Stages { get; set; } = new();
    }

    public sealed class NarrativeDialogueChoiceDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string NextNodeId { get; set; } = string.Empty;
        public List<NarrativeConditionDefinition> Conditions { get; set; } = new();
        public List<NarrativeActionDefinition> Actions { get; set; } = new();
    }

    public sealed class NarrativeDialogueNodeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string SpeakerAlias { get; set; } = string.Empty;
        public string SpeakerName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
        public string NextNodeId { get; set; } = string.Empty;
        public float AutoAdvanceSeconds { get; set; }
        public List<NarrativeActionDefinition> OnEnter { get; set; } = new();
        public List<NarrativeDialogueChoiceDefinition> Choices { get; set; } = new();
    }

    public sealed class NarrativeDialogueDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string StartNodeId { get; set; } = string.Empty;
        public List<NarrativeDialogueNodeDefinition> Nodes { get; set; } = new();
    }

    public sealed class NarrativeCinematicStepDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
        public string SpeakerAlias { get; set; } = string.Empty;
        public string SpeakerName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public float DurationSeconds { get; set; } = 0.75f;
        public bool RequiresAdvance { get; set; }
        public List<NarrativeActionDefinition> OnEnter { get; set; } = new();
    }

    public sealed class NarrativeCinematicDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool ClearCameraOnComplete { get; set; } = true;
        public List<NarrativeCinematicStepDefinition> Steps { get; set; } = new();
    }

    public sealed class NarrativeDefinitionRegistry
    {
        private readonly Dictionary<string, NarrativeQuestDefinition> _quests = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NarrativeDialogueDefinition> _dialogues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NarrativeCinematicDefinition> _cinematics = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NarrativeVariableDefinition> _variables = new(StringComparer.OrdinalIgnoreCase);

        public NarrativeDefinitionRegistry()
        {
            VariableIds = new StringIntRegistry(capacity: 128, comparer: StringComparer.OrdinalIgnoreCase);
        }

        public StringIntRegistry VariableIds { get; }
        public IEnumerable<NarrativeQuestDefinition> Quests => _quests.Values;
        public IEnumerable<NarrativeDialogueDefinition> Dialogues => _dialogues.Values;
        public IEnumerable<NarrativeCinematicDefinition> Cinematics => _cinematics.Values;
        public IEnumerable<NarrativeVariableDefinition> Variables => _variables.Values;

        public void Clear()
        {
            _quests.Clear();
            _dialogues.Clear();
            _cinematics.Clear();
            _variables.Clear();
        }

        public void Register(NarrativeVariableDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                return;
            }

            VariableIds.Register(definition.Id);
            _variables[definition.Id] = definition;
        }

        public void Register(NarrativeQuestDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                return;
            }

            _quests[definition.Id] = definition;
        }

        public void Register(NarrativeDialogueDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                return;
            }

            _dialogues[definition.Id] = definition;
        }

        public void Register(NarrativeCinematicDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                return;
            }

            _cinematics[definition.Id] = definition;
        }

        public bool TryGetVariable(string id, out NarrativeVariableDefinition definition)
            => _variables.TryGetValue(id, out definition!);

        public bool TryGetQuest(string id, out NarrativeQuestDefinition definition)
            => _quests.TryGetValue(id, out definition!);

        public bool TryGetDialogue(string id, out NarrativeDialogueDefinition definition)
            => _dialogues.TryGetValue(id, out definition!);

        public bool TryGetCinematic(string id, out NarrativeCinematicDefinition definition)
            => _cinematics.TryGetValue(id, out definition!);
    }
}
