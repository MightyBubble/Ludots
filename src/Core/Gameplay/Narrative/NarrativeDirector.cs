using System;
using System.Collections.Generic;
using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Quests;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Narrative
{
    public static class NarrativeInputActionIds
    {
        public const string Interact = "NarrativeInteract";
        public const string Advance = "NarrativeAdvance";
        public const string Skip = "NarrativeSkip";
        public const string Choice1 = "NarrativeChoice1";
        public const string Choice2 = "NarrativeChoice2";
        public const string Choice3 = "NarrativeChoice3";
    }

    public sealed record NarrativeEntityBindingSnapshot(
        string Alias,
        Entity Entity);

    public sealed record NarrativeDialogueSnapshot(
        string DialogueId,
        string NodeId,
        float ElapsedSeconds);

    public sealed record NarrativeCinematicSnapshot(
        string CinematicId,
        int StepIndex,
        float ElapsedSeconds,
        bool AdvanceRequested);

    public sealed record NarrativeDirectorSnapshot(
        IReadOnlyDictionary<string, NarrativeValue> Variables,
        IReadOnlyList<NarrativeEntityBindingSnapshot> Bindings,
        NarrativeDialogueSnapshot ActiveDialogue,
        NarrativeCinematicSnapshot ActiveCinematic);

    public sealed class NarrativeDirector
    {
        private readonly GameEngine _engine;
        private readonly NarrativeDefinitionRegistry _definitions;
        private readonly QuestRuntimeService _questRuntime;
        private readonly NarrativeValueStore _variables;
        private readonly Dictionary<string, Entity> _bindings = new(StringComparer.OrdinalIgnoreCase);
        private NarrativeDialogueSession _activeDialogue;
        private NarrativeCinematicSession _activeCinematic;

        public NarrativeDirector(GameEngine engine, NarrativeDefinitionRegistry definitions, QuestRuntimeService questRuntime)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _questRuntime = questRuntime ?? throw new ArgumentNullException(nameof(questRuntime));
            _variables = new NarrativeValueStore(definitions);
            _questRuntime.QuestEventPublished += HandleQuestEventPublished;
        }

        public bool HasActiveDialogue => _activeDialogue != null;
        public bool HasActiveCinematic => _activeCinematic != null;

        public void ResetState()
        {
            ResetNarrativeState();
            _questRuntime.ResetState();
        }

        public void ResetNarrativeState()
        {
            _variables.ResetToDefaults();
            _bindings.Clear();
            _activeDialogue = null;
            _activeCinematic = null;
        }

        public void BindEntity(string alias, Entity entity)
        {
            if (string.IsNullOrWhiteSpace(alias) || entity == Entity.Null)
            {
                return;
            }

            _bindings[alias] = entity;
        }

        public bool TryResolveEntity(string alias, out Entity entity)
        {
            if (!string.IsNullOrWhiteSpace(alias) && _bindings.TryGetValue(alias, out entity))
            {
                return entity != Entity.Null && _engine.World.IsAlive(entity);
            }

            entity = Entity.Null;
            return false;
        }

        public NarrativeValue GetVariable(string variableId) => _variables.Get(variableId);

        public string GetVariableText(string variableId)
        {
            var value = _variables.Get(variableId);
            return value.Kind switch
            {
                NarrativeValueKind.Float => value.FloatValue.ToString("0.##"),
                NarrativeValueKind.Bool => value.BoolValue ? "true" : "false",
                NarrativeValueKind.String => value.StringValue,
                _ => value.IntValue.ToString(),
            };
        }

        public bool TryGetQuestState(string questId, out QuestState state, out string stageId)
            => _questRuntime.TryGetQuestState(questId, out state, out stageId);

        public IReadOnlyList<QuestView> GetQuestViews()
            => _questRuntime.GetQuestViews();

        public NarrativeDirectorSnapshot CaptureSnapshot()
        {
            var bindings = new List<NarrativeEntityBindingSnapshot>(_bindings.Count);
            foreach (var pair in _bindings)
            {
                bindings.Add(new NarrativeEntityBindingSnapshot(pair.Key, pair.Value));
            }

            NarrativeDialogueSnapshot dialogue = null;
            if (_activeDialogue?.CurrentNode != null)
            {
                dialogue = new NarrativeDialogueSnapshot(
                    _activeDialogue.Definition.Id,
                    _activeDialogue.CurrentNode.Id,
                    _activeDialogue.ElapsedSeconds);
            }

            NarrativeCinematicSnapshot cinematic = null;
            if (_activeCinematic?.CurrentStep != null)
            {
                cinematic = new NarrativeCinematicSnapshot(
                    _activeCinematic.Definition.Id,
                    _activeCinematic.StepIndex,
                    _activeCinematic.ElapsedSeconds,
                    _activeCinematic.AdvanceRequested);
            }

            return new NarrativeDirectorSnapshot(
                _variables.CaptureSnapshot(),
                bindings,
                dialogue,
                cinematic);
        }

        public void RestoreSnapshot(NarrativeDirectorSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _variables.RestoreSnapshot(snapshot.Variables);
            _bindings.Clear();
            for (int i = 0; i < snapshot.Bindings.Count; i++)
            {
                NarrativeEntityBindingSnapshot binding = snapshot.Bindings[i];
                _bindings[binding.Alias] = binding.Entity;
            }

            _activeDialogue = null;
            if (snapshot.ActiveDialogue != null)
            {
                if (!_definitions.TryGetDialogue(snapshot.ActiveDialogue.DialogueId, out NarrativeDialogueDefinition definition))
                {
                    throw new InvalidOperationException(
                        $"Narrative dialogue '{snapshot.ActiveDialogue.DialogueId}' is not registered.");
                }

                NarrativeDialogueNodeDefinition node = definition.Nodes.Find(n =>
                    string.Equals(n.Id, snapshot.ActiveDialogue.NodeId, StringComparison.OrdinalIgnoreCase));
                if (node == null)
                {
                    throw new InvalidOperationException(
                        $"Narrative dialogue '{snapshot.ActiveDialogue.DialogueId}' node '{snapshot.ActiveDialogue.NodeId}' is not registered.");
                }

                _activeDialogue = new NarrativeDialogueSession(definition)
                {
                    CurrentNode = node,
                    ElapsedSeconds = snapshot.ActiveDialogue.ElapsedSeconds,
                    CurrentChoices = BuildDialogueChoices(node)
                };
            }

            _activeCinematic = null;
            if (snapshot.ActiveCinematic != null)
            {
                if (!_definitions.TryGetCinematic(snapshot.ActiveCinematic.CinematicId, out NarrativeCinematicDefinition definition))
                {
                    throw new InvalidOperationException(
                        $"Narrative cinematic '{snapshot.ActiveCinematic.CinematicId}' is not registered.");
                }

                int stepIndex = snapshot.ActiveCinematic.StepIndex;
                if (stepIndex < 0 || stepIndex >= definition.Steps.Count)
                {
                    throw new InvalidOperationException(
                        $"Narrative cinematic '{snapshot.ActiveCinematic.CinematicId}' has invalid step index {stepIndex}.");
                }

                _activeCinematic = new NarrativeCinematicSession(definition)
                {
                    StepIndex = stepIndex,
                    CurrentStep = definition.Steps[stepIndex],
                    ElapsedSeconds = snapshot.ActiveCinematic.ElapsedSeconds,
                    AdvanceRequested = snapshot.ActiveCinematic.AdvanceRequested
                };
            }
        }

        public void StartQuest(string questId)
        {
            _questRuntime.StartQuest(questId);
        }

        public void AdvanceQuestStage(string questId, string targetStageId = "")
        {
            _questRuntime.AdvanceQuestStage(questId, targetStageId);
        }

        public void CompleteQuest(string questId)
        {
            _questRuntime.CompleteQuest(questId);
        }

        public void FailQuest(string questId)
        {
            _questRuntime.FailQuest(questId);
        }

        public void StartDialogue(string dialogueId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId))
            {
                throw new ArgumentException("Narrative dialogue id is required.", nameof(dialogueId));
            }

            if (!_definitions.TryGetDialogue(dialogueId, out var definition))
            {
                throw new InvalidOperationException($"Narrative dialogue '{dialogueId}' is not registered.");
            }

            if (string.IsNullOrWhiteSpace(definition.StartNodeId))
            {
                throw new InvalidOperationException($"Narrative dialogue '{dialogueId}' must define a start node.");
            }

            _activeDialogue = new NarrativeDialogueSession(definition);
            EnterDialogueNode(definition.StartNodeId);
        }

        public void AdvanceDialogue()
        {
            if (_activeDialogue == null || _activeDialogue.HasChoices)
            {
                return;
            }

            string nextNodeId = _activeDialogue.CurrentNode?.NextNodeId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nextNodeId))
            {
                _activeDialogue = null;
                return;
            }

            EnterDialogueNode(nextNodeId);
        }

        public void ChooseDialogueOption(int index)
        {
            if (_activeDialogue?.CurrentChoices == null || index < 0 || index >= _activeDialogue.CurrentChoices.Count)
            {
                return;
            }

            var choice = _activeDialogue.CurrentChoices[index];
            ExecuteActions(choice.Actions);
            FireNarrativeEvent(NarrativeEventKeys.DialogueChoiceCommitted, ctx =>
            {
                ctx.Set(NarrativeServiceKeys.DialogueId, _activeDialogue.Definition.Id);
                ctx.Set(NarrativeServiceKeys.DialogueNodeId, _activeDialogue.CurrentNode?.Id ?? string.Empty);
                ctx.Set(NarrativeServiceKeys.DialogueChoiceId, choice.Id);
                ctx.Set(NarrativeServiceKeys.BodyText, FormatText(choice.Text));
            });

            if (string.IsNullOrWhiteSpace(choice.NextNodeId))
            {
                _activeDialogue = null;
                return;
            }

            EnterDialogueNode(choice.NextNodeId);
        }

        public void StartCinematic(string cinematicId)
        {
            if (string.IsNullOrWhiteSpace(cinematicId))
            {
                throw new ArgumentException("Narrative cinematic id is required.", nameof(cinematicId));
            }

            if (!_definitions.TryGetCinematic(cinematicId, out var definition))
            {
                throw new InvalidOperationException($"Narrative cinematic '{cinematicId}' is not registered.");
            }

            if (definition.Steps.Count == 0)
            {
                throw new InvalidOperationException($"Narrative cinematic '{cinematicId}' must define at least one step.");
            }

            _activeCinematic = new NarrativeCinematicSession(definition);
            EnterCinematicStep(0);
        }

        public void SkipCinematic()
        {
            if (_activeCinematic == null)
            {
                return;
            }

            CompleteCinematic();
        }

        public void EmitSignal(string signalId, int intValue = 0, string stringValue = "")
        {
            if (string.IsNullOrWhiteSpace(signalId))
            {
                throw new ArgumentException("Narrative signal id is required.", nameof(signalId));
            }

            FireNarrativeEvent(NarrativeEventKeys.Signal, ctx =>
            {
                ctx.Set(NarrativeServiceKeys.SignalId, signalId);
                ctx.Set(NarrativeServiceKeys.SignalIntValue, intValue);
                ctx.Set(NarrativeServiceKeys.SignalStringValue, stringValue ?? string.Empty);
            });

            _questRuntime.EmitSignal(signalId);
        }

        public void Update(float dt)
        {
            ConsumeInput();
            TickDialogue(dt);
            TickCinematic(dt);
        }

        public string BuildQuestSummary()
        {
            var sb = new StringBuilder();
            IReadOnlyList<QuestView> quests = _questRuntime.GetQuestViews();
            for (int i = 0; i < quests.Count; i++)
            {
                QuestView quest = quests[i];
                if (quest.State == QuestState.Inactive)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append(quest.DisplayName);
                sb.Append(": ");
                sb.Append(quest.State);
                if (!string.IsNullOrWhiteSpace(quest.StageTitle))
                {
                    sb.Append(" - ");
                    sb.Append(quest.StageTitle);
                }
            }

            return sb.Length == 0 ? "No active quests" : sb.ToString();
        }

        public string BuildObjectiveSummary()
        {
            IReadOnlyList<QuestView> quests = _questRuntime.GetQuestViews();
            for (int i = 0; i < quests.Count; i++)
            {
                QuestView quest = quests[i];
                if (quest.State == QuestState.Active)
                {
                    return $"{quest.DisplayName}: {quest.ObjectiveText}";
                }
            }

            return "Awaiting quest";
        }

        public string BuildDialogueSummary()
        {
            if (_activeDialogue?.CurrentNode == null)
            {
                return "No active dialogue";
            }

            string speaker = ResolveSpeakerName(_activeDialogue.CurrentNode.SpeakerAlias, _activeDialogue.CurrentNode.SpeakerName);
            return $"{speaker}: {FormatText(_activeDialogue.CurrentNode.Text)}";
        }

        public IReadOnlyList<NarrativeDialogueChoiceDefinition> GetCurrentChoices()
            => _activeDialogue?.CurrentChoices != null
                ? _activeDialogue.CurrentChoices
                : Array.Empty<NarrativeDialogueChoiceDefinition>();

        public bool TryGetActiveDialogueView(out NarrativeDialogueView view)
        {
            if (_activeDialogue?.CurrentNode == null)
            {
                view = null!;
                return false;
            }

            var choices = new List<NarrativeDialogueChoiceView>(_activeDialogue.CurrentChoices.Count);
            for (int i = 0; i < _activeDialogue.CurrentChoices.Count; i++)
            {
                NarrativeDialogueChoiceDefinition choice = _activeDialogue.CurrentChoices[i];
                choices.Add(new NarrativeDialogueChoiceView(
                    choice.Id,
                    FormatText(choice.Text),
                    choice.NextNodeId));
            }

            view = new NarrativeDialogueView(
                _activeDialogue.Definition.Id,
                _activeDialogue.Definition.DisplayName,
                _activeDialogue.CurrentNode.Id,
                ResolveSpeakerName(_activeDialogue.CurrentNode.SpeakerAlias, _activeDialogue.CurrentNode.SpeakerName),
                FormatText(_activeDialogue.CurrentNode.Text),
                _activeDialogue.CurrentNode.CameraId,
                _activeDialogue.CurrentNode.AutoAdvanceSeconds,
                _activeDialogue.ElapsedSeconds,
                choices);
            return true;
        }

        public string BuildCinematicSummary()
        {
            if (_activeCinematic?.CurrentStep == null)
            {
                return "No active cinematic";
            }

            string speaker = ResolveSpeakerName(_activeCinematic.CurrentStep.SpeakerAlias, _activeCinematic.CurrentStep.SpeakerName);
            string body = FormatText(_activeCinematic.CurrentStep.Text);
            return string.IsNullOrWhiteSpace(speaker) ? body : $"{speaker}: {body}";
        }

        public bool TryGetActiveCinematicView(out NarrativeCinematicView view)
        {
            if (_activeCinematic?.CurrentStep == null)
            {
                view = null!;
                return false;
            }

            view = new NarrativeCinematicView(
                _activeCinematic.Definition.Id,
                _activeCinematic.Definition.DisplayName,
                _activeCinematic.CurrentStep.Id,
                ResolveSpeakerName(_activeCinematic.CurrentStep.SpeakerAlias, _activeCinematic.CurrentStep.SpeakerName),
                FormatText(_activeCinematic.CurrentStep.Text),
                _activeCinematic.CurrentStep.CameraId,
                _activeCinematic.CurrentStep.DurationSeconds,
                _activeCinematic.ElapsedSeconds,
                _activeCinematic.CurrentStep.RequiresAdvance);
            return true;
        }

        public string BuildVariableSummary(params string[] variableIds)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < variableIds.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append(variableIds[i]);
                sb.Append('=');
                sb.Append(GetVariableText(variableIds[i]));
            }

            return sb.ToString();
        }

        private void ConsumeInput()
        {
            var input = _engine.GetService(CoreServiceKeys.AuthoritativeInput);
            if (input == null)
            {
                return;
            }

            if (_activeCinematic != null)
            {
                if (input.PressedThisFrame(NarrativeInputActionIds.Skip))
                {
                    SkipCinematic();
                    return;
                }

                if (input.PressedThisFrame(NarrativeInputActionIds.Advance))
                {
                    _activeCinematic.AdvanceRequested = true;
                }
            }

            if (_activeDialogue == null)
            {
                return;
            }

            if (_activeDialogue.HasChoices)
            {
                if (input.PressedThisFrame(NarrativeInputActionIds.Choice1)) ChooseDialogueOption(0);
                if (input.PressedThisFrame(NarrativeInputActionIds.Choice2)) ChooseDialogueOption(1);
                if (input.PressedThisFrame(NarrativeInputActionIds.Choice3)) ChooseDialogueOption(2);
            }
            else if (input.PressedThisFrame(NarrativeInputActionIds.Advance))
            {
                AdvanceDialogue();
            }
        }

        private void TickDialogue(float dt)
        {
            if (_activeDialogue?.CurrentNode == null || _activeDialogue.HasChoices)
            {
                return;
            }

            if (_activeDialogue.CurrentNode.AutoAdvanceSeconds <= 0f)
            {
                return;
            }

            _activeDialogue.ElapsedSeconds += dt;
            if (_activeDialogue.ElapsedSeconds >= _activeDialogue.CurrentNode.AutoAdvanceSeconds)
            {
                AdvanceDialogue();
            }
        }

        private void TickCinematic(float dt)
        {
            if (_activeCinematic?.CurrentStep == null)
            {
                return;
            }

            _activeCinematic.ElapsedSeconds += dt;
            bool shouldAdvance = _activeCinematic.AdvanceRequested;
            if (!shouldAdvance && !_activeCinematic.CurrentStep.RequiresAdvance)
            {
                shouldAdvance = _activeCinematic.ElapsedSeconds >= Math.Max(0.05f, _activeCinematic.CurrentStep.DurationSeconds);
            }

            if (!shouldAdvance)
            {
                return;
            }

            int nextIndex = _activeCinematic.StepIndex + 1;
            if (nextIndex >= _activeCinematic.Definition.Steps.Count)
            {
                CompleteCinematic();
                return;
            }

            EnterCinematicStep(nextIndex);
        }

        private void HandleQuestEventPublished(QuestEvent questEvent)
        {
            switch (questEvent.Kind)
            {
                case QuestEventKind.StageChanged:
                    HandleQuestStageChanged(questEvent);
                    break;
                case QuestEventKind.Completed:
                    FireNarrativeEvent(NarrativeEventKeys.QuestCompleted, ctx =>
                    {
                        ctx.Set(NarrativeServiceKeys.QuestId, questEvent.QuestId);
                        ctx.Set(NarrativeServiceKeys.QuestStageId, questEvent.StageId);
                    });
                    break;
            }
        }

        private void HandleQuestStageChanged(QuestEvent questEvent)
        {
            if (!_questRuntime.TryGetStage(questEvent.QuestId, questEvent.StageId, out QuestStageDefinition stage))
            {
                throw new InvalidOperationException(
                    $"Quest event references missing stage '{questEvent.StageId}' on quest '{questEvent.QuestId}'.");
            }

            if (!string.IsNullOrWhiteSpace(stage.CinematicOnEnterId))
            {
                StartCinematic(stage.CinematicOnEnterId);
            }

            if (!string.IsNullOrWhiteSpace(stage.DialogueOnEnterId))
            {
                StartDialogue(stage.DialogueOnEnterId);
            }

            FireNarrativeEvent(NarrativeEventKeys.QuestStageChanged, ctx =>
            {
                ctx.Set(NarrativeServiceKeys.QuestId, questEvent.QuestId);
                ctx.Set(NarrativeServiceKeys.QuestStageId, stage.Id);
                ctx.Set(NarrativeServiceKeys.BodyText, stage.ObjectiveText);
            });
        }

        private void EnterDialogueNode(string nodeId)
        {
            if (_activeDialogue == null)
            {
                return;
            }

            var node = _activeDialogue.Definition.Nodes.Find(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
            if (node == null)
            {
                throw new InvalidOperationException(
                    $"Narrative dialogue '{_activeDialogue.Definition.Id}' references missing node '{nodeId}'.");
            }

            _activeDialogue.CurrentNode = node;
            _activeDialogue.ElapsedSeconds = 0f;
            _activeDialogue.CurrentChoices = BuildDialogueChoices(node);

            ExecuteActions(node.OnEnter);
            if (!string.IsNullOrWhiteSpace(node.CameraId))
            {
                ActivateCamera(node.CameraId);
            }

            FireNarrativeEvent(NarrativeEventKeys.DialogueNodeEntered, ctx =>
            {
                ctx.Set(NarrativeServiceKeys.DialogueId, _activeDialogue.Definition.Id);
                ctx.Set(NarrativeServiceKeys.DialogueNodeId, node.Id);
                ctx.Set(NarrativeServiceKeys.SpeakerName, ResolveSpeakerName(node.SpeakerAlias, node.SpeakerName));
                ctx.Set(NarrativeServiceKeys.BodyText, FormatText(node.Text));
            });
        }

        private void EnterCinematicStep(int stepIndex)
        {
            if (_activeCinematic == null || stepIndex < 0 || stepIndex >= _activeCinematic.Definition.Steps.Count)
            {
                return;
            }

            _activeCinematic.StepIndex = stepIndex;
            _activeCinematic.CurrentStep = _activeCinematic.Definition.Steps[stepIndex];
            _activeCinematic.ElapsedSeconds = 0f;
            _activeCinematic.AdvanceRequested = false;

            ExecuteActions(_activeCinematic.CurrentStep.OnEnter);
            if (!string.IsNullOrWhiteSpace(_activeCinematic.CurrentStep.CameraId))
            {
                ActivateCamera(_activeCinematic.CurrentStep.CameraId);
            }

            FireNarrativeEvent(NarrativeEventKeys.CinematicStepEntered, ctx =>
            {
                ctx.Set(NarrativeServiceKeys.CinematicId, _activeCinematic.Definition.Id);
                ctx.Set(NarrativeServiceKeys.CinematicStepId, _activeCinematic.CurrentStep.Id);
                ctx.Set(NarrativeServiceKeys.SpeakerName, ResolveSpeakerName(_activeCinematic.CurrentStep.SpeakerAlias, _activeCinematic.CurrentStep.SpeakerName));
                ctx.Set(NarrativeServiceKeys.BodyText, FormatText(_activeCinematic.CurrentStep.Text));
            });
        }

        private void CompleteCinematic()
        {
            if (_activeCinematic == null)
            {
                return;
            }

            var completed = _activeCinematic;
            if (completed.Definition.ClearCameraOnComplete)
            {
                _engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest { Clear = true });
            }

            _activeCinematic = null;
            FireNarrativeEvent(NarrativeEventKeys.CinematicCompleted, ctx =>
            {
                ctx.Set(NarrativeServiceKeys.CinematicId, completed.Definition.Id);
                ctx.Set(NarrativeServiceKeys.CinematicStepId, completed.CurrentStep?.Id ?? string.Empty);
            });
        }

        private void ExecuteActions(IReadOnlyList<NarrativeActionDefinition> actions)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                ExecuteAction(actions[i]);
            }
        }

        private void ExecuteAction(NarrativeActionDefinition action)
        {
            switch (action.Kind)
            {
                case NarrativeActionKind.SetVariable:
                    _variables.Set(action.VariableId, CreateValue(action));
                    break;
                case NarrativeActionKind.AddVariable:
                    _variables.Add(action.VariableId, CreateValue(action));
                    break;
                case NarrativeActionKind.StartQuest:
                    StartQuest(action.QuestId);
                    break;
                case NarrativeActionKind.AdvanceQuestStage:
                    AdvanceQuestStage(action.QuestId, action.StageId);
                    break;
                case NarrativeActionKind.StartDialogue:
                    StartDialogue(action.DialogueId);
                    break;
                case NarrativeActionKind.StartCinematic:
                    StartCinematic(action.CinematicId);
                    break;
                case NarrativeActionKind.EmitSignal:
                    EmitSignal(action.SignalId, action.IntValue, action.StringValue);
                    break;
                case NarrativeActionKind.CompleteQuest:
                    CompleteQuest(action.QuestId);
                    break;
                case NarrativeActionKind.FailQuest:
                    FailQuest(action.QuestId);
                    break;
                case NarrativeActionKind.ActivateCamera:
                    ActivateCamera(action.CameraId);
                    break;
                case NarrativeActionKind.ClearCamera:
                    _engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest { Clear = true });
                    break;
            }
        }

        private NarrativeValue CreateValue(NarrativeActionDefinition action)
        {
            return action.ValueKind switch
            {
                NarrativeValueKind.Float => NarrativeValue.FromFloat(action.FloatValue),
                NarrativeValueKind.Bool => NarrativeValue.FromBool(action.BoolValue),
                NarrativeValueKind.String => NarrativeValue.FromString(FormatText(action.StringValue)),
                _ => NarrativeValue.FromInt(action.IntValue),
            };
        }

        private bool EvaluateConditions(IReadOnlyList<NarrativeConditionDefinition> conditions)
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                if (!EvaluateCondition(conditions[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool EvaluateCondition(NarrativeConditionDefinition condition)
        {
            switch (condition.Kind)
            {
                case NarrativeConditionKind.QuestState:
                    return TryGetQuestState(condition.QuestId, out var questState, out _) && questState == condition.QuestState;
                case NarrativeConditionKind.SignalCount:
                    _questRuntime.Signals.TryGetValue(condition.SignalId, out int count);
                    return CompareInt(count, condition.IntValue, condition.Operator);
                case NarrativeConditionKind.EntityTag:
                    return EvaluateEntityTag(condition);
                case NarrativeConditionKind.EntityAttribute:
                    return EvaluateEntityAttribute(condition);
                default:
                    return EvaluateVariable(condition);
            }
        }

        private bool EvaluateVariable(NarrativeConditionDefinition condition)
        {
            var value = _variables.Get(condition.VariableId);
            return value.Kind switch
            {
                NarrativeValueKind.Float => CompareFloat(value.FloatValue, condition.FloatValue, condition.Operator),
                NarrativeValueKind.Bool => CompareBool(value.BoolValue, condition.BoolValue, condition.Operator),
                NarrativeValueKind.String => CompareString(value.StringValue, FormatText(condition.StringValue), condition.Operator),
                _ => CompareInt(value.IntValue, condition.IntValue, condition.Operator),
            };
        }

        private bool EvaluateEntityTag(NarrativeConditionDefinition condition)
        {
            if (!TryResolveEntity(condition.EntityAlias, out var entity) || !_engine.World.TryGet(entity, out GameplayTagContainer tags))
            {
                return false;
            }

            int tagId = TagRegistry.GetId(condition.TagId);
            return tagId > 0 && CompareBool(tags.HasTag(tagId), condition.BoolValue, condition.Operator);
        }

        private bool EvaluateEntityAttribute(NarrativeConditionDefinition condition)
        {
            if (!TryResolveEntity(condition.EntityAlias, out var entity) || !_engine.World.TryGet(entity, out AttributeBuffer attributes))
            {
                return false;
            }

            int attributeId = AttributeRegistry.GetId(condition.AttributeId);
            if (attributeId <= 0)
            {
                return false;
            }

            return CompareFloat(attributes.GetCurrent(attributeId), condition.FloatValue, condition.Operator);
        }

        private void FireNarrativeEvent(EventKey eventKey, Action<ScriptContext> enrichContext)
        {
            var ctx = _engine.CreateContext();
            enrichContext(ctx);
            string mapId = _engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(mapId))
            {
                _engine.TriggerManager.FireMapEvent(new MapId(mapId), eventKey, ctx);
                return;
            }

            _engine.TriggerManager.FireEvent(eventKey, ctx);
        }

        private void ActivateCamera(string cameraId)
        {
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                return;
            }

            _engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest { Id = cameraId });
        }

        private string ResolveSpeakerName(string speakerAlias, string speakerName)
        {
            if (!string.IsNullOrWhiteSpace(speakerName))
            {
                return FormatText(speakerName);
            }

            if (TryResolveEntity(speakerAlias, out var entity) && _engine.World.TryGet(entity, out Name name))
            {
                return name.Value;
            }

            return speakerAlias;
        }

        private string FormatText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string result = text;
            foreach (var variable in _definitions.Variables)
            {
                result = result.Replace("{" + variable.Id + "}", GetVariableText(variable.Id), StringComparison.OrdinalIgnoreCase);
            }

            foreach (var binding in _bindings)
            {
                if (_engine.World.IsAlive(binding.Value) && _engine.World.TryGet(binding.Value, out Name name))
                {
                    result = result.Replace("{" + binding.Key + "}", name.Value, StringComparison.OrdinalIgnoreCase);
                }
            }

            return result;
        }

        private static bool CompareInt(int left, int right, NarrativeComparisonOperator op)
        {
            return op switch
            {
                NarrativeComparisonOperator.NotEquals => left != right,
                NarrativeComparisonOperator.Greater => left > right,
                NarrativeComparisonOperator.GreaterOrEqual => left >= right,
                NarrativeComparisonOperator.Less => left < right,
                NarrativeComparisonOperator.LessOrEqual => left <= right,
                NarrativeComparisonOperator.Truthy => left != 0,
                NarrativeComparisonOperator.Falsy => left == 0,
                _ => left == right,
            };
        }

        private static bool CompareFloat(float left, float right, NarrativeComparisonOperator op)
        {
            return op switch
            {
                NarrativeComparisonOperator.NotEquals => Math.Abs(left - right) > 0.0001f,
                NarrativeComparisonOperator.Greater => left > right,
                NarrativeComparisonOperator.GreaterOrEqual => left >= right,
                NarrativeComparisonOperator.Less => left < right,
                NarrativeComparisonOperator.LessOrEqual => left <= right,
                NarrativeComparisonOperator.Truthy => Math.Abs(left) > 0.0001f,
                NarrativeComparisonOperator.Falsy => Math.Abs(left) <= 0.0001f,
                _ => Math.Abs(left - right) <= 0.0001f,
            };
        }

        private static bool CompareBool(bool left, bool right, NarrativeComparisonOperator op)
        {
            return op switch
            {
                NarrativeComparisonOperator.NotEquals => left != right,
                NarrativeComparisonOperator.Truthy => left,
                NarrativeComparisonOperator.Falsy => !left,
                _ => left == right,
            };
        }

        private static bool CompareString(string left, string right, NarrativeComparisonOperator op)
        {
            bool equals = string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            return op switch
            {
                NarrativeComparisonOperator.NotEquals => !equals,
                NarrativeComparisonOperator.Truthy => !string.IsNullOrWhiteSpace(left),
                NarrativeComparisonOperator.Falsy => string.IsNullOrWhiteSpace(left),
                _ => equals,
            };
        }

        private List<NarrativeDialogueChoiceDefinition> BuildDialogueChoices(NarrativeDialogueNodeDefinition node)
        {
            var choices = new List<NarrativeDialogueChoiceDefinition>();
            for (int i = 0; i < node.Choices.Count; i++)
            {
                if (EvaluateConditions(node.Choices[i].Conditions))
                {
                    choices.Add(node.Choices[i]);
                }
            }

            return choices;
        }

        private sealed class NarrativeDialogueSession
        {
            public NarrativeDialogueSession(NarrativeDialogueDefinition definition)
            {
                Definition = definition;
            }

            public NarrativeDialogueDefinition Definition { get; }
            public NarrativeDialogueNodeDefinition CurrentNode { get; set; }
            public float ElapsedSeconds { get; set; }
            public List<NarrativeDialogueChoiceDefinition> CurrentChoices { get; set; } = new();
            public bool HasChoices => CurrentChoices.Count > 0;
        }

        private sealed class NarrativeCinematicSession
        {
            public NarrativeCinematicSession(NarrativeCinematicDefinition definition)
            {
                Definition = definition;
            }

            public NarrativeCinematicDefinition Definition { get; }
            public int StepIndex { get; set; } = -1;
            public NarrativeCinematicStepDefinition CurrentStep { get; set; }
            public float ElapsedSeconds { get; set; }
            public bool AdvanceRequested { get; set; }
        }
    }
}
