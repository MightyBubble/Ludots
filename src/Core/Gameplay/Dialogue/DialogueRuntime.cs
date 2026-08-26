using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Story;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.Dialogue
{
    public sealed record DialogueSessionSnapshot(
        string DialogueId,
        string NodeId,
        float ElapsedSeconds);

    public sealed record DialogueBindingSnapshot(string Alias, Entity Entity);

    public sealed record DialogueRuntimeSnapshot(
        IReadOnlyList<DialogueBindingSnapshot> Bindings,
        DialogueSessionSnapshot? ActiveDialogue);

    public sealed class DialogueRuntime
    {
        private readonly GameEngine _engine;
        private readonly DialogueDefinitionRegistry _dialogues;
        private readonly StoryDefinitionRegistry _story;
        private readonly StoryGraphInvoker _graphs;
        private readonly TaskRuntimeService _tasks;
        private readonly PresentationTextCatalog? _textCatalog;
        private readonly Dictionary<string, Entity> _bindings = new(StringComparer.OrdinalIgnoreCase);
        private ActiveDialogueSession? _active;

        public DialogueRuntime(
            GameEngine engine,
            DialogueDefinitionRegistry dialogues,
            StoryDefinitionRegistry story,
            StoryGraphInvoker graphs,
            TaskRuntimeService tasks,
            PresentationTextCatalog? textCatalog)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _dialogues = dialogues ?? throw new ArgumentNullException(nameof(dialogues));
            _story = story ?? throw new ArgumentNullException(nameof(story));
            _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
            _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            _textCatalog = textCatalog;
            _tasks.TaskStateChanged += HandleTaskStateChanged;
        }

        public bool HasActiveDialogue => _active != null;

        public void ResetState()
        {
            _bindings.Clear();
            _active = null;
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

        public void StartDialogue(string dialogueId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId))
            {
                throw new ArgumentException("Dialogue id is required.", nameof(dialogueId));
            }

            DialogueDefinition definition = _dialogues.Require(dialogueId);
            _active = new ActiveDialogueSession(definition);
            EnterNode(definition.EntryNode);
        }

        public void AdvanceDialogue()
        {
            if (_active == null || _active.Choices.Count > 0)
            {
                return;
            }

            string nextNode = _active.CurrentNode?.NextNode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nextNode))
            {
                _active = null;
                return;
            }

            EnterNode(nextNode);
        }

        public void ChooseOption(int index)
        {
            if (_active == null || index < 0 || index >= _active.Choices.Count)
            {
                return;
            }

            DialogueChoiceDefinition choice = _active.Choices[index];
            Entity subject = ResolveSubject();
            _graphs.ExecuteAction(choice.ActionGraphId, subject);
            FireEvent(DialogueEventKeys.ChoiceCommitted, ctx =>
            {
                ctx.Set(DialogueServiceKeys.DialogueId, _active.Definition.Id);
                ctx.Set(DialogueServiceKeys.DialogueNodeId, _active.CurrentNode?.Id ?? string.Empty);
                ctx.Set(DialogueServiceKeys.DialogueChoiceId, choice.Id);
                ctx.Set(DialogueServiceKeys.LineId, choice.LineId);
                ctx.Set(DialogueServiceKeys.BodyText, ResolveLineText(choice.LineId));
            });

            if (string.IsNullOrWhiteSpace(choice.NextNode))
            {
                _active = null;
                return;
            }

            EnterNode(choice.NextNode);
        }

        public void Update(float dt)
        {
            ConsumeInput();
            TickAutoAdvance(dt);
        }

        public bool TryGetActiveView(out DialogueView view)
        {
            if (_active?.CurrentNode == null)
            {
                view = null!;
                return false;
            }

            DialogueNodeDefinition node = _active.CurrentNode;
            StoryLineDefinition line = _story.RequireLine(node.LineId);
            var choices = new List<DialogueChoiceView>(_active.Choices.Count);
            for (int i = 0; i < _active.Choices.Count; i++)
            {
                DialogueChoiceDefinition choice = _active.Choices[i];
                choices.Add(new DialogueChoiceView(
                    choice.Id,
                    choice.LineId,
                    ResolveLineText(choice.LineId),
                    choice.NextNode,
                    choice.ConditionGraphId,
                    choice.ActionGraphId));
            }

            view = new DialogueView(
                _active.Definition.Id,
                _active.Definition.DisplayName,
                node.Id,
                node.LineId,
                line.SpeakerId,
                line.TextToken,
                ResolveLineText(node.LineId),
                node.PresentationProfile,
                node.CameraId,
                node.AutoAdvanceSeconds,
                _active.ElapsedSeconds,
                choices);
            return true;
        }

        public DialogueRuntimeSnapshot CaptureSnapshot()
        {
            var bindings = new List<DialogueBindingSnapshot>(_bindings.Count);
            foreach (KeyValuePair<string, Entity> pair in _bindings)
            {
                bindings.Add(new DialogueBindingSnapshot(pair.Key, pair.Value));
            }

            DialogueSessionSnapshot? dialogue = null;
            if (_active?.CurrentNode != null)
            {
                dialogue = new DialogueSessionSnapshot(_active.Definition.Id, _active.CurrentNode.Id, _active.ElapsedSeconds);
            }

            return new DialogueRuntimeSnapshot(bindings, dialogue);
        }

        public void RestoreSnapshot(DialogueRuntimeSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            _bindings.Clear();
            for (int i = 0; i < snapshot.Bindings.Count; i++)
            {
                DialogueBindingSnapshot binding = snapshot.Bindings[i];
                _bindings[binding.Alias] = binding.Entity;
            }

            _active = null;
            if (snapshot.ActiveDialogue == null)
            {
                return;
            }

            DialogueDefinition definition = _dialogues.Require(snapshot.ActiveDialogue.DialogueId);
            DialogueNodeDefinition node = definition.Nodes.Find(n =>
                string.Equals(n.Id, snapshot.ActiveDialogue.NodeId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Dialogue '{snapshot.ActiveDialogue.DialogueId}' node '{snapshot.ActiveDialogue.NodeId}' is not registered.");
            _active = new ActiveDialogueSession(definition)
            {
                CurrentNode = node,
                ElapsedSeconds = snapshot.ActiveDialogue.ElapsedSeconds,
                Choices = BuildAvailableChoices(node)
            };
        }

        private void EnterNode(string nodeId)
        {
            if (_active == null)
            {
                return;
            }

            DialogueNodeDefinition node = _active.Definition.Nodes.Find(n =>
                string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Dialogue '{_active.Definition.Id}' node '{nodeId}' is not registered.");

            _active.CurrentNode = node;
            _active.ElapsedSeconds = 0f;
            _active.Choices = BuildAvailableChoices(node);

            Entity subject = ResolveSubject();
            _graphs.ExecuteAction(node.OnEnterActionGraphId, subject);
            if (!string.IsNullOrWhiteSpace(node.CameraId))
            {
                ActivateCamera(node.CameraId);
            }

            StoryLineDefinition line = _story.RequireLine(node.LineId);
            FireEvent(DialogueEventKeys.NodeEntered, ctx =>
            {
                ctx.Set(DialogueServiceKeys.DialogueId, _active.Definition.Id);
                ctx.Set(DialogueServiceKeys.DialogueNodeId, node.Id);
                ctx.Set(DialogueServiceKeys.LineId, node.LineId);
                ctx.Set(DialogueServiceKeys.SpeakerId, line.SpeakerId);
                ctx.Set(DialogueServiceKeys.BodyText, ResolveLineText(node.LineId));
                ctx.Set(DialogueServiceKeys.PresentationProfile, node.PresentationProfile);
            });
        }

        private List<DialogueChoiceDefinition> BuildAvailableChoices(DialogueNodeDefinition node)
        {
            var available = new List<DialogueChoiceDefinition>();
            Entity subject = ResolveSubject();
            for (int i = 0; i < node.Choices.Count; i++)
            {
                DialogueChoiceDefinition choice = node.Choices[i];
                if (_graphs.EvaluateCondition(choice.ConditionGraphId, subject))
                {
                    available.Add(choice);
                }
            }

            return available;
        }

        private void ConsumeInput()
        {
            var input = _engine.GetService(CoreServiceKeys.AuthoritativeInput);
            if (input == null || _active == null)
            {
                return;
            }

            if (_active.Choices.Count > 0)
            {
                if (input.PressedThisFrame(DialogueInputActionIds.Choice1)) ChooseOption(0);
                if (input.PressedThisFrame(DialogueInputActionIds.Choice2)) ChooseOption(1);
                if (input.PressedThisFrame(DialogueInputActionIds.Choice3)) ChooseOption(2);
                return;
            }

            if (input.PressedThisFrame(DialogueInputActionIds.Advance))
            {
                AdvanceDialogue();
            }
        }

        private void TickAutoAdvance(float dt)
        {
            if (_active?.CurrentNode == null || _active.Choices.Count > 0)
            {
                return;
            }

            if (_active.CurrentNode.AutoAdvanceSeconds <= 0f)
            {
                return;
            }

            _active.ElapsedSeconds += dt;
            if (_active.ElapsedSeconds >= _active.CurrentNode.AutoAdvanceSeconds)
            {
                AdvanceDialogue();
            }
        }

        private void HandleTaskStateChanged(TaskStateChangedInfo change)
        {
            if (change.State != TaskInstanceState.Active)
            {
                return;
            }

            if (!_tasks.TryGetDefinition(change.TaskId, out TaskDefinition definition))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(definition.OnEnterDialogueId))
            {
                StartDialogue(definition.OnEnterDialogueId);
            }
        }

        private Entity ResolveSubject()
        {
            if (TryResolveEntity("player", out Entity player))
            {
                return player;
            }

            foreach (KeyValuePair<string, Entity> pair in _bindings)
            {
                if (pair.Value != Entity.Null && _engine.World.IsAlive(pair.Value))
                {
                    return pair.Value;
                }
            }

            return Entity.Null;
        }

        private string ResolveLineText(string lineId)
        {
            StoryLineDefinition line = _story.RequireLine(lineId);
            if (_textCatalog == null)
            {
                return line.TextToken;
            }

            int tokenId = _textCatalog.GetTokenId(line.TextToken);
            if (tokenId <= 0)
            {
                throw new InvalidOperationException(
                    $"Story line '{lineId}' textToken '{line.TextToken}' is not registered in PresentationTextCatalog.");
            }

            var packet = PresentationTextPacket.FromToken(tokenId);
            for (int i = 0; i < line.Args.Count; i++)
            {
                packet.SetArg(i, line.Args[i]);
            }

            if (!PresentationTextFormatter.TryFormat(_textCatalog, _textCatalog.DefaultLocaleId, in packet, out string text) ||
                string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(
                    $"Story line '{lineId}' textToken '{line.TextToken}' has no locale template for default locale.");
            }

            return text;
        }

        private void ActivateCamera(string cameraId)
        {
            _engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest { Id = cameraId });
        }

        private void FireEvent(EventKey eventKey, Action<ScriptContext> populate)
        {
            ScriptContext context = _engine.CreateContext();
            populate(context);
            string mapId = _engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(mapId))
            {
                _engine.TriggerManager.FireMapEvent(new Ludots.Core.Map.MapId(mapId), eventKey, context);
                return;
            }

            _engine.TriggerManager.FireEvent(eventKey, context);
        }

        private sealed class ActiveDialogueSession
        {
            public ActiveDialogueSession(DialogueDefinition definition)
            {
                Definition = definition;
                Choices = new List<DialogueChoiceDefinition>();
            }

            public DialogueDefinition Definition { get; }
            public DialogueNodeDefinition? CurrentNode { get; set; }
            public float ElapsedSeconds { get; set; }
            public List<DialogueChoiceDefinition> Choices { get; set; }
        }
    }
}
