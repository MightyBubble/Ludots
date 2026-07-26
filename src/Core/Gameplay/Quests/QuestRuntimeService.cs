using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.Quests
{
    public enum QuestEventKind : byte
    {
        Started = 1,
        StageChanged = 2,
        Completed = 3,
        Failed = 4,
    }

    public readonly record struct QuestEvent(
        QuestEventKind Kind,
        string QuestId,
        string StageId,
        Entity QuestEntity);

    public sealed record QuestRuntimeSnapshot(
        IReadOnlyDictionary<string, int> Signals);

    public sealed class QuestRuntimeService
    {
        private static readonly QueryDescription QuestQuery = new QueryDescription()
            .WithAll<QuestInstanceCm>();

        private readonly World _world;
        private readonly QuestDefinitionRegistry _definitions;
        private readonly Dictionary<QuestKey, Entity> _questIndex = new();
        private readonly Dictionary<string, int> _signals = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Entity> _rebuildScratch = new(64);

        public QuestRuntimeService(World world, QuestDefinitionRegistry definitions)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            RebuildIndexFromWorld();
        }

        public event Action<QuestEvent>? QuestEventPublished;

        public IReadOnlyDictionary<string, int> Signals => _signals;

        public bool TryGetDefinition(string questId, out QuestDefinition definition)
        {
            return _definitions.TryGet(questId, out definition);
        }

        public bool TryGetStage(string questId, string stageId, out QuestStageDefinition stage)
        {
            stage = null!;
            if (!_definitions.TryGet(questId, out QuestDefinition definition))
            {
                return false;
            }

            for (int i = 0; i < definition.Stages.Count; i++)
            {
                if (string.Equals(definition.Stages[i].Id, stageId, StringComparison.OrdinalIgnoreCase))
                {
                    stage = definition.Stages[i];
                    return true;
                }
            }

            return false;
        }

        public void ResetState()
        {
            _rebuildScratch.Clear();
            _world.Query(in QuestQuery, (Entity entity, ref QuestInstanceCm _) =>
            {
                _rebuildScratch.Add(entity);
            });

            for (int i = 0; i < _rebuildScratch.Count; i++)
            {
                if (_world.IsAlive(_rebuildScratch[i]))
                {
                    _world.Destroy(_rebuildScratch[i]);
                }
            }

            _questIndex.Clear();
            _signals.Clear();
        }

        public QuestRuntimeSnapshot CaptureSnapshot()
        {
            return new QuestRuntimeSnapshot(new Dictionary<string, int>(_signals, StringComparer.OrdinalIgnoreCase));
        }

        public void RestoreSnapshot(QuestRuntimeSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _signals.Clear();
            foreach (KeyValuePair<string, int> pair in snapshot.Signals)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value <= 0)
                {
                    throw new InvalidOperationException(
                        $"Quest signal snapshot contains invalid entry '{pair.Key}' with count {pair.Value}.");
                }

                _signals[pair.Key] = pair.Value;
            }

            RebuildIndexFromWorld();
        }

        public void RebuildIndexFromWorld()
        {
            _questIndex.Clear();
            _world.Query(in QuestQuery, (Entity entity, ref QuestInstanceCm quest) =>
            {
                ValidateQuestEntityForIndex(entity, in quest);
                QuestKey key = new(quest.ScopeHost, quest.DefinitionId);
                if (_questIndex.TryGetValue(key, out Entity existing))
                {
                    string questId = _definitions.GetName(quest.DefinitionId);
                    throw new InvalidOperationException(
                        $"Duplicate quest entity projection for '{questId}' scope {key.ScopeHost.Id}:{key.ScopeHost.WorldId}:{key.ScopeHost.Version}: " +
                        $"{existing.Id}:{existing.WorldId}:{existing.Version} and {entity.Id}:{entity.WorldId}:{entity.Version}.");
                }

                _questIndex[key] = entity;
            });
        }

        public bool TryGetQuestState(string questId, out QuestState state, out string stageId)
        {
            if (TryGetQuestEntity(questId, Entity.Null, out Entity entity))
            {
                ref QuestInstanceCm quest = ref _world.Get<QuestInstanceCm>(entity);
                state = quest.State;
                stageId = ResolveStageId(in quest);
                return true;
            }

            state = QuestState.Inactive;
            stageId = string.Empty;
            return false;
        }

        public IReadOnlyList<QuestView> GetQuestViews()
        {
            var views = new List<QuestView>(_questIndex.Count);
            foreach (KeyValuePair<QuestKey, Entity> pair in _questIndex)
            {
                Entity entity = pair.Value;
                if (!_world.IsAlive(entity) || !_world.Has<QuestInstanceCm>(entity))
                {
                    continue;
                }

                ref QuestInstanceCm quest = ref _world.Get<QuestInstanceCm>(entity);
                if (quest.State == QuestState.Inactive ||
                    !_definitions.TryGet(quest.DefinitionId, out QuestDefinition definition))
                {
                    continue;
                }

                QuestStageDefinition? stage = TryResolveStage(definition, quest.StageIndex);
                views.Add(new QuestView(
                    definition.Id,
                    definition.DisplayName,
                    definition.Summary,
                    quest.State,
                    stage?.Id ?? string.Empty,
                    stage?.Title ?? string.Empty,
                    stage?.ObjectiveText ?? string.Empty,
                    stage?.ObjectiveHint ?? string.Empty,
                    entity,
                    quest.ScopeHost,
                    quest.Revision));
            }

            views.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.QuestId, b.QuestId));
            return views;
        }

        public bool TryResolveQuestEntity(string questId, out Entity entity)
        {
            return TryGetQuestEntity(questId, Entity.Null, out entity);
        }

        public bool TryResolveQuestEntity(string questId, Entity scopeHost, out Entity entity)
        {
            return TryGetQuestEntity(questId, scopeHost, out entity);
        }

        public Entity StartQuest(string questId, Entity scopeHost = default)
        {
            scopeHost = NormalizeScopeHost(scopeHost);
            int definitionId = RequireQuestDefinition(questId, out QuestDefinition definition);
            ValidateScopeHost(scopeHost);

            QuestKey key = new(scopeHost, definitionId);
            if (_questIndex.TryGetValue(key, out Entity existing) &&
                _world.IsAlive(existing) &&
                _world.Has<QuestInstanceCm>(existing))
            {
                ref QuestInstanceCm existingQuest = ref _world.Get<QuestInstanceCm>(existing);
                if (existingQuest.State != QuestState.Inactive)
                {
                    return existing;
                }

                existingQuest.State = QuestState.Active;
                existingQuest.StageIndex = -1;
                existingQuest.Revision++;
                EnterStage(existing, ref existingQuest, definition, 0, emitStarted: true);
                return existing;
            }

            Entity questEntity = _world.Create(
                new QuestInstanceCm
                {
                    DefinitionId = definitionId,
                    State = QuestState.Active,
                    StageIndex = -1,
                    ScopeHost = scopeHost,
                    Revision = 1
                },
                _definitions.CreateAttributeBuffer(definition),
                _definitions.CreateTagContainer(definition),
                new ActiveEffectContainer());
            TagStateInstaller.EnsureInstalled(_world, questEntity);

            _questIndex[key] = questEntity;
            ref QuestInstanceCm quest = ref _world.Get<QuestInstanceCm>(questEntity);
            EnterStage(questEntity, ref quest, definition, 0, emitStarted: true);
            return questEntity;
        }

        public bool AdvanceQuestStage(string questId, string targetStageId = "", Entity scopeHost = default)
        {
            RequireQuestDefinition(questId, out _);
            if (!TryGetActiveQuest(questId, scopeHost, out Entity questEntity, out QuestDefinition definition, out _))
            {
                throw new InvalidOperationException($"Quest '{questId}' is not active.");
            }

            ref QuestInstanceCm questRef = ref _world.Get<QuestInstanceCm>(questEntity);
            int nextIndex;
            if (!string.IsNullOrWhiteSpace(targetStageId))
            {
                nextIndex = FindStageIndex(definition, targetStageId);
                if (nextIndex < 0)
                {
                    throw new InvalidOperationException($"Quest '{definition.Id}' stage '{targetStageId}' is not registered.");
                }
            }
            else
            {
                nextIndex = questRef.StageIndex + 1;
            }

            if (nextIndex >= definition.Stages.Count)
            {
                return CompleteQuest(questId, scopeHost);
            }

            EnterStage(questEntity, ref questRef, definition, nextIndex, emitStarted: false);
            return true;
        }

        public bool CompleteQuest(string questId, Entity scopeHost = default)
        {
            RequireQuestDefinition(questId, out _);
            if (!TryGetQuestRuntime(questId, scopeHost, out Entity questEntity, out QuestDefinition definition))
            {
                throw new InvalidOperationException($"Quest '{questId}' is not started.");
            }

            ref QuestInstanceCm questRef = ref _world.Get<QuestInstanceCm>(questEntity);
            questRef.State = QuestState.Completed;
            questRef.Revision++;
            Publish(QuestEventKind.Completed, definition.Id, ResolveStageId(in questRef), questEntity);
            return true;
        }

        public bool FailQuest(string questId, Entity scopeHost = default)
        {
            RequireQuestDefinition(questId, out _);
            if (!TryGetQuestRuntime(questId, scopeHost, out Entity questEntity, out QuestDefinition definition))
            {
                throw new InvalidOperationException($"Quest '{questId}' is not started.");
            }

            ref QuestInstanceCm questRef = ref _world.Get<QuestInstanceCm>(questEntity);
            questRef.State = QuestState.Failed;
            questRef.Revision++;
            Publish(QuestEventKind.Failed, definition.Id, ResolveStageId(in questRef), questEntity);
            return true;
        }

        public void EmitSignal(string signalId)
        {
            if (string.IsNullOrWhiteSpace(signalId))
            {
                throw new ArgumentException("Quest signal id is required.", nameof(signalId));
            }

            _signals.TryGetValue(signalId, out int count);
            _signals[signalId] = count + 1;
            EvaluateSignalProgress();
        }

        private void EvaluateSignalProgress()
        {
            foreach (KeyValuePair<QuestKey, Entity> pair in _questIndex)
            {
                Entity questEntity = pair.Value;
                if (!_world.IsAlive(questEntity) || !_world.Has<QuestInstanceCm>(questEntity))
                {
                    continue;
                }

                ref QuestInstanceCm quest = ref _world.Get<QuestInstanceCm>(questEntity);
                if (quest.State != QuestState.Active ||
                    !_definitions.TryGet(quest.DefinitionId, out QuestDefinition definition))
                {
                    continue;
                }

                QuestStageDefinition? stage = TryResolveStage(definition, quest.StageIndex);
                if (stage == null || stage.RequiredSignals.Count == 0)
                {
                    continue;
                }

                bool allSignalsSatisfied = true;
                for (int i = 0; i < stage.RequiredSignals.Count; i++)
                {
                    if (!_signals.TryGetValue(stage.RequiredSignals[i], out int count) || count <= 0)
                    {
                        allSignalsSatisfied = false;
                        break;
                    }
                }

                if (!allSignalsSatisfied)
                {
                    continue;
                }

                AdvanceQuestStage(definition.Id, scopeHost: quest.ScopeHost);
                return;
            }
        }

        private bool TryGetQuestEntity(string questId, Entity scopeHost, out Entity entity)
        {
            scopeHost = NormalizeScopeHost(scopeHost);
            ValidateScopeHost(scopeHost);
            int definitionId = _definitions.GetId(questId);
            if (definitionId > 0 &&
                _questIndex.TryGetValue(new QuestKey(scopeHost, definitionId), out entity) &&
                _world.IsAlive(entity) &&
                _world.Has<QuestInstanceCm>(entity))
            {
                return true;
            }

            entity = Entity.Null;
            return false;
        }

        private bool TryGetQuestRuntime(
            string questId,
            Entity scopeHost,
            out Entity questEntity,
            out QuestDefinition definition)
        {
            questEntity = Entity.Null;
            definition = null!;

            if (!TryGetQuestEntity(questId, scopeHost, out questEntity))
            {
                return false;
            }

            QuestInstanceCm quest = _world.Get<QuestInstanceCm>(questEntity);
            return _definitions.TryGet(quest.DefinitionId, out definition);
        }

        private bool TryGetActiveQuest(
            string questId,
            Entity scopeHost,
            out Entity questEntity,
            out QuestDefinition definition,
            out QuestInstanceCm quest)
        {
            questEntity = Entity.Null;
            definition = null!;
            quest = default;

            if (!TryGetQuestRuntime(questId, scopeHost, out questEntity, out definition))
            {
                return false;
            }

            quest = _world.Get<QuestInstanceCm>(questEntity);
            return quest.State == QuestState.Active;
        }

        private void EnterStage(
            Entity questEntity,
            ref QuestInstanceCm quest,
            QuestDefinition definition,
            int stageIndex,
            bool emitStarted)
        {
            if (stageIndex < 0 || stageIndex >= definition.Stages.Count)
            {
                throw new InvalidOperationException($"Quest '{definition.Id}' stage index {stageIndex} is invalid.");
            }

            quest.State = QuestState.Active;
            quest.StageIndex = stageIndex;
            quest.Revision++;

            if (emitStarted)
            {
                Publish(QuestEventKind.Started, definition.Id, definition.Stages[stageIndex].Id, questEntity);
            }

            Publish(QuestEventKind.StageChanged, definition.Id, definition.Stages[stageIndex].Id, questEntity);
        }

        private void Publish(QuestEventKind kind, string questId, string stageId, Entity questEntity)
        {
            QuestEventPublished?.Invoke(new QuestEvent(kind, questId, stageId, questEntity));
        }

        private string ResolveStageId(in QuestInstanceCm quest)
        {
            if (!_definitions.TryGet(quest.DefinitionId, out QuestDefinition definition))
            {
                return string.Empty;
            }

            return TryResolveStage(definition, quest.StageIndex)?.Id ?? string.Empty;
        }

        private static QuestStageDefinition? TryResolveStage(QuestDefinition definition, int stageIndex)
        {
            return stageIndex >= 0 && stageIndex < definition.Stages.Count
                ? definition.Stages[stageIndex]
                : null;
        }

        private static int FindStageIndex(QuestDefinition definition, string stageId)
        {
            for (int i = 0; i < definition.Stages.Count; i++)
            {
                if (string.Equals(definition.Stages[i].Id, stageId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private int RequireQuestDefinition(string questId, out QuestDefinition definition)
        {
            int definitionId = _definitions.GetId(questId);
            if (definitionId <= 0 || !_definitions.TryGet(definitionId, out definition))
            {
                throw new InvalidOperationException($"Quest '{questId}' is not registered.");
            }

            if (definition.Stages.Count == 0)
            {
                throw new InvalidOperationException($"Quest '{definition.Id}' must define at least one stage.");
            }

            return definitionId;
        }

        private void ValidateQuestEntityForIndex(Entity entity, in QuestInstanceCm quest)
        {
            if (quest.DefinitionId <= 0 || !_definitions.TryGet(quest.DefinitionId, out QuestDefinition definition))
            {
                throw new InvalidOperationException(
                    $"Quest entity {entity.Id}:{entity.WorldId}:{entity.Version} references missing quest definition id {quest.DefinitionId}.");
            }

            if (definition.Stages.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Quest entity {entity.Id}:{entity.WorldId}:{entity.Version} references quest '{definition.Id}' without stages.");
            }

            Entity scopeHost = NormalizeScopeHost(quest.ScopeHost);
            if (scopeHost != Entity.Null && !_world.IsAlive(scopeHost))
            {
                throw new InvalidOperationException(
                    $"Quest entity {entity.Id}:{entity.WorldId}:{entity.Version} references a missing scope host entity.");
            }

            if (quest.StageIndex < -1 || quest.StageIndex >= definition.Stages.Count)
            {
                throw new InvalidOperationException(
                    $"Quest entity {entity.Id}:{entity.WorldId}:{entity.Version} has invalid stage index {quest.StageIndex} for quest '{definition.Id}'.");
            }
        }

        private void ValidateScopeHost(Entity scopeHost)
        {
            if (scopeHost != Entity.Null && !_world.IsAlive(scopeHost))
            {
                throw new InvalidOperationException("Quest scope host must be a live entity.");
            }
        }

        private static Entity NormalizeScopeHost(Entity scopeHost)
        {
            return scopeHost.Equals(default(Entity)) || scopeHost.Equals(Entity.Null)
                ? Entity.Null
                : scopeHost;
        }

        private readonly struct QuestKey : IEquatable<QuestKey>
        {
            public QuestKey(Entity scopeHost, int definitionId)
            {
                ScopeHost = NormalizeScopeHost(scopeHost);
                DefinitionId = definitionId;
            }

            public Entity ScopeHost { get; }
            public int DefinitionId { get; }

            public bool Equals(QuestKey other)
            {
                return ScopeHost.Equals(other.ScopeHost) && DefinitionId == other.DefinitionId;
            }

            public override bool Equals(object? obj)
            {
                return obj is QuestKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(ScopeHost, DefinitionId);
            }
        }
    }
}
