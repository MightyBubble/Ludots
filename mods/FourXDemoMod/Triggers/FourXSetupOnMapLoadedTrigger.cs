using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Scripting;
using Ludots.Core.Modding;

namespace FourXDemoMod.Triggers
{
    public sealed class FourXSetupOnMapLoadedTrigger : Trigger
    {
        private readonly IModContext _ctx;

        public FourXSetupOnMapLoadedTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapTags = context.Get(CoreServiceKeys.MapTags) ?? new List<string>();
            if (!HasTag(mapTags, "fourx")) return Task.CompletedTask;

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.TagOps.Name, out var tagOpsObj) || tagOpsObj is not TagOps tagOps)
            {
                return Task.CompletedTask;
            }

            var world = engine.World;
            var entities = FindFourXEntities(world);
            if (!world.IsAlive(entities.Governor)) return Task.CompletedTask;

            EnsureTagComponents(world, entities.Governor);

            int canColonize = TagRegistry.Register("Status.CanColonize");
            ref var tags = ref world.Get<GameplayTagContainer>(entities.Governor);
            ref var counts = ref world.Get<TagCountContainer>(entities.Governor);
            tagOps.AddTag(ref tags, ref counts, canColonize);
            SeedRelationshipEdges(engine, world, entities);

            return Task.CompletedTask;
        }

        private static bool HasTag(List<string> tags, string t)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], t, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void EnsureTagComponents(World world, Entity e)
        {
            if (!world.Has<GameplayTagContainer>(e)) world.Add(e, new GameplayTagContainer());
            if (!world.Has<TagCountContainer>(e)) world.Add(e, new TagCountContainer());
            if (!world.Has<TimedTagBuffer>(e)) world.Add(e, new TimedTagBuffer());
        }

        private static (Entity Governor, Entity Site, Entity City, Entity Camp, Entity Caravan) FindFourXEntities(World world)
        {
            Entity governor = Entity.Null;
            Entity site = Entity.Null;
            Entity city = Entity.Null;
            Entity camp = Entity.Null;
            Entity caravan = Entity.Null;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (governor == Entity.Null && string.Equals(name.Value, "Governor", StringComparison.OrdinalIgnoreCase)) governor = e;
                if (site == Entity.Null && string.Equals(name.Value, "OutpostSite", StringComparison.OrdinalIgnoreCase)) site = e;
                if (city == Entity.Null && string.Equals(name.Value, "FederationCity", StringComparison.OrdinalIgnoreCase)) city = e;
                if (camp == Entity.Null && string.Equals(name.Value, "HordeCamp", StringComparison.OrdinalIgnoreCase)) camp = e;
                if (caravan == Entity.Null && string.Equals(name.Value, "NomadCaravan", StringComparison.OrdinalIgnoreCase)) caravan = e;
            });
            return (governor, site, city, camp, caravan);
        }

        private static void SeedRelationshipEdges(
            GameEngine engine,
            World world,
            (Entity Governor, Entity Site, Entity City, Entity Camp, Entity Caravan) entities)
        {
            RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("FourX relationship showcase requires RelationshipRuntime.");
            RelationshipTypeRegistry types = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
                ?? throw new InvalidOperationException("FourX relationship showcase requires RelationshipTypeRegistry.");
            RelationshipMetricRegistry metrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
                ?? throw new InvalidOperationException("FourX relationship showcase requires RelationshipMetricRegistry.");
            RelationshipFlagRegistry flags = engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
                ?? throw new InvalidOperationException("FourX relationship showcase requires RelationshipFlagRegistry.");
            RelationshipReasonRegistry reasons = engine.GetService(CoreServiceKeys.RelationshipReasonRegistry)
                ?? throw new InvalidOperationException("FourX relationship showcase requires RelationshipReasonRegistry.");

            int diplomacyTypeId = types.GetId("Diplomacy");
            int trustMetricId = metrics.GetId("Trust");
            int tradeValueMetricId = metrics.GetId("TradeValue");
            int tradePactFlagId = flags.GetId("TradePact");
            int atWarFlagId = flags.GetId("AtWar");
            int setupReasonId = reasons.Register("Scenario.Setup");

            SeedDiplomacy(relationships, world, entities.Governor, entities.City, diplomacyTypeId, trustMetricId, tradeValueMetricId, tradePactFlagId, trust: 80, tradeValue: 140, setupReasonId);
            SeedDiplomacy(relationships, world, entities.Governor, entities.Caravan, diplomacyTypeId, trustMetricId, tradeValueMetricId, tradePactFlagId, trust: 55, tradeValue: 95, setupReasonId);

            if (world.IsAlive(entities.Camp))
            {
                relationships.SetMetric(entities.Governor, entities.Camp, diplomacyTypeId, trustMetricId, -35, setupReasonId);
                relationships.SetFlag(entities.Governor, entities.Camp, diplomacyTypeId, atWarFlagId, enabled: true, reasonId: setupReasonId);
            }
        }

        private static void SeedDiplomacy(
            RelationshipRuntime relationships,
            World world,
            Entity source,
            Entity target,
            int typeId,
            int trustMetricId,
            int tradeValueMetricId,
            int tradePactFlagId,
            int trust,
            int tradeValue,
            int reasonId)
        {
            if (!world.IsAlive(target))
            {
                return;
            }

            relationships.SetMetric(source, target, typeId, trustMetricId, trust, reasonId);
            relationships.SetMetric(source, target, typeId, tradeValueMetricId, tradeValue, reasonId);
            relationships.SetFlag(source, target, typeId, tradePactFlagId, enabled: true, reasonId: reasonId);
        }
    }
}
