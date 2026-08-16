using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using DesertStrikeShowcaseMod.Runtime;

namespace DesertStrikeShowcaseMod.Triggers
{
    public sealed class DesertStrikeSetupOnMapLoadedTrigger : Trigger
    {
        private const string PlayerBaseName = "Command Center P1";
        private const string AiBaseName = "Command Center P2";

        private readonly IModContext _ctx;

        public DesertStrikeSetupOnMapLoadedTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            var mapTags = context.Get(CoreServiceKeys.MapTags) ?? new List<string>();
            if (!HasTag(mapTags, "desert_strike"))
            {
                return Task.CompletedTask;
            }

            if (!engine.GlobalContext.TryGetValue(InstallDesertStrikeOnGameStartTrigger.StateKey, out var stateObj) ||
                stateObj is not DesertStrikeState state)
            {
                throw new InvalidOperationException("[DesertStrikeShowcaseMod] State missing when desert_strike map loaded.");
            }

            if (!engine.GlobalContext.TryGetValue(InstallDesertStrikeOnGameStartTrigger.ConfigKey, out var configObj) ||
                configObj is not DesertStrikeConfig config)
            {
                throw new InvalidOperationException("[DesertStrikeShowcaseMod] Config missing when desert_strike map loaded.");
            }

            if (engine.World.IsAlive(state.PlayerBase))
            {
                return Task.CompletedTask;
            }

            var world = engine.World;
            state.PlayerBase = RequireNamedEntity(world, PlayerBaseName);
            state.AiBase = RequireNamedEntity(world, AiBaseName);
            state.PlayerTeam = RequireTeam(world, state.PlayerBase);
            state.AiTeam = RequireTeam(world, state.AiBase);

            BindSpawnMarkers(world, state, "Spawn P1 L", state.PlayerSpawnMarkers);
            BindSpawnMarkers(world, state, "Spawn P2 L", state.AiSpawnMarkers);

            var tagOps = engine.GetService(CoreServiceKeys.TagOps);
            int mineralsId = EnsureAttributeId("Minerals");
            SetMinerals(world, tagOps, mineralsId, state.PlayerBase, config.StartingMinerals);
            SetMinerals(world, tagOps, mineralsId, state.AiBase, config.StartingMinerals);

            SeedStarterQueue(config.StarterWave.Player, state.PlayerQueue);
            SeedStarterQueue(config.StarterWave.Ai, state.AiQueue);

            int step = engine.GetService(CoreServiceKeys.Clock).Now(ClockDomainId.FixedFrame);
            state.NextWaveStep = step + config.WaveIntervalTicks;
            state.NextIncomeStep = step + config.IncomeIntervalTicks;
            state.WaveNumber = 0;
            state.GameOver = false;
            state.WinnerPlayerId = 0;
            state.DestroyedBaseTeam = 0;
            _ctx.Log($"[DesertStrikeShowcaseMod] Bound desert_strike map: player team {state.PlayerTeam}, ai team {state.AiTeam}.");
            return Task.CompletedTask;
        }

        private static void SeedStarterQueue(List<DesertStrikeConfig.StarterUnitEntry> entries, List<DesertStrikePurchase> queue)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                queue.Add(new DesertStrikePurchase(entries[i].Unit, entries[i].Lane));
            }
        }

        private static void BindSpawnMarkers(World world, DesertStrikeState state, string prefix, Dictionary<int, Entity> markers)
        {
            for (int lane = 0; lane < 3; lane++)
            {
                Entity marker = RequireNamedEntity(world, $"{prefix}{lane}");
                markers[lane] = marker;
            }
        }

        private static Entity RequireNamedEntity(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (result == Entity.Null && string.Equals(entityName.Value, name, StringComparison.Ordinal))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"[DesertStrikeShowcaseMod] Missing required map entity '{name}'.");
            }

            return result;
        }

        private static int RequireTeam(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<Team>(entity))
            {
                throw new InvalidOperationException("[DesertStrikeShowcaseMod] Base entity requires a Team component.");
            }

            return world.Get<Team>(entity).Id;
        }

        private static void SetMinerals(World world, TagOps tagOps, int mineralsId, Entity entity, int value)
        {
            if (world.Has<AttributeBuffer>(entity))
            {
                AttributeMutationOps.SetCurrent(world, entity, mineralsId, value, tagOps);
            }
        }

        private static int EnsureAttributeId(string attributeName)
        {
            int id = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register(attributeName);
        }

        private static bool HasTag(List<string> tags, string tag)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
