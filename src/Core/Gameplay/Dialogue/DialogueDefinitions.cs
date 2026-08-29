using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Dialogue
{
    public sealed class DialogueChoiceDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string LineId { get; set; } = string.Empty;
        public string ConditionGraphId { get; set; } = string.Empty;
        public string ActionGraphId { get; set; } = string.Empty;
        public string NextNode { get; set; } = string.Empty;
    }

    public sealed class DialogueNodeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string LineId { get; set; } = string.Empty;
        public string PresentationProfile { get; set; } = string.Empty;
        public string CameraId { get; set; } = string.Empty;
        public string NextNode { get; set; } = string.Empty;
        public float AutoAdvanceSeconds { get; set; }
        public string OnEnterActionGraphId { get; set; } = string.Empty;
        public List<DialogueChoiceDefinition> Choices { get; set; } = new();
    }

    public sealed class DialogueDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public string DisplayToken { get; set; } = string.Empty;
        public string EntryNode { get; set; } = string.Empty;
        public List<DialogueNodeDefinition> Nodes { get; set; } = new();
    }

    public sealed class DialogueDefinitionRegistry
    {
        private readonly Dictionary<string, DialogueDefinition> _dialogues = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<DialogueDefinition> Dialogues => _dialogues.Values;

        public void Clear() => _dialogues.Clear();

        public void Register(DialogueDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Dialogue id is required.");
            }

            if (string.IsNullOrWhiteSpace(definition.EntryNode))
            {
                throw new InvalidOperationException($"Dialogue '{definition.Id}' requires entryNode.");
            }

            if (definition.Nodes == null || definition.Nodes.Count == 0)
            {
                throw new InvalidOperationException($"Dialogue '{definition.Id}' requires at least one node.");
            }

            for (int i = 0; i < definition.Nodes.Count; i++)
            {
                DialogueNodeDefinition node = definition.Nodes[i];
                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    throw new InvalidOperationException($"Dialogue '{definition.Id}' has a node without id.");
                }

                if (string.IsNullOrWhiteSpace(node.LineId))
                {
                    throw new InvalidOperationException($"Dialogue '{definition.Id}' node '{node.Id}' requires lineId.");
                }

                if (string.IsNullOrWhiteSpace(node.PresentationProfile))
                {
                    throw new InvalidOperationException($"Dialogue '{definition.Id}' node '{node.Id}' requires presentationProfile.");
                }

                RejectLegacyNarrativeFields(definition.Id, node);
            }

            if (definition.Nodes.Find(n => string.Equals(n.Id, definition.EntryNode, StringComparison.OrdinalIgnoreCase)) == null)
            {
                throw new InvalidOperationException($"Dialogue '{definition.Id}' entryNode '{definition.EntryNode}' is missing.");
            }

            _dialogues[definition.Id] = definition;
        }

        public bool TryGet(string dialogueId, out DialogueDefinition definition)
            => _dialogues.TryGetValue(dialogueId ?? string.Empty, out definition!);

        public DialogueDefinition Require(string dialogueId)
        {
            if (!TryGet(dialogueId, out DialogueDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Dialogue '{dialogueId}' is not registered. Author it under Dialogue/dialogues.json.");
            }

            return definition;
        }

        private static void RejectLegacyNarrativeFields(string dialogueId, DialogueNodeDefinition node)
        {
            // Structural contract: choices must not carry inline text / conditions / actions enums.
            // Those belong to lineId + conditionGraphId + actionGraphId.
            for (int i = 0; i < node.Choices.Count; i++)
            {
                DialogueChoiceDefinition choice = node.Choices[i];
                if (string.IsNullOrWhiteSpace(choice.Id))
                {
                    throw new InvalidOperationException($"Dialogue '{dialogueId}' node '{node.Id}' has a choice without id.");
                }

                if (string.IsNullOrWhiteSpace(choice.LineId))
                {
                    throw new InvalidOperationException($"Dialogue '{dialogueId}' choice '{choice.Id}' requires lineId.");
                }
            }
        }
    }
}
