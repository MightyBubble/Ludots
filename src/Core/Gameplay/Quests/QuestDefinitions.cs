using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.Quests
{
    public sealed class QuestAttributeDefinition
    {
        public string AttributeId { get; set; } = string.Empty;
        public float BaseValue { get; set; }
        public float? CurrentValue { get; set; }

        [JsonIgnore]
        public int ResolvedAttributeId { get; set; } = AttributeRegistry.InvalidId;
    }

    public sealed class QuestStageDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ObjectiveText { get; set; } = string.Empty;
        public string ObjectiveHint { get; set; } = string.Empty;
        public string DialogueOnEnterId { get; set; } = string.Empty;
        public string CinematicOnEnterId { get; set; } = string.Empty;
        public List<string> RequiredSignals { get; set; } = new();
    }

    public sealed class QuestDefinition : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public List<QuestAttributeDefinition> Attributes { get; set; } = new();
        public List<QuestStageDefinition> Stages { get; set; } = new();

        [JsonIgnore]
        public List<int> ResolvedTagIds { get; } = new();
    }

    public sealed class QuestDefinitionRegistry
    {
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<QuestDefinition?> _definitions = new() { null };

        public IEnumerable<QuestDefinition> Definitions
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

        public int Register(string id, QuestDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Quest id is required.", nameof(id));
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            definition.Id = id;
            if (string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                definition.DisplayName = id;
            }

            ResolveGasSchemaIds(definition);

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

        public int GetId(string id)
        {
            return _nameToId.TryGetValue(id, out int value) ? value : 0;
        }

        public string GetName(int id)
        {
            return TryGet(id, out QuestDefinition definition) ? definition.Id : string.Empty;
        }

        public bool TryGet(int id, out QuestDefinition definition)
        {
            if ((uint)id < (uint)_definitions.Count && _definitions[id] != null)
            {
                definition = _definitions[id]!;
                return true;
            }

            definition = null!;
            return false;
        }

        public bool TryGet(string id, out QuestDefinition definition)
        {
            if (_nameToId.TryGetValue(id, out int definitionId))
            {
                return TryGet(definitionId, out definition);
            }

            definition = null!;
            return false;
        }

        public AttributeBuffer CreateAttributeBuffer(QuestDefinition definition)
        {
            var buffer = default(AttributeBuffer);
            for (int i = 0; i < definition.Attributes.Count; i++)
            {
                QuestAttributeDefinition attribute = definition.Attributes[i];
                if (attribute.ResolvedAttributeId == AttributeRegistry.InvalidId)
                {
                    continue;
                }

                buffer.SetBase(attribute.ResolvedAttributeId, attribute.BaseValue);
                if (attribute.CurrentValue.HasValue)
                {
                    buffer.SetCurrent(attribute.ResolvedAttributeId, attribute.CurrentValue.Value);
                }
            }

            return buffer;
        }

        public GameplayTagContainer CreateTagContainer(QuestDefinition definition)
        {
            var tags = default(GameplayTagContainer);
            for (int i = 0; i < definition.ResolvedTagIds.Count; i++)
            {
                tags.AddTag(definition.ResolvedTagIds[i]);
            }

            return tags;
        }

        private static void ResolveGasSchemaIds(QuestDefinition definition)
        {
            definition.ResolvedTagIds.Clear();
            for (int i = 0; i < definition.Tags.Count; i++)
            {
                string tag = definition.Tags[i];
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                int tagId = TagRegistry.GetId(tag);
                if (tagId == TagRegistry.InvalidId)
                {
                    tagId = TagRegistry.Register(tag);
                }

                definition.ResolvedTagIds.Add(tagId);
            }

            for (int i = 0; i < definition.Attributes.Count; i++)
            {
                QuestAttributeDefinition attribute = definition.Attributes[i];
                if (string.IsNullOrWhiteSpace(attribute.AttributeId))
                {
                    attribute.ResolvedAttributeId = AttributeRegistry.InvalidId;
                    continue;
                }

                int attributeId = AttributeRegistry.GetId(attribute.AttributeId);
                if (attributeId == AttributeRegistry.InvalidId)
                {
                    attributeId = AttributeRegistry.Register(attribute.AttributeId);
                }

                attribute.ResolvedAttributeId = attributeId;
            }
        }
    }

    public sealed record QuestView(
        string QuestId,
        string DisplayName,
        string Summary,
        QuestState State,
        string StageId,
        string StageTitle,
        string ObjectiveText,
        string ObjectiveHint,
        Entity QuestEntity);
}
