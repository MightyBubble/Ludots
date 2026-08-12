using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    /// <summary>
    /// #623: AI draft must be structured patch data, never free-text execution.
    /// </summary>
    public sealed class LiveAiSkillDraft
    {
        public LiveAiSkillDraft(
            string draftId,
            string prompt,
            string displayName,
            IReadOnlyList<LiveDebugPatchOperation> operations,
            string? bindAbilityKey = null)
        {
            DraftId = draftId ?? throw new ArgumentNullException(nameof(draftId));
            Prompt = prompt ?? string.Empty;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Operations = operations ?? throw new ArgumentNullException(nameof(operations));
            BindAbilityKey = bindAbilityKey;
        }

        public string DraftId { get; }
        public string Prompt { get; }
        public string DisplayName { get; }
        public IReadOnlyList<LiveDebugPatchOperation> Operations { get; }
        public string? BindAbilityKey { get; }
    }

    public interface IAiSkillDraftGenerator
    {
        LiveAiSkillDraft Generate(string prompt, in LiveEditProvenance provenance);
    }

    /// <summary>
    /// Deterministic fake generator for tests and Showcase. Replaceable adapter — no cloud LLM.
    /// </summary>
    public sealed class DeterministicFakeAiSkillDraftGenerator : IAiSkillDraftGenerator
    {
        public const string FrostNovaAbilityKey = "ability.AiDraft.FrostNova";
        public const string FrostNovaEffectKey = "effect.AiDraft.FrostNova";
        public const string FrostNovaGraphKey = "Graph.AiDraft.FrostNovaConst";

        public LiveAiSkillDraft Generate(string prompt, in LiveEditProvenance provenance)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("AI draft prompt is required.", nameof(prompt));
            }

            if (prompt.Contains("REJECT", StringComparison.OrdinalIgnoreCase))
            {
                // Structured but intentionally invalid field → pipeline MapReload / fail.
                var bad = new List<LiveDebugPatchOperation>(1)
                {
                    LiveDebugPatchOperation.SkillEffectNumeric(
                        "effect.DoesNotExist",
                        "damage",
                        99d,
                        provenance)
                };
                return new LiveAiSkillDraft(
                    draftId: "draft.reject",
                    prompt,
                    displayName: "Rejected Draft",
                    bad);
            }

            string graphJson = $$"""
                {
                  "id": "{{FrostNovaGraphKey}}",
                  "kind": "Script",
                  "entry": "c",
                  "nodes": [
                    { "id": "c", "op": "ConstInt", "intValue": 12 },
                    { "id": "h", "op": "HaltReturnInt" }
                  ],
                  "controlEdges": [
                    { "from": "c", "fromPort": "next", "to": "h" }
                  ],
                  "valueEdges": [
                    { "from": "c", "fromPort": "value", "to": "h", "toPort": "value" }
                  ]
                }
                """;

            var ops = new List<LiveDebugPatchOperation>(2)
            {
                LiveDebugPatchOperation.GraphBodyReplace(FrostNovaGraphKey, graphJson, provenance),
                LiveDebugPatchOperation.SkillEffectNumeric(
                    FrostNovaEffectKey,
                    "duration.durationTicks",
                    30d,
                    provenance)
            };

            return new LiveAiSkillDraft(
                draftId: "draft.frost-nova",
                prompt,
                displayName: "小范围冰冻（AI 草稿）",
                ops,
                bindAbilityKey: FrostNovaAbilityKey);
        }
    }

    /// <summary>
    /// Temporary playtest bind for a validated draft. Does not persist Mod files (#624).
    /// </summary>
    public sealed class LiveAiDraftPlaytestBind
    {
        public LiveAiDraftPlaytestBind(string draftId, string abilityKey, int actorEntityId, DateTime boundUtc)
        {
            DraftId = draftId;
            AbilityKey = abilityKey;
            ActorEntityId = actorEntityId;
            BoundUtc = boundUtc;
        }

        public string DraftId { get; }
        public string AbilityKey { get; }
        public int ActorEntityId { get; }
        public DateTime BoundUtc { get; }
    }

    public sealed class LiveAiDraftBinder
    {
        private LiveAiDraftPlaytestBind? _active;

        public LiveAiDraftPlaytestBind? Active => _active;

        public LiveAiDraftPlaytestBind Bind(LiveAiSkillDraft draft, int actorEntityId, DateTime? utc = null)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (string.IsNullOrWhiteSpace(draft.BindAbilityKey))
            {
                throw new InvalidOperationException(
                    $"Draft '{draft.DraftId}' has no bindAbilityKey; cannot playtest-bind.");
            }

            if (actorEntityId == 0)
            {
                throw new InvalidOperationException("Playtest bind requires a non-zero actor entity id.");
            }

            _active = new LiveAiDraftPlaytestBind(
                draft.DraftId,
                draft.BindAbilityKey!,
                actorEntityId,
                utc ?? DateTime.UtcNow);
            return _active;
        }

        public void Clear() => _active = null;
    }
}
