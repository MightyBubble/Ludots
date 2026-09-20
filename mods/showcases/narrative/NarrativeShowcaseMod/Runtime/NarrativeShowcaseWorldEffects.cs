using System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Arch.Core;
using Ludots.Core.Components;

namespace NarrativeShowcaseMod.Runtime
{
    /// <summary>
    /// World side effects of the showcase beat flow (beast spawn / blessing rewards).
    /// UI composition stays in NarrativeShowcaseRuntime; world mutation lives here.
    /// </summary>
    internal sealed class NarrativeShowcaseWorldEffects
    {
        private readonly NarrativeShowcaseFrontendConfig _config;
        private readonly Action<GameEngine, string> _appendHistory;

        internal NarrativeShowcaseWorldEffects(NarrativeShowcaseFrontendConfig config, Action<GameEngine, string> appendHistory)
        {
            _config = config;
            _appendHistory = appendHistory;
        }

        internal void SpawnBeast(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.BeastSpawnedKey, out var spawned) && spawned is bool b && b)
            {
                return;
            }

            RuntimeEntitySpawnQueue queue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) as RuntimeEntitySpawnQueue
                ?? throw new InvalidOperationException("Narrative showcase beast spawn requires RuntimeEntitySpawnQueue.");

            queue.TryEnqueue(new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = NarrativeShowcaseIds.SpawnedBeastTemplateId,
                MapId = new Ludots.Core.Map.MapId(NarrativeShowcaseIds.MapId),
                HasWorldPosition = 1,
                WorldPositionCm = Fix64Vec2.FromInt(_config.Bootstrap.BeastSpawnXcm, _config.Bootstrap.BeastSpawnYcm),
                HasFacing = 1,
                FacingAngleRad = _config.Bootstrap.BeastSpawnFacingRad
            });
            engine.GlobalContext[NarrativeShowcaseIds.BeastSpawnedKey] = true;
            _appendHistory(engine, ResolveToken(engine, _config.Templates.BeastSpawned));
        }

        internal void ApplyReward(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(NarrativeShowcaseIds.RewardAppliedKey, out var rewardObj) && rewardObj is bool rewardApplied && rewardApplied)
            {
                return;
            }

            EffectRequestQueue queue = engine.GetService(CoreServiceKeys.EffectRequestQueue) as EffectRequestQueue
                ?? throw new InvalidOperationException("Narrative showcase reward requires EffectRequestQueue.");
            if (!TryFindPlayer(engine, out Entity player))
            {
                throw new InvalidOperationException(
                    $"Narrative showcase reward requires a player entity named '{NarrativeShowcaseIds.PlayerName}'.");
            }

            PublishBlessing(engine, queue, player, NarrativeShowcaseIds.BlessingHealEffectId);
            PublishBlessing(engine, queue, player, NarrativeShowcaseIds.BlessingSpeedEffectId);
            engine.GlobalContext[NarrativeShowcaseIds.RewardAppliedKey] = true;
            _appendHistory(engine, ResolveToken(engine, _config.Templates.RewardApplied));
        }

        private static void PublishBlessing(GameEngine engine, EffectRequestQueue queue, in Entity player, string effectId)
        {
            int templateId = EffectTemplateIdRegistry.GetId(effectId);
            if (templateId <= 0)
            {
                throw new InvalidOperationException(
                    $"Narrative showcase blessing effect '{effectId}' is not registered in the effect template registry.");
            }

            queue.Publish(new Ludots.Core.Gameplay.GAS.EffectRequest { Source = player, Target = player, TemplateId = templateId });
        }

        private static string ResolveToken(GameEngine engine, string token)
        {
            return Ludots.Core.Gameplay.Story.StoryTextResolution.FormatToken(
                engine.GetService(CoreServiceKeys.PresentationTextCatalog),
                engine.GetService(CoreServiceKeys.PresentationDisplayResolver),
                token);
        }

        private static bool TryFindPlayer(GameEngine engine, out Entity player)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (found == Entity.Null && string.Equals(entityName.Value, NarrativeShowcaseIds.PlayerName, StringComparison.OrdinalIgnoreCase))
                {
                    found = entity;
                }
            });

            player = found;
            return found != Entity.Null;
        }
    }
}
